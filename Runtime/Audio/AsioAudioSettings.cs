using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// The project's ASIO audio settings (Project Settings > ASIO Audio): driver, sample rate, format, master volume, overlay and mixer snapshots.
    ///
    /// The settings are an asset (created from Project Settings). It is registered as the package's configuration in the Editor,
    /// and added to the Player Settings' Preloaded Assets, so builds use it too. Changes made at runtime (overlay, ASIO Audio Mixer...) are saved
    /// to the asset in the Editor, like any asset edited in Play mode.
    /// </summary>
    public class AsioAudioSettings : ScriptableObject
    {
        /// <summary>The key of the settings asset in the Editor's configuration objects.</summary>
        public const string ConfigName = "fr.liris.asioaudiounity.settings";

        [SerializeField] private string _asioDriverName = "";
        /// <summary>The name of the ASIO driver to play on.</summary>
        public string AsioDriverName
        {
            get { return _asioDriverName; }
            set { if (_asioDriverName != value) { _asioDriverName = value; NotifyChanged(); } }
        }

        [SerializeField] private int _targetSampleRate = 48000;
        /// <summary>The sample rate the ASIO driver runs at. Audio files are resampled to it on the fly.</summary>
        public int TargetSampleRate
        {
            get { return _targetSampleRate; }
            set { if (_targetSampleRate != value) { _targetSampleRate = value; NotifyChanged(); } }
        }

        [SerializeField] private BitsPerSample _targetBitsPerSample = BitsPerSample.Bits32;
        /// <summary>The sample format the mix is handed to NAudio in (it converts it to the driver's own format).</summary>
        public BitsPerSample TargetBitsPerSample
        {
            get { return _targetBitsPerSample; }
            set { if (_targetBitsPerSample != value) { _targetBitsPerSample = value; NotifyChanged(); } }
        }

        [SerializeField][Range(0f, 1f)] private float _masterVolume = 1f;
        /// <summary>Volume applied to all sources, after their own volume (linear gain, 0 to 1).</summary>
        public float MasterVolume
        {
            get { return _masterVolume; }
            set { _masterVolume = Mathf.Clamp01(value); }
        }

        [SerializeField] private bool _displayInfoOnGameWindow = true;
        /// <summary>Whether the in-game overlay is shown.</summary>
        public bool DisplayInfoOnGameWindow
        {
            get { return _displayInfoOnGameWindow; }
            set { _displayInfoOnGameWindow = value; }
        }

        [SerializeField] private KeyCode _overlayToggleKey = KeyCode.F9;
        /// <summary>The key that shows or hides the in-game overlay (None: no key).</summary>
        public KeyCode OverlayToggleKey
        {
            get { return _overlayToggleKey; }
            set { _overlayToggleKey = value; }
        }

        [SerializeField][Range(0.5f, 3f)] private float _overlayScale = 1f;
        /// <summary>The size of the in-game overlay (e.g. 2 on a 4K screen).</summary>
        public float OverlayScale
        {
            get { return _overlayScale; }
            set { _overlayScale = Mathf.Clamp(value, 0.5f, 3f); }
        }

        [SerializeField] private List<AsioMixerSnapshot> _snapshots = new List<AsioMixerSnapshot>();
        /// <summary>The saved mixer snapshots. Sources are identified by their output channel, so snapshots work in every scene.</summary>
        public List<AsioMixerSnapshot> Snapshots => _snapshots;

        /// <summary>Called when the driver, sample rate or format change (the ASIO Audio Manager reconnects).</summary>
        public event Action ConnectionSettingsChanged;

        private void NotifyChanged()
        {
            ConnectionSettingsChanged?.Invoke();
        }

        // ---------- Snapshots ----------

        /// <summary>
        /// Saves the mix of the given sources (volume, mute, solo, by output channel) and the master volume as a snapshot.
        /// A snapshot with the same name is replaced. Returns the snapshot.
        /// </summary>
        public AsioMixerSnapshot SaveSnapshot(string snapshotName, IEnumerable<AsioAudioSource> sources)
        {
            AsioMixerSnapshot snapshot = new AsioMixerSnapshot { Name = snapshotName, MasterVolume = MasterVolume };
            foreach (AsioAudioSource source in sources.Where(source => source != null && source.TargetOutputChannel > 0).OrderBy(source => source.TargetOutputChannel))
            {
                if (snapshot.Sources.Any(settings => settings.OutputChannel == source.TargetOutputChannel)) continue; // channel conflict: the first one wins
                snapshot.Sources.Add(new AsioMixerSnapshot.SourceSettings { OutputChannel = source.TargetOutputChannel, Volume = source.Volume, Mute = source.Mute, Solo = source.Solo });
            }

            int existing = _snapshots.FindIndex(other => other.Name == snapshotName);
            if (existing >= 0) _snapshots[existing] = snapshot;
            else _snapshots.Add(snapshot);
            return snapshot;
        }

        /// <summary>Applies a snapshot to the given sources (matched by output channel) and to the master volume.</summary>
        public void ApplySnapshot(AsioMixerSnapshot snapshot, IEnumerable<AsioAudioSource> sources)
        {
            MasterVolume = snapshot.MasterVolume;
            foreach (AsioAudioSource source in sources)
            {
                if (source == null) continue;
                AsioMixerSnapshot.SourceSettings settings = snapshot.Sources.Find(other => other.OutputChannel == source.TargetOutputChannel);
                if (settings == null) continue;
                source.Volume = settings.Volume;
                source.Mute = settings.Mute;
                source.Solo = settings.Solo;
            }
        }

        // ---------- Instance ----------

        private static AsioAudioSettings _instance;
        private static bool _instanceIsDefault;

        /// <summary>
        /// The project's settings. If no settings asset was created yet, default settings (not saved) are returned: see HasSettingsAsset.
        /// </summary>
        public static AsioAudioSettings Instance
        {
            get
            {
                if (_instance != null && !_instanceIsDefault) return _instance;

                AsioAudioSettings asset = null;
#if UNITY_EDITOR
                UnityEditor.EditorBuildSettings.TryGetConfigObject(ConfigName, out asset);
#endif
                if (asset != null)
                {
                    if (_instanceIsDefault && _instance != null) DestroyImmediate(_instance);
                    _instance = asset;
                    _instanceIsDefault = false;
                    return _instance;
                }

                if (_instance == null)
                {
                    _instance = CreateInstance<AsioAudioSettings>();
                    _instance.name = "Default ASIO Audio Settings (not saved)";
                    _instance.hideFlags = HideFlags.DontSave;
                    _instanceIsDefault = true;
                }
                return _instance;
            }
        }

        /// <summary>Whether the project has a settings asset (otherwise, default settings are used and nothing is saved).</summary>
        public static bool HasSettingsAsset
        {
            get
            {
                AsioAudioSettings settings = Instance;
                return settings != null && !_instanceIsDefault;
            }
        }

        private void OnEnable()
        {
#if !UNITY_EDITOR
            // In a build, the settings asset is a Preloaded Asset: it is loaded at startup, before any scene.
            if (_instance == null || _instanceIsDefault)
            {
                _instance = this;
                _instanceIsDefault = false;
            }
#endif
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Edited in Project Settings: the ASIO Audio Manager reconnects if needed (it compares with the last connection).
            NotifyChanged();
        }

        /// <summary>[Editor] Forgets the cached instance (after the settings asset is created, replaced or deleted).</summary>
        internal static void ResetInstance()
        {
            if (_instanceIsDefault && _instance != null) DestroyImmediate(_instance);
            _instance = null;
            _instanceIsDefault = false;
        }
#endif
    }
}
