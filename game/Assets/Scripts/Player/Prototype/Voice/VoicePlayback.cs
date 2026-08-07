using UnityEngine;

namespace CaseClosed.Game.Prototype.Voice
{
    /// <summary>
    /// WHY THIS EXISTS: turns received frames back into sound coming out of the
    /// speaker's body. It lives on every player object; on your own it stays silent
    /// (you do not want to hear yourself half a second late).
    ///
    /// The proximity effect is NOT computed here. The AudioSource is attached to the
    /// speaking character and set to fully 3D, so Unity's audio engine does distance
    /// and direction for us. We only hand it the curve and the max range.
    ///
    /// Playback uses a streaming AudioClip rather than PlayOneShot per packet:
    /// one continuous clip whose read callback drains the jitter buffer. Playing
    /// packets individually produces a click at every seam.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class VoicePlayback : MonoBehaviour
    {
        [Header("Buffering")]
        [Tooltip("How much audio to hold back before playing, in milliseconds. " +
                 "Higher survives worse networks but adds delay.")]
        [Range(20f, 400f)] public float TargetLatencyMs = 90f;

        [Header("Occlusion")]
        [Tooltip("Volume multiplier when fully blocked by walls.")]
        [Range(0f, 1f)] public float OccludedVolume = 0.18f;

        [Tooltip("Low-pass cutoff when fully blocked, Hz. Muffling is what actually " +
                 "reads as 'through a wall' - volume alone just sounds far away.")]
        public float OccludedCutoffHz = 750f;

        [Tooltip("Cutoff with clear line of sight. 22000 = filter effectively off.")]
        public float OpenCutoffHz = 22000f;

        [Tooltip("Seconds to blend between blocked and clear, so walking through a " +
                 "doorway fades instead of popping.")]
        [Range(0.01f, 1f)] public float OcclusionSmoothing = 0.18f;

        /// <summary>True while audio is actually coming out of this character.</summary>
        public bool IsPlaying { get; private set; }

        /// <summary>Loudness of what is currently being played, 0..1. Drives the remote speaking indicator.</summary>
        public float OutputLevel { get; private set; }

        /// <summary>0 = clear line of sight, 1 = fully walled off. Set by PlayerVoice.</summary>
        public float Occlusion { get; private set; }

        private AudioSource _source;
        private AudioLowPassFilter _lowPass;
        private VoiceJitterBuffer _buffer;
        private float[] _decoded;
        private float _silenceTimer;
        private float _smoothedOcclusion;
        private float _baseVolume = 1f;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _decoded = new float[VoiceCodec.FrameSamples];
            _baseVolume = _source.volume;

            // Added here rather than on the prefab so the component is self-contained
            // and cannot be half-configured.
            _lowPass = GetComponent<AudioLowPassFilter>();
            if (_lowPass == null) _lowPass = gameObject.AddComponent<AudioLowPassFilter>();
            _lowPass.cutoffFrequency = OpenCutoffHz;

            // Two seconds of ring. Far more than the target latency, so a burst of
            // late packets is absorbed instead of overrunning.
            _buffer = new VoiceJitterBuffer(VoiceCodec.SampleRate * 2);

            // A looping streaming clip: the callback is asked for samples forever,
            // and returns silence whenever nobody is speaking.
            var clip = AudioClip.Create("VoiceStream", VoiceCodec.SampleRate, 1,
                                        VoiceCodec.SampleRate, true, OnPcmRead);
            _source.clip = clip;
            _source.loop = true;
            _source.playOnAwake = false;
        }

        /// <summary>
        /// Applies the proximity settings. Called by PlayerVoice so there is a single
        /// source of truth shared with the server's culling distance.
        /// </summary>
        public void ConfigureSpatial(float maxDistance, float minVolume, AnimationCurve falloff)
        {
            _source.spatialBlend = 1f;                 // fully 3D. 0 would be global — never do that here
            _source.dopplerLevel = 0f;                 // pitch-shifting speech sounds wrong
            _source.rolloffMode = AudioRolloffMode.Custom;
            _source.minDistance = 1f;
            _source.maxDistance = Mathf.Max(1.5f, maxDistance);
            _source.bypassReverbZones = true;

            // Unity samples this curve over 0..1 = minDistance..maxDistance.
            var curve = NormalizeFalloff(falloff, minVolume);
            _source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
        }

        /// <summary>
        /// Guarantees the curve starts at full volume and ends at the configured
        /// floor, whatever was drawn in the Inspector. Without this a hand-edited
        /// curve can end above zero and leave distant players faintly audible.
        /// </summary>
        private static AnimationCurve NormalizeFalloff(AnimationCurve source, float minVolume)
        {
            if (source == null || source.length < 2)
                return AnimationCurve.EaseInOut(0f, 1f, 1f, Mathf.Clamp01(minVolume));

            var curve = new AnimationCurve(source.keys);
            var first = curve.keys[0];
            var last = curve.keys[curve.length - 1];

            first.time = 0f; first.value = 1f;
            last.time = 1f; last.value = Mathf.Clamp01(minVolume);

            curve.MoveKey(0, first);
            curve.MoveKey(curve.length - 1, last);
            return curve;
        }

        /// <summary>Called from the network layer when a frame arrives for this player.</summary>
        public void PushFrame(byte[] frame)
        {
            if (frame == null || frame.Length == 0) return;

            int count = Mathf.Min(frame.Length, _decoded.Length);
            VoiceCodec.Decode(frame, count, _decoded);
            _buffer.Write(_decoded, count);

            OutputLevel = VoiceCodec.Rms(_decoded, count);
            _silenceTimer = 0f;
            IsPlaying = true;

            // Wait until enough is queued before starting, or the first words play
            // into an empty buffer and stutter.
            if (!_source.isPlaying)
            {
                int needed = (int)(VoiceCodec.SampleRate * (TargetLatencyMs / 1000f));
                if (_buffer.Available >= needed) _source.Play();
            }
        }

        /// <summary>
        /// Called by PlayerVoice with the measured line-of-sight blockage between
        /// this speaker and the local listener.
        /// </summary>
        public void SetOcclusion(float occlusion) => Occlusion = Mathf.Clamp01(occlusion);

        private void ApplyOcclusion()
        {
            // Exponential smoothing rather than a hard cut: doorways and corners
            // otherwise produce an audible click every time you cross them.
            float rate = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.001f, OcclusionSmoothing));
            _smoothedOcclusion = Mathf.Lerp(_smoothedOcclusion, Occlusion, rate);

            _source.volume = _baseVolume * Mathf.Lerp(1f, OccludedVolume, _smoothedOcclusion);

            if (_lowPass != null)
            {
                // Lerp in log space - pitch and perceived brightness are logarithmic,
                // so a linear sweep spends most of its travel doing nothing audible.
                float open = Mathf.Log(Mathf.Max(20f, OpenCutoffHz));
                float shut = Mathf.Log(Mathf.Max(20f, OccludedCutoffHz));
                _lowPass.cutoffFrequency = Mathf.Exp(Mathf.Lerp(open, shut, _smoothedOcclusion));
            }
        }

        private void Update()
        {
            ApplyOcclusion();
            if (!IsPlaying) return;

            _silenceTimer += Time.deltaTime;
            if (_silenceTimer > 0.35f)
            {
                IsPlaying = false;
                OutputLevel = 0f;
                if (_source.isPlaying) _source.Stop();
                _buffer.Clear();
            }
        }

        /// <summary>
        /// AUDIO THREAD. No allocation, no Unity API calls, no logging — this runs
        /// hundreds of times a second and stalling it clicks the whole game's audio.
        /// </summary>
        private void OnPcmRead(float[] data)
        {
            _buffer.Read(data, data.Length);
        }
    }
}
