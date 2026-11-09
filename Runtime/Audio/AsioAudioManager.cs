using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace AsioAudioUnity
{
    /// <summary>
    /// The main class to manage ASIO audio playback in Unity. It allows you to set the ASIO driver name, target sample rate, and bits per sample.
    /// The ASIO driver stays connected while this component is enabled, and continuously plays the mix of all registered ASIO Audio Sources
    /// (each one on its own output channel). Playing, pausing or stopping a source doesn't interrupt the driver.
    /// </summary>
    [DefaultExecutionOrder(-100)] // Connect before the ASIO Audio Sources are enabled
    public class AsioAudioManager : MonoBehaviour
    {
        private UnityEvent _onAsioDriverNameChanged;
        public UnityEvent OnAsioDriverNameChanged
        {
            get { return _onAsioDriverNameChanged; }
            private set { _onAsioDriverNameChanged = value; }
        }

        [SerializeField] private string _asioDriverName;
        /// <summary>
        /// The name of the ASIO driver to play on. Changing it at runtime reconnects to the new driver.
        /// </summary>
        public string AsioDriverName
        {
            get { return _asioDriverName; }
            set
            {
                if (_asioDriverName == value) return;
                _asioDriverName = value;
                ApplyConfigurationChange();
                if (OnAsioDriverNameChanged != null)
                    OnAsioDriverNameChanged.Invoke();
            }
        }

        private UnityEvent _onTargetSampleRateChanged;
        public UnityEvent OnTargetSampleRateChanged
        {
            get { return _onTargetSampleRateChanged; }
            private set { _onTargetSampleRateChanged = value; }
        }

        [SerializeField] private int _targetSampleRate = 48000;
        /// <summary>
        /// The sample rate the ASIO driver runs at. Audio files are resampled to it on the fly. Changing it at runtime reconnects the driver.
        /// </summary>
        public int TargetSampleRate
        {
            get { return _targetSampleRate; }
            set
            {
                if (_targetSampleRate == value) return;
                _targetSampleRate = value;
                ApplyConfigurationChange();
                if (OnTargetSampleRateChanged != null)
                    OnTargetSampleRateChanged.Invoke();
            }
        }

        private UnityEvent _onTargetBitsPerSampleChanged;
        public UnityEvent OnTargetBitsPerSampleChanged
        {
            get { return _onTargetBitsPerSampleChanged; }
            private set { _onTargetBitsPerSampleChanged = value; }
        }

        [SerializeField] private BitsPerSample _targetBitsPerSample = BitsPerSample.Bits32;
        /// <summary>
        /// The sample format sent to the ASIO driver (16-bit integer or 32-bit float). Changing it at runtime reconnects the driver.
        /// </summary>
        public BitsPerSample TargetBitsPerSample
        {
            get { return _targetBitsPerSample; }
            set
            {
                if (_targetBitsPerSample == value) return;
                _targetBitsPerSample = value;
                ApplyConfigurationChange();
                if (OnTargetBitsPerSampleChanged != null)
                    OnTargetBitsPerSampleChanged.Invoke();
            }
        }

        [SerializeField][ReadOnly] private int _asioDriverInputChannelCount;
        public int AsioDriverInputChannelCount
        {
            get { return _asioDriverInputChannelCount; }
            private set { _asioDriverInputChannelCount = value; }
        }

        [SerializeField][ReadOnly] private int _asioDriverOutputChannelCount;
        /// <summary>
        /// Number of output channels exposed by the ASIO driver. Each ASIO Audio Source targets one of these.
        /// </summary>
        public int AsioDriverOutputChannelCount
        {
            get { return _asioDriverOutputChannelCount; }
            private set { _asioDriverOutputChannelCount = value; }
        }

        [SerializeField][ReadOnly] private string _asioDriverSampleFormat = "";
        /// <summary>
        /// The sample format the ASIO driver works with, e.g. "24-bit integer" (empty until connected). The mix is converted to it, whatever TargetBitsPerSample is.
        /// </summary>
        public string AsioDriverSampleFormat
        {
            get { return _asioDriverSampleFormat; }
            private set { _asioDriverSampleFormat = value; }
        }

        [SerializeField][ReadOnly] private List<string> _asioDriverOutputChannelNames = new List<string>();
        /// <summary>
        /// Names of the ASIO driver's output channels, as given by the driver (e.g. "ReaRoute 3"); index 0 is channel 1. Empty until connected.
        /// </summary>
        public IReadOnlyList<string> AsioDriverOutputChannelNames => _asioDriverOutputChannelNames;

        /// <summary>The driver's name of an output channel (1-based), or an empty string if unknown.</summary>
        public string GetOutputChannelName(int channel)
        {
            return channel >= 1 && channel <= _asioDriverOutputChannelNames.Count ? _asioDriverOutputChannelNames[channel - 1] : "";
        }

        [SerializeField][Range(0f, 1f)] private float _masterVolume = 1f;
        /// <summary>
        /// Volume applied to all sources (linear gain, 0 to 1, like Volume on the sources), after their own volume. Applied live and smoothly.
        /// </summary>
        public float MasterVolume
        {
            get { return _masterVolume; }
            set
            {
                _masterVolume = Mathf.Clamp01(value);
                if (_mixer != null) _mixer.MasterVolume = _masterVolume;
            }
        }

        /// <summary>
        /// The master volume in decibels (0 dB: unchanged; negative infinity: silence).
        /// </summary>
        public float MasterVolumeDecibels
        {
            get { return AsioVolume.ToDecibels(MasterVolume); }
            set { MasterVolume = AsioVolume.ToLinear(value); }
        }

        /// <summary>
        /// Output level sent to the ASIO driver (peak of all channels, 0 to 1, after the master volume), with a meter-like falloff. Updated every frame while connected.
        /// </summary>
        public float MasterMeterLevel { get; private set; }

        [SerializeField] private List<AsioMixerSnapshot> _snapshots = new List<AsioMixerSnapshot>();
        /// <summary>
        /// The saved mixer snapshots (master volume, and volume / mute / solo of each source). See SaveSnapshot and RecallSnapshot.
        /// </summary>
        public List<AsioMixerSnapshot> Snapshots => _snapshots;

        [SerializeField][ReadOnly][FormerlySerializedAs("_customAsioAudioSources")] private List<AsioAudioSource> _asioAudioSources = new List<AsioAudioSource>();
        public List<AsioAudioSource> AsioAudioSources
        {
            get { return _asioAudioSources; }
            private set { _asioAudioSources = value; }
        }

        [SerializeField] public bool _displayInfoOnGameWindow = true;
        /// <summary>
        /// Whether the in-game overlay (driver status and settings, source controls) is shown. Toggled at runtime with OverlayToggleKey.
        /// </summary>
        public bool DisplayInfoOnGameWindow
        {
            get { return _displayInfoOnGameWindow; }
            set { _displayInfoOnGameWindow = value; }
        }

        [SerializeField] private KeyCode _overlayToggleKey = KeyCode.F9;
        /// <summary>
        /// Key that shows or hides the in-game overlay (None: no key).
        /// </summary>
        public KeyCode OverlayToggleKey
        {
            get { return _overlayToggleKey; }
            set { _overlayToggleKey = value; }
        }

        [SerializeField][Range(0.5f, 3f)] private float _overlayScale = 1f;
        /// <summary>
        /// Size of the in-game overlay (e.g. 2 on a 4K screen).
        /// </summary>
        public float OverlayScale
        {
            get { return _overlayScale; }
            set { _overlayScale = Mathf.Clamp(value, 0.5f, 3f); }
        }

        private AsioAudioManagerOverlay _overlay;

        private AsioOut _asioOutPlayer;
        public AsioOut AsioOutPlayer
        {
            get { return _asioOutPlayer; }
            private set { _asioOutPlayer = value; }
        }

        /// <summary>
        /// True when the ASIO driver is connected and playing.
        /// </summary>
        public bool IsConnected => AsioOutPlayer != null;

        // Mixes all sources into one stream for the driver. Only exists while connected.
        private AsioMixer _mixer;

        // The configuration of the last connection attempt, to detect changes made from the Inspector (which bypass the property setters).
        private string _attemptedDriverName;
        private int _attemptedSampleRate;
        private BitsPerSample _attemptedBitsPerSample;

        private void Awake()
        {
            OnAsioDriverNameChanged ??= new UnityEvent();
            OnTargetSampleRateChanged ??= new UnityEvent();
            OnTargetBitsPerSampleChanged ??= new UnityEvent();
            AsioAudioSources ??= new List<AsioAudioSource>();
        }

        private void OnEnable()
        {
            Connect();
        }

        private void Update()
        {
            // Settings edited in the Inspector during Play mode don't go through the property setters.
            if (_attemptedDriverName != AsioDriverName || _attemptedSampleRate != TargetSampleRate || _attemptedBitsPerSample != TargetBitsPerSample)
            {
                Reconnect();
            }

            ApplyMuteAndSolo();

            if (_mixer != null)
            {
                _mixer.MasterVolume = _masterVolume; // Also picks up Inspector edits
                // Falls by 20 dB per second
                MasterMeterLevel = Mathf.Max(_mixer.ConsumeMasterPeak(), MasterMeterLevel * Mathf.Pow(0.1f, Time.unscaledDeltaTime));
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
            Disconnect();
        }

        private void OnApplicationQuit()
        {
            Disconnect();
        }

        private void ApplyConfigurationChange()
        {
            if (Application.isPlaying && isActiveAndEnabled) Reconnect();
        }

        /// <summary>
        /// Disconnects from the ASIO driver and connects again with the current settings. Sources keep their state and position.
        /// </summary>
        public void Reconnect()
        {
            Disconnect();
            Connect();
        }

        /// <summary>
        /// Connects to the ASIO driver set in AsioDriverName, and starts playing the mix of all registered sources.
        /// Errors are logged, and the manager stays disconnected.
        /// </summary>
        private bool Connect()
        {
            if (IsConnected) return true;

            _attemptedDriverName = AsioDriverName;
            _attemptedSampleRate = TargetSampleRate;
            _attemptedBitsPerSample = TargetBitsPerSample;

            if (string.IsNullOrEmpty(AsioDriverName))
            {
                Debug.LogError("ASIO Audio Manager: no ASIO driver name is set. Available drivers: " + GetAvailableDriverNamesText());
                return false;
            }

            if (!AsioOut.GetDriverNames().Contains(AsioDriverName))
            {
                Debug.LogError("ASIO Audio Manager: the ASIO driver \"" + AsioDriverName + "\" was not found on the system. Available drivers: " + GetAvailableDriverNamesText());
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
                AsioMixer mixer = new AsioMixer(TargetSampleRate, AsioDriverOutputChannelCount) { MasterVolume = _masterVolume };
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
                asioOut.Play();

                _mixer = mixer;
                AsioOutPlayer = asioOut;
                Debug.Log("ASIO Audio Manager: connected to \"" + AsioDriverName + "\" (" + AsioDriverOutputChannelCount + " outputs, " + TargetSampleRate + " Hz"
                    + (string.IsNullOrEmpty(AsioDriverSampleFormat) ? "" : ", driver format: " + AsioDriverSampleFormat) + ").");

#if UNITY_EDITOR
                // Remembered so the Inspector can show them outside Play mode (keys read by AsioEditorUtility).
                UnityEditor.EditorPrefs.SetInt("AsioAudioUnity.OutputChannelCount." + AsioDriverName, AsioDriverOutputChannelCount);
                if (!string.IsNullOrEmpty(AsioDriverSampleFormat)) UnityEditor.EditorPrefs.SetString("AsioAudioUnity.DriverSampleFormat." + AsioDriverName, AsioDriverSampleFormat);
                UnityEditor.EditorPrefs.SetString("AsioAudioUnity.OutputChannelNames." + AsioDriverName, string.Join("\n", _asioDriverOutputChannelNames));
#endif
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("ASIO Audio Manager: could not start the ASIO driver \"" + AsioDriverName + "\" at " + TargetSampleRate + " Hz, " + (int)TargetBitsPerSample + " bits: " + e.GetBaseException().Message);
                asioOut?.Dispose();
                return false;
            }
        }

        private void Disconnect()
        {
            if (AsioOutPlayer != null)
            {
                AsioOutPlayer.Stop();
                AsioOutPlayer.Dispose();
                AsioOutPlayer = null;
            }
            _mixer = null;
        }

        private static readonly FieldInfo AsioOutDriverField = typeof(AsioOut).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);

        // The sample format of the driver's first output channel (NAudio doesn't expose it publicly). Null if it can't be read.
        private static AsioSampleType? GetDriverOutputSampleType(AsioOut asioOut)
        {
            try
            {
                AsioChannelInfo[] outputChannels = (AsioOutDriverField?.GetValue(asioOut) as AsioDriverExt)?.Capabilities.OutputChannelInfos;
                if (outputChannels != null && outputChannels.Length > 0) return outputChannels[0].type;
            }
            catch (Exception) { }
            return null;
        }

        private static string DescribeSampleType(AsioSampleType? sampleType)
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

        /// <summary>
        /// Check if the ASIO Audio Source is valid, returns true if added to the list of ASIO Audio Sources, false otherwise.
        /// </summary>
        /// <param name="asioAudioSource">The ASIO Audio Source to check.</param>
        /// <returns></returns>
        public bool RequestAddAsioAudioSource(AsioAudioSource asioAudioSource)
        {
            // Check if file name is empty
            if (string.IsNullOrEmpty(asioAudioSource.AudioFilePath))
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + asioAudioSource.gameObject.name + "\" doesn't have any Audio File Name specified. It will be ignored.");
            }
            // Check if target output channel is not specified
            else if (asioAudioSource.TargetOutputChannel <= 0)
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + asioAudioSource.gameObject.name + "\" doesn't have any Target Output Channel specified. It will be ignored.");
            }
            // Check if target output channel exists on the driver
            else if (IsConnected && asioAudioSource.TargetOutputChannel > AsioDriverOutputChannelCount)
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + asioAudioSource.gameObject.name + "\" targets output channel " + asioAudioSource.TargetOutputChannel + ", but the ASIO driver \"" + AsioDriverName + "\" only has " + AsioDriverOutputChannelCount + " output channels. It will be ignored.");
            }
            // Check if target output channel is specified on another ASIO Audio Source
            else if (AsioAudioSources.Any((otherAsioAudioSource) => otherAsioAudioSource != asioAudioSource && otherAsioAudioSource.TargetOutputChannel == asioAudioSource.TargetOutputChannel))
            {
                Debug.LogError("The targeted output channel of the ASIO Audio Source attached to GameObject \"" + asioAudioSource.gameObject.name + "\" is already specified on another ASIO Audio Source, and needs to be unique. It will be ignored.");
            }
            // Check if the ASIO Audio Source is already associated with another ASIO Audio Manager
            else if (asioAudioSource.ReferencedAsioAudioManager && asioAudioSource.ReferencedAsioAudioManager != this)
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + asioAudioSource.gameObject.name + "\" is already associated with another ASIO Audio Manager. It will be ignored.");
            }
            else
            {
                asioAudioSource.ReferencedAsioAudioManager = this;
                if (!AsioAudioSources.Contains(asioAudioSource)) AsioAudioSources.Add(asioAudioSource);
                if (_mixer != null && asioAudioSource.Voice != null) _mixer.AddVoice(asioAudioSource.Voice);
                return true;
            }

            if (asioAudioSource.ReferencedAsioAudioManager == this) asioAudioSource.ReferencedAsioAudioManager = null;
            RemoveFromMix(asioAudioSource);
            return false;
        }

        // ---------- Snapshots ----------

        /// <summary>
        /// Saves the current mix (master volume, and volume / mute / solo of every source using this manager, in the open scenes)
        /// as a snapshot. A snapshot with the same name is replaced. Returns the snapshot.
        /// </summary>
        public AsioMixerSnapshot SaveSnapshot(string snapshotName)
        {
            AsioMixerSnapshot snapshot = new AsioMixerSnapshot { Name = snapshotName, MasterVolume = MasterVolume };
            foreach (AsioAudioSource source in FindObjectsByType<AsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (source.ReferencedAsioAudioManager != null && source.ReferencedAsioAudioManager != this) continue;
                snapshot.Sources.Add(new AsioMixerSnapshot.SourceSettings { Source = source, Volume = source.Volume, Mute = source.Mute, Solo = source.Solo });
            }

            int existing = _snapshots.FindIndex(other => other.Name == snapshotName);
            if (existing >= 0) _snapshots[existing] = snapshot;
            else _snapshots.Add(snapshot);
            return snapshot;
        }

        /// <summary>
        /// Applies a saved snapshot: master volume, and volume / mute / solo of its sources (volume changes are smoothed while playing).
        /// Returns false if there is no snapshot with this name.
        /// </summary>
        public bool RecallSnapshot(string snapshotName)
        {
            AsioMixerSnapshot snapshot = _snapshots.Find(other => other.Name == snapshotName);
            if (snapshot == null) return false;
            ApplySnapshot(snapshot);
            return true;
        }

        /// <summary>Applies the snapshot at an index of Snapshots. Returns false if the index is out of range.</summary>
        public bool RecallSnapshot(int index)
        {
            if (index < 0 || index >= _snapshots.Count) return false;
            ApplySnapshot(_snapshots[index]);
            return true;
        }

        /// <summary>Deletes a snapshot. Returns false if there is no snapshot with this name.</summary>
        public bool DeleteSnapshot(string snapshotName)
        {
            return _snapshots.RemoveAll(other => other.Name == snapshotName) > 0;
        }

        private void ApplySnapshot(AsioMixerSnapshot snapshot)
        {
            MasterVolume = snapshot.MasterVolume;
            foreach (AsioMixerSnapshot.SourceSettings settings in snapshot.Sources)
            {
                if (settings.Source == null) continue; // deleted since
                settings.Source.Volume = settings.Volume;
                settings.Source.Mute = settings.Mute;
                settings.Source.Solo = settings.Solo;
            }
        }

        // ---------- Transport of all sources ----------

        /// <summary>
        /// Plays (or resumes) all the sources managed by this manager. They start on exactly the same sample,
        /// so files that belong together (e.g. the stems of a multichannel mix) stay in sync.
        /// </summary>
        public void PlayAll()
        {
            ForAllSourcesInSync((source, deferred) => source.Play(deferred));
        }

        /// <summary>Pauses all the playing sources managed by this manager, on the same sample (so they stay in sync when resumed).</summary>
        public void PauseAll()
        {
            ForAllSourcesInSync((source, deferred) => source.Pause(deferred));
        }

        /// <summary>Stops all the sources managed by this manager (they go back to the start of their file), on the same sample.</summary>
        public void StopAll()
        {
            ForAllSourcesInSync((source, deferred) => source.Stop(deferred));
        }

        /// <summary>Plays all the sources managed by this manager from the start of their file, starting on the same sample. (This doesn't trigger events.)</summary>
        public void RestartAll()
        {
            ForAllSourcesInSync((source, deferred) => source.Restart(deferred));
        }

        // The sources' transport changes are deferred, then applied together by the mixer at the start of the next audio buffer.
        private void ForAllSourcesInSync(Action<AsioAudioSource, bool> action)
        {
            AsioAudioSource[] sources = AsioAudioSources.Where(source => source).ToArray(); // a copy: event listeners may change the list
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

        /// <summary>
        /// Remove an ASIO Audio Source from the list of ASIO Audio Sources, returns true if removed, false otherwise.
        /// </summary>
        /// <param name="asioAudioSource">The ASIO Audio Source to remove.</param>
        /// <returns></returns>
        public bool RequestRemoveAsioAudioSource(AsioAudioSource asioAudioSource)
        {
            if (!AsioAudioSources.Contains(asioAudioSource)) return false;

            asioAudioSource.ReferencedAsioAudioManager = null;
            RemoveFromMix(asioAudioSource);
            return true;
        }

        private void RemoveFromMix(AsioAudioSource asioAudioSource)
        {
            AsioAudioSources.Remove(asioAudioSource);
            if (_mixer != null && asioAudioSource.Voice != null) _mixer.RemoveVoice(asioAudioSource.Voice);
        }

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
