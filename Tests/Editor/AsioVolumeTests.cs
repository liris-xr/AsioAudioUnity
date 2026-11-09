using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace AsioAudioUnity.Tests
{
    public class AsioVolumeTests
    {
        [Test]
        public void LinearAndDecibels_Convert_BothWays()
        {
            Assert.That(AsioVolume.ToDecibels(1f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(AsioVolume.ToDecibels(0.5f), Is.EqualTo(-6.0206f).Within(0.001f));
            Assert.That(AsioVolume.ToLinear(-20f), Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(float.IsNegativeInfinity(AsioVolume.ToDecibels(0f)));
            Assert.AreEqual(0f, AsioVolume.ToLinear(float.NegativeInfinity));
        }

        [Test]
        public void FaderLaw_MatchesAMixingDesk_AndRoundTrips()
        {
            Assert.That(AsioVolume.FaderPositionToDecibels(1f), Is.EqualTo(0f));
            Assert.That(AsioVolume.FaderPositionToDecibels(0.5f), Is.EqualTo(-20f).Within(0.001f), "Half travel is -20 dB.");
            Assert.That(float.IsNegativeInfinity(AsioVolume.FaderPositionToDecibels(0f)));

            for (float position = 0.05f; position <= 1f; position += 0.05f)
            {
                Assert.That(AsioVolume.LinearToFaderPosition(AsioVolume.FaderPositionToLinear(position)), Is.EqualTo(position).Within(0.0001f));
            }
        }

        [Test]
        public void FormatDecibels_UsesADecimalPoint_WhateverTheLanguage()
        {
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("fr-FR");
                Assert.AreEqual("-6.0 dB", AsioVolume.FormatDecibels(-6f));
                Assert.AreEqual("-∞ dB", AsioVolume.FormatLinear(0f));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }
    }
}
