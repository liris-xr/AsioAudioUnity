using System;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AsioAudioUnity
{
    /// <summary>
    /// The real-time playback state of one Custom ASIO Audio Source: an audio file, read as mono samples at the output sample rate,
    /// with its transport state (playing / paused / stopped), position, volume, mute, loop and output channel.
    ///
    /// Play, pause, stop and mute fade in or out over a few milliseconds, and volume changes are ramped, to avoid clicks.
    ///
    /// Threading: control methods are called from the Unity main thread; <see cref="MixInto"/> is called from the ASIO driver thread.
    /// The audio thread never blocks: if the main thread holds the lock (loading or seeking), the voice is skipped for one buffer.
    /// </summary>
    internal sealed class AsioSourceVoice : IDisposable
    {
        private const int StateStopped = 0;
        private const int StatePlaying = 1;
        private const int StatePaused = 2;

        private const double FadeDurationSeconds = 0.005;

        private readonly object _sync = new object();

        // Guarded by _sync
        private VoiceSource _source; // Mono samples at the file's sample rate
        private ISampleProvider _monoOutput; // Mono samples at the output sample rate, read from _source
        private float[] _scratchBuffer = new float[0];
        private float _appliedVolume; // Last volume applied by the audio thread, used to ramp volume changes smoothly
        private float _transportGain; // Fade-in / fade-out gain of play, pause and stop (0 to 1)
        private WaveFormat _decodedFileWaveFormat;

        private volatile int _state = StateStopped;
        private volatile int _outputSampleRate = 48000;
        private volatile float _volume = 1f;
        private volatile bool _muted;
        private volatile bool _loop;
        private volatile int _outputChannel; // 1-based
        private long _framesPlayed; // At output sample rate, accessed through Interlocked
        private int _endReached; // Set to 1 by the audio thread when playback reaches the end without looping
        private int _rewindPending; // Set to 1 by Stop: the audio thread rewinds once the fade-out is done
        private float _peak; // Highest absolute output sample since last read by the main thread (for level meters)

        // Lock-free (these are polled every frame: taking the lock could make the audio thread skip a buffer).
        public bool IsLoaded => Volatile.Read(ref _source) != null;
        public bool IsPreloaded => Volatile.Read(ref _source) is MemoryVoiceSource;

        public float Volume { get => _volume; set => _volume = value; }
        /// <summary>Silences the voice (smoothly) without changing its volume or stopping playback.</summary>
        public bool Muted { get => _muted; set => _muted = value; }
        public bool Loop { get => _loop; set => _loop = value; }
        public int OutputChannel { get => _outputChannel; set => _outputChannel = value; }

        /// <summary>The format the file is decoded to (always 32-bit float, at the file's sample rate and channel count).</summary>
        public WaveFormat DecodedWaveFormat { get { lock (_sync) return _decodedFileWaveFormat; } }
        public TimeSpan TotalTime { get { lock (_sync) return _source?.TotalTime ?? TimeSpan.Zero; } }

        /// <summary>Current playback position, in seconds.</summary>
        public double CurrentTime => Interlocked.Read(ref _framesPlayed) / (double)_outputSampleRate;

        /// <summary>
        /// Opens an audio file and prepares it for playback at the given output sample rate.
        /// With preloadIntoMemory, the whole file is decoded now (this can take a moment for long files), and never read from disk while playing.
        /// With keepPosition (reloading the same file), playback continues seamlessly from where the previous file was when the swap happens;
        /// otherwise the position is reset to the start.
        /// Throws if the file cannot be read.
        /// </summary>
        public void Load(string audioFilePath, int outputSampleRate, bool preloadIntoMemory, bool keepPosition = false)
        {
            // Open (and possibly decode) the file outside the lock: disk access must not stall the audio thread.
            VoiceSource newSource = VoiceSource.Open(audioFilePath, preloadIntoMemory, out WaveFormat decodedFileWaveFormat);
            ISampleProvider newMonoOutput = BuildMonoOutput(newSource, outputSampleRate);

            VoiceSource oldSource;
            lock (_sync)
            {
                oldSource = _source;
                keepPosition &= oldSource != null;

                // The previous file kept playing while the new one was opened: take its position now, at the swap.
                double position = keepPosition ? CurrentTime : 0;
                newSource.Position = (long)(position * newSource.WaveFormat.SampleRate);

                _source = newSource;
                _monoOutput = newMonoOutput;
                _decodedFileWaveFormat = decodedFileWaveFormat;
                _outputSampleRate = outputSampleRate;
                Interlocked.Exchange(ref _framesPlayed, (long)(position * outputSampleRate));

                if (!keepPosition)
                {
                    _appliedVolume = _volume;
                    _transportGain = 0f;
                    Interlocked.Exchange(ref _rewindPending, 0);
                }
            }
            oldSource?.Dispose();
        }

        /// <summary>
        /// Changes the output sample rate (when the ASIO Audio Manager's target sample rate changes), keeping the current position.
        /// </summary>
        public void SetOutputSampleRate(int outputSampleRate)
        {
            lock (_sync)
            {
                if (outputSampleRate == _outputSampleRate) return;

                double currentTime = CurrentTime;
                _outputSampleRate = outputSampleRate;
                if (_source != null) _monoOutput = BuildMonoOutput(_source, outputSampleRate);
                Interlocked.Exchange(ref _framesPlayed, (long)(currentTime * outputSampleRate));
            }
        }

        /// <summary>Closes the audio file.</summary>
        public void Unload()
        {
            VoiceSource oldSource;
            lock (_sync)
            {
                oldSource = _source;
                _source = null;
                _monoOutput = null;
                _decodedFileWaveFormat = null;
                _state = StateStopped;
                _transportGain = 0f;
                Interlocked.Exchange(ref _rewindPending, 0);
                Interlocked.Exchange(ref _framesPlayed, 0);
            }
            oldSource?.Dispose();
        }

        public void Play()
        {
            // A Stop whose fade-out was never rendered (driver not running): rewind now.
            if (Interlocked.Exchange(ref _rewindPending, 0) == 1) RewindLocked(true);

            Interlocked.Exchange(ref _endReached, 0);
            _state = StatePlaying;
        }

        public void Pause()
        {
            _state = StatePaused;
        }

        /// <summary>Stops playback (fading out) and goes back to the start of the file.</summary>
        public void Stop()
        {
            // Rewinding is left to the audio thread, after the fade-out: rewinding now would make the fade-out play the start of the file.
            Interlocked.Exchange(ref _rewindPending, 1);
            _state = StateStopped;
        }

        /// <summary>Moves the playback position, in seconds (clamped to the file's length).</summary>
        public void Seek(double seconds)
        {
            Interlocked.Exchange(ref _rewindPending, 0);
            lock (_sync)
            {
                if (_source == null) return;

                seconds = Math.Max(0, Math.Min(seconds, _source.TotalTime.TotalSeconds));
                _source.Position = (long)(seconds * _source.WaveFormat.SampleRate);
                Interlocked.Exchange(ref _framesPlayed, (long)(seconds * _outputSampleRate));
            }
        }

        /// <summary>
        /// Returns true once after playback has reached the end of a non-looping file (the voice is then stopped and rewound).
        /// </summary>
        public bool ConsumeEndReached()
        {
            return Interlocked.Exchange(ref _endReached, 0) == 1;
        }

        /// <summary>
        /// Returns the highest absolute sample value played since the last call (0 to 1, after volume and mute), and resets it.
        /// </summary>
        public float ConsumePeak()
        {
            return Interlocked.Exchange(ref _peak, 0f);
        }

        /// <summary>
        /// [Audio thread] Adds this voice's next samples to its output channel in an interleaved buffer.
        /// </summary>
        public void MixInto(float[] buffer, int offset, int frames, int channelCount)
        {
            bool shouldPlay = _state == StatePlaying;

            // Nothing to do when not playing, unless a fade-out is still in progress (racy read, only the audio thread writes it).
            if (!shouldPlay && _transportGain <= 0f && Volatile.Read(ref _rewindPending) == 0) return;

            int channelIndex = _outputChannel - 1;
            bool validChannel = channelIndex >= 0 && channelIndex < channelCount;

            // Never block the audio thread: if the main thread is loading or seeking, skip this buffer.
            if (!Monitor.TryEnter(_sync)) return;
            try
            {
                if (_monoOutput == null) return;

                int fadeFrames = Math.Max(1, (int)(_outputSampleRate * FadeDurationSeconds));
                float gainStep = 1f / fadeFrames;

                // When fading out, only render what the fade needs.
                int framesToRender = shouldPlay ? frames : Math.Min(frames, (int)Math.Ceiling(_transportGain / gainStep));
                int framesRead = framesToRender > 0 ? ReadWithLoop(framesToRender) : 0;

                // Ramp from the previous volume to the current one over the buffer (muting goes through the same ramp).
                float startVolume = _appliedVolume;
                float targetVolume = _muted ? 0f : _volume;
                float volumeStep = (targetVolume - startVolume) / frames;
                float transportGain = _transportGain;
                float peak = 0f;

                for (int frame = 0; frame < framesRead; frame++)
                {
                    transportGain = shouldPlay ? Math.Min(1f, transportGain + gainStep) : Math.Max(0f, transportGain - gainStep);
                    float sample = _scratchBuffer[frame] * (startVolume + volumeStep * (frame + 1)) * transportGain;

                    if (validChannel) buffer[offset + frame * channelCount + channelIndex] += sample;
                    if (sample > peak) peak = sample;
                    else if (-sample > peak) peak = -sample;
                }
                _appliedVolume = targetVolume;
                _transportGain = shouldPlay ? transportGain : 0f;

                // Losing a value to a race with ConsumePeak is harmless for a meter.
                if (peak > Volatile.Read(ref _peak)) Volatile.Write(ref _peak, peak);

                if (shouldPlay && framesRead < framesToRender)
                {
                    // End of a non-looping file: stop, rewind, and let the main thread know.
                    _state = StateStopped;
                    _transportGain = 0f;
                    RewindLocked(false);
                    Interlocked.Exchange(ref _endReached, 1);
                }
                else if (!shouldPlay && Interlocked.Exchange(ref _rewindPending, 0) == 1)
                {
                    // Fade-out of a Stop is done.
                    RewindLocked(false);
                }
            }
            finally
            {
                Monitor.Exit(_sync);
            }
        }

        // [Audio thread, under _sync] Fills the scratch buffer, wrapping around to the start of the file when looping.
        private int ReadWithLoop(int frames)
        {
            if (_scratchBuffer.Length < frames) _scratchBuffer = new float[frames];

            int totalRead = 0;
            bool justRewound = false;

            while (totalRead < frames)
            {
                int read = _monoOutput.Read(_scratchBuffer, totalRead, frames - totalRead);
                if (read > 0)
                {
                    totalRead += read;
                    Interlocked.Add(ref _framesPlayed, read);
                    justRewound = false;
                    continue;
                }

                // End of file. Stop there, unless looping (and the file is not empty: nothing was read right after rewinding).
                if (!_loop || justRewound) break;

                // The resampler is kept as is, so the loop point stays seamless.
                RewindLocked(false);
                justRewound = true;
            }

            return totalRead;
        }

        private void RewindLocked(bool takeLock)
        {
            if (takeLock) Monitor.Enter(_sync);
            try
            {
                if (_source != null) _source.Position = 0;
                Interlocked.Exchange(ref _framesPlayed, 0);
            }
            finally
            {
                if (takeLock) Monitor.Exit(_sync);
            }
        }

        private static ISampleProvider BuildMonoOutput(VoiceSource source, int outputSampleRate)
        {
            return source.WaveFormat.SampleRate == outputSampleRate ? source : new WdlResamplingSampleProvider(source, outputSampleRate);
        }

        public void Dispose()
        {
            Unload();
        }
    }
}
