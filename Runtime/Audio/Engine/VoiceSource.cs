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

        /// <summary>
        /// Returns the first frames of the file (mono, at the file's sample rate), without moving the playback position:
        /// used to crossfade the end of the file with its start when looping. Can be called from any thread.
        /// </summary>
        public abstract float[] ReadHead(int frames);

        protected static float[] ReadAll(ISampleProvider mono, int frames)
        {
            float[] head = new float[Math.Max(0, frames)];
            int total = 0, read;
            while (total < head.Length && (read = mono.Read(head, total, head.Length - total)) > 0) total += read;
            if (total < head.Length) Array.Resize(ref head, total);
            return head;
        }

        public TimeSpan TotalTime => TimeSpan.FromSeconds(Length / (double)WaveFormat.SampleRate);

        /// <summary>Opens an audio file, either streamed from disk or fully decoded into memory.</summary>
        public static VoiceSource Open(string audioFilePath, bool preloadIntoMemory, out WaveFormat decodedFileWaveFormat)
        {
            AudioFileReader reader = new AudioFileReader(audioFilePath);
            decodedFileWaveFormat = reader.WaveFormat;

            if (!preloadIntoMemory) return new StreamingVoiceSource(reader, audioFilePath);

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
        private readonly string _path;

        public StreamingVoiceSource(AudioFileReader reader, string path)
        {
            _reader = reader;
            _path = path;
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

        // Decoded from a second reader, so the playing one isn't disturbed (the same decoder gives the same samples).
        public override float[] ReadHead(int frames)
        {
            using (AudioFileReader reader = new AudioFileReader(_path))
            {
                ISampleProvider mono = reader.WaveFormat.Channels == 1 ? (ISampleProvider)reader : new MonoDownmixSampleProvider(reader);
                return ReadAll(mono, frames);
            }
        }

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

        public override float[] ReadHead(int frames)
        {
            float[] head = new float[Math.Max(0, Math.Min(frames, _samples.Length))];
            Array.Copy(_samples, head, head.Length);
            return head;
        }
    }
}
