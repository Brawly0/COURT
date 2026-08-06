using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game
{
    /// <summary>
    /// Proximity voice chat over NGO - no Vivox, no Dissonance, no accounts.
    ///
    /// Signal path (v2, the sound-design pass):
    ///   mic (native rate) -> decimate to 12 kHz -> DC-block -> gain + soft
    ///   limiter -> noise gate (RMS + hold) -> u-law 8-bit -> 20 ms frames ->
    ///   server -> forwarded ONLY to clients within earshot -> u-law decode ->
    ///   jitter buffer (60 ms prime) -> 3D AudioSource on the speaker's head,
    ///   with wall-occlusion low-pass.
    ///
    /// Why each stage exists:
    ///   - capture at the DEVICE's supported rate and decimate ourselves:
    ///     many Windows mics refuse 12 kHz and deliver 44.1/48 k; treating
    ///     that as 12 k plays voices 4x slow and deep (classic bug).
    ///   - u-law instead of linear 8-bit: logarithmic quantisation matches
    ///     speech dynamics - quiet syllables keep detail, ~half the perceived
    ///     noise floor at the same bitrate (it's what telephones used).
    ///   - soft limiter before quantise: hard clipping at 8 bits sounds like
    ///     tearing paper; a gentle knee saturates musically instead.
    ///   - noise gate with hold: no keyboard clatter / breath hiss on open
    ///     mic, the 350 ms hold stops it chopping tails off words.
    ///   - jitter buffer priming: never start playback with an empty buffer,
    ///     or the first word stutters; 3 frames (60 ms) of cushion, capped at
    ///     250 ms so latency can never silently grow.
    ///   - occlusion: a wall between you and the speaker muffles them
    ///     (low-pass + volume dip). Eavesdropping through doors is gameplay.
    ///
    /// GDD 04/05: voice is the social fabric; being overheard is a mechanic.
    /// V = push to talk (hold). OpenMic in the inspector for hot mic.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ProximityVoice : NetworkBehaviour
    {
        [Header("Range (metres)")]
        public float HearingRange = 16f;     // server stops forwarding past this
        public float FalloffStart = 2.5f;    // full volume inside this

        [Header("Capture")]
        public int SampleRate = 12000;       // wire rate; capture decimates to this
        public bool OpenMic = false;
        public float MicGain = 1.9f;

        [Header("Noise gate")]
        public float GateThreshold = 0.015f; // frame RMS below this = silence
        public float GateHold = 0.35f;       // seconds the gate stays open after speech

        public bool IsTalking { get; private set; }
        /// <summary>Set by gameplay (exhaustion, contempt, trial floor control).</summary>
        public bool Muted;

        private AudioSource _source;
        private AudioLowPassFilter _lowPass;
        private CharacterAnimator _anim;
        private AudioClip _micClip;
        private string _device;
        private int _captureRate;
        private float _resamplePhase;
        private int _lastMicPos;
        private float _dc;                    // running DC offset estimate
        private float _gateOpenUntil;
        private bool _primed;                 // jitter buffer has enough to start
        private float _occlusion, _occlusionTarget, _occlusionTimer;
        private readonly Queue<float> _playback = new Queue<float>();
        private readonly List<float> _capture = new List<float>();
        private const int FrameSamples = 240;             // 20 ms @ 12 kHz
        private const int PrimeSamples = FrameSamples * 3; // 60 ms cushion
        private const int MaxBufferedSamples = 3000;       // 250 ms latency cap

        public override void OnNetworkSpawn()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = true;
            _source.spatialBlend = 1f;                       // fully 3D
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = FalloffStart;
            _source.maxDistance = HearingRange;
            _source.dopplerLevel = 0f;
            _source.spread = 40f;                            // voices aren't laser-panned

            if (IsOwner) StartCapture();
            else
            {
                _lowPass = gameObject.GetComponent<AudioLowPassFilter>();
                if (_lowPass == null) _lowPass = gameObject.AddComponent<AudioLowPassFilter>();
                _lowPass.cutoffFrequency = 22000f;
                StartPlayback();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && _device != null && Microphone.IsRecording(_device))
                Microphone.End(_device);
        }

        // ---------------------------------------------------------------- capture
        private void StartCapture()
        {
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[Voice] no microphone found - voice disabled for this client");
                return;
            }
            _device = Microphone.devices[0];

            // ask for what the DEVICE supports, decimate to 12 kHz ourselves
            Microphone.GetDeviceCaps(_device, out int minF, out int maxF);
            _captureRate = (minF == 0 && maxF == 0) ? SampleRate : Mathf.Clamp(SampleRate, minF, maxF);
            _micClip = Microphone.Start(_device, true, 1, _captureRate);
            Debug.Log($"[Voice] capturing on '{_device}' @ {_captureRate} Hz -> {SampleRate} Hz on wire");
        }

        private void Update()
        {
            if (IsOwner) UpdateCapture();
            else UpdateOcclusion();
        }

        private void UpdateCapture()
        {
            if (_micClip == null) return;

            var kb = Keyboard.current;
            bool ptt = OpenMic || (kb != null && kb.vKey.isPressed);
            bool wantTalk = ptt && !Muted;

            int pos = Microphone.GetPosition(_device);
            if (pos < 0 || pos == _lastMicPos) return;

            int count = pos >= _lastMicPos ? pos - _lastMicPos : (_micClip.samples - _lastMicPos) + pos;
            if (count <= 0) return;

            var buf = new float[count];
            _micClip.GetData(buf, _lastMicPos);          // wraps internally
            _lastMicPos = pos;

            if (!wantTalk) { _capture.Clear(); _resamplePhase = 0f; IsTalking = false; return; }

            // decimate device rate -> wire rate, phase carried across reads.
            // BOX-AVERAGE over the step (not point-sample): averaging is a crude
            // anti-alias low-pass, without it sibilance above 6 kHz folds back
            // into the voice band as harsh metallic fizz
            float step = (float)_captureRate / SampleRate;
            float idx = _resamplePhase;
            while (idx < count)
            {
                int start = (int)idx;
                int end = Mathf.Min((int)(idx + step), count);
                float s = 0f;
                for (int k = start; k < end; k++) s += buf[k];
                s /= Mathf.Max(1, end - start);

                _dc = Mathf.Lerp(_dc, s, 0.002f);        // DC-block (cheap high-pass)
                s = (s - _dc) * MicGain;
                s = s / (1f + 0.5f * Mathf.Abs(s));      // soft limiter knee
                _capture.Add(Mathf.Clamp(s, -1f, 1f));
                idx += step;
            }
            _resamplePhase = idx - count;

            bool sentThisUpdate = false;
            while (_capture.Count >= FrameSamples)
            {
                // ---- noise gate: frame RMS with hold ----
                float sum = 0f;
                for (int i = 0; i < FrameSamples; i++) sum += _capture[i] * _capture[i];
                float rms = Mathf.Sqrt(sum / FrameSamples);
                if (rms > GateThreshold) _gateOpenUntil = Time.time + GateHold;

                if (Time.time <= _gateOpenUntil)
                {
                    var frame = new byte[FrameSamples];
                    for (int i = 0; i < FrameSamples; i++) frame[i] = MuLawEncode(_capture[i]);
                    SendVoiceServerRpc(frame);
                    sentThisUpdate = true;
                }
                _capture.RemoveRange(0, FrameSamples);
            }
            IsTalking = sentThisUpdate || Time.time <= _gateOpenUntil;
        }

        // ---------------------------------------------------------------- occlusion
        private void UpdateOcclusion()
        {
            if (_lowPass == null) return;

            _occlusionTimer -= Time.deltaTime;
            if (_occlusionTimer <= 0f)
            {
                _occlusionTimer = 0.2f;
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 from = cam.transform.position, to = transform.position;
                    bool blocked = false;
                    var hits = Physics.RaycastAll(from, (to - from).normalized, Vector3.Distance(from, to));
                    foreach (var h in hits)
                    {
                        if (h.collider.isTrigger || h.collider is CharacterController) continue;
                        // people and props aren't walls: a witness standing
                        // between two players must not muffle their voices
                        if (h.collider.GetComponentInParent<IInteractable>() != null) continue;
                        blocked = true; break;               // static geometry between us
                    }
                    _occlusionTarget = blocked ? 1f : 0f;
                }
            }
            _occlusion = Mathf.MoveTowards(_occlusion, _occlusionTarget, Time.deltaTime * 4f);
            _lowPass.cutoffFrequency = Mathf.Lerp(22000f, 1200f, _occlusion);
            _source.volume = Mathf.Lerp(1f, 0.55f, _occlusion);
        }

        // ---------------------------------------------------------------- transport
        // UNRELIABLE on purpose: a lost voice frame should be DROPPED, not
        // retransmitted - reliable delivery turns one packet loss into a
        // growing latency bubble. 240-byte frames fit any MTU.
        [ServerRpc(Delivery = RpcDelivery.Unreliable)]
        private void SendVoiceServerRpc(byte[] frame, ServerRpcParams p = default)
        {
            // forward only to players within earshot - the whole point of proximity
            var speaker = transform.position;
            var targets = new List<ulong>();
            foreach (var kv in NetworkManager.ConnectedClients)
            {
                if (kv.Key == OwnerClientId) continue;
                var obj = kv.Value.PlayerObject;
                if (obj == null) continue;
                if ((obj.transform.position - speaker).sqrMagnitude <= HearingRange * HearingRange)
                    targets.Add(kv.Key);
            }
            if (targets.Count == 0) return;

            ReceiveVoiceClientRpc(frame, new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = targets.ToArray() }
            });
        }

        [ClientRpc(Delivery = RpcDelivery.Unreliable)]
        private void ReceiveVoiceClientRpc(byte[] frame, ClientRpcParams p = default)
        {
            if (IsOwner) return;                       // never hear yourself
            lock (_playback)
            {
                foreach (var b in frame) _playback.Enqueue(MuLawDecode(b));
                while (_playback.Count > MaxBufferedSamples) _playback.Dequeue();
            }
            // the speaker's puppet flaps its jaw on every listener's screen
            if (_anim == null) _anim = GetComponentInParent<CharacterAnimator>();
            if (_anim != null) _anim.SetTalking(0.3f);
        }

        // ---------------------------------------------------------------- playback
        private void StartPlayback()
        {
            // streaming clip pulls from the jitter buffer on the audio thread
            var clip = AudioClip.Create("VoiceStream", SampleRate, 1, SampleRate, true, OnAudioRead);
            _source.clip = clip;
            _source.Play();
        }

        private void OnAudioRead(float[] data)
        {
            lock (_playback)
            {
                if (!_primed && _playback.Count >= PrimeSamples) _primed = true;
                for (int i = 0; i < data.Length; i++)
                {
                    if (_primed && _playback.Count > 0) data[i] = _playback.Dequeue();
                    else { data[i] = 0f; _primed = _playback.Count >= PrimeSamples; }
                }
            }
        }

        // ---------------------------------------------------------------- u-law
        private static byte MuLawEncode(float s)
        {
            float sign = s < 0f ? -1f : 1f;
            float mag = Mathf.Log(1f + 255f * Mathf.Min(Mathf.Abs(s), 1f)) / 5.5452f; // ln(256)
            return (byte)Mathf.RoundToInt((sign * mag * 0.5f + 0.5f) * 255f);
        }

        private static float MuLawDecode(byte b)
        {
            float u = b / 255f * 2f - 1f;
            float sign = u < 0f ? -1f : 1f;
            return sign * (Mathf.Pow(256f, Mathf.Abs(u)) - 1f) / 255f;
        }
    }
}
