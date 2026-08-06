using System;

namespace CaseClosed.Game.Prototype.Voice
{
    /// <summary>
    /// WHY THIS EXISTS: network packets arrive in clumps and occasionally late, but
    /// the audio hardware demands a steady stream and will not wait. Feeding it
    /// packets directly produces clicks and gaps.
    ///
    /// This sits between the two: the network thread writes whenever frames land,
    /// the audio thread reads a fixed amount every callback. A little buffering
    /// absorbs the jitter, at the cost of a little latency.
    ///
    /// It is touched by two threads, so every operation is locked. The audio
    /// callback must never block for long — hence a plain ring buffer and no
    /// allocation anywhere in the read path.
    /// </summary>
    public class VoiceJitterBuffer
    {
        private readonly float[] _buffer;
        private readonly object _gate = new object();
        private int _readIndex;
        private int _writeIndex;
        private int _available;

        /// <summary>Samples currently waiting to be played.</summary>
        public int Available { get { lock (_gate) return _available; } }

        public VoiceJitterBuffer(int capacitySamples)
        {
            _buffer = new float[capacitySamples];
        }

        /// <summary>
        /// Called from the network thread. If the buffer is full the OLDEST audio is
        /// dropped — a listener who has fallen behind wants the newest speech, not a
        /// growing backlog of stale words.
        /// </summary>
        public void Write(float[] samples, int count)
        {
            lock (_gate)
            {
                for (int i = 0; i < count; i++)
                {
                    _buffer[_writeIndex] = samples[i];
                    _writeIndex = (_writeIndex + 1) % _buffer.Length;

                    if (_available < _buffer.Length) _available++;
                    else _readIndex = (_readIndex + 1) % _buffer.Length; // overrun: drop oldest
                }
            }
        }

        /// <summary>
        /// Called from the audio thread. Always fills the whole destination —
        /// silence on underrun, because returning short would glitch the stream.
        /// </summary>
        public void Read(float[] destination, int count)
        {
            lock (_gate)
            {
                for (int i = 0; i < count; i++)
                {
                    if (_available > 0)
                    {
                        destination[i] = _buffer[_readIndex];
                        _readIndex = (_readIndex + 1) % _buffer.Length;
                        _available--;
                    }
                    else
                    {
                        destination[i] = 0f;
                    }
                }
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _readIndex = _writeIndex = _available = 0;
            }
        }
    }
}
