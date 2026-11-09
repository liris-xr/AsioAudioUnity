using System.Globalization;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Conversions between linear volume (the gain applied to the samples, 0 to 1) and decibels, and the fader law used by the volume faders.
    /// </summary>
    public static class AsioVolume
    {
        /// <summary>Volumes at or below this level (in dB) are silence.</summary>
        public const float SilenceDecibels = -80f;

        // Fader law of a mixing desk: most of the travel covers the useful range (-30 dB to 0 dB). Half travel is -20 dB.
        private static readonly float[] FaderDecibels = { 0f, -5f, -10f, -20f, -30f, -40f, -60f, SilenceDecibels };
        private static readonly float[] FaderPositions = { 1f, 0.85f, 0.7f, 0.5f, 0.35f, 0.22f, 0.06f, 0f };

        /// <summary>The decibel values marked on the fader scales.</summary>
        public static readonly float[] ScaleMarks = { 0f, -5f, -10f, -20f, -30f, -40f, -60f };

        /// <summary>Linear volume (0 to 1) to decibels. Silence gives negative infinity.</summary>
        public static float ToDecibels(float linear)
        {
            if (linear <= 0f) return float.NegativeInfinity;
            float decibels = 20f * Mathf.Log10(linear);
            return decibels <= SilenceDecibels ? float.NegativeInfinity : decibels;
        }

        /// <summary>Decibels to linear volume. SilenceDecibels and below (or negative infinity) give 0.</summary>
        public static float ToLinear(float decibels)
        {
            return decibels <= SilenceDecibels ? 0f : Mathf.Pow(10f, decibels / 20f);
        }

        /// <summary>Fader position (0: bottom, 1: top) for a level in decibels.</summary>
        public static float DecibelsToFaderPosition(float decibels)
        {
            if (decibels >= FaderDecibels[0]) return 1f;
            for (int i = 1; i < FaderDecibels.Length; i++)
            {
                if (decibels >= FaderDecibels[i])
                {
                    return Mathf.Lerp(FaderPositions[i], FaderPositions[i - 1], Mathf.InverseLerp(FaderDecibels[i], FaderDecibels[i - 1], decibels));
                }
            }
            return 0f;
        }

        /// <summary>Level in decibels for a fader position (0: bottom = silence, 1: top = 0 dB).</summary>
        public static float FaderPositionToDecibels(float position)
        {
            if (position <= 0f) return float.NegativeInfinity;
            if (position >= 1f) return 0f;
            for (int i = 1; i < FaderPositions.Length; i++)
            {
                if (position >= FaderPositions[i])
                {
                    return Mathf.Lerp(FaderDecibels[i], FaderDecibels[i - 1], Mathf.InverseLerp(FaderPositions[i], FaderPositions[i - 1], position));
                }
            }
            return float.NegativeInfinity;
        }

        public static float LinearToFaderPosition(float linear) => DecibelsToFaderPosition(ToDecibels(linear));

        public static float FaderPositionToLinear(float position) => ToLinear(FaderPositionToDecibels(position));

        /// <summary>Formats a level in decibels, e.g. "-6.0 dB" or "-∞ dB" (always with a decimal point).</summary>
        public static string FormatDecibels(float decibels, bool withUnit = true)
        {
            string text = float.IsNegativeInfinity(decibels) || decibels <= SilenceDecibels ? "-∞" : decibels.ToString("0.0", CultureInfo.InvariantCulture);
            return withUnit ? text + " dB" : text;
        }

        /// <summary>Formats a linear volume in decibels, e.g. "-6.0 dB".</summary>
        public static string FormatLinear(float linear, bool withUnit = true) => FormatDecibels(ToDecibels(linear), withUnit);
    }
}
