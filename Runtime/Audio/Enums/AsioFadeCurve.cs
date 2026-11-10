using System;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// The shape of a fade, as the gain over the fade's progress. A fade-out uses the mirrored shape of the same name
    /// (like REAPER's item fades): <see cref="Squared"/> starts gently from silence on a fade-in, and ends gently into silence on a fade-out.
    /// </summary>
    public enum AsioFadeCurve
    {
        /// <summary>Gain = x². Gentle near silence, the default (sounds smoother than linear).</summary>
        [InspectorName("Squared (default)")] Squared = 0,
        /// <summary>Gain = x. A constant change of amplitude: sounds abrupt near silence.</summary>
        Linear = 1,
        /// <summary>Gain = 1 - (1 - x)². Fast near silence, gentle near full volume.</summary>
        [InspectorName("Inverted Squared")] InvertedSquared = 2,
        /// <summary>Gain = (1 - cos(πx)) / 2. Gentle at both ends.</summary>
        [InspectorName("S-Curve")] SCurve = 3,
    }

    public static class AsioFadeCurves
    {
        /// <summary>
        /// The gain (0 to 1) of a fade with this curve, where <paramref name="x"/> is how far the sound is from silence
        /// (0: silent, 1: full volume), i.e. the progress of a fade-in, or the remaining part of a fade-out.
        /// </summary>
        public static float Evaluate(AsioFadeCurve curve, float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            switch (curve)
            {
                case AsioFadeCurve.Linear: return x;
                case AsioFadeCurve.InvertedSquared: return 1f - (1f - x) * (1f - x);
                case AsioFadeCurve.SCurve: return (float)((1.0 - Math.Cos(Math.PI * x)) * 0.5);
                default: return x * x;
            }
        }

        /// <summary>The position <c>x</c> at which <see cref="Evaluate"/> gives this gain.</summary>
        public static float Inverse(AsioFadeCurve curve, float gain)
        {
            if (gain <= 0f) return 0f;
            if (gain >= 1f) return 1f;
            switch (curve)
            {
                case AsioFadeCurve.Linear: return gain;
                case AsioFadeCurve.InvertedSquared: return 1f - (float)Math.Sqrt(1f - gain);
                case AsioFadeCurve.SCurve: return (float)(Math.Acos(1.0 - 2.0 * gain) / Math.PI);
                default: return (float)Math.Sqrt(gain);
            }
        }
    }
}
