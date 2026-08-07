using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CaseClosed.Game.Prototype.Voice
{
    /// <summary>
    /// WHY THIS EXISTS: the only script that touches a microphone. It turns the
    /// local device into a stream of encoded frames and raises an event; it knows
    /// nothing about networking, players or distance.
    ///
    /// That boundary is deliberate. Courtroom rules later ("only the witness may
    /// speak") belong in the network layer deciding whether to send a frame, never
    /// in here deciding whether to record one.
    ///
    /// Runs on the LOCAL player only.
    /// </summary>
    public class VoiceCapture : MonoBehaviour
    {
        [Header("Push to talk")]
        [Tooltip("Hold to transmit.")]
        public Key PushToTalkKey = Key.V;

        [Tooltip("Transmit continuously instead of holding a key. Noisy without echo cancellation.")]
        public bool OpenMic = false;

        [Header("Input")]
        [Tooltip("Loudness below this is treated as silence and not transmitted. 0 = send everything.")]
        [Range(0f, 0.2f)] public float InputSensitivity = 0.015f;

        [Tooltip("Multiplies the captured signal before encoding.")]
        [Range(0.1f, 8f)] public float MicrophoneGain = 1.6f;

        [Tooltip("Muted overrides push-to-talk and open mic. Nothing is transmitted.")]
        public bool Muted = false;

        [Header("Testing")]
        [Tooltip("Transmit a synthetic tone instead of the microphone. Lets the whole " +
                 "voice pipeline be tested on machines with no mic, and in headless builds. " +
                 "Ignores push-to-talk; still respects Muted.")]
        public bool UseTestTone = false;

        [Tooltip("Frequency of the test tone, Hz.")]
        public float TestToneHz = 440f;

        /// <summary>Fires once per 20 ms frame while actually transmitting.</summary>
        public event Action<byte[]> FrameReady;

        /// <summary>0..1 loudness of the local mic. Drives the on-screen indicator.</summary>
        public float InputLevel { get; private set; }

        /// <summary>True when frames are actually going out (key held, not muted, above the gate).</summary>
        public bool IsTransmitting { get; private set; }

        /// <summary>Name of the device in use, or empty if capture failed.</summary>
        public string ActiveDevice { get; private set; } = "";

        public bool HasMicrophone => Microphone.devices.Length > 0;

        private AudioClip _clip;
        private int _captureRate;
        private int _lastPosition;

        private float[] _rawBuffer;      // samples straight from the device
        private float[] _resampleTemp;   // this tick's conversion output
        private float[] _resampled;      // queue at VoiceCodec.SampleRate, _pending long
        private float[] _frame;          // exactly one frame's worth
        private byte[] _encoded;
        private int _pending;            // resampled samples not yet sent

        private void Start()
        {
            _resampleTemp = new float[VoiceCodec.SampleRate];   // 1s headroom
            _resampled = new float[VoiceCodec.SampleRate];
            _frame = new float[VoiceCodec.FrameSamples];
            _encoded = new byte[VoiceCodec.FrameSamples];

            if (UseTestTone) return;

            if (HasMicrophone) StartCapture(Microphone.devices[0]);
            else Debug.LogWarning("[Voice] No microphone detected - you can still hear others.");
        }

        private void OnDestroy() => StopCapture();

        /// <summary>
        /// Devices disagree about supported rates, so ask before assuming. 0/0 from
        /// GetDeviceCaps means "anything goes".
        /// </summary>
        public void StartCapture(string device)
        {
            StopCapture();
            if (string.IsNullOrEmpty(device)) return;

            Microphone.GetDeviceCaps(device, out int minFreq, out int maxFreq);
            _captureRate = (minFreq == 0 && maxFreq == 0)
                ? VoiceCodec.SampleRate
                : Mathf.Clamp(VoiceCodec.SampleRate, minFreq, maxFreq);

            _clip = Microphone.Start(device, true, 1, _captureRate);
            if (_clip == null)
            {
                Debug.LogError($"[Voice] Could not open microphone '{device}'.");
                return;
            }

            _rawBuffer = new float[_captureRate];
            _lastPosition = 0;
            _pending = 0;
            ActiveDevice = device;
            Debug.Log($"[Voice] Capturing from '{device}' at {_captureRate} Hz.");
        }

        public void StopCapture()
        {
            if (!string.IsNullOrEmpty(ActiveDevice) && Microphone.IsRecording(ActiveDevice))
                Microphone.End(ActiveDevice);

            _clip = null;
            ActiveDevice = "";
            IsTransmitting = false;
            InputLevel = 0f;
        }

        /// <summary>Cycles to the next device. Enough selection for a prototype.</summary>
        public void SelectNextDevice()
        {
            var devices = Microphone.devices;
            if (devices.Length == 0) return;

            int current = Array.IndexOf(devices, ActiveDevice);
            StartCapture(devices[(current + 1) % devices.Length]);
        }

        private void Update()
        {
            if (UseTestTone) { FeedTestTone(); return; }
            if (_clip == null) return;

            int position = Microphone.GetPosition(ActiveDevice);
            if (position < 0 || position == _lastPosition) return;

            // The device buffer is a ring; work out how much is new since last frame.
            int newSamples = position >= _lastPosition
                ? position - _lastPosition
                : (_clip.samples - _lastPosition) + position;

            if (newSamples <= 0 || newSamples > _rawBuffer.Length)
            {
                _lastPosition = position;
                return;
            }

            _clip.GetData(_rawBuffer, _lastPosition);
            _lastPosition = position;

            for (int i = 0; i < newSamples; i++)
                _rawBuffer[i] = Mathf.Clamp(_rawBuffer[i] * MicrophoneGain, -1f, 1f);

            InputLevel = VoiceCodec.Rms(_rawBuffer, newSamples);

            bool wants = !Muted && (OpenMic || IsPushToTalkHeld());
            bool loudEnough = InputLevel >= InputSensitivity;
            IsTransmitting = wants && loudEnough;

            if (!wants)
            {
                // Drop anything half-collected so releasing the key cuts cleanly.
                _pending = 0;
                return;
            }

            if (!loudEnough)
            {
                _pending = 0;
                return;
            }

            // Convert this tick's audio, then append it behind whatever is still
            // queued. Frames are a fixed 320 samples but a tick rarely lands on an
            // exact multiple, so the remainder has to survive to the next tick.
            int produced = VoiceCodec.Resample(_rawBuffer, newSamples, _captureRate,
                                               _resampleTemp, VoiceCodec.SampleRate);

            int space = _resampled.Length - _pending;
            int copied = produced < space ? produced : space;
            Array.Copy(_resampleTemp, 0, _resampled, _pending, copied);
            _pending += copied;

            EmitWholeFrames();
        }

        /// <summary>
        /// Synthesises audio straight into the transmit queue, at exactly the rate
        /// real capture would produce it. Everything downstream — framing, encoding,
        /// RPC, culling, decoding, playback — is the production path untouched.
        /// </summary>
        private float _tonePhase;

        private void FeedTestTone()
        {
            IsTransmitting = !Muted;
            if (Muted) { _pending = 0; InputLevel = 0f; return; }

            int wanted = Mathf.Min((int)(Time.deltaTime * VoiceCodec.SampleRate), _resampleTemp.Length);
            if (wanted <= 0) return;

            float step = 2f * Mathf.PI * TestToneHz / VoiceCodec.SampleRate;
            for (int i = 0; i < wanted; i++)
            {
                _resampleTemp[i] = Mathf.Sin(_tonePhase) * 0.35f;
                _tonePhase += step;
                if (_tonePhase > Mathf.PI * 2f) _tonePhase -= Mathf.PI * 2f;
            }

            InputLevel = VoiceCodec.Rms(_resampleTemp, wanted);

            int space = _resampled.Length - _pending;
            int copied = wanted < space ? wanted : space;
            Array.Copy(_resampleTemp, 0, _resampled, _pending, copied);
            _pending += copied;

            EmitWholeFrames();
        }

        private void EmitWholeFrames()
        {
            while (_pending >= VoiceCodec.FrameSamples)
            {
                Array.Copy(_resampled, 0, _frame, 0, VoiceCodec.FrameSamples);
                VoiceCodec.Encode(_frame, VoiceCodec.FrameSamples, _encoded);

                // Fresh array per frame: the RPC serialises asynchronously and must
                // not see the buffer mutate underneath it.
                var packet = new byte[VoiceCodec.FrameSamples];
                Array.Copy(_encoded, packet, VoiceCodec.FrameSamples);
                FrameReady?.Invoke(packet);

                _pending -= VoiceCodec.FrameSamples;
                if (_pending > 0)
                    Array.Copy(_resampled, VoiceCodec.FrameSamples, _resampled, 0, _pending);
            }
        }

        private bool IsPushToTalkHeld()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;
            if (PushToTalkKey == Key.None || !Enum.IsDefined(typeof(Key), PushToTalkKey)) return false;
            return keyboard[PushToTalkKey].isPressed;
        }
    }
}
