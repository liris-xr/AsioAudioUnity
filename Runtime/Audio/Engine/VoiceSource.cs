using System;
using System.Collections.Generic;
using NAudio.Wave;

namespace AsioAudioUnity
{
    /// <summary>
    /// The mono samples of an audio file, at the file's sample rate, with a seekable position (in frames).
    /// </summary>
    internal abstract class VoiceSource : ISampleProvider, IDisposable
    {
        public abstract WaveFormat WaveFormat { get; }
        public abstract long Position { get; set; }
        public abstract long Length { get; }
        public abstract int Read(float[] buffer, int offset, int count);
        public virtual void Dispose() { }

        public TimeSpan TotalTime => TimeSpan.FromSeconds(Length / (double)WaveFormat.SampleRate);

        /// <summary>Opens an audio file, either streamed from disk or fully decoded into memory.</summary>
        public static VoiceSource Open(string audioFilePath, bool preloadIntoMemory, out WaveFormat decodedFileWaveFormat)
        {
            AudioFileReader reader = new AudioFileReader(audioFilePath);
            decodedFileWaveFormat = reader.WaveFormat;

            if (!preloadIntoMemory) return new StreamingVoiceSource(reader);

            try
            {
                return new MemoryVoiceSource(reader);
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
        private readonly ISampleProvider _mono;
        private readonly int _blockAlign;

        public StreamingVoiceSource(AudioFileReader reader)
        {
            _reader = reader;
            _mono = reader.WaveFormat.Channels == 1 ? (ISampleProvider)reader : new MonoDownmixSampleProvider(reader);
            _blockAlign = reader.WaveFormat.BlockAlign;
        }

        public override WaveFormat WaveFormat => _mono.WaveFormat;
        public override long Length => _reader.Length / _blockAlign;
        public override long Position
        {
            get => _reader.Position / _blockAlign;
            set => _reader.Position = Math.Max(0, Math.Min(value, Length)) * _blockAlign;
        }

        public override int Read(float[] buffer, int offset, int count) => _mono.Read(buffer, offset, count);

        public override void Dispose() => _reader.Dispose();
    }

    /// <summary>Holds the whole file, decoded to mono, in memory: no disk access while playing.</summary>
    internal sealed class MemoryVoiceSource : VoiceSource
    {
        private readonly float[] _samples;
        private long _position;

        public MemoryVoiceSource(AudioFileReader reader)
        {
            ISampleProvider mono = reader.WaveFormat.Channels == 1 ? (ISampleProvider)reader : new MonoDownmixSampleProvider(reader);
            WaveFormat = mono.WaveFormat;

            // The reported length can be approximate for compressed formats, so read until the end rather than trusting it.
            List<float> samples = new List<float>((int)Math.Min(int.MaxValue, reader.Length / reader.WaveFormat.BlockAlign));
            float[] chunk = new float[WaveFormat.SampleRate];
            int read;
            while ((read = mono.Read(chunk, 0, chunk.Length)) > 0)
            {
                for (int i = 0; i < read; i++) samples.Add(chunk[i]);
            }
            _samples = samples.ToArray();
        }

        public override WaveFormat WaveFormat { get; }
        public override long Length => _samples.Length;
        public override long Position
        {
            get => _position;
            set => _position = Math.Max(0, Math.Min(value, _samples.Length));
        }

        public override int Read(float[] buffer, int offset, int count)
        {
            int available = (int)Math.Min(count, _samples.Length - _position);
            if (available <= 0) return 0;
            Array.Copy(_samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }
}
