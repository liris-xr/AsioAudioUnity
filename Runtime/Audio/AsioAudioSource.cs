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
        /// In the Editor, a file inside the project is also tracked by its asset GUID, so the path is recovered if the file is moved or renamed.
        /// </summary>
        public string AudioFilePath
        {
            get { return _audioFilePath; }
            set
            {
                if (_audioFilePath == value) return;
                _audioFilePath = value;
#if UNITY_EDITOR
                _audioFileGuid = GetAssetGuid(value);
#endif
                if (IsRuntimeActive)
                {
                    Stop();
                    Revalidate(true);
                }
                if (OnAudioFilePathChanged != null)
                    OnAudioFilePathChanged.Invoke();
            }
        }

        // The asset GUID of the audio file when it is inside the project (empty otherwise): used in the Editor to find the file again
        // after it was moved or renamed (the path stays the reference: the GUID only recovers a path that no longer exists).
        // Not a reference to the asset, so the build doesn't include the file's AudioClip data on top of the copied file.
        [SerializeField][HideInInspector] private string _audioFileGuid;
        internal string AudioFileGuid => _audioFileGuid;

#if UNITY_EDITOR
        internal static string GetAssetGuid(string audioFilePath)
        {
            if (string.IsNullOrEmpty(audioFilePath) || Path.IsPathRooted(audioFilePath)) return "";
            return UnityEditor.AssetDatabase.AssetPathToGUID(audioFilePath.Replace('\\', '/')) ?? "";
        }
#endif

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

        // The manager this source plays through (AsioAudioManager.Instance, created automatically), once registered.
        private AsioAudioManager _manager;

        /// <summary>The ASIO Audio Manager this source plays through (null until the source is enabled in Play mode).</summary>
        public AsioAudioManager ReferencedAsioAudioManager => _manager;

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

        private float[] _channelMeterLevels = new float[0];
        private float[] _channelPeaks = new float[1];
        /// <summary>
        /// The output level of each channel the source plays on (peak, 0 to 1, after volume and mute), with a meter-like falloff:
        /// one value, or one per channel of the file with <see cref="PlayAllChannels"/> (Read Only).
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<float> ChannelMeterLevels => _channelMeterLevels;

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

        [SerializeField][Min(0f)] private float _loopCrossfade = 0f;
        /// <summary>
        /// Smooth loop: when looping, the end of the file is crossfaded with its start over this many seconds (at most half the file),
        /// so the loop point can't be heard; each loop then continues from this time. 0: the file restarts directly.
        /// </summary>
        public float LoopCrossfadeDuration
        {
            get { return _loopCrossfade; }
            set
            {
                _loopCrossfade = Mathf.Max(0f, value);
                if (_voice != null) _voice.LoopCrossfadeSeconds = _loopCrossfade;
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
        /// Fade-out duration of Pause and Stop, and of the end of a non-looping file, in seconds. 0: a few milliseconds, just enough to avoid clicks (and no fade at the end of the file).
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

        [SerializeField] private AsioFadeCurve _fadeInCurve = AsioFadeCurve.Squared;
        /// <summary>Shape of the fade-in (see <see cref="AsioFadeCurve"/>).</summary>
        public AsioFadeCurve FadeInCurve
        {
            get { return _fadeInCurve; }
            set
            {
                _fadeInCurve = value;
                if (_voice != null) _voice.FadeInCurve = value;
            }
        }

        [SerializeField] private AsioFadeCurve _fadeOutCurve = AsioFadeCurve.Squared;
        /// <summary>Shape of the fade-out, of Pause and Stop and at the end of a non-looping file (see <see cref="AsioFadeCurve"/>).</summary>
        public AsioFadeCurve FadeOutCurve
        {
            get { return _fadeOutCurve; }
            set
            {
                _fadeOutCurve = value;
                if (_voice != null) _voice.FadeOutCurve = value;
            }
        }

        [SerializeField] private bool _playAllChannels = false;
        /// <summary>
        /// Plays every channel of the file on consecutive output channels, starting at <see cref="TargetOutputChannel"/>
        /// (e.g. a stereo file on channels 5 and 6), instead of mixing the file down to mono on one channel (default).
        /// Changing it reloads the file, without interrupting playback.
        /// </summary>
        public bool PlayAllChannels
        {
            get { return _playAllChannels; }
            set
            {
                if (_playAllChannels == value) return;
                _playAllChannels = value;
                if (IsRuntimeActive && _voice.IsLoaded) ApplyChannelMode();
            }
        }

        /// <summary>
        /// The number of output channels this source uses: 1, or the file's channel count with <see cref="PlayAllChannels"/>
        /// (known once the file has been loaded).
        /// </summary>
        public int OutputChannelCount => !_playAllChannels ? 1 : _voice != null && _voice.IsLoaded ? _voice.ChannelCount : Mathf.Max(1, _channels);

        /// <summary>Whether this source plays on the given output channel (1-based).</summary>
        public bool UsesOutputChannel(int channel) => _targetOutputChannel > 0 && channel >= _targetOutputChannel && channel < _targetOutputChannel + OutputChannelCount;

        // Reloads the file mixed down to mono or with all its channels, and checks the output channels it now uses.
        private void ApplyChannelMode()
        {
            Revalidate(false); // Sees the change, reloads the file keeping its position, and checks the channels
            if (OnTargetOutputChannelChanged != null) OnTargetOutputChannelChanged.Invoke();
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

            if (_voice.IsLoaded && _voice.DownmixToMono == _playAllChannels)
            {
                // Play All Channels edited in the Inspector during Play mode
                ApplyChannelMode();
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
            float falloff = Mathf.Pow(0.1f, Time.unscaledDeltaTime);
            MeterLevel = Mathf.Max(_voice.ConsumePeak(), MeterLevel * falloff);

            int channels = _voice.IsLoaded ? _voice.ChannelCount : 0;
            if (_channelMeterLevels.Length != channels) _channelMeterLevels = new float[channels];
            if (_channelPeaks.Length < channels) _channelPeaks = new float[channels];
            int measured = _voice.ConsumeChannelPeaks(_channelPeaks);
            for (int c = 0; c < channels; c++) _channelMeterLevels[c] = Mathf.Max(c < measured ? _channelPeaks[c] : 0f, _channelMeterLevels[c] * falloff);
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
#if UNITY_EDITOR
            // Scenes saved before files were tracked by GUID get it here (saved with the scene the next time it is saved).
            if (string.IsNullOrEmpty(_audioFileGuid) && !string.IsNullOrEmpty(_audioFilePath)) _audioFileGuid = GetAssetGuid(_audioFilePath);
#endif
            if (_voice == null) return; // Edit mode, or not initialised yet

            _voice.Volume = Mathf.Clamp01(_volume);
            _voice.Loop = _loop;
            _voice.FadeInSeconds = _fadeInDuration;
            _voice.FadeOutSeconds = _fadeOutDuration;
            _voice.FadeInCurve = _fadeInCurve;
            _voice.FadeOutCurve = _fadeOutCurve;
            _voice.LoopCrossfadeSeconds = _loopCrossfade;

            // Revalidating the channel logs errors and changes the manager's list: deferred to Update rather than done during validation.
            if (_voice.OutputChannel != _targetOutputChannel) _outputChannelEditedInInspector = true;
        }

        /// <summary>
        /// Registers this source with its ASIO Audio Manager (or the first one in the scene), applies its settings, and loads the audio file if needed.
        /// Returns true if the source is ready to play.
        /// </summary>
        private bool Revalidate(bool reloadAudioFile)
        {
            AsioAudioManager asioAudioManager = AsioAudioManager.Instance; // created if needed
            if (!asioAudioManager)
            {
                UnityEngine.Debug.LogError("The ASIO Audio Source attached to GameObject \"" + gameObject.name + "\" can't start the ASIO Audio Manager (sources only play in Play mode or in a build). It will be ignored.");
                return false;
            }

            _manager = asioAudioManager;
            if (!asioAudioManager.RequestAddAsioAudioSource(this))
            {
                if (AudioStatus != AsioAudioStatus.Stopped) Stop();
                return false;
            }

            _voice.Volume = _volume;
            _voice.Loop = _loop;
            _voice.FadeInSeconds = _fadeInDuration;
            _voice.FadeOutSeconds = _fadeOutDuration;
            _voice.FadeInCurve = _fadeInCurve;
            _voice.FadeOutCurve = _fadeOutCurve;
            _voice.LoopCrossfadeSeconds = _loopCrossfade;
            _voice.OutputChannel = _targetOutputChannel;
            bool channelModeChanged = _voice.DownmixToMono == _playAllChannels;
            _voice.DownmixToMono = !_playAllChannels;

            if (reloadAudioFile || channelModeChanged || !_voice.IsLoaded)
            {
                if (!LoadAudioFile(channelModeChanged && !reloadAudioFile && _voice.IsLoaded)) return false;
                // With all its channels, the source's channel count is only known now that the file is loaded: check them again.
                if (_playAllChannels && !asioAudioManager.RequestAddAsioAudioSource(this))
                {
                    if (AudioStatus != AsioAudioStatus.Stopped) Stop();
                    return false;
                }
            }
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
                _voice.Load(resolvedAudioFilePath, _manager.OutputSampleRate, PreloadIntoMemory, keepPosition);
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

            if (_voice == null || !_voice.IsLoaded || _manager == null)
            {
                UnityEngine.Debug.LogError("The ASIO Audio Source attached to GameObject \"" + gameObject.name + "\" can't play: it has no valid audio file or ASIO Audio Manager (see previous errors).");
                return;
            }

            if (!_manager.IsConnected)
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
            if (_manager) _manager.RequestRemoveAsioAudioSource(this);
            _manager = null;
            _voice?.Unload();
        }

        private void OnDestroy()
        {
            _voice?.Dispose();
        }
    }
}
