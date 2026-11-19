using System;
using NUnit.Framework;

namespace AsioAudioUnity.Tests
{
    /// <summary>Tests of Test Channels' beeps (AsioTestTone), mixed by the AsioMixer without an ASIO driver.</summary>
    public class AsioTestToneTests
    {
        private const int SampleRate = 48000;
        private const int Channels = 4;

        // Reads the mixer for this many seconds, and returns the peak of each channel.
        private static float[] ReadPeaks(AsioMixer mixer, double seconds)
        {
            float[] peaks = new float[Channels];
            int frames = (int)(seconds * SampleRate);
            float[] buffer = new float[480 * Channels];
            for (int done = 0; done < frames; done += 480) // 480 frames: 1 s is exactly 100 buffers
            {
                mixer.Read(buffer, 0, buffer.Length);
                for (int i = 0; i < buffer.Length; i++) peaks[i % Channels] = Math.Max(peaks[i % Channels], Math.Abs(buffer[i]));
            }
            return peaks;
        }

        [Test]
        public void EachStepBeepsOnlyOnItsChannels()
        {
            AsioTestTone tone = new AsioTestTone(SampleRate, new[] { new[] { 2 }, new[] { 3, 4 } });
            AsioMixer mixer = new AsioMixer(SampleRate, Channels) { TestTone = tone };

            Assert.AreEqual(2, tone.StepCount);
            Assert.AreEqual(0, tone.CurrentStep);
            float[] first = ReadPeaks(mixer, 1.0);
            Assert.AreEqual(0f, first[0]);
            Assert.Greater(first[1], 0.2f); // -12 dBFS: 0.25
            Assert.LessOrEqual(first[1], 0.26f);
            Assert.AreEqual(0f, first[2]);
            Assert.AreEqual(0f, first[3]);

            Assert.AreEqual(1, tone.CurrentStep);
            float[] second = ReadPeaks(mixer, 1.0);
            Assert.AreEqual(0f, second[0]);
            Assert.AreEqual(0f, second[1]);
            Assert.Greater(second[2], 0.2f);
            Assert.Greater(second[3], 0.2f);

            Assert.IsTrue(tone.IsFinished);
            Assert.AreEqual(-1, tone.CurrentStep);
            float[] after = ReadPeaks(mixer, 0.5);
            Assert.AreEqual(0f, Math.Max(Math.Max(after[0], after[1]), Math.Max(after[2], after[3])));
        }

        [Test]
        public void ChannelsOutsideTheDriverAreIgnored()
        {
            AsioTestTone tone = new AsioTestTone(SampleRate, new[] { new[] { 0, 9 } });
            AsioMixer mixer = new AsioMixer(SampleRate, Channels) { TestTone = tone };
            float[] peaks = ReadPeaks(mixer, 1.0);
            foreach (float peak in peaks) Assert.AreEqual(0f, peak);
        }

        [Test]
        public void StopSilencesAtOnce()
        {
            AsioTestTone tone = new AsioTestTone(SampleRate, new[] { new[] { 1 } });
            AsioMixer mixer = new AsioMixer(SampleRate, Channels) { TestTone = tone };
            tone.Stop();
            Assert.IsTrue(tone.IsFinished);
            Assert.AreEqual(0f, ReadPeaks(mixer, 0.5)[0]);
        }
    }
}
