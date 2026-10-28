using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// A two-pole filter (the standard "biquad"): a low pass that takes 12 dB an octave off above its cutoff (two in a
    /// row: 24), or a bell that lifts (or cuts) a band around a frequency. Keeps its own memory, so one per channel.
    /// </summary>
    internal struct Biquad
    {
        private float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

        /// <summary>Lets everything through unchanged (until it's set).</summary>
        public static Biquad Flat => new() { _b0 = 1f };

        /// <summary>Keeps what's below <paramref name="cutoff"/> Hz. <paramref name="q"/> 0.707 is flat right up to the cutoff.</summary>
        public void SetLowPass(float cutoff, float q, int sampleRate)
        {
            float w = 2f * Mathf.PI * Mathf.Clamp(cutoff, 10f, sampleRate * 0.45f) / sampleRate;
            float cos = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2f * Mathf.Max(0.1f, q));
            float a0 = 1f + alpha;
            _b0 = (1f - cos) * 0.5f / a0;
            _b1 = (1f - cos) / a0;
            _b2 = _b0;
            _a1 = -2f * cos / a0;
            _a2 = (1f - alpha) / a0;
        }

        /// <summary>Lifts (positive <paramref name="gainDb"/>) or cuts a band around <paramref name="frequency"/> Hz; <paramref name="q"/> how narrow.</summary>
        public void SetPeak(float frequency, float q, float gainDb, int sampleRate)
        {
            float w = 2f * Mathf.PI * Mathf.Clamp(frequency, 10f, sampleRate * 0.45f) / sampleRate;
            float cos = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2f * Mathf.Max(0.1f, q));
            float a = Mathf.Pow(10f, gainDb / 40f);
            float a0 = 1f + alpha / a;
            _b0 = (1f + alpha * a) / a0;
            _b1 = -2f * cos / a0;
            _b2 = (1f - alpha * a) / a0;
            _a1 = _b1;
            _a2 = (1f - alpha / a) / a0;
        }

        public float Process(float x)
        {
            float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            // (Denormals and blow-ups would stall or break the mix.)
            if (float.IsNaN(y) || float.IsInfinity(y) || Mathf.Abs(y) < 1e-20f)
                y = 0f;
            _x2 = _x1;
            _x1 = x;
            _y2 = _y1;
            _y1 = y;
            return y;
        }

        /// <summary>Forgets what it's heard (a voice played again).</summary>
        public void Clear() => _x1 = _x2 = _y1 = _y2 = 0f;
    }
}
