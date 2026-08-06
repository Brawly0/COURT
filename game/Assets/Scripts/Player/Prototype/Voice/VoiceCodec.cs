namespace CaseClosed.Game.Prototype.Voice
{
    /// <summary>
    /// WHY THIS EXISTS: raw microphone audio is far too fat to put on the wire.
    /// Unity hands us 32-bit floats; at 16 kHz that is 64 KB/s per talking player,
    /// and the host has to relay that to everyone in earshot.
    ///
    /// G.711 mu-law squeezes each sample to one byte with a logarithmic curve —
    /// fine detail where quiet speech lives, coarse steps where loud sounds are.
    /// It sounds like a telephone, which is exactly good enough for a prototype,
    /// and it halves the traffic to 16 KB/s per speaker.
    ///
    /// Chosen over Opus (much better) purely because it is ~40 lines with no
    /// native plugin. Swapping in Opus later touches only this file.
    /// </summary>
    public static class VoiceCodec
    {
        /// <summary>Transmission rate. Speech lives under 8 kHz, so 16 kHz is plenty.</summary>
        public const int SampleRate = 16000;

        /// <summary>20 ms of audio per packet — small enough to feel live, big enough that RPC overhead is not the bulk of it.</summary>
        public const int FrameSamples = 320;

        private const int Bias = 0x84;
        private const int Clip = 32635;

        public static byte EncodeSample(short sample)
        {
            int sign = sample < 0 ? 0x80 : 0x00;
            if (sign != 0) sample = (short)-sample;
            if (sample > Clip) sample = Clip;

            int value = sample + Bias;

            // Find the highest set bit: that is the exponent (the "segment").
            int exponent = 7;
            for (int mask = 0x4000; (value & mask) == 0 && exponent > 0; exponent--, mask >>= 1) { }

            int mantissa = (value >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public static short DecodeSample(byte encoded)
        {
            int u = ~encoded & 0xFF;
            int sign = u & 0x80;
            int exponent = (u >> 4) & 0x07;
            int mantissa = u & 0x0F;

            int value = (((mantissa << 3) + Bias) << exponent) - Bias;
            return (short)(sign != 0 ? -value : value);
        }

        /// <summary>float PCM (-1..1) -> mu-law bytes.</summary>
        public static void Encode(float[] source, int count, byte[] destination)
        {
            for (int i = 0; i < count; i++)
            {
                float clamped = source[i] < -1f ? -1f : source[i] > 1f ? 1f : source[i];
                destination[i] = EncodeSample((short)(clamped * 32767f));
            }
        }

        /// <summary>mu-law bytes -> float PCM (-1..1).</summary>
        public static void Decode(byte[] source, int count, float[] destination)
        {
            for (int i = 0; i < count; i++)
                destination[i] = DecodeSample(source[i]) / 32768f;
        }

        /// <summary>
        /// Linear resample. Microphones rarely offer exactly 16 kHz, so whatever the
        /// device gives us gets converted before encoding. Linear interpolation is
        /// crude but inaudible at speech frequencies.
        /// </summary>
        public static int Resample(float[] source, int sourceCount, int sourceRate,
                                   float[] destination, int destinationRate)
        {
            if (sourceCount <= 0) return 0;
            if (sourceRate == destinationRate)
            {
                int copy = sourceCount < destination.Length ? sourceCount : destination.Length;
                System.Array.Copy(source, destination, copy);
                return copy;
            }

            double ratio = (double)sourceRate / destinationRate;
            int outCount = (int)(sourceCount / ratio);
            if (outCount > destination.Length) outCount = destination.Length;

            for (int i = 0; i < outCount; i++)
            {
                double position = i * ratio;
                int index = (int)position;
                double fraction = position - index;

                float a = source[index];
                float b = index + 1 < sourceCount ? source[index + 1] : a;
                destination[i] = (float)(a + (b - a) * fraction);
            }

            return outCount;
        }

        /// <summary>Root-mean-square loudness, 0..1. Drives the level meter and the noise gate.</summary>
        public static float Rms(float[] samples, int count)
        {
            if (count <= 0) return 0f;
            double sum = 0d;
            for (int i = 0; i < count; i++) sum += samples[i] * samples[i];
            return (float)System.Math.Sqrt(sum / count);
        }
    }
}
