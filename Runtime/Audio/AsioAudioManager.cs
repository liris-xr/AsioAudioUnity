using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using NAudio.Wave;

namespace AsioAudioUnity
{
    /// <summary>
    /// The main class to manage ASIO audio playback in Unity. It allows you to set the ASIO driver name, target sample rate, and bits per sample.
    /// The ASIO driver stays connected while this component is enabled, and continuously plays the mix of all registered Custom ASIO Audio Sources
    /// (each one on its own output channel). Playing, pausing or stopping a source doesn't interrupt the driver.
    /// </summary>
    [DefaultExecutionOrder(-100)] // Connect before the Custom ASIO Audio Sources are enabled
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
        /// Number of output channels exposed by the ASIO driver. Each Custom ASIO Audio Source targets one of these.
        /// </summary>
        public int AsioDriverOutputChannelCount
        {
            get { return _asioDriverOutputChannelCount; }
            private set { _asioDriverOutputChannelCount = value; }
        }

        [SerializeField][ReadOnly] private List<CustomAsioAudioSource> _customAsioAudioSources = new List<CustomAsioAudioSource>();
        public List<CustomAsioAudioSource> CustomAsioAudioSources
        {
            get { return _customAsioAudioSources; }
            private set { _customAsioAudioSources = value; }
        }

        [SerializeField] public bool _displayInfoOnGameWindow = true;
        public bool DisplayInfoOnGameWindow
        {
            get { return _displayInfoOnGameWindow; }
            set { _displayInfoOnGameWindow = value; }
        }

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

        private bool _displayModeClear = false;
        private int _currentAsioAudioSourceGUIIndex = 0;
        private string _currentAsioAudioSourceGUIStatus = "Stopped";
        private string _asioDriverNameGUI = "";
        private string _targetSampleRateGUI = "";
        private string _targetBitsPerSampleGUI = "";

        private void Awake()
        {
            OnAsioDriverNameChanged ??= new UnityEvent();
            OnTargetSampleRateChanged ??= new UnityEvent();
            OnTargetBitsPerSampleChanged ??= new UnityEvent();
            CustomAsioAudioSources ??= new List<CustomAsioAudioSource>();

            _asioDriverNameGUI = AsioDriverName;
            _targetSampleRateGUI = TargetSampleRate.ToString();
            _targetBitsPerSampleGUI = ((int)TargetBitsPerSample).ToString();
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
        }

        // When any source is soloed, every source that isn't soloed is muted.
        private void ApplyMuteAndSolo()
        {
            bool anySolo = false;
            foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources)
            {
                if (customAsioAudioSource && customAsioAudioSource.Solo) { anySolo = true; break; }
            }

            foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources)
            {
                if (customAsioAudioSource) customAsioAudioSource.SetEffectiveMute(customAsioAudioSource.Mute || (anySolo && !customAsioAudioSource.Solo));
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

                // Use all the driver's outputs, so any source can target any channel without reconnecting.
                AsioMixer mixer = new AsioMixer(TargetSampleRate, AsioDriverOutputChannelCount);
                foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources)
                {
                    if (customAsioAudioSource == null || customAsioAudioSource.Voice == null) continue;
                    customAsioAudioSource.Voice.SetOutputSampleRate(TargetSampleRate);
                    mixer.AddVoice(customAsioAudioSource.Voice);
                }

                IWaveProvider output = TargetBitsPerSample == BitsPerSample.Bits16 ? mixer.ToWaveProvider16() : mixer.ToWaveProvider();
                asioOut.Init(output);
                asioOut.Play();

                _mixer = mixer;
                AsioOutPlayer = asioOut;
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

        private static string GetAvailableDriverNamesText()
        {
            string[] driverNames = AsioOut.GetDriverNames();
            return driverNames.Length == 0 ? "(none)" : "\"" + string.Join("\", \"", driverNames) + "\"";
        }

        /// <summary>
        /// Check if the ASIO Audio Source is valid, returns true if added to the list of ASIO Audio Sources, false otherwise.
        /// </summary>
        /// <param name="customAsioAudioSource">The ASIO Audio Source to check.</param>
        /// <returns></returns>
        public bool RequestAddAsioAudioSource(CustomAsioAudioSource customAsioAudioSource)
        {
            // Check if file name is empty
            if (string.IsNullOrEmpty(customAsioAudioSource.AudioFilePath))
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + customAsioAudioSource.gameObject.name + "\" doesn't have any Audio File Name specified. It will be ignored.");
            }
            // Check if target output channel is not specified
            else if (customAsioAudioSource.TargetOutputChannel <= 0)
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + customAsioAudioSource.gameObject.name + "\" doesn't have any Target Output Channel specified. It will be ignored.");
            }
            // Check if target output channel exists on the driver
            else if (IsConnected && customAsioAudioSource.TargetOutputChannel > AsioDriverOutputChannelCount)
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + customAsioAudioSource.gameObject.name + "\" targets output channel " + customAsioAudioSource.TargetOutputChannel + ", but the ASIO driver \"" + AsioDriverName + "\" only has " + AsioDriverOutputChannelCount + " output channels. It will be ignored.");
            }
            // Check if target output channel is specified on another ASIO Audio Source
            else if (CustomAsioAudioSources.Any((otherCustomAsioAudioSource) => otherCustomAsioAudioSource != customAsioAudioSource && otherCustomAsioAudioSource.TargetOutputChannel == customAsioAudioSource.TargetOutputChannel))
            {
                Debug.LogError("The targeted output channel of the ASIO Audio Source attached to GameObject \"" + customAsioAudioSource.gameObject.name + "\" is already specified on another ASIO Audio Source, and needs to be unique. It will be ignored.");
            }
            // Check if the ASIO Audio Source is already associated with another ASIO Audio Manager
            else if (customAsioAudioSource.ReferencedAsioAudioManager && customAsioAudioSource.ReferencedAsioAudioManager != this)
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + customAsioAudioSource.gameObject.name + "\" is already associated with another ASIO Audio Manager. It will be ignored.");
            }
            else
            {
                customAsioAudioSource.ReferencedAsioAudioManager = this;
                if (!CustomAsioAudioSources.Contains(customAsioAudioSource)) CustomAsioAudioSources.Add(customAsioAudioSource);
                if (_mixer != null && customAsioAudioSource.Voice != null) _mixer.AddVoice(customAsioAudioSource.Voice);
                return true;
            }

            if (customAsioAudioSource.ReferencedAsioAudioManager == this) customAsioAudioSource.ReferencedAsioAudioManager = null;
            RemoveFromMix(customAsioAudioSource);
            return false;
        }

        /// <summary>
        /// Remove an ASIO Audio Source from the list of ASIO Audio Sources, returns true if removed, false otherwise.
        /// </summary>
        /// <param name="customAsioAudioSource">The ASIO Audio Source to remove.</param>
        /// <returns></returns>
        public bool RequestRemoveAsioAudioSource(CustomAsioAudioSource customAsioAudioSource)
        {
            if (!CustomAsioAudioSources.Contains(customAsioAudioSource)) return false;

            customAsioAudioSource.ReferencedAsioAudioManager = null;
            RemoveFromMix(customAsioAudioSource);
            return true;
        }

        private void RemoveFromMix(CustomAsioAudioSource customAsioAudioSource)
        {
            CustomAsioAudioSources.Remove(customAsioAudioSource);
            if (_mixer != null && customAsioAudioSource.Voice != null) _mixer.RemoveVoice(customAsioAudioSource.Voice);
        }

        private void OnGUI()
        {
            if (!DisplayInfoOnGameWindow) return;

            float horizontalOffset = 20;
            float verticalOffset = 280;

            GUIStyle headerStyle = new GUIStyle();
            headerStyle.fontSize = 20;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.normal.textColor = _displayModeClear ? Color.white : Color.black;

            GUIStyle standardStyle = new GUIStyle();
            standardStyle.fontSize = 15;
            standardStyle.wordWrap = true;
            standardStyle.clipping = TextClipping.Clip;
            standardStyle.normal.textColor = _displayModeClear ? Color.white : Color.black;

            GUIStyle asioSourceTitleStyle = new GUIStyle();
            asioSourceTitleStyle.fontSize = 15;
            asioSourceTitleStyle.fontStyle = FontStyle.Bold;
            asioSourceTitleStyle.alignment = TextAnchor.MiddleCenter;
            asioSourceTitleStyle.normal.textColor = _displayModeClear ? Color.white : Color.black;

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.hover.textColor = _displayModeClear ? Color.black : Color.white;
            buttonStyle.normal.textColor = _displayModeClear ? Color.white : Color.black;

            GUIStyle invertedButtonStyle = new GUIStyle(buttonStyle);
            invertedButtonStyle.normal.textColor = _displayModeClear ? Color.black : Color.white;

            GUIStyle connectionStyle = new GUIStyle();
            connectionStyle.fontSize = 15;
            connectionStyle.normal.textColor = IsConnected ? Color.green : Color.red;

            GUIStyle modifiedStyle = new GUIStyle(standardStyle);
            modifiedStyle.normal.textColor = Color.yellow;

            GUI.color = _displayModeClear ? new Color(0f, 0f, 0f, 0.5f) : new Color(1f, 1f, 1f, 0.5f);

            GUI.Box(new Rect(5, 5, 2 * verticalOffset, 14 * horizontalOffset), "");
            GUI.Box(new Rect(10, 20 + horizontalOffset * 7, 2 * verticalOffset - 10, 6 * horizontalOffset), "");

            GUI.color = Color.white;

            GUI.Label(new Rect(10, 10, verticalOffset, horizontalOffset), "ASIO Audio Manager", headerStyle);

            if (GUI.Button(new Rect(5 + (10 * verticalOffset / 6), 10, (2 * verticalOffset / 6) - 10, horizontalOffset), "Clear/Dark", invertedButtonStyle))
            {
                _displayModeClear = !_displayModeClear;
            }

            GUI.Label(new Rect(10, 20 + horizontalOffset, verticalOffset - 10, horizontalOffset), "ASIO Driver name:", standardStyle);

            _asioDriverNameGUI = GUI.TextField(new Rect(10 + verticalOffset, 20 + horizontalOffset, (2 * verticalOffset / 3) - 10, horizontalOffset), _asioDriverNameGUI, _asioDriverNameGUI == AsioDriverName ? standardStyle : modifiedStyle);
            if (GUI.Button(new Rect(5 + (10 * verticalOffset / 6), 20 + horizontalOffset, (2 * verticalOffset / 6) - 10, horizontalOffset), "Set", buttonStyle))
                AsioDriverName = _asioDriverNameGUI;

            GUI.Label(new Rect(10, 20 + horizontalOffset * 2, verticalOffset - 10, horizontalOffset), "ASIO Driver connection:", standardStyle);
            GUI.Label(new Rect(10 + verticalOffset, 20 + horizontalOffset * 2, verticalOffset - 10, horizontalOffset), IsConnected ? "OK" : "Error", connectionStyle);

            GUI.Label(new Rect(10, 20 + horizontalOffset * 3, verticalOffset - 10, horizontalOffset), "Number of supported channels:", standardStyle);
            GUI.Label(new Rect(10 + verticalOffset, 20 + horizontalOffset * 3, verticalOffset - 10, horizontalOffset), AsioDriverOutputChannelCount.ToString(), standardStyle);

            GUI.Label(new Rect(10, 20 + horizontalOffset * 4, verticalOffset - 10, horizontalOffset), "Target Sample Rate (Hz):", standardStyle);
            _targetSampleRateGUI = GUI.TextField(new Rect(10 + verticalOffset, 20 + horizontalOffset * 4, (2 * verticalOffset / 3) - 10, horizontalOffset), _targetSampleRateGUI, _targetSampleRateGUI == TargetSampleRate.ToString() ? standardStyle : modifiedStyle);
            if (GUI.Button(new Rect(5 + (10 * verticalOffset / 6), 20 + horizontalOffset * 4, (2 * verticalOffset / 6) - 10, horizontalOffset), "Set", buttonStyle))
            {
                if (int.TryParse(_targetSampleRateGUI, out int targetSampleRateParsed) && targetSampleRateParsed > 0)
                {
                    TargetSampleRate = targetSampleRateParsed;
                }
            }

            GUI.Label(new Rect(10, 20 + horizontalOffset * 5, verticalOffset - 10, horizontalOffset), "Target Bits Per Sample:", standardStyle);
            _targetBitsPerSampleGUI = GUI.TextField(new Rect(10 + verticalOffset, 20 + horizontalOffset * 5, (2 * verticalOffset / 3) - 10, horizontalOffset), _targetBitsPerSampleGUI, _targetBitsPerSampleGUI == ((int)TargetBitsPerSample).ToString() ? standardStyle : modifiedStyle);
            if (GUI.Button(new Rect(5 + (10 * verticalOffset / 6), 20 + horizontalOffset * 5, (2 * verticalOffset / 6) - 10, horizontalOffset), "Set", buttonStyle))
            {
                if (_targetBitsPerSampleGUI == "16") TargetBitsPerSample = BitsPerSample.Bits16;
                else if (_targetBitsPerSampleGUI == "32") TargetBitsPerSample = BitsPerSample.Bits32;
            }

            GUI.Label(new Rect(10, 20 + horizontalOffset * 6, verticalOffset - 10, horizontalOffset), "Number of Custom ASIO Audio Sources:", standardStyle);
            GUI.Label(new Rect(10 + verticalOffset, 20 + horizontalOffset * 6, verticalOffset - 10, horizontalOffset), CustomAsioAudioSources.Count.ToString(), standardStyle);

            if (CustomAsioAudioSources.Count == 0) return;
            if (_currentAsioAudioSourceGUIIndex < 0 || _currentAsioAudioSourceGUIIndex >= CustomAsioAudioSources.Count) _currentAsioAudioSourceGUIIndex = 0;

            if (GUI.Button(new Rect(15, 25 + horizontalOffset * 7, 50, horizontalOffset), "<-", buttonStyle))
            {
                _currentAsioAudioSourceGUIIndex--;
                if (_currentAsioAudioSourceGUIIndex < 0) _currentAsioAudioSourceGUIIndex = CustomAsioAudioSources.Count - 1;
            }
            if (GUI.Button(new Rect(2 * verticalOffset - 55, 25 + horizontalOffset * 7, 50, horizontalOffset), "->", buttonStyle))
            {
                _currentAsioAudioSourceGUIIndex++;
                if (_currentAsioAudioSourceGUIIndex >= CustomAsioAudioSources.Count) _currentAsioAudioSourceGUIIndex = 0;
            }

            CustomAsioAudioSource currentSource = CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex];

            GUI.Label(new Rect(65, 25 + horizontalOffset * 7, 2 * verticalOffset - 2 * 65 , 20), currentSource.gameObject.name, asioSourceTitleStyle);

            GUI.Label(new Rect(15, 25 + horizontalOffset * 8, verticalOffset - 15, horizontalOffset), "Audio File Name:", standardStyle);
            GUI.Label(new Rect(15 + verticalOffset, 25 + horizontalOffset * 8, verticalOffset - 15, horizontalOffset), Path.GetFileName(currentSource.AudioFilePath), standardStyle);

            GUI.Label(new Rect(15, 25 + horizontalOffset * 9, verticalOffset - 15, horizontalOffset), "Target Output Channel:", standardStyle);
            GUI.Label(new Rect(15 + verticalOffset, 25 + horizontalOffset * 9, verticalOffset - 15, horizontalOffset), currentSource.TargetOutputChannel.ToString(), standardStyle);

            GUI.Label(new Rect(15, 25 + horizontalOffset * 10, verticalOffset - 15, horizontalOffset), "Audio Status:", standardStyle);

            GUIStyle audioStatusStyle = new GUIStyle();
            audioStatusStyle.fontSize = 15;
            if (currentSource.AudioStatus == AsioAudioStatus.Playing)
            {
                audioStatusStyle.normal.textColor = Color.green;
                _currentAsioAudioSourceGUIStatus = "Playing";
            }
            else if (currentSource.AudioStatus == AsioAudioStatus.Paused)
            {
                audioStatusStyle.normal.textColor = Color.yellow;
                _currentAsioAudioSourceGUIStatus = "Paused";
            }
            else
            {
                audioStatusStyle.normal.textColor = Color.red;
                _currentAsioAudioSourceGUIStatus = "Stopped";
            }
            GUI.Label(new Rect(15 + verticalOffset, 25 + horizontalOffset * 10, verticalOffset - 15, horizontalOffset), _currentAsioAudioSourceGUIStatus, audioStatusStyle);

            if (GUI.Button(new Rect(15, 25 + horizontalOffset * 11, (2 * verticalOffset / 3) - 10, 3 * horizontalOffset / 2), "Play", buttonStyle))
            {
                currentSource.Play();
            }

            if (GUI.Button(new Rect(10 + (2 * verticalOffset / 3), 25 + horizontalOffset * 11, (2 * verticalOffset / 3) - 10, 3 * horizontalOffset / 2), "Pause", buttonStyle))
            {
                currentSource.Pause();
            }

            if (GUI.Button(new Rect(5 + (4 * verticalOffset / 3), 25 + horizontalOffset * 11, (2 * verticalOffset / 3) - 10, 3 * horizontalOffset / 2), "Stop", buttonStyle))
            {
                currentSource.Stop();
            }
        }
    }
}
