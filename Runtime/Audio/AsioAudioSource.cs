using System;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Scripting.APIUpdating;
using NAudio.Wave;

namespace AsioAudioUnity
{
    /// <summary>
    /// A representation of an Audio Source that can be used to play audio files using ASIO protocol.
    /// The audio file is mixed down to mono and played on one output channel of the ASIO driver (TargetOutputChannel), through the ASIO Audio Manager.
    /// Files are resampled on the fly to the ASIO Audio Manager's target sample rate, so they can have any sample rate.
    /// </summary>
    [MovedFrom(true, sourceClassName: "CustomAsioAudioSource")] // Formerly CustomAsioAudioSource: Unity's API updater rewrites scripts that still use the old name
    public class AsioAudioSource : MonoBehaviour
    {
        private UnityEvent _onAudioFilePathChanged;
        public UnityEvent OnAudioFilePathChanged
        {
            get { return _onAudioFilePathChanged; }
            private set { _onAudioFilePathChanged = value; }
        }

        [SerializeField][ReadOnly] private string _audioFilePath;
        /// <summary>
        /// Path of the audio file to play: relative to the project folder (e.g. "Assets/Sounds/file.wav"), or absolute.
        /// Changing it at runtime stops the source and loads the new file.
        /// </summary>
        public string AudioFilePath
        {
            get { return _audioFilePath; }
            set
            {
                if (_audioFilePath == value) return;
                _audioFilePath = value;
                if (IsRuntimeActive)
                {
                    Stop();
                    Revalidate(true);
                }
                if (OnAudioFilePathChanged != null)
                    OnAudioFilePathChanged.Invoke();
            }
        }

        /// <summary>
        /// The full path of the audio file. Relative paths are resolved from the project folder in the Editor, and from the player's folder in builds.
        /// </summary>
        public string ResolvedAudioFilePath
        {
            get
            {
                if (string.IsNullOrEmpty(AudioFilePath)) return AudioFilePath;
                // In the Editor, dataPath is "<project>/Assets"; in a Windows player, it's "<build>/<name>_Data": the parent is the base folder in both cases.
                return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), AudioFilePath));
            }
        }

        private UnityEvent _onTargetOutputChannelChanged;
        public UnityEvent OnTargetOutputChannelChanged
        {
            get { return _onTargetOutputChannelChanged; }
            private set { _onTargetOutputChannelChanged = value; }
        }

        [SerializeField] private int _targetOutputChannel = 0;
        /// <summary>
        /// The ASIO output channel this source plays on (1-based, unique among sources). Can be changed while playing.
        /// </summary>
        public int TargetOutputChannel
        {
            get { return _targetOutputChannel; }
            set
            {
                if (_targetOutputChannel == value) return;
                _targetOutputChannel = value;
                if (IsRuntimeActive) Revalidate(false);
                if (OnTargetOutputChannelChanged != null)
                    OnTargetOutputChannelChanged.Invoke();
            }
        }

        [SerializeField] private AsioAudioManager _referencedAsioAudioManager;
        public AsioAudioManager ReferencedAsioAudioManager
        {
            get { return _referencedAsioAudioManager; }
            set { _referencedAsioAudioManager = value; }
        }

        private UnityEvent _onVolumeChanged;
        public UnityEvent OnVolumeChanged
        {
            get { return _onVolumeChanged; }
            private set { _onVolumeChanged = value; }
        }

        [SerializeField][Range(0f, 1f)] private float _volume = 1f;
        /// <summary>
        /// Playback volume, from 0 to 1: the linear gain applied to the samples (0.5 is -6 dB, not half as loud; see VolumeDecibels).
        /// Applied live (smoothly) while playing.
        /// </summary>
        public float Volume
        {
            get { return _volume; }
            set
            {
                value = Mathf.Clamp01(value);
                if (_volume == value) return;
                _volume = value;
                if (_voice != null) _voice.Volume = value;
                if (OnVolumeChanged != null)
                    OnVolumeChanged.Invoke();
            }
        }

        /// <summary>
        /// Playback volume in decibels (0 dB: original level, -6 dB: half the amplitude, about -10 dB: half as loud). Negative infinity is silence.
        /// Same setting as Volume, which is linear.
        /// </summary>
        public float VolumeDecibels
        {
            get { return AsioVolume.ToDecibels(Volume); }
            set { Volume = AsioVolume.ToLinear(value); }
        }

        [SerializeField] private bool _mute = false;
        /// <summary>
        /// Silences this source (smoothly) without stopping it or changing its volume.
        /// </summary>
        public bool Mute
        {
            get { return _mute; }
            set { _mute = value; }
        }

        [SerializeField] private bool _solo = false;
        /// <summary>
        /// When at least one source is soloed, only soloed sources are heard.
        /// </summary>
        public bool Solo
        {
            get { return _solo; }
            set { _solo = value; }
        }

        /// <summary>
        /// Output level of this source (peak, 0 to 1, after volume and mute), with a meter-like falloff. Updated every frame while playing.
        /// </summary>
        public float MeterLevel { get; private set; }

        [SerializeField] private bool _preloadIntoMemory = false;
        /// <summary>
        /// Decode the whole audio file into memory when it is loaded, instead of reading it from disk while playing.
        /// Avoids dropouts with many sources or slow disks, at the cost of memory (about 10 MB per minute of audio at 44.1 kHz) and a longer load.
        /// Changing it at runtime reloads the file, keeping the playback position.
        /// </summary>
        public bool PreloadIntoMemory
        {
            get { return _preloadIntoMemory; }
            set
            {
                if (_preloadIntoMemory == value) return;
                _preloadIntoMemory = value;
                if (IsRuntimeActive && _voice.IsLoaded) ReloadKeepingPosition();
            }
        }

        [SerializeField] private bool _playOnEnable = true;
        public bool PlayOnEnable
        {
            get { return _playOnEnable; }
            set { _playOnEnable = value; }
        }

        [SerializeField] private bool _loop = false;
        /// <summary>
        /// Whether the audio file restarts (seamlessly) when it reaches the end. Can be changed while playing.
        /// </summary>
        public bool Loop
        {
            get { return _loop; }
            set
            {
                _loop = value;
                if (_voice != null) _voice.Loop = value;
            }
        }

        [SerializeField][Min(0f)] private float _fadeInDuration = 0f;
        /// <summary>
        /// Fade-in duration of Play (and of resuming after a pause), in seconds. 0: a few milliseconds, just enough to avoid clicks.
        /// </summary>
        public float FadeInDuration
        {
            get { return _fadeInDuration; }
            set
            {
                _fadeInDuration = Mathf.Max(0f, value);
                if (_voice != null) _voice.FadeInSeconds = _fadeInDuration;
            }
        }

        [SerializeField][Min(0f)] private float _fadeOutDuration = 0f;
        /// <summary>
        /// Fade-out duration of Pause and Stop, in seconds. 0: a few milliseconds, just enough to avoid clicks.
        /// The status changes immediately; the sound fades out afterwards.
        /// </summary>
        public float FadeOutDuration
        {
            get { return _fadeOutDuration; }
            set
            {
                _fadeOutDuration = Mathf.Max(0f, value);
                if (_voice != null) _voice.FadeOutSeconds = _fadeOutDuration;
            }
        }

        // Format of the audio file (as stored on disk for WAV files, as decoded for other formats)

        [SerializeField][ReadOnly] private int _channels;
        public int Channels
        {
            get { return _channels; }
            private set { _channels = value; }
        }

        [SerializeField][ReadOnly] private int _sampleRate;
        public int SampleRate
        {
            get { return _sampleRate; }
            private set { _sampleRate = value; }
        }

        [SerializeField][ReadOnly] private int _averageBytesPerSecond;
        public int AverageBytesPerSecond
        {
            get { return _averageBytesPerSecond; }
            private set { _averageBytesPerSecond = value; }
        }

        [SerializeField][ReadOnly] private int _blockAlign;
        public int BlockAlign
        {
            get { return _blockAlign; }
            private set { _blockAlign = value; }
        }

        [SerializeField][ReadOnly] private int _bitsPerSample;
        public int BitsPerSample
        {
            get { return _bitsPerSample; }
            private set { _bitsPerSample = value; }
        }

        [SerializeField][ReadOnly] private int _extraSize;
        public int ExtraSize
        {
            get { return _extraSize; }
            private set { _extraSize = value; }
        }

        [SerializeField][ReadOnly] private double _audioFileTotalLength;
        /// <summary>
        /// Length of the audio file, in seconds.
        /// </summary>
        public double AudioFileTotalLength
        {
            get { return _audioFileTotalLength; }
            private set { _audioFileTotalLength = value; }
        }

        [SerializeField][ReadOnly] private AsioAudioStatus _audioStatus = AsioAudioStatus.Stopped;
        public AsioAudioStatus AudioStatus
        {
            get { return _audioStatus; }
            private set { _audioStatus = value; }
        }

        [SerializeField][ReadOnly] private double _currentTimestamp = 0;
        /// <summary>
        /// Current playback position, in seconds, as actually played by the ASIO driver.
        /// </summary>
        public double CurrentTimestamp
        {
            get { return _currentTimestamp; }
            private set { _currentTimestamp = value; }
        }

        [SerializeField] private UnityEvent _onPlay;
        public UnityEvent OnPlay
        {
            get { return _onPlay; }
            set { _onPlay = value; }
        }

        [SerializeField] private UnityEvent _onStop;
        /// <summary>
        /// Triggered when playback stops: when Stop is called, or when a non-looping file reaches its end.
        /// </summary>
        public UnityEvent OnStop
        {
            get { return _onStop; }
            set { _onStop = value; }
        }

        [SerializeField] private UnityEvent _onPause;
        public UnityEvent OnPause
        {
            get { return _onPause; }
            set { _onPause = value; }
        }

        // The real-time playback state, read by the ASIO Audio Manager's mixer on the audio thread.
        private AsioSourceVoice _voice;
        internal AsioSourceVoice Voice => _voice;

        private bool _outputChannelEditedInInspector;

        private bool IsRuntimeActive => Application.isPlaying && isActiveAndEnabled && _voice != null;

        private void Awake()
        {
            OnAudioFilePathChanged ??= new UnityEvent();
            OnTargetOutputChannelChanged ??= new UnityEvent();
            OnVolumeChanged ??= new UnityEvent();

            // These events are serialized: only create them if missing, otherwise the listeners set in the Inspector would be wiped.
            OnPlay ??= new UnityEvent();
            OnStop ??= new UnityEvent();
            OnPause ??= new UnityEvent();

            _voice = new AsioSourceVoice();
        }

        private void OnEnable()
        {
            if (Revalidate(false) && PlayOnEnable) Play();
        }

        private void Update()
        {
            if (_voice == null) return;

            if (_voice.IsLoaded && _voice.IsPreloaded != _preloadIntoMemory)
            {
                // Edited in the Inspector during Play mode
                ReloadKeepingPosition();
            }

            if (_outputChannelEditedInInspector)
            {
                _outputChannelEditedInInspector = false;
                Revalidate(false);
                if (OnTargetOutputChannelChanged != null)
                    OnTargetOutputChannelChanged.Invoke();
            }

            if (_voice.ConsumeEndReached())
            {
                // A non-looping file reached its end: the voice has already stopped and rewound.
                AudioStatus = AsioAudioStatus.Stopped;
                CurrentTimestamp = 0;
                OnStop.Invoke();
            }
            else if (AudioStatus != AsioAudioStatus.Stopped)
            {
                CurrentTimestamp = _voice.CurrentTime;
            }

            // Falls by 20 dB per second
            MeterLevel = Mathf.Max(_voice.ConsumePeak(), MeterLevel * Mathf.Pow(0.1f, Time.unscaledDeltaTime));
        }

        /// <summary>
        /// Called by the ASIO Audio Manager every frame, from this source's Mute and the Solo state of all sources.
        /// </summary>
        internal void SetEffectiveMute(bool muted)
        {
            if (_voice != null) _voice.Muted = muted;
        }

        // Called when a value is edited in the Inspector (which writes the fields directly, bypassing the property setters).
        private void OnValidate()
        {
            if (_voice == null) return; // Edit mode, or not initialised yet

            _voice.Volume = Mathf.Clamp01(_volume);
            _voice.Loop = _loop;
            _voice.FadeInSeconds = _fadeInDuration;
            _voice.FadeOutSeconds = _fadeOutDuration;

            // Revalidating the channel logs errors and changes the manager's list: deferred to Update rather than done during validation.
            if (_voice.OutputChannel != _targetOutputChannel) _outputChannelEditedInInspector = true;
        }

        /// <summary>
        /// Registers this source with its ASIO Audio Manager (or the first one in the scene), applies its settings, and loads the audio file if needed.
        /// Returns true if the source is ready to play.
        /// </summary>
        private bool Revalidate(bool reloadAudioFile)
        {
            AsioAudioManager asioAudioManager = ReferencedAsioAudioManager ? ReferencedAsioAudioManager : FindFirstObjectByType<AsioAudioManager>();
            if (!asioAudioManager)
            {
                UnityEngine.Debug.LogError("The ASIO Audio Source attached to GameObject \"" + gameObject.name + "\" can't find any ASIO Audio Manager in the scene. It will be ignored.");
                return false;
            }

            if (!asioAudioManager.RequestAddAsioAudioSource(this))
            {
                if (AudioStatus != AsioAudioStatus.Stopped) Stop();
                return false;
            }

            _voice.Volume = _volume;
            _voice.Loop = _loop;
            _voice.FadeInSeconds = _fadeInDuration;
            _voice.FadeOutSeconds = _fadeOutDuration;
            _voice.OutputChannel = _targetOutputChannel;

            if (reloadAudioFile || !_voice.IsLoaded) return LoadAudioFile();
            return true;
        }

        private bool LoadAudioFile(bool keepPosition = false)
        {
            string resolvedAudioFilePath = ResolvedAudioFilePath;

            if (string.IsNullOrEmpty(resolvedAudioFilePath) || !File.Exists(resolvedAudioFilePath))
            {
                UnityEngine.Debug.LogError("The ASIO Audio Source attached to GameObject \"" + gameObject.name + "\" has an invalid Audio File Name specified (file not found: \"" + resolvedAudioFilePath + "\"). It will be ignored.");
                _voice.Unload();
                return false;
            }

            try
            {
                _voice.Load(resolvedAudioFilePath, ReferencedAsioAudioManager.TargetSampleRate, PreloadIntoMemory, keepPosition);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("The ASIO Audio Source attached to GameObject \"" + gameObject.name + "\" could not read its audio file \"" + resolvedAudioFilePath + "\": " + e.GetBaseException().Message);
                _voice.Unload();
                return false;
            }

            WaveFormat fileWaveFormat = ReadNativeWaveFormat(resolvedAudioFilePath) ?? _voice.DecodedWaveFormat;
            Channels = fileWaveFormat.Channels;
            SampleRate = fileWaveFormat.SampleRate;
            AverageBytesPerSecond = fileWaveFormat.AverageBytesPerSecond;
            BlockAlign = fileWaveFormat.BlockAlign;
            BitsPerSample = fileWaveFormat.BitsPerSample;
            ExtraSize = fileWaveFormat.ExtraSize;
            AudioFileTotalLength = _voice.TotalTime.TotalSeconds;
            CurrentTimestamp = keepPosition ? _voice.CurrentTime : 0;

            return true;
        }

        // Reloads the audio file (e.g. after switching PreloadIntoMemory) without interrupting playback: same position, same state.
        private void ReloadKeepingPosition()
        {
            if (!LoadAudioFile(true) && AudioStatus != AsioAudioStatus.Stopped) Stop();
        }

        /// <summary>
        /// Returns the format stored in a WAV file (its real bit depth, which the decoder doesn't expose), or null for other file types.
        /// </summary>
        private static WaveFormat ReadNativeWaveFormat(string audioFilePath)
        {
            if (!string.Equals(Path.GetExtension(audioFilePath), ".wav", StringComparison.OrdinalIgnoreCase)) return null;

            try
            {
                using (WaveFileReader waveFileReader = new WaveFileReader(audioFilePath)) return waveFileReader.WaveFormat;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Starts (or resumes) playback, and triggers OnPlay.
        /// </summary>
        public void Play()
        {
            Play(false);
        }

        // Deferred: the voice starts when the ASIO Audio Manager applies the deferred changes (together with other sources).
        internal void Play(bool deferred)
        {
            if (AudioStatus == AsioAudioStatus.Playing) return;

            if (_voice == null || !_voice.IsLoaded || ReferencedAsioAudioManager == null)
            {
                UnityEngine.Debug.LogError("The ASIO Audio Source attached to GameObject \"" + gameObject.name + "\" can't play: it has no valid audio file or ASIO Audio Manager (see previous errors).");
                return;
            }

            if (!ReferencedAsioAudioManager.IsConnected)
            {
                UnityEngine.Debug.LogWarning("The ASIO Audio Source attached to GameObject \"" + gameObject.name + "\" is playing, but the ASIO driver is not connected: it will be heard once the ASIO Audio Manager connects.");
            }

            _voice.Play(deferred);
            AudioStatus = AsioAudioStatus.Playing;
            OnPlay.Invoke();
        }

        /// <summary>
        /// Stops playback and goes back to the start of the file, and triggers OnStop.
        /// </summary>
        public void Stop()
        {
            Stop(false);
        }

        internal void Stop(bool deferred)
        {
            if (AudioStatus == AsioAudioStatus.Stopped) return;

            _voice?.Stop(deferred);
            CurrentTimestamp = 0;
            AudioStatus = AsioAudioStatus.Stopped;
            OnStop.Invoke();
        }

        /// <summary>
        /// Pauses playback (keeping the current position), and triggers OnPause.
        /// </summary>
        public void Pause()
        {
            Pause(false);
        }

        internal void Pause(bool deferred)
        {
            if (AudioStatus != AsioAudioStatus.Playing) return;

            _voice.Pause(deferred);
            AudioStatus = AsioAudioStatus.Paused;
            OnPause.Invoke();
        }

        /// <summary>
        /// Restarts playback from the beginning of the file. (This doesn't trigger events.)
        /// </summary>
        public void Restart()
        {
            Restart(false);
        }

        internal void Restart(bool deferred)
        {
            if (_voice == null || !_voice.IsLoaded) return;

            _voice.Restart(deferred);
            CurrentTimestamp = 0;
            AudioStatus = AsioAudioStatus.Playing;
        }

        /// <summary>
        /// Moves the playback position, in seconds (clamped to the file's length). Works while playing, paused or stopped.
        /// </summary>
        public void Seek(double seconds)
        {
            if (_voice == null || !_voice.IsLoaded) return;

            _voice.Seek(seconds);
            CurrentTimestamp = _voice.CurrentTime;
        }

        private void OnDisable()
        {
            Stop();
            if (ReferencedAsioAudioManager) ReferencedAsioAudioManager.RequestRemoveAsioAudioSource(this);
            _voice?.Unload();
        }

        private void OnDestroy()
        {
            _voice?.Dispose();
        }
    }
}
