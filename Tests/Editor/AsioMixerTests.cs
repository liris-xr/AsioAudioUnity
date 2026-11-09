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
