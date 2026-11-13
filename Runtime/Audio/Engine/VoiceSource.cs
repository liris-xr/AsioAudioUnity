using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace AsioAudioUnity
{
    /// <summary>
    /// The samples of an audio file at the file's sample rate: mixed down to mono, or with all the file's channels (interleaved).
    /// Position and Length are in frames (one sample per channel).
    /// </summary>
    internal abstract class VoiceSource : ISampleProvider, IDisposable
    {
        public abstract WaveFormat WaveFormat { get; }
        public int Channels => WaveFormat.Channels;
        public abstract long Position { get; set; }
        public abstract long Length { get; }
        /// <summary>Reads interleaved samples (count is in samples, a multiple of <see cref="Channels"/>).</summary>
        public abstract int Read(float[] buffer, int offset, int count);
        public virtual void Dispose() { }

        /// <summary>
        /// Returns the first frames of the file (interleaved samples, at the file's sample rate), without moving the playback position:
        /// used to crossfade the end of the file with its start when looping. Can be called from any thread.
        /// </summary>
        public abstract float[] ReadHead(int frames);

        public TimeSpan TotalTime => TimeSpan.FromSeconds(Length / (double)WaveFormat.SampleRate);

        protected static float[] ReadAll(ISampleProvider provider, int samples)
        {
            float[] head = new float[Math.Max(0, samples)];
            int total = 0, read;
            while (total < head.Length && (read = provider.Read(head, total, head.Length - total)) > 0) total += read;
            if (total < head.Length) Array.Resize(ref head, total - total % provider.WaveFormat.Channels);
            return head;
        }

        protected static ISampleProvider Prepare(AudioFileReader reader, bool downmixToMono)
        {
            return downmixToMono && reader.WaveFormat.Channels > 1 ? new MonoDownmixSampleProvider(reader) : (ISampleProvider)reader;
        }

        /// <summary>Opens an audio file, either streamed from disk or fully decoded into memory, mixed down to mono or with all its channels.</summary>
        public static VoiceSource Open(string audioFilePath, bool preloadIntoMemory, bool downmixToMono, out WaveFormat decodedFileWaveFormat)
        {
            AudioFileReader reader = new AudioFileReader(audioFilePath);
            decodedFileWaveFormat = reader.WaveFormat;

            if (!preloadIntoMemory) return new StreamingVoiceSource(reader, audioFilePath, downmixToMono);

            try
            {
                return new MemoryVoiceSource(reader, downmixToMono);
            }
            finally
            {
                reader.Dispose();
            }
        }
    }

    /// <summary>Reads the file from disk while playing (low memory use).</summary>
    internal sealed class StreamingVoiceSource : VoiceSource
    {
        private readonly AudioFileReader _reader;
        private readonly ISampleProvider _output;
        private readonly int _blockAlign;
        private readonly string _path;
        private readonly bool _downmixToMono;

        public StreamingVoiceSource(AudioFileReader reader, string path, bool downmixToMono)
        {
            _reader = reader;
            _path = path;
            _downmixToMono = downmixToMono;
            _output = Prepare(reader, downmixToMono);
            _blockAlign = reader.WaveFormat.BlockAlign;
        }

        public override WaveFormat WaveFormat => _output.WaveFormat;
        public override long Length => _reader.Length / _blockAlign;
        public override long Position
        {
            get => _reader.Position / _blockAlign;
            set => _reader.Position = Math.Max(0, Math.Min(value, Length)) * _blockAlign;
        }

        public override int Read(float[] buffer, int offset, int count) => _output.Read(buffer, offset, count);

        // Decoded from a second reader, so the playing one isn't disturbed (the same decoder gives the same samples).
        public override float[] ReadHead(int frames)
        {
            using (AudioFileReader reader = new AudioFileReader(_path))
            {
                ISampleProvider provider = Prepare(reader, _downmixToMono);
                return ReadAll(provider, frames * provider.WaveFormat.Channels);
            }
        }

        public override void Dispose() => _reader.Dispose();
    }

    /// <summary>Holds the whole file, decoded, in memory: no disk access while playing.</summary>
    internal sealed class MemoryVoiceSource : VoiceSource
    {
        private readonly float[] _samples; // Interleaved
        private readonly int _channels;
        private long _position; // In frames

        public MemoryVoiceSource(AudioFileReader reader, bool downmixToMono)
        {
            ISampleProvider provider = Prepare(reader, downmixToMono);
            WaveFormat = provider.WaveFormat;
            _channels = WaveFormat.Channels;

            // The reported length can be approximate for compressed formats, so read until the end rather than trusting it.
            List<float> samples = new List<float>((int)Math.Min(int.MaxValue, reader.Length / reader.WaveFormat.BlockAlign * _channels));
            float[] chunk = new float[WaveFormat.SampleRate * _channels];
            int read;
            while ((read = provider.Read(chunk, 0, chunk.Length)) > 0)
            {
                for (int i = 0; i < read; i++) samples.Add(chunk[i]);
            }
            if (samples.Count % _channels != 0) samples.RemoveRange(samples.Count - samples.Count % _channels, samples.Count % _channels);
            _samples = samples.ToArray();
        }

        public override WaveFormat WaveFormat { get; }
        public override long Length => _samples.Length / _channels;
        public override long Position
        {
            get => _position;
            set => _position = Math.Max(0, Math.Min(value, Length));
        }

        public override int Read(float[] buffer, int offset, int count)
        {
            long availableFrames = Math.Min(count / _channels, Length - _position);
            if (availableFrames <= 0) return 0;
            int samples = (int)availableFrames * _channels;
            Array.Copy(_samples, _position * _channels, buffer, offset, samples);
            _position += availableFrames;
            return samples;
        }

        public override float[] ReadHead(int frames)
        {
            float[] head = new float[Math.Max(0, Math.Min(frames, Length)) * _channels];
            Array.Copy(_samples, head, head.Length);
            return head;
        }
    }
}
