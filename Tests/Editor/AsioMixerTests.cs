using System;
using System.IO;
using NAudio.Wave;
using NUnit.Framework;

namespace AsioAudioUnity.Tests
{
    /// <summary>
    /// Tests of the audio engine without an ASIO driver: the mixer is read the way NAudio's AsioOut reads it
    /// (32-bit float through SampleToWaveProvider, which hands the mixer a byte[] disguised as a float[]).
    /// </summary>
    public class AsioMixerTests
    {
        private const int SampleRate = 48000;
        private const int Channels = 4;
        private const int FramesPerBuffer = 512;
        private const float ToneAmplitude = 0.5f;

        private string _toneFilePath;

        [OneTimeSetUp]
        public void CreateToneFile()
        {
            // 2 seconds of a 440 Hz sine at -6 dB, mono, 32-bit float
            _toneFilePath = Path.Combine(Path.GetTempPath(), "AsioAudioUnityTests_tone_" + Guid.NewGuid().ToString("N") + ".wav");
            using (WaveFileWriter writer = new WaveFileWriter(_toneFilePath, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1)))
            {
                float[] samples = new float[SampleRate * 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = ToneAmplitude * (float)Math.Sin(2 * Math.PI * 440 * i / SampleRate);
                writer.WriteSamples(samples, 0, samples.Length);
            }
        }

        [OneTimeTearDown]
        public void DeleteToneFile()
        {
            if (File.Exists(_toneFilePath)) File.Delete(_toneFilePath);
        }

        private AsioSourceVoice CreateVoice(int outputChannel, float volume = 1f)
        {
            AsioSourceVoice voice = new AsioSourceVoice { Volume = volume, OutputChannel = outputChannel };
            voice.Load(_toneFilePath, SampleRate, true);
            return voice;
        }

        // Reads one buffer like AsioOut does, and returns the peak of each channel.
        private static float[] ReadBufferPeaks(IWaveProvider output, byte[] bytes)
        {
            output.Read(bytes, 0, bytes.Length);
            float[] samples = new WaveBuffer(bytes).FloatBuffer;
            float[] peaks = new float[Channels];
            for (int frame = 0; frame < FramesPerBuffer; frame++)
            {
                for (int channel = 0; channel < Channels; channel++)
                {
                    peaks[channel] = Math.Max(peaks[channel], Math.Abs(samples[frame * Channels + channel]));
                }
            }
            return peaks;
        }

        private static byte[] NewDirtyBuffer()
        {
            byte[] bytes = new byte[FramesPerBuffer * Channels * 4];
            float[] samples = new WaveBuffer(bytes).FloatBuffer;
            for (int i = 0; i < FramesPerBuffer * Channels; i++) samples[i] = 0.9f; // Leftovers from a "previous buffer"
            return bytes;
        }

        [Test]
        public void Read_ClearsTheWholeBuffer_WhenNAudioPassesAByteArrayAsFloats()
        {
            AsioMixer mixer = new AsioMixer(SampleRate, Channels);
            byte[] bytes = NewDirtyBuffer();

            float[] peaks = ReadBufferPeaks(mixer.ToWaveProvider(), bytes);

            CollectionAssert.AreEqual(new float[Channels], peaks, "With no voice, every sample of the buffer must be silent.");
        }

        [Test]
        public void Read_DoesNotBuildUp_OverSuccessiveBuffers()
        {
            AsioMixer mixer = new AsioMixer(SampleRate, Channels);
            AsioSourceVoice voice = CreateVoice(2);
            mixer.AddVoice(voice);
            voice.Play();

            IWaveProvider output = mixer.ToWaveProvider();
            byte[] bytes = NewDirtyBuffer();
            float highestPeak = 0f;
            for (int i = 0; i < 100; i++)
            {
                float[] peaks = ReadBufferPeaks(output, bytes);
                highestPeak = Math.Max(highestPeak, peaks[1]);
                Assert.AreEqual(0f, peaks[0], "Channel 1 has no voice.");
                Assert.AreEqual(0f, peaks[2], "Channel 3 has no voice.");
            }

            Assert.That(highestPeak, Is.EqualTo(ToneAmplitude).Within(0.01f), "The output level must stay at the file's level.");
            voice.Dispose();
        }

        [Test]
        public void SourceVolume_And_MasterVolume_ScaleTheOutput()
        {
            AsioMixer mixer = new AsioMixer(SampleRate, Channels) { MasterVolume = 0.5f };
            AsioSourceVoice voice = CreateVoice(1, 0.5f);
            mixer.AddVoice(voice);
            voice.Play();

            IWaveProvider output = mixer.ToWaveProvider();
            byte[] bytes = NewDirtyBuffer();
            float highestPeak = 0f;
            for (int i = 0; i < 50; i++) highestPeak = Math.Max(highestPeak, ReadBufferPeaks(output, bytes)[0]);

            Assert.That(highestPeak, Is.EqualTo(ToneAmplitude * 0.5f * 0.5f).Within(0.01f));
            voice.Dispose();
        }

        [Test]
        public void DeferredPlay_StartsAllVoicesOnTheSameSample()
        {
            AsioMixer mixer = new AsioMixer(SampleRate, Channels);
            AsioSourceVoice first = CreateVoice(1);
            AsioSourceVoice second = CreateVoice(2);
            mixer.AddVoice(first);
            mixer.AddVoice(second);
            IWaveProvider output = mixer.ToWaveProvider();
            byte[] bytes = NewDirtyBuffer();

            first.Play(true);
            ReadBufferPeaks(output, bytes); // A buffer between the two calls, like the audio thread could render
            second.Play(true);

            float[] beforeApply = ReadBufferPeaks(output, bytes);
            Assert.AreEqual(0f, beforeApply[0], "Deferred voices must wait for ApplyPendingStates.");
            Assert.AreEqual(0f, beforeApply[1], "Deferred voices must wait for ApplyPendingStates.");

            mixer.ApplyPendingStates();
            for (int i = 0; i < 10; i++) ReadBufferPeaks(output, bytes);

            Assert.AreEqual(first.CurrentTime, second.CurrentTime, "Both voices must have played the same number of samples.");
            Assert.Greater(first.CurrentTime, 0);
            first.Dispose();
            second.Dispose();
        }

        [Test]
        public void FadeOut_AlsoAppliesAtTheEndOfANonLoopingFile()
        {
            AsioMixer mixer = new AsioMixer(SampleRate, Channels);
            AsioSourceVoice voice = CreateVoice(1);
            voice.FadeOutSeconds = 0.5f;
            mixer.AddVoice(voice);
            voice.Play();

            IWaveProvider output = mixer.ToWaveProvider();
            byte[] bytes = NewDirtyBuffer();
            int buffersInFile = SampleRate * 2 / FramesPerBuffer; // the tone lasts 2 s
            float middlePeak = 0f, lastPeak = 0f;
            for (int i = 0; i < buffersInFile; i++)
            {
                float peak = ReadBufferPeaks(output, bytes)[0];
                if (i == buffersInFile / 2) middlePeak = peak;   // 1 s: before the fade-out
                if (i == buffersInFile - 1) lastPeak = peak;     // last buffer: end of the fade-out
            }

            Assert.That(middlePeak, Is.EqualTo(ToneAmplitude).Within(0.01f), "Before the fade-out, the level is the file's level.");
            Assert.Less(lastPeak, ToneAmplitude * 0.05f, "At the end of the file, the fade-out is almost silent.");
            voice.Dispose();
        }

        [Test]
        public void PauseFadeOut_LastsTheWholeFadeDuration()
        {
            AsioMixer mixer = new AsioMixer(SampleRate, Channels);
            AsioSourceVoice voice = CreateVoice(1);
            voice.FadeOutSeconds = 0.5f;
            voice.FadeOutCurve = AsioFadeCurve.Linear;
            mixer.AddVoice(voice);
            voice.Play();

            IWaveProvider output = mixer.ToWaveProvider();
            byte[] bytes = NewDirtyBuffer();
            for (int i = 0; i < 5; i++) ReadBufferPeaks(output, bytes);
            voice.Pause();

            int buffersInFade = (int)(SampleRate * 0.5f / FramesPerBuffer);
            float midFadePeak = 0f;
            for (int i = 0; i < buffersInFade + 2; i++)
            {
                float peak = ReadBufferPeaks(output, bytes)[0];
                if (i == buffersInFade / 2) midFadePeak = peak;
            }
            float afterFadePeak = ReadBufferPeaks(output, bytes)[0];

            Assert.That(midFadePeak, Is.EqualTo(ToneAmplitude * 0.5f).Within(0.03f), "Halfway through a linear fade-out, the level is halved.");
            Assert.AreEqual(0f, afterFadePeak, "After the fade-out, the voice is silent.");
            voice.Dispose();
        }

        [TestCase(AsioFadeCurve.Linear, 0.5f)]
        [TestCase(AsioFadeCurve.Squared, 0.25f)]
        [TestCase(AsioFadeCurve.InvertedSquared, 0.75f)]
        [TestCase(AsioFadeCurve.SCurve, 0.5f)]
        public void FadeInCurve_ShapesTheFadeIn(AsioFadeCurve curve, float gainHalfway)
        {
            AsioMixer mixer = new AsioMixer(SampleRate, Channels);
            AsioSourceVoice voice = CreateVoice(1);
            voice.FadeInSeconds = 1f;
            voice.FadeInCurve = curve;
            mixer.AddVoice(voice);
            voice.Play();

            IWaveProvider output = mixer.ToWaveProvider();
            byte[] bytes = NewDirtyBuffer();
            int halfway = SampleRate / 2 / FramesPerBuffer;
            for (int i = 0; i < halfway; i++) ReadBufferPeaks(output, bytes);
            float peak = ReadBufferPeaks(output, bytes)[0];

            Assert.That(peak, Is.EqualTo(ToneAmplitude * gainHalfway).Within(0.03f));
            voice.Dispose();
        }

        [Test]
        public void FadeCurves_InverseUndoesEvaluate()
        {
            foreach (AsioFadeCurve curve in (AsioFadeCurve[])Enum.GetValues(typeof(AsioFadeCurve)))
            {
                Assert.AreEqual(0f, AsioFadeCurves.Evaluate(curve, 0f), curve.ToString());
                Assert.AreEqual(1f, AsioFadeCurves.Evaluate(curve, 1f), curve.ToString());
                for (float x = 0.05f; x < 1f; x += 0.05f)
                {
                    Assert.That(AsioFadeCurves.Inverse(curve, AsioFadeCurves.Evaluate(curve, x)), Is.EqualTo(x).Within(1e-4f), curve + " at " + x);
                }
            }
        }

        // Plays a 1 s file whose sine doesn't end where it starts (a hard loop jumps from 0.5 to 0) for 3 s, and returns the biggest
        // difference between two consecutive output samples (a click) and the final playback position.
        private static (float biggestStep, double finalTime) PlayLoopingClickyFile(float crossfadeSeconds, bool preload)
        {
            string path = Path.Combine(Path.GetTempPath(), "AsioAudioUnityTests_loop_" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                using (WaveFileWriter writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1)))
                {
                    float[] samples = new float[SampleRate];
                    for (int i = 0; i < samples.Length; i++) samples[i] = ToneAmplitude * (float)Math.Sin(2 * Math.PI * 300.25 * i / SampleRate);
                    writer.WriteSamples(samples, 0, samples.Length);
                }

                AsioMixer mixer = new AsioMixer(SampleRate, Channels);
                AsioSourceVoice voice = new AsioSourceVoice { OutputChannel = 1, Loop = true, LoopCrossfadeSeconds = crossfadeSeconds };
                voice.Load(path, SampleRate, preload);
                mixer.AddVoice(voice);
                voice.Play();

                IWaveProvider output = mixer.ToWaveProvider();
                byte[] bytes = new byte[FramesPerBuffer * Channels * 4];
                float biggestStep = 0f, previous = 0f;
                for (int b = 0; b < SampleRate * 3 / FramesPerBuffer; b++)
                {
                    output.Read(bytes, 0, bytes.Length);
                    float[] buffer = new WaveBuffer(bytes).FloatBuffer;
                    for (int frame = 0; frame < FramesPerBuffer; frame++)
                    {
                        float sample = buffer[frame * Channels];
                        biggestStep = Math.Max(biggestStep, Math.Abs(sample - previous));
                        previous = sample;
                    }
                }
                double finalTime = voice.CurrentTime;
                voice.Dispose();
                return (biggestStep, finalTime);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void HardLoop_ClicksOnAFileThatDoesNotLoopCleanly()
        {
            // Checks that the click detection of the smooth loop test works
            Assert.Greater(PlayLoopingClickyFile(0f, true).biggestStep, 0.3f);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void LoopCrossfade_HidesTheLoopPoint(bool preload)
        {
            (float biggestStep, double finalTime) = PlayLoopingClickyFile(0.2f, preload);

            // A 300 Hz sine at 0.5 changes by at most 0.02 per sample: anything much bigger is a click.
            Assert.Less(biggestStep, 0.04f, "The crossfade must hide the loop point.");
            Assert.That(finalTime, Is.InRange(0.0, 1.0), "The playback position must loop too.");
        }

        // A 1 s stereo file: left = sine at 0.5, right = sine at 0.25 (300.25 Hz: it doesn't loop cleanly).
        private static string CreateStereoFile()
        {
            string path = Path.Combine(Path.GetTempPath(), "AsioAudioUnityTests_stereo_" + Guid.NewGuid().ToString("N") + ".wav");
            using (WaveFileWriter writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)))
            {
                float[] samples = new float[SampleRate * 2];
                for (int i = 0; i < SampleRate; i++)
                {
                    float sine = (float)Math.Sin(2 * Math.PI * 300.25 * i / SampleRate);
                    samples[i * 2] = 0.5f * sine;
                    samples[i * 2 + 1] = 0.25f * sine;
                }
                writer.WriteSamples(samples, 0, samples.Length);
            }
            return path;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Multichannel_PlaysEachChannelOfTheFileOnConsecutiveOutputs(bool preload)
        {
            string path = CreateStereoFile();
            try
            {
                AsioMixer mixer = new AsioMixer(SampleRate, Channels);
                AsioSourceVoice voice = new AsioSourceVoice { OutputChannel = 2, DownmixToMono = false };
                voice.Load(path, SampleRate, preload);
                mixer.AddVoice(voice);
                voice.Play();
                Assert.AreEqual(2, voice.ChannelCount);

                IWaveProvider output = mixer.ToWaveProvider();
                byte[] bytes = NewDirtyBuffer();
                float[] highest = new float[Channels];
                for (int i = 0; i < 20; i++)
                {
                    float[] peaks = ReadBufferPeaks(output, bytes);
                    for (int c = 0; c < Channels; c++) highest[c] = Math.Max(highest[c], peaks[c]);
                }

                Assert.AreEqual(0f, highest[0], "Output 1 has nothing.");
                Assert.That(highest[1], Is.EqualTo(0.5f).Within(0.01f), "The left channel plays on output 2.");
                Assert.That(highest[2], Is.EqualTo(0.25f).Within(0.01f), "The right channel plays on output 3.");
                Assert.AreEqual(0f, highest[3], "Output 4 has nothing.");
                voice.Dispose();
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void ChannelPeaks_MeasureEachChannel()
        {
            string path = CreateStereoFile();
            try
            {
                AsioMixer mixer = new AsioMixer(SampleRate, Channels);
                AsioSourceVoice voice = new AsioSourceVoice { OutputChannel = 1, DownmixToMono = false };
                voice.Load(path, SampleRate, true);
                mixer.AddVoice(voice);
                voice.Play();

                IWaveProvider output = mixer.ToWaveProvider();
                byte[] bytes = NewDirtyBuffer();
                for (int i = 0; i < 20; i++) ReadBufferPeaks(output, bytes);

                float[] peaks = new float[4];
                Assert.AreEqual(2, voice.ConsumeChannelPeaks(peaks));
                Assert.That(peaks[0], Is.EqualTo(0.5f).Within(0.01f), "Left channel level.");
                Assert.That(peaks[1], Is.EqualTo(0.25f).Within(0.01f), "Right channel level.");

                voice.ConsumeChannelPeaks(peaks);
                Assert.AreEqual(0f, peaks[0], "Consuming resets the peaks.");
                voice.Dispose();
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void DownmixToMono_IsTheDefault_AndMixesTheChannelsOnOneOutput()
        {
            string path = CreateStereoFile();
            try
            {
                AsioMixer mixer = new AsioMixer(SampleRate, Channels);
                AsioSourceVoice voice = new AsioSourceVoice { OutputChannel = 2 };
                voice.Load(path, SampleRate, true);
                mixer.AddVoice(voice);
                voice.Play();
                Assert.AreEqual(1, voice.ChannelCount);

                IWaveProvider output = mixer.ToWaveProvider();
                byte[] bytes = NewDirtyBuffer();
                float[] highest = new float[Channels];
                for (int i = 0; i < 20; i++)
                {
                    float[] peaks = ReadBufferPeaks(output, bytes);
                    for (int c = 0; c < Channels; c++) highest[c] = Math.Max(highest[c], peaks[c]);
                }

                Assert.That(highest[1], Is.GreaterThan(0.3f), "Both channels are mixed on output 2.");
                Assert.AreEqual(0f, highest[2], "Nothing on output 3.");
                voice.Dispose();
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void Multichannel_LoopCrossfade_HidesTheLoopPointOnEveryChannel()
        {
            string path = CreateStereoFile();
            try
            {
                AsioMixer mixer = new AsioMixer(SampleRate, Channels);
                AsioSourceVoice voice = new AsioSourceVoice { OutputChannel = 1, DownmixToMono = false, Loop = true, LoopCrossfadeSeconds = 0.2f };
                voice.Load(path, SampleRate, false);
                mixer.AddVoice(voice);
                voice.Play();

                IWaveProvider output = mixer.ToWaveProvider();
                byte[] bytes = new byte[FramesPerBuffer * Channels * 4];
                float[] previous = new float[2], biggestStep = new float[2];
                for (int b = 0; b < SampleRate * 3 / FramesPerBuffer; b++)
                {
                    output.Read(bytes, 0, bytes.Length);
                    float[] buffer = new WaveBuffer(bytes).FloatBuffer;
                    for (int frame = 0; frame < FramesPerBuffer; frame++)
                    {
                        for (int c = 0; c < 2; c++)
                        {
                            float sample = buffer[frame * Channels + c];
                            biggestStep[c] = Math.Max(biggestStep[c], Math.Abs(sample - previous[c]));
                            previous[c] = sample;
                        }
                    }
                }

                Assert.Less(biggestStep[0], 0.04f, "Left: no click at the loop point.");
                Assert.Less(biggestStep[1], 0.02f, "Right: no click at the loop point.");
                Assert.That(voice.CurrentTime, Is.InRange(0.0, 1.0));
                voice.Dispose();
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void DeferredRestart_RewindsAllVoicesTogether()
        {
            AsioMixer mixer = new AsioMixer(SampleRate, Channels);
            AsioSourceVoice first = CreateVoice(1);
            AsioSourceVoice second = CreateVoice(2);
            mixer.AddVoice(first);
            mixer.AddVoice(second);
            IWaveProvider output = mixer.ToWaveProvider();
            byte[] bytes = NewDirtyBuffer();

            first.Play();
            for (int i = 0; i < 5; i++) ReadBufferPeaks(output, bytes);
            second.Play(); // Starts later: the two are out of sync
            for (int i = 0; i < 3; i++) ReadBufferPeaks(output, bytes);
            Assert.AreNotEqual(first.CurrentTime, second.CurrentTime);

            first.Restart(true);
            second.Restart(true);
            mixer.ApplyPendingStates();
            for (int i = 0; i < 4; i++) ReadBufferPeaks(output, bytes);

            Assert.AreEqual(first.CurrentTime, second.CurrentTime, "After a deferred restart, both voices must be in sync.");
            first.Dispose();
            second.Dispose();
        }
    }
}
