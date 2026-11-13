using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace AsioAudioUnity
{
    /// <summary>
    /// Runs ASIO playback: keeps the ASIO driver connected and continuously plays the mix of all the ASIO Audio Sources
    /// (each one on its own output channel). Playing, pausing or stopping a source doesn't interrupt the driver.
    ///
    /// There is no need to add it to a scene: it is created automatically (AsioAudioManager.Instance) when the first source needs it,
    /// and kept across scene loads, so the driver stays connected. Its settings are the project's ASIO Audio Settings
    /// (Project Settings > ASIO Audio).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("")] // Created automatically: not in the Add Component menu
    public class AsioAudioManager : MonoBehaviour
    {
        // ---------- Instance ----------

        private static AsioAudioManager _instance;

        /// <summary>
        /// The ASIO Audio Manager. In Play mode, it is created if needed (and kept across scenes). Null in Edit mode.
        /// </summary>
        public static AsioAudioManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                if (!Application.isPlaying) return null;

                // A scene from before version 3.0 may still contain a manager (not migrated yet): use it.
                AsioAudioManager sceneManager = FindFirstObjectByType<AsioAudioManager>();
                if (sceneManager != null)
                {
                    _instance = sceneManager;
                    return _instance;
                }

                GameObject managerObject = new GameObject("ASIO Audio Manager");
                DontDestroyOnLoad(managerObject);
                _instance = managerObject.AddComponent<AsioAudioManager>(); // Awake and OnEnable run now: the driver connects
                return _instance;
            }
        }

        /// <summary>The ASIO Audio Manager if it exists, without creating it (null in Edit mode, or before any source played).</summary>
        public static AsioAudioManager Existing => _instance;

        // ---------- Settings ----------

        // The settings of a scene manager from before version 3.0 (hidden). Migrated to the ASIO Audio Settings asset with
        // AsioSettingsMigration (button in Project Settings > ASIO Audio, or in the manager's Inspector); used at runtime only while the project has no settings asset.
        [SerializeField, HideInInspector] private string _asioDriverName;
        [SerializeField, HideInInspector] private int _targetSampleRate = 48000;
        [SerializeField, HideInInspector] private BitsPerSample _targetBitsPerSample = BitsPerSample.Bits32;
        [SerializeField, HideInInspector] private float _masterVolume = 1f;
        [SerializeField, HideInInspector] private bool _displayInfoOnGameWindow = true;
        [SerializeField, HideInInspector] private KeyCode _overlayToggleKey = KeyCode.F9;
        [SerializeField, HideInInspector] private float _overlayScale = 1f;
        [SerializeField, HideInInspector] private List<AsioMixerSnapshot> _snapshots = new List<AsioMixerSnapshot>();

        /// <summary>Whether this is a manager saved in a scene (from before version 3.0), whose settings can be migrated to Project Settings.</summary>
        public bool HasLegacySceneSettings => !string.IsNullOrEmpty(_asioDriverName);

        internal string LegacyDriverName => _asioDriverName;
        internal int LegacySampleRate => _targetSampleRate;
        internal BitsPerSample LegacyBitsPerSample => _targetBitsPerSample;
        internal float LegacyMasterVolume => _masterVolume;
        internal bool LegacyDisplayInfoOnGameWindow => _displayInfoOnGameWindow;
        internal KeyCode LegacyOverlayToggleKey => _overlayToggleKey;
        internal float LegacyOverlayScale => _overlayScale;
        internal List<AsioMixerSnapshot> LegacySnapshots => _snapshots;

        private AsioAudioSettings _legacySettings;

        /// <summary>The settings in use: the project's ASIO Audio Settings.</summary>
        public AsioAudioSettings Settings
        {
            get
            {
                if (AsioAudioSettings.HasSettingsAsset || !HasLegacySceneSettings) return AsioAudioSettings.Instance;

                // Not migrated yet: run with the scene's settings.
                if (_legacySettings == null)
                {
                    _legacySettings = ScriptableObject.CreateInstance<AsioAudioSettings>();
                    _legacySettings.hideFlags = HideFlags.DontSave;
                    _legacySettings.AsioDriverName = _asioDriverName;
                    _legacySettings.TargetSampleRate = _targetSampleRate;
                    _legacySettings.TargetBitsPerSample = _targetBitsPerSample;
                    _legacySettings.MasterVolume = _masterVolume;
                    _legacySettings.DisplayInfoOnGameWindow = _displayInfoOnGameWindow;
                    _legacySettings.OverlayToggleKey = _overlayToggleKey;
                    _legacySettings.OverlayScale = _overlayScale;
                    Debug.LogWarning("ASIO Audio Manager: the scene's ASIO Audio Manager settings are used, because the project has no ASIO Audio Settings yet. "
                        + "Migrate them from Project Settings > ASIO Audio, or with the button in this component's Inspector.", this);
                }
                return _legacySettings;
            }
        }

        public UnityEvent OnAsioDriverNameChanged { get; } = new UnityEvent();
        public UnityEvent OnTargetSampleRateChanged { get; } = new UnityEvent();
        public UnityEvent OnTargetBitsPerSampleChanged { get; } = new UnityEvent();

        /// <summary>The name of the ASIO driver to play on (ASIO Audio Settings). Changing it reconnects.</summary>
        public string AsioDriverName
        {
            get { return Settings.AsioDriverName; }
            set
            {
                if (Settings.AsioDriverName == value) return;
                Settings.AsioDriverName = value;
                ApplyConfigurationChange();
                OnAsioDriverNameChanged.Invoke();
            }
        }

        /// <summary>The sample rate the ASIO driver runs at (ASIO Audio Settings). Changing it reconnects.</summary>
        public int TargetSampleRate
        {
            get { return Settings.TargetSampleRate; }
            set
            {
                if (Settings.TargetSampleRate == value) return;
                Settings.TargetSampleRate = value;
                ApplyConfigurationChange();
                OnTargetSampleRateChanged.Invoke();
            }
        }

        /// <summary>The sample format sent to NAudio (ASIO Audio Settings). Changing it reconnects.</summary>
        public BitsPerSample TargetBitsPerSample
        {
            get { return Settings.TargetBitsPerSample; }
            set
            {
                if (Settings.TargetBitsPerSample == value) return;
                Settings.TargetBitsPerSample = value;
                ApplyConfigurationChange();
                OnTargetBitsPerSampleChanged.Invoke();
            }
        }

        /// <summary>Volume applied to all sources, after their own volume (linear gain, 0 to 1; ASIO Audio Settings). Applied live and smoothly.</summary>
        public float MasterVolume
        {
            get { return Settings.MasterVolume; }
            set
            {
                Settings.MasterVolume = value;
                if (_mixer != null) _mixer.MasterVolume = Settings.MasterVolume;
            }
        }

        /// <summary>The master volume in decibels (0 dB: unchanged; negative infinity: silence).</summary>
        public float MasterVolumeDecibels
        {
            get { return AsioVolume.ToDecibels(MasterVolume); }
            set { MasterVolume = AsioVolume.ToLinear(value); }
        }

        /// <summary>Whether the in-game overlay is shown (ASIO Audio Settings). Toggled at runtime with OverlayToggleKey.</summary>
        public bool DisplayInfoOnGameWindow
        {
            get { return Settings.DisplayInfoOnGameWindow; }
            set { Settings.DisplayInfoOnGameWindow = value; }
        }

        /// <summary>The key that shows or hides the in-game overlay (ASIO Audio Settings).</summary>
        public KeyCode OverlayToggleKey
        {
            get { return Settings.OverlayToggleKey; }
            set { Settings.OverlayToggleKey = value; }
        }

        /// <summary>The size of the in-game overlay (ASIO Audio Settings).</summary>
        public float OverlayScale
        {
            get { return Settings.OverlayScale; }
            set { Settings.OverlayScale = value; }
        }

        /// <summary>The saved mixer snapshots (ASIO Audio Settings).</summary>
        public List<AsioMixerSnapshot> Snapshots => Settings.Snapshots;

        // ---------- Driver state ----------

        public int AsioDriverInputChannelCount { get; private set; }

        /// <summary>Number of output channels exposed by the ASIO driver. Each ASIO Audio Source targets one of these.</summary>
        public int AsioDriverOutputChannelCount { get; private set; }

        /// <summary>The sample format the ASIO driver works with, e.g. "24-bit integer" (empty until connected).</summary>
        public string AsioDriverSampleFormat { get; private set; } = "";

        private List<string> _asioDriverOutputChannelNames = new List<string>();
        /// <summary>Names of the ASIO driver's output channels (e.g. "ReaRoute 3"); index 0 is channel 1. Empty until connected.</summary>
        public IReadOnlyList<string> AsioDriverOutputChannelNames => _asioDriverOutputChannelNames;

        /// <summary>The driver's name of an output channel (1-based), or an empty string if unknown.</summary>
        public string GetOutputChannelName(int channel)
        {
            return channel >= 1 && channel <= _asioDriverOutputChannelNames.Count ? _asioDriverOutputChannelNames[channel - 1] : "";
        }

        /// <summary>Output level sent to the ASIO driver (peak of all channels, 0 to 1, after the master volume), with a meter-like falloff.</summary>
        public float MasterMeterLevel { get; private set; }

        /// <summary>
        /// How many frames the ASIO driver has asked for since it was connected. Stays at 0 when the driver is connected but doesn't process audio
        /// (with ReaRoute: REAPER isn't running, or its audio device is closed).
        /// </summary>
        public long RenderedFrames => _mixer?.RenderedFrames ?? 0;

        private float _connectedAt;
        private bool _notProcessingWarned;

        /// <summary>The ASIO Audio Sources playing through this manager.</summary>
        public List<AsioAudioSource> AsioAudioSources { get; } = new List<AsioAudioSource>();

        public AsioOut AsioOutPlayer { get; private set; }

        /// <summary>True when the ASIO driver is connected and playing.</summary>
        public bool IsConnected => AsioOutPlayer != null;

        /// <summary>
        /// Whether the sources play through Unity's own audio output, because no ASIO driver could be used and the settings allow the fallback
        /// (all channels mixed together, on every speaker).
        /// </summary>
        public bool IsUsingUnityAudio => _unityAudioOutput != null;

        /// <summary>The sample rate the sources are mixed at: the driver's (<see cref="TargetSampleRate"/>), or Unity's output rate with the Unity audio fallback.</summary>
        public int OutputSampleRate => _mixer != null ? _mixer.WaveFormat.SampleRate : TargetSampleRate;
        private AsioUnityAudioOutput _unityAudioOutput;

        private AsioAudioManagerOverlay _overlay;

        // Mixes all sources into one stream for the driver. Only exists while connected.
        private AsioMixer _mixer;

        // The configuration of the last connection attempt, to detect changes (Project Settings, overlay...).
        private string _attemptedDriverName;
        private int _attemptedSampleRate;
        private BitsPerSample _attemptedBitsPerSample;

        // ---------- Lifecycle ----------

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // Another manager already runs (e.g. kept from a previous scene): this one (an old scene manager) is not needed.
                Destroy(this);
                return;
            }
            _instance = this;
        }

        private void OnEnable()
        {
            if (_instance == this) Connect();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged += OnEditorPauseStateChanged;
#endif
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_legacySettings != null) Destroy(_legacySettings);
        }

        private void Update()
        {
            if (_attemptedDriverName != AsioDriverName || _attemptedSampleRate != TargetSampleRate || _attemptedBitsPerSample != TargetBitsPerSample)
            {
                Reconnect();
            }

            ApplyMuteAndSolo();

            if (_mixer != null)
            {
                _mixer.MasterVolume = MasterVolume; // Also picks up edits made in Project Settings or the ASIO Audio Mixer
                // Falls by 20 dB per second
                MasterMeterLevel = Mathf.Max(_mixer.ConsumeMasterPeak(), MasterMeterLevel * Mathf.Pow(0.1f, Time.unscaledDeltaTime));

                if (!_notProcessingWarned && _mixer.RenderedFrames == 0 && Time.realtimeSinceStartup - _connectedAt > 2f)
                {
                    _notProcessingWarned = true;
                    if (IsUsingUnityAudio) Debug.LogWarning("ASIO Audio Manager: Unity's audio output isn't playing, so nothing can be heard. Check that Unity's audio is enabled (Project Settings > Audio > Disable Unity Audio is off).");
                    else Debug.LogWarning("ASIO Audio Manager: the ASIO driver \"" + AsioDriverName + "\" is connected but isn't asking for audio, so nothing can be heard. "
                        + "With ReaRoute, REAPER must be running with its audio device open (check that REAPER isn't set to close the audio device when inactive or stopped, "
                        + "in Preferences > Audio). With another driver, check that no other application holds it.");
                }
            }
            else
            {
                MasterMeterLevel = 0f;
            }
        }

        // When any source is soloed, every source that isn't soloed is muted.
        private void ApplyMuteAndSolo()
        {
            bool anySolo = false;
            foreach (AsioAudioSource asioAudioSource in AsioAudioSources)
            {
                if (asioAudioSource && asioAudioSource.Solo) { anySolo = true; break; }
            }

            foreach (AsioAudioSource asioAudioSource in AsioAudioSources)
            {
                if (asioAudioSource) asioAudioSource.SetEffectiveMute(asioAudioSource.Mute || (anySolo && !asioAudioSource.Solo));
            }
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged -= OnEditorPauseStateChanged;
            _sourcesPausedByEditor.Clear();
#endif
            Disconnect();
        }

#if UNITY_EDITOR
        // The sources that were playing when the editor was paused, to resume exactly those.
        private readonly List<AsioAudioSource> _sourcesPausedByEditor = new List<AsioAudioSource>();

        // The ASIO audio runs on its own thread: the editor's Pause button doesn't stop it, so the playing sources are paused (and resumed) here.
        private void OnEditorPauseStateChanged(UnityEditor.PauseState state)
        {
            if (_instance != this || !Application.isPlaying) return;

            if (state == UnityEditor.PauseState.Paused)
            {
                if (Settings == null || !Settings.PauseWithEditor) return;
                _sourcesPausedByEditor.Clear();
                _sourcesPausedByEditor.AddRange(AsioAudioSources.Where(source => source && source.AudioStatus == AsioAudioStatus.Playing));
                if (_sourcesPausedByEditor.Count == 0) return;

                ForSourcesInSync(_sourcesPausedByEditor, (source, deferred) => source.Pause(deferred));
                TransportCommandIssued?.Invoke(AsioTransportCommand.Pause);
            }
            else
            {
                AsioAudioSource[] toResume = _sourcesPausedByEditor.Where(source => source && source.AudioStatus == AsioAudioStatus.Paused).ToArray();
                _sourcesPausedByEditor.Clear();
                if (toResume.Length == 0) return;

                ForSourcesInSync(toResume, (source, deferred) => source.Play(deferred));
                TransportCommandIssued?.Invoke(AsioTransportCommand.Play);
            }
        }
#endif

        private void OnApplicationQuit()
        {
            Disconnect();
        }

        private void ApplyConfigurationChange()
        {
            if (Application.isPlaying && isActiveAndEnabled) Reconnect();
        }

        /// <summary>Disconnects from the ASIO driver and connects again with the current settings. Sources keep their state and position.</summary>
        public void Reconnect()
        {
            Disconnect();
            Connect();
        }

        // ---------- Connection ----------

        /// <summary>
        /// Connects to the ASIO driver set in AsioDriverName, and starts playing the mix of all registered sources.
        /// Errors are logged, and the manager stays disconnected.
        /// </summary>
        // Connects to the ASIO driver, or, if it can't and the settings allow it, plays through Unity's audio instead.
        private bool Connect()
        {
            if (IsConnected) return true;
            if (ConnectAsio()) return true;
            if (Settings != null && Settings.FallBackToUnityAudio) StartUnityAudioFallback();
            return false;
        }

        // A connection problem: an error, or a warning when the sources will play through Unity's audio instead.
        private void ReportConnectionProblem(string message)
        {
            if (Settings != null && Settings.FallBackToUnityAudio) Debug.LogWarning(message + " The sources play through Unity's audio output instead (all channels mixed together).");
            else Debug.LogError(message);
        }

        private void StartUnityAudioFallback()
        {
            int channels = Mathf.Max(2, AsioAudioSources.Where(source => source).Select(source => source.TargetOutputChannel + source.OutputChannelCount - 1).DefaultIfEmpty(0).Max());
            channels = Mathf.Max(channels, 64); // Room for sources added later on high channels (all channels are mixed together anyway)
            int sampleRate = AudioSettings.outputSampleRate;
            if (FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == 0)
            {
                Debug.LogWarning("ASIO Audio Manager: the scene has no Audio Listener, so Unity's audio output (used instead of the ASIO driver) won't be heard. Add one, e.g. on the camera.");
            }

            AsioMixer mixer = new AsioMixer(sampleRate, channels) { MasterVolume = MasterVolume };
            foreach (AsioAudioSource asioAudioSource in AsioAudioSources)
            {
                if (asioAudioSource == null || asioAudioSource.Voice == null) continue;
                asioAudioSource.Voice.SetOutputSampleRate(sampleRate);
                mixer.AddVoice(asioAudioSource.Voice);
            }

            _mixer = mixer;
            _unityAudioOutput = AsioUnityAudioOutput.Create(transform, mixer);
            _connectedAt = Time.realtimeSinceStartup;
            _notProcessingWarned = false;
        }

        private bool ConnectAsio()
        {

            _attemptedDriverName = AsioDriverName;
            _attemptedSampleRate = TargetSampleRate;
            _attemptedBitsPerSample = TargetBitsPerSample;

            if (string.IsNullOrEmpty(AsioDriverName))
            {
                ReportConnectionProblem("ASIO Audio Manager: no ASIO driver is set: choose one in Project Settings > ASIO Audio. Available drivers: " + GetAvailableDriverNamesText() + ".");
                return false;
            }

            if (!AsioOut.GetDriverNames().Contains(AsioDriverName))
            {
                ReportConnectionProblem("ASIO Audio Manager: the ASIO driver \"" + AsioDriverName + "\" was not found on the system. Available drivers: " + GetAvailableDriverNamesText() + ".");
                return false;
            }

            AsioOut asioOut = null;
            try
            {
                asioOut = new AsioOut(AsioDriverName);
                AsioDriverInputChannelCount = asioOut.DriverInputChannelCount;
                AsioDriverOutputChannelCount = asioOut.DriverOutputChannelCount;

                if (AsioDriverOutputChannelCount <= 0) throw new Exception("the driver has no output channels.");

                _asioDriverOutputChannelNames = new List<string>();
                for (int channel = 0; channel < AsioDriverOutputChannelCount; channel++)
                {
                    string channelName;
                    try { channelName = asioOut.AsioOutputChannelName(channel); }
                    catch (Exception) { channelName = ""; }
                    _asioDriverOutputChannelNames.Add(channelName ?? "");
                }

                // Use all the driver's outputs, so any source can target any channel without reconnecting.
                AsioMixer mixer = new AsioMixer(TargetSampleRate, AsioDriverOutputChannelCount) { MasterVolume = MasterVolume };
                foreach (AsioAudioSource asioAudioSource in AsioAudioSources)
                {
                    if (asioAudioSource == null || asioAudioSource.Voice == null) continue;
                    asioAudioSource.Voice.SetOutputSampleRate(TargetSampleRate);
                    mixer.AddVoice(asioAudioSource.Voice);
                }

                // NAudio converts the stream to the driver's own sample format (e.g. 24-bit integer).
                // It can't convert 16-bit integers to 24-bit integer or 32-bit float drivers: send 32-bit float instead.
                AsioSampleType? driverSampleType = GetDriverOutputSampleType(asioOut);
                AsioDriverSampleFormat = DescribeSampleType(driverSampleType);
                bool use16Bits = TargetBitsPerSample == BitsPerSample.Bits16;
                if (use16Bits && driverSampleType.HasValue && driverSampleType != AsioSampleType.Int16LSB && driverSampleType != AsioSampleType.Int32LSB)
                {
                    Debug.LogWarning("ASIO Audio Manager: the ASIO driver \"" + AsioDriverName + "\" uses " + AsioDriverSampleFormat + " samples, which can't be fed with 16-bit integers. 32-bit float is sent instead.");
                    use16Bits = false;
                }

                IWaveProvider output = use16Bits ? mixer.ToWaveProvider16() : mixer.ToWaveProvider();
                asioOut.Init(output);
                asioOut.PlaybackStopped += OnAsioPlaybackStopped;
                asioOut.Play();

                _mixer = mixer;
                AsioOutPlayer = asioOut;
                _connectedAt = Time.realtimeSinceStartup;
                _notProcessingWarned = false;
                Debug.Log("ASIO Audio Manager: connected to \"" + AsioDriverName + "\" (" + AsioDriverOutputChannelCount + " outputs, " + TargetSampleRate + " Hz"
                    + (string.IsNullOrEmpty(AsioDriverSampleFormat) ? "" : ", driver format: " + AsioDriverSampleFormat) + ").");

#if UNITY_EDITOR
                // Remembered so the editor can show them outside Play mode (keys read by AsioEditorUtility).
                UnityEditor.EditorPrefs.SetInt("AsioAudioUnity.OutputChannelCount." + AsioDriverName, AsioDriverOutputChannelCount);
                if (!string.IsNullOrEmpty(AsioDriverSampleFormat)) UnityEditor.EditorPrefs.SetString("AsioAudioUnity.DriverSampleFormat." + AsioDriverName, AsioDriverSampleFormat);
                UnityEditor.EditorPrefs.SetString("AsioAudioUnity.OutputChannelNames." + AsioDriverName, string.Join("\n", _asioDriverOutputChannelNames));
#endif
                return true;
            }
            catch (Exception e)
            {
                ReportConnectionProblem("ASIO Audio Manager: could not start the ASIO driver \"" + AsioDriverName + "\" at " + TargetSampleRate + " Hz, " + (int)TargetBitsPerSample + " bits: " + e.GetBaseException().Message + ".");
                asioOut?.Dispose();
                return false;
            }
        }

        // [Audio thread] NAudio stops the driver when the audio callback throws: without this, the sources would just go silent.
        private void OnAsioPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null) Debug.LogError("ASIO Audio Manager: the ASIO driver stopped because of an error in the audio callback: " + e.Exception);
        }

        private void Disconnect()
        {
            if (_unityAudioOutput != null)
            {
                _unityAudioOutput.Close();
                _unityAudioOutput = null;
            }
            if (AsioOutPlayer != null)
            {
                AsioOutPlayer.PlaybackStopped -= OnAsioPlaybackStopped;
                AsioOutPlayer.Stop();
                AsioOutPlayer.Dispose();
                AsioOutPlayer = null;
            }
            _mixer = null;
        }

        private static readonly FieldInfo AsioOutDriverField = typeof(AsioOut).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);

        // The sample format of the driver's first output channel (NAudio doesn't expose it publicly). Null if it can't be read.
        internal static AsioSampleType? GetDriverOutputSampleType(AsioOut asioOut)
        {
            try
            {
                AsioChannelInfo[] outputChannels = (AsioOutDriverField?.GetValue(asioOut) as AsioDriverExt)?.Capabilities.OutputChannelInfos;
                if (outputChannels != null && outputChannels.Length > 0) return outputChannels[0].type;
            }
            catch (Exception) { }
            return null;
        }

        internal static string DescribeSampleType(AsioSampleType? sampleType)
        {
            switch (sampleType)
            {
                case null: return "";
                case AsioSampleType.Int16LSB: return "16-bit integer";
                case AsioSampleType.Int24LSB: return "24-bit integer";
                case AsioSampleType.Int32LSB: return "32-bit integer";
                case AsioSampleType.Int32LSB24: return "24-bit integer (in 32 bits)";
                case AsioSampleType.Float32LSB: return "32-bit float";
                case AsioSampleType.Float64LSB: return "64-bit float";
                default: return sampleType.Value.ToString();
            }
        }

        private static string GetAvailableDriverNamesText()
        {
            string[] driverNames = AsioOut.GetDriverNames();
            return driverNames.Length == 0 ? "(none)" : "\"" + string.Join("\", \"", driverNames) + "\"";
        }

        // ---------- Sources ----------

        /// <summary>
        /// Check if the ASIO Audio Source is valid, returns true if added to the list of ASIO Audio Sources, false otherwise.
        /// </summary>
        public bool RequestAddAsioAudioSource(AsioAudioSource asioAudioSource)
        {
            if (string.IsNullOrEmpty(asioAudioSource.AudioFilePath))
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + asioAudioSource.gameObject.name + "\" doesn't have any Audio File Name specified. It will be ignored.");
            }
            else if (asioAudioSource.TargetOutputChannel <= 0)
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + asioAudioSource.gameObject.name + "\" doesn't have any Target Output Channel specified. It will be ignored.");
            }
            else if (IsConnected && asioAudioSource.TargetOutputChannel > AsioDriverOutputChannelCount)
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + asioAudioSource.gameObject.name + "\" targets output channel " + asioAudioSource.TargetOutputChannel + ", but the ASIO driver \"" + AsioDriverName + "\" only has " + AsioDriverOutputChannelCount + " output channels. It will be ignored.");
            }
            else if (AsioAudioSources.Any(otherAsioAudioSource => otherAsioAudioSource != asioAudioSource && otherAsioAudioSource && SharesOutputChannel(otherAsioAudioSource, asioAudioSource)))
            {
                Debug.LogError("The targeted output channel of the ASIO Audio Source attached to GameObject \"" + asioAudioSource.gameObject.name + "\" is already used by another ASIO Audio Source, and needs to be unique. It will be ignored.");
            }
            else
            {
                int lastChannel = asioAudioSource.TargetOutputChannel + asioAudioSource.OutputChannelCount - 1;
                if (IsConnected && lastChannel > AsioDriverOutputChannelCount)
                {
                    Debug.LogWarning("The ASIO Audio Source attached to GameObject \"" + asioAudioSource.gameObject.name + "\" plays " + asioAudioSource.OutputChannelCount + " channels from output channel "
                        + asioAudioSource.TargetOutputChannel + ", but the ASIO driver \"" + AsioDriverName + "\" only has " + AsioDriverOutputChannelCount + " output channels: the channels beyond are not played.");
                }
                if (!AsioAudioSources.Contains(asioAudioSource)) AsioAudioSources.Add(asioAudioSource);
                if (_mixer != null && asioAudioSource.Voice != null) _mixer.AddVoice(asioAudioSource.Voice);
                return true;
            }

            RemoveFromMix(asioAudioSource);
            return false;
        }

        /// <summary>Whether two sources use a common output channel (a source with Play All Channels uses several consecutive channels).</summary>
        public static bool SharesOutputChannel(AsioAudioSource a, AsioAudioSource b)
        {
            if (a == null || b == null || a.TargetOutputChannel <= 0 || b.TargetOutputChannel <= 0) return false;
            return a.TargetOutputChannel < b.TargetOutputChannel + b.OutputChannelCount && b.TargetOutputChannel < a.TargetOutputChannel + a.OutputChannelCount;
        }

        /// <summary>Remove an ASIO Audio Source from the list of ASIO Audio Sources, returns true if removed, false otherwise.</summary>
        public bool RequestRemoveAsioAudioSource(AsioAudioSource asioAudioSource)
        {
            if (!AsioAudioSources.Contains(asioAudioSource)) return false;
            RemoveFromMix(asioAudioSource);
            return true;
        }

        private void RemoveFromMix(AsioAudioSource asioAudioSource)
        {
            AsioAudioSources.Remove(asioAudioSource);
            if (_mixer != null && asioAudioSource.Voice != null) _mixer.RemoveVoice(asioAudioSource.Voice);
        }

        // ---------- Snapshots ----------

        private static AsioAudioSource[] SourcesInLoadedScenes() => FindObjectsByType<AsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        /// <summary>
        /// Saves the current mix (master volume, and volume / mute / solo of every source in the loaded scenes, by output channel)
        /// as a snapshot of the ASIO Audio Settings. A snapshot with the same name is replaced. Returns the snapshot.
        /// </summary>
        public AsioMixerSnapshot SaveSnapshot(string snapshotName)
        {
            return Settings.SaveSnapshot(snapshotName, SourcesInLoadedScenes());
        }

        /// <summary>Applies a saved snapshot (volume changes are smoothed while playing). Returns false if there is no snapshot with this name.</summary>
        public bool RecallSnapshot(string snapshotName)
        {
            AsioMixerSnapshot snapshot = Snapshots.Find(other => other.Name == snapshotName);
            if (snapshot == null) return false;
            Settings.ApplySnapshot(snapshot, SourcesInLoadedScenes());
            return true;
        }

        /// <summary>Applies the snapshot at an index of Snapshots. Returns false if the index is out of range.</summary>
        public bool RecallSnapshot(int index)
        {
            if (index < 0 || index >= Snapshots.Count) return false;
            Settings.ApplySnapshot(Snapshots[index], SourcesInLoadedScenes());
            return true;
        }

        /// <summary>Deletes a snapshot. Returns false if there is no snapshot with this name.</summary>
        public bool DeleteSnapshot(string snapshotName)
        {
            return Snapshots.RemoveAll(other => other.Name == snapshotName) > 0;
        }

        // ---------- Transport of all sources ----------

        /// <summary>
        /// Plays (or resumes) all the sources. They start on exactly the same sample,
        /// so files that belong together (e.g. the stems of a multichannel mix) stay in sync.
        /// </summary>
        public void PlayAll()
        {
            ForAllSourcesInSync((source, deferred) => source.Play(deferred));
            TransportCommandIssued?.Invoke(AsioTransportCommand.Play);
        }

        /// <summary>Pauses all the playing sources, on the same sample (so they stay in sync when resumed).</summary>
        public void PauseAll()
        {
            ForAllSourcesInSync((source, deferred) => source.Pause(deferred));
            TransportCommandIssued?.Invoke(AsioTransportCommand.Pause);
        }

        /// <summary>Stops all the sources (they go back to the start of their file), on the same sample.</summary>
        public void StopAll()
        {
            ForAllSourcesInSync((source, deferred) => source.Stop(deferred));
            TransportCommandIssued?.Invoke(AsioTransportCommand.Stop);
        }

        /// <summary>Plays all the sources from the start of their file, starting on the same sample. (This doesn't trigger events.)</summary>
        public void RestartAll()
        {
            ForAllSourcesInSync((source, deferred) => source.Restart(deferred));
            TransportCommandIssued?.Invoke(AsioTransportCommand.Restart);
        }

        private readonly List<AsioAudioSource> _queuedPlays = new List<AsioAudioSource>();

        /// <summary>
        /// Plays (or resumes) the source at the end of the frame, on the same sample as the other sources queued during the same frame
        /// (e.g. Timeline clips starting together). Seek the source first to choose where it starts. Doesn't raise <see cref="TransportCommandIssued"/>.
        /// </summary>
        public void PlayInSync(AsioAudioSource source)
        {
            if (source != null && !_queuedPlays.Contains(source)) _queuedPlays.Add(source);
        }

        private void LateUpdate()
        {
            if (_queuedPlays.Count == 0) return;
            AsioAudioSource[] sources = _queuedPlays.Where(source => source && source.isActiveAndEnabled).ToArray();
            _queuedPlays.Clear();
            ForSourcesInSync(sources, (source, deferred) => source.Play(deferred));
        }

        /// <summary>
        /// Called after PlayAll, PauseAll, StopAll or RestartAll, with the command (e.g. to mirror the transport in another application,
        /// see the REAPER OSC plugin). Static, so it can be listened to before the manager is created.
        /// </summary>
        public static event Action<AsioTransportCommand> TransportCommandIssued;

        // The sources' transport changes are deferred, then applied together by the mixer at the start of the next audio buffer.
        private void ForAllSourcesInSync(Action<AsioAudioSource, bool> action)
        {
            ForSourcesInSync(AsioAudioSources.Where(source => source).ToArray(), action); // a copy: event listeners may change the list
        }

        private void ForSourcesInSync(IList<AsioAudioSource> sources, Action<AsioAudioSource, bool> action)
        {
            foreach (AsioAudioSource source in sources) action(source, true);

            if (_mixer != null)
            {
                _mixer.ApplyPendingStates();
            }
            else
            {
                // Not connected: nothing plays, apply the changes now.
                foreach (AsioAudioSource source in sources) source.Voice?.ApplyPendingState();
            }
        }

        // ---------- Overlay ----------

        private void OnGUI()
        {
            // Read the toggle key from IMGUI events: this works with both the legacy Input Manager and the Input System package.
            Event currentEvent = Event.current;
            if (OverlayToggleKey != KeyCode.None && currentEvent.type == EventType.KeyDown && currentEvent.keyCode == OverlayToggleKey)
            {
                DisplayInfoOnGameWindow = !DisplayInfoOnGameWindow;
                currentEvent.Use();
            }

            if (!DisplayInfoOnGameWindow) return;

            _overlay ??= new AsioAudioManagerOverlay(this);
            _overlay.Draw();
        }
    }
}
