using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game
{
    /// <summary>
    /// Proximity voice chat over NGO - no Vivox, no Dissonance, no accounts.
    /// The owner captures the mic, downsamples to 12 kHz, quantises to 8-bit
    /// and ships ~20 ms frames to the server; the server forwards each frame
    /// ONLY to clients within earshot (bandwidth + the GDD's proximity rule),
    /// and every listener plays it through a 3D AudioSource on the speaker's
    /// head, so distance falloff is real spatial audio rather than a volume hack.
    ///
    /// GDD 04/05: voice is the social fabric. Being overheard is a mechanic -
    /// low stamina wheezing and hallway muttering both leak position.
    /// V = push to talk (hold). Toggle OpenMic in the inspector for hot mic.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ProximityVoice : NetworkBehaviour
    {
        [Header("Range (metres)")]
        public float HearingRange = 16f;     // server stops forwarding past this
        public float FalloffStart = 2.5f;    // full volume inside this

        [Header("Capture")]
        public int SampleRate = 12000;       // plenty for speech, cheap on wire
        public bool OpenMic = false;
        public float MicGain = 1.6f;

        public bool IsTalking { get; private set; }
        /// <summary>Set by gameplay (exhaustion, contempt, trial floor control).</summary>
        public bool Muted;

        private AudioSource _source;
        private AudioClip _mic;
        private string _device;
        private int _lastMicPos;
        private readonly Queue<float> _playback = new Queue<float>();
        private readonly List<float> _capture = new List<float>();
        private const int FrameSamples = 240;   // 20 ms at 12 kHz

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

            if (IsOwner) StartCapture();
            else StartPlayback();
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
            _mic = Microphone.Start(_device, true, 1, SampleRate);
            Debug.Log($"[Voice] capturing on '{_device}' @ {SampleRate} Hz");
        }

        private void Update()
        {
            if (!IsOwner || _mic == null) return;

            var kb = Keyboard.current;
            bool ptt = OpenMic || (kb != null && kb.vKey.isPressed);
            IsTalking = ptt && !Muted;

            int pos = Microphone.GetPosition(_device);
            if (pos < 0 || pos == _lastMicPos) return;

            int count = pos >= _lastMicPos ? pos - _lastMicPos : (_mic.samples - _lastMicPos) + pos;
            if (count <= 0) return;

            var buf = new float[count];
            _mic.GetData(buf, _lastMicPos);          // wraps internally
            _lastMicPos = pos;

            if (!IsTalking) { _capture.Clear(); return; }

            for (int i = 0; i < buf.Length; i++) _capture.Add(buf[i] * MicGain);
            while (_capture.Count >= FrameSamples)
            {
                var frame = new byte[FrameSamples];
                for (int i = 0; i < FrameSamples; i++)
                {
                    float s = Mathf.Clamp(_capture[i], -1f, 1f);
                    frame[i] = (byte)Mathf.RoundToInt((s * 0.5f + 0.5f) * 255f);  // 8-bit PCM
                }
                _capture.RemoveRange(0, FrameSamples);
                SendVoiceServerRpc(frame);
            }
        }

        // ---------------------------------------------------------------- transport
        [ServerRpc]
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

        [ClientRpc]
        private void ReceiveVoiceClientRpc(byte[] frame, ClientRpcParams p = default)
        {
            if (IsOwner) return;                       // never hear yourself
            lock (_playback)
            {
                foreach (var b in frame) _playback.Enqueue(b / 255f * 2f - 1f);
                // don't let a laggy client build a growing delay
                while (_playback.Count > SampleRate) _playback.Dequeue();
            }
        }

        // ---------------------------------------------------------------- playback
        private void StartPlayback()
        {
            // streaming clip pulls from the jitter buffer on the audio thread
            _mic = AudioClip.Create("VoiceStream", SampleRate, 1, SampleRate, true, OnAudioRead);
            _source.clip = _mic;
            _source.Play();
        }

        private void OnAudioRead(float[] data)
        {
            lock (_playback)
            {
                for (int i = 0; i < data.Length; i++)
                    data[i] = _playback.Count > 0 ? _playback.Dequeue() : 0f;   // silence when idle
            }
        }
    }
}
