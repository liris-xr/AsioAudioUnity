using System;
using NAudio.Wave;

namespace AsioAudioUnity
{
    /// <summary>
    /// Mixes all source voices into one interleaved multichannel stream (one channel per ASIO output), read continuously by the ASIO driver.
    /// Voices can be added and removed while playing.
    /// </summary>
    internal sealed class AsioMixer : ISampleProvider
    {
        private readonly object _voicesLock = new object();

        // Replaced (never modified) when voices are added or removed, so the audio thread can iterate it without locking.
        private volatile AsioSourceVoice[] _voices = Array.Empty<AsioSourceVoice>();

        public WaveFormat WaveFormat { get; }

        public AsioMixer(int sampleRate, int channelCount)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount);
        }

        public void AddVoice(AsioSourceVoice voice)
        {
            lock (_voicesLock)
            {
                if (Array.IndexOf(_voices, voice) >= 0) return;

                AsioSourceVoice[] newVoices = new AsioSourceVoice[_voices.Length + 1];
                _voices.CopyTo(newVoices, 0);
                newVoices[_voices.Length] = voice;
                _voices = newVoices;
            }
        }

        public void RemoveVoice(AsioSourceVoice voice)
        {
            lock (_voicesLock)
            {
                int index = Array.IndexOf(_voices, voice);
                if (index < 0) return;

                AsioSourceVoice[] newVoices = new AsioSourceVoice[_voices.Length - 1];
                Array.Copy(_voices, 0, newVoices, 0, index);
                Array.Copy(_voices, index + 1, newVoices, index, _voices.Length - index - 1);
                _voices = newVoices;
            }
        }

        /// <summary>[Audio thread] Always fills the whole buffer (silence where nothing plays).</summary>
        public int Read(float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);

            int channelCount = WaveFormat.Channels;
            int frames = count / channelCount;

            AsioSourceVoice[] voices = _voices;
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i].MixInto(buffer, offset, frames, channelCount);
            }

            return count;
        }
    }
}
