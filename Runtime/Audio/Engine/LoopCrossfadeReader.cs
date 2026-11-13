using System;
using NAudio.Wave;

namespace AsioAudioUnity
{
    /// <summary>
    /// Reads a voice's file (at the file's sample rate, mono or with all its channels, interleaved) and, when the voice loops with a crossfade,
    /// mixes the end of the file with its start: over the last <c>C</c> frames, the end fades out while the first <c>C</c> frames of the file fade in
    /// (equal-power), then playback continues from frame <c>C</c>. The loop point is hidden inside the crossfade.
    /// The start of the file comes from a cached copy (the head), so streamed files don't need a second read position.
    /// Read is called by the audio thread only (under the voice's lock), before resampling.
    /// </summary>
    internal sealed class LoopCrossfadeReader : ISampleProvider
    {
        private readonly AsioSourceVoice _voice;
        private readonly int _channels;
        private volatile float[] _head = new float[0]; // Interleaved
        private long _wrappedFrames; // [Audio thread] Frames skipped back by the crossfade loops since the last ConsumeWrappedFrames

        public LoopCrossfadeReader(VoiceSource source, AsioSourceVoice voice)
        {
            Source = source;
            _voice = voice;
            _channels = source.Channels;
        }

        public VoiceSource Source { get; }
        public WaveFormat WaveFormat => Source.WaveFormat;
        /// <summary>The number of frames of the file's start that are cached.</summary>
        public int HeadLength => _head.Length / _channels;

        /// <summary>Replaces the cached start of the file (any thread).</summary>
        public void SetHead(float[] head) => _head = head ?? new float[0];

        /// <summary>[Audio thread] Returns how many frames (at the file's sample rate) playback jumped back since the last call, and resets it.</summary>
        public long ConsumeWrappedFrames()
        {
            long wrapped = _wrappedFrames;
            _wrappedFrames = 0;
            return wrapped;
        }

        /// <summary>The crossfade length in frames for this file: 0 when not looping, at most half the file and what the head holds.</summary>
        private int GetCrossfadeFrames(long length, float[] head)
        {
            if (!_voice.Loop) return 0;
            long frames = (long)(_voice.LoopCrossfadeSeconds * (double)WaveFormat.SampleRate);
            frames = Math.Min(frames, Math.Min(length / 2, head.Length / _channels));
            return (int)Math.Max(0, frames);
        }

        /// <summary>Reads interleaved samples (count in samples, a multiple of the channel count).</summary>
        public int Read(float[] buffer, int offset, int count)
        {
            int channels = _channels;
            int framesWanted = count / channels;
            int total = 0; // In frames
            bool lastWrapWasEmpty = false;

            while (total < framesWanted)
            {
                float[] head = _head;
                long length = Source.Length;
                int crossfade = GetCrossfadeFrames(length, head);
                if (crossfade <= 0)
                {
                    // No crossfade: plain read (the voice rewinds at the end of the file if it loops)
                    total += Source.Read(buffer, offset + total * channels, (framesWanted - total) * channels) / channels;
                    break;
                }

                long position = Source.Position;
                long crossfadeStart = length - crossfade;

                if (position < crossfadeStart)
                {
                    int read = Source.Read(buffer, offset + total * channels, (int)Math.Min(framesWanted - total, crossfadeStart - position) * channels) / channels;
                    if (read <= 0) break; // The file ended before its reported length: the voice loops it without a crossfade
                    total += read;
                    continue;
                }

                // In the crossfade: the end of the file fades out, its start fades in (equal power: constant loudness for most material)
                int toRead = (int)Math.Min(framesWanted - total, Math.Max(0, length - position));
                int got = toRead > 0 ? Source.Read(buffer, offset + total * channels, toRead * channels) / channels : 0;
                for (int i = 0; i < got; i++)
                {
                    long headFrame = position + i - crossfadeStart;
                    double angle = (headFrame + 0.5) / crossfade * (Math.PI / 2);
                    float fadeOut = (float)Math.Cos(angle), fadeIn = (float)Math.Sin(angle);
                    int index = offset + (total + i) * channels;
                    long headIndex = headFrame * channels;
                    for (int c = 0; c < channels; c++)
                    {
                        buffer[index + c] = buffer[index + c] * fadeOut + head[headIndex + c] * fadeIn;
                    }
                }
                total += got;

                if (got < toRead || position + got >= length)
                {
                    // End of the crossfade: continue where the head's fade-in stopped (frame C, or less if the file ended early)
                    long resume = position + got - crossfadeStart;
                    Source.Position = resume;
                    _wrappedFrames += position + got - resume;

                    if (got == 0)
                    {
                        if (lastWrapWasEmpty) break; // Nothing to read anywhere: give up for this buffer
                        lastWrapWasEmpty = true;
                    }
                }
            }

            return total * channels;
        }
    }
}
