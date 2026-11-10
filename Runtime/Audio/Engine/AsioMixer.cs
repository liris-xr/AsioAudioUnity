using System;
using System.Threading;
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

        /// <summary>Master volume (linear gain, 0 to 1) applied to all channels. Changes are ramped over one buffer.</summary>
        public float MasterVolume
        {
            get => _masterVolume;
            set => _masterVolume = Math.Max(0f, Math.Min(1f, value));
        }

        private volatile float _masterVolume = 1f;
        private long _renderedFrames; // Frames requested by the driver since the start (Interlocked)
        private float _appliedMasterVolume = -1f; // [Audio thread] -1: not applied yet (no ramp on the first buffer)
        private float _masterPeak;

        /// <summary>Highest absolute sample value sent to the driver (all channels, after the master volume) since the last call, and resets it.</summary>
        public float ConsumeMasterPeak()
        {
            return Interlocked.Exchange(ref _masterPeak, 0f);
        }

        private int _applyPendingStatesRequest; // Interlocked

        /// <summary>
        /// Applies the deferred transport changes of all voices (see AsioSourceVoice.Play(deferred)) at the start of the next buffer,
        /// so they all start, pause or stop on the same sample.
        /// </summary>
        /// <summary>How many frames the driver has asked for since the mixer was created (0: the driver isn't processing audio).</summary>
        public long RenderedFrames => Interlocked.Read(ref _renderedFrames);

        public void ApplyPendingStates()
        {
            Interlocked.Exchange(ref _applyPendingStatesRequest, 1);
        }

        /// <summary>[Audio thread] Always fills the whole buffer (silence where nothing plays).</summary>
        public int Read(float[] buffer, int offset, int count)
        {
            if (Interlocked.Exchange(ref _applyPendingStatesRequest, 0) == 1)
            {
                AsioSourceVoice[] pendingVoices = _voices;
                bool allApplied = true;
                for (int i = 0; i < pendingVoices.Length; i++) allApplied &= pendingVoices[i].ApplyPendingState();
                if (!allApplied) Interlocked.Exchange(ref _applyPendingStatesRequest, 1); // Retry the busy voices on the next buffer
            }

            // Not Array.Clear: NAudio passes a byte[] disguised as a float[] (WaveBuffer), and Array.Clear on it only clears
            // count bytes (a quarter of the samples), so the previous buffers would add up and get louder and louder.
            for (int i = offset, end = offset + count; i < end; i++) buffer[i] = 0f;

            int channelCount = WaveFormat.Channels;
            int frames = count / channelCount;
            Interlocked.Add(ref _renderedFrames, frames);

            AsioSourceVoice[] voices = _voices;
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i].MixInto(buffer, offset, frames, channelCount);
            }

            // Master volume, ramped from the previous value over the buffer, and master peak
            float target = _masterVolume;
            float start = _appliedMasterVolume < 0f ? target : _appliedMasterVolume;
            float step = frames > 0 ? (target - start) / frames : 0f;
            float peak = 0f;
            bool unity = start == 1f && target == 1f;

            for (int frame = 0; frame < frames; frame++)
            {
                float gain = start + step * (frame + 1);
                int frameStart = offset + frame * channelCount;
                for (int channel = 0; channel < channelCount; channel++)
                {
                    float sample = unity ? buffer[frameStart + channel] : buffer[frameStart + channel] * gain;
                    buffer[frameStart + channel] = sample;
                    if (sample > peak) peak = sample;
                    else if (-sample > peak) peak = -sample;
                }
            }
            _appliedMasterVolume = target;

            // Losing a value to a race with ConsumeMasterPeak is harmless for a meter.
            if (peak > Volatile.Read(ref _masterPeak)) Volatile.Write(ref _masterPeak, peak);

            return count;
        }
    }
}
