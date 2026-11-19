using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AsioAudioUnity
{
    /// <summary>
    /// A short beep on each step's output channels in turn, mixed on top of the sources: Test Channels plays one step per ASIO Audio Source,
    /// to check where each source's channels arrive (REAPER track, Spat Revolution source). Plays once, then <see cref="IsFinished"/>.
    /// </summary>
    public sealed class AsioTestTone
    {
        private readonly int[][] _steps; // Output channels of each step, 0-based
        private readonly long _stepFrames;
        private readonly long _beepFrames;
        private readonly float _fadeFrames;
        private readonly double _phaseStep;
        private readonly float _gain;

        private long _frame; // Frames played since the start (written by the audio thread, read with Interlocked)
        private volatile bool _stopped;

        /// <param name="sampleRate">The mixer's sample rate.</param>
        /// <param name="steps">The output channels (1-based) of each step, played in this order.</param>
        /// <param name="stepSeconds">The duration of each step: the beep, then silence.</param>
        /// <param name="beepSeconds">The duration of the beep.</param>
        /// <param name="levelDecibels">The level of the beep, in dBFS.</param>
        /// <param name="frequency">The frequency of the beep, in Hz.</param>
        public AsioTestTone(int sampleRate, IEnumerable<IEnumerable<int>> steps, double stepSeconds = 1.0, double beepSeconds = 0.5, float levelDecibels = -12f, double frequency = 1000.0)
        {
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            _steps = (steps ?? Enumerable.Empty<IEnumerable<int>>())
                .Select(step => (step ?? Enumerable.Empty<int>()).Where(channel => channel >= 1).Select(channel => channel - 1).Distinct().ToArray()).ToArray();
            _stepFrames = Math.Max(1L, (long)(Math.Max(0.05, stepSeconds) * sampleRate));
            _beepFrames = Math.Min(_stepFrames, Math.Max(1L, (long)(Math.Max(0.01, beepSeconds) * sampleRate)));
            _fadeFrames = Math.Max(1f, Math.Min(0.005f * sampleRate, _beepFrames / 2f)); // 5 ms fades: no click
            _phaseStep = 2.0 * Math.PI * frequency / sampleRate;
            _gain = (float)Math.Pow(10.0, levelDecibels / 20.0);
        }

        /// <summary>The number of steps.</summary>
        public int StepCount => _steps.Length;

        /// <summary>The step playing now (0-based), or -1 once finished.</summary>
        public int CurrentStep
        {
            get
            {
                if (IsFinished) return -1;
                return (int)Math.Min(_steps.Length - 1, Interlocked.Read(ref _frame) / _stepFrames);
            }
        }

        /// <summary>Whether every step has played, or the tone was stopped.</summary>
        public bool IsFinished => _stopped || Interlocked.Read(ref _frame) >= _stepFrames * _steps.Length;

        /// <summary>Stops the tone (it can't be restarted: create a new one).</summary>
        public void Stop() => _stopped = true;

        /// <summary>[Audio thread] Adds the beep to an interleaved buffer of <paramref name="channelCount"/> channels, and moves on by <paramref name="frames"/>.</summary>
        internal void MixInto(float[] buffer, int offset, int frames, int channelCount)
        {
            if (_stopped || frames <= 0) return;
            long start = Interlocked.Read(ref _frame);
            long total = _stepFrames * _steps.Length;
            if (start >= total) return;

            for (int f = 0; f < frames; f++)
            {
                long frame = start + f;
                if (frame >= total) break;
                int step = (int)(frame / _stepFrames);
                long inStep = frame - step * _stepFrames;
                if (inStep >= _beepFrames) continue;

                float envelope = Math.Min(1f, Math.Min(inStep + 1, _beepFrames - inStep) / _fadeFrames);
                float sample = (float)Math.Sin(inStep * _phaseStep) * _gain * envelope;
                int[] channels = _steps[step];
                int frameStart = offset + f * channelCount;
                for (int c = 0; c < channels.Length; c++)
                {
                    if (channels[c] < channelCount) buffer[frameStart + channels[c]] += sample;
                }
            }
            Interlocked.Exchange(ref _frame, start + frames);
        }
    }
}
