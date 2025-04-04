using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AsioAudioUnity
{
    public class AsioAudioManager : MonoBehaviour
    {
        private UnityEvent _onAsioDriverNameChanged;
        public UnityEvent OnAsioDriverNameChanged
        {
            get { return _onAsioDriverNameChanged; }
            private set { _onAsioDriverNameChanged = value; }
        }

        [SerializeField] private string _asioDriverName;
        public string AsioDriverName
        {
            get { return _asioDriverName; }
            private set 
            { 
                if (_asioDriverName == value) return;
                _asioDriverName = value;
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
        public int TargetSampleRate
        {
            get { return _targetSampleRate; }
            private set 
            {
                if (_targetSampleRate == value) return;
                _targetSampleRate = value;
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
        public BitsPerSample TargetBitsPerSample
        {
            get { return _targetBitsPerSample; }
            private set 
            {
                if (_targetBitsPerSample == value) return;
                _targetBitsPerSample = value;
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

        [SerializeField][ReadOnly] private List<CustomAsioAudioSource> _customAsioAudioSources;
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

        private bool _displayModeClear = false;
        private string _connectionStatusGUI = "";
        private int _currentAsioAudioSourceGUIIndex = 0;
        private string _currentAsioAudioSourceGUIStatus = "Stopped";
        private string _asioDriverNameGUI = "";
        private string _targetSampleRateGUI = "";
        private string _targetBitsPerSampleGUI = "";

        private MultiplexingWaveProvider _globalMultiplexingWaveProvider;
        public MultiplexingWaveProvider GlobalMultiplexingWaveProvider
        {
            get { return _globalMultiplexingWaveProvider; }
            private set { _globalMultiplexingWaveProvider = value; }
        }

        private AsioOut _asioOutPlayer;
        public AsioOut AsioOutPlayer
        {
            get { return _asioOutPlayer; }
            private set { _asioOutPlayer = value; }
        }

        private void OnGUI()
        {
            if (!DisplayInfoOnGameWindow) return;

            //GUI.backgroundColor = _displayModeClear ? Color.black : Color.white;

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
            connectionStyle.normal.textColor = _connectionStatusGUI == "OK" ? Color.green : Color.red;

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


            _connectionStatusGUI = (AsioOutPlayer != null) ? "OK" : "Error";

            GUI.Label(new Rect(10 + verticalOffset, 20 + horizontalOffset * 2, verticalOffset - 10, horizontalOffset), _connectionStatusGUI, connectionStyle);

            GUI.Label(new Rect(10, 20 + horizontalOffset * 3, verticalOffset - 10, horizontalOffset), "Number of supported channels:", standardStyle);
            GUI.Label(new Rect(10 + verticalOffset, 20 + horizontalOffset * 3, verticalOffset - 10, horizontalOffset), AsioDriverInputChannelCount.ToString(), standardStyle);

            GUI.Label(new Rect(10, 20 + horizontalOffset * 4, verticalOffset - 10, horizontalOffset), "Target Sample Rate (Hz):", standardStyle);
            _targetSampleRateGUI = GUI.TextField(new Rect(10 + verticalOffset, 20 + horizontalOffset * 4, (2 * verticalOffset / 3) - 10, horizontalOffset), _targetSampleRateGUI, _targetSampleRateGUI == TargetSampleRate.ToString() ? standardStyle : modifiedStyle);
            if (GUI.Button(new Rect(5 + (10 * verticalOffset / 6), 20 + horizontalOffset * 4, (2 * verticalOffset / 6) - 10, horizontalOffset), "Set", buttonStyle))
            {
                if (int.TryParse(_targetSampleRateGUI, out _targetSampleRate))
                {
                    int targetSampleRateParsed = int.Parse(_targetSampleRateGUI);
                    if (targetSampleRateParsed != TargetSampleRate) TargetSampleRate = targetSampleRateParsed;
                }
            }
            //TargetSampleRate = int.TryParse(_targetSampleRateGUI, out _targetSampleRate) ? int.Parse(_targetSampleRateGUI) : TargetSampleRate; 

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

            GUI.Label(new Rect(65, 25 + horizontalOffset * 7, 2 * verticalOffset - 2 * 65 , 20), CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex].gameObject.name, asioSourceTitleStyle);

            GUI.Label(new Rect(15, 25 + horizontalOffset * 8, verticalOffset - 15, horizontalOffset), "Audio File Name:", standardStyle);
            GUI.Label(new Rect(15 + verticalOffset, 25 + horizontalOffset * 8, verticalOffset - 15, horizontalOffset), Path.GetFileName(CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex].AudioFilePath), standardStyle);

            GUI.Label(new Rect(15, 25 + horizontalOffset * 9, verticalOffset - 15, horizontalOffset), "Target Output Channel:", standardStyle);
            GUI.Label(new Rect(15 + verticalOffset, 25 + horizontalOffset * 9, verticalOffset - 15, horizontalOffset), CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex].TargetOutputChannel.ToString(), standardStyle);

            GUI.Label(new Rect(15, 25 + horizontalOffset * 10, verticalOffset - 15, horizontalOffset), "Audio Status:", standardStyle);

            GUIStyle audioStatusStyle = new GUIStyle();
            audioStatusStyle.fontSize = 15;
            if (CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex].AudioStatus == AsioAudioStatus.Playing)
            {
                audioStatusStyle.normal.textColor = Color.green;
                _currentAsioAudioSourceGUIStatus = "Playing";
            }
            else if (CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex].AudioStatus == AsioAudioStatus.Paused)
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
                CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex].Play();
            }

            if (GUI.Button(new Rect(10 + (2 * verticalOffset / 3), 25 + horizontalOffset * 11, (2 * verticalOffset / 3) - 10, 3 * horizontalOffset / 2), "Pause", buttonStyle))
            {
                CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex].Pause();
            }

            if (GUI.Button(new Rect(5 + (4 * verticalOffset / 3), 25 + horizontalOffset * 11, (2 * verticalOffset / 3) - 10, 3 * horizontalOffset / 2), "Stop", buttonStyle))
            {
                CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex].Stop();
            }    
        }

        private void Awake()
        {
            OnAsioDriverNameChanged = new UnityEvent();
            OnTargetSampleRateChanged = new UnityEvent();
            OnTargetBitsPerSampleChanged = new UnityEvent();

            _asioDriverNameGUI = AsioDriverName;
            _targetSampleRateGUI = TargetSampleRate.ToString();
            _targetBitsPerSampleGUI = ((int)TargetBitsPerSample).ToString();

            ConnectToAsioDriver();
        }

        private void Start()
        {
            OnAsioDriverNameChanged.AddListener(delegate { ResetDriverAndSamples(); });
            OnTargetSampleRateChanged.AddListener(delegate { ResetDriverAndSamples(); });
            OnTargetBitsPerSampleChanged.AddListener(delegate { ResetDriverAndSamples(); });
        }

        private void ResetDriverAndSamples()
        {
            if (CustomAsioAudioSources != null)
            {
                SetAllAsioAudioSourceSampleOffsets(true);
            }
            if (AsioOutPlayer != null)
            {
                AsioOutPlayer.Stop();
                AsioOutPlayer.Dispose();
            }
            ConnectMixAndPlay();
        }

        /// <summary>
        /// Intialise AsioOutPlayer by establishing the connection to a specified ASIO driver, whose name is set on AsioDriverName property.
        /// </summary>
        private void ConnectToAsioDriver()
        {
            if (string.IsNullOrEmpty(AsioDriverName))
            {
                throw new Exception("The ASIO driver was not specified.");
            }

            AsioOut asioOutPlayer;
            string[] driverNames = AsioOut.GetDriverNames();
            foreach (var driverName in driverNames)
            {
                if (driverName == AsioDriverName)
                {
                    try
                    {
                        asioOutPlayer = new AsioOut(driverName);
                        AsioDriverInputChannelCount = asioOutPlayer.DriverInputChannelCount;
                        AsioOutPlayer = asioOutPlayer;
                        return;
                    }
                    catch (Exception e)
                    {
                        AsioOutPlayer = null;
                        throw e.GetBaseException();
                    }
                }
            }
            AsioOutPlayer = null;
            throw new Exception("The ASIO driver \"" + AsioDriverName + "\" was not found on the system.");
        }

        /// <summary>
        /// Check if the ASIO Audio Source is valid for processing.
        /// </summary>
        /// <param name="customAsioAudioSource">The ASIO Audio Source to check.</param>
        /// <returns></returns>
        public bool RequestValidationAsioAudioSource(CustomAsioAudioSource customAsioAudioSource)
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
            // Check if target output channel is specified on another ASIO Audio Source
            else if (CustomAsioAudioSources.Find((targetCustomAsioAudioSource) => targetCustomAsioAudioSource.TargetOutputChannel == customAsioAudioSource.TargetOutputChannel) != null &&
                CustomAsioAudioSources.Find((targetCustomAsioAudioSource) => targetCustomAsioAudioSource.TargetOutputChannel == customAsioAudioSource.TargetOutputChannel) != customAsioAudioSource)
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
                return true;
            }

            if (customAsioAudioSource.ReferencedAsioAudioManager == this) customAsioAudioSource.ReferencedAsioAudioManager = null;
            if (CustomAsioAudioSources.Contains(customAsioAudioSource)) CustomAsioAudioSources.Remove(customAsioAudioSource);
            return false;
        }

        /// <summary>
        /// Remove an ASIO Audio Source from the list of ASIO Audio Sources.
        /// </summary>
        /// <param name="customAsioAudioSource">The ASIO Audio Source to remove.</param>
        /// <returns></returns>
        public bool RequestRemoveAsioAudioSource(CustomAsioAudioSource customAsioAudioSource)
        {
            if (CustomAsioAudioSources.Contains(customAsioAudioSource))
            {
                customAsioAudioSource.ReferencedAsioAudioManager = null;
                CustomAsioAudioSources.Remove(customAsioAudioSource);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Setup the global provider by associating each ASIO Audio Source to a specific output channel on the ASIO driver.
        /// </summary>
        private void SetGlobalMultiplexingWaveProvider()
        {
            if (CustomAsioAudioSources.Count > AsioDriverInputChannelCount)
            {
                throw new Exception("There is more audio channels from all ASIO Audio Sources (" + CustomAsioAudioSources.Count + ") than there is input channels on the ASIO driver (" + AsioDriverInputChannelCount + ")." +
                    "Consider removing ASIO Audio Sources or reducing the amount of input channels (TargetSampleChannelCount) per track in an ASIO Audio Source.");
            }
            if (CustomAsioAudioSources.Select((customAsioAudioSource) => customAsioAudioSource.TargetOutputChannel).Distinct().Count() != CustomAsioAudioSources.Count)
            {
                throw new Exception("There are ASIO Audio Sources with the same Target Output Channel. Each ASIO Audio Source must have a unique Target Output Channel.");
            }

            // Step 1: Get all the Sample Providers from the ASIO Audio Sources, convert them to Wave Providers (to manage silenced sources), and store them in a dictionary
            List<IWaveProvider> asioSourcesWaveProviders = new List<IWaveProvider>();
            int numberOfOutputChannels = CustomAsioAudioSources.Max((customAsioAudioSource) => customAsioAudioSource.TargetOutputChannel);

            for (int i = 0; i < numberOfOutputChannels; i++)
            {
                CustomAsioAudioSource customAsioAudioSourceFound = CustomAsioAudioSources.Find((CustomAsioAudioSource) => CustomAsioAudioSource.TargetOutputChannel == i + 1);

                if (customAsioAudioSourceFound != null)
                {
                    if (customAsioAudioSourceFound.SourceWaveProvider == null)
                    {
                        throw new NullReferenceException("Cannot create the Global Sample Provider because the Sample Provider of the ASIO Audio Source attached to GameObject \"" + customAsioAudioSourceFound.gameObject.name + "\" is not yet set. The process will be aborted.");
                    }
                    else
                    {
                        if (customAsioAudioSourceFound.AudioStatus == AsioAudioStatus.Playing) asioSourcesWaveProviders.Add(customAsioAudioSourceFound.SourceWaveProvider);
                        else asioSourcesWaveProviders.Add(new SilenceProvider(customAsioAudioSourceFound.SourceWaveProvider.WaveFormat));
                    }
                }
                else asioSourcesWaveProviders.Add(new SilenceProvider(new WaveFormat(TargetSampleRate, (int)TargetBitsPerSample, 1)));
            }
            try
            {
                // Step 2: Use the dictionary to connect the input channels of the ASIO driver to the output channels of the ASIO Audio Sources
                GlobalMultiplexingWaveProvider = new MultiplexingWaveProvider(asioSourcesWaveProviders, asioSourcesWaveProviders.Count);

                for (int i = 0; i < asioSourcesWaveProviders.Count; i++)
                {
                    GlobalMultiplexingWaveProvider.ConnectInputToOutput(i, i);
                }
            }
            catch (Exception e)
            {
                foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources) customAsioAudioSource.Pause();
                throw e.GetBaseException();
            }

        }

        /// <summary>
        /// Redefine all ASIO Audio Sources samples providers with offsets.
        /// </summary>
        /// <param name="reinitialiseSamples">True if the samples need to be reinitialised.</param>
        public void SetAllAsioAudioSourceSampleOffsets(bool reinitialiseSamples)
        {
            // Get the data before updating the status of sounds to play
            foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources)
            {
                if (reinitialiseSamples && customAsioAudioSource.AudioFilePathOriginal != null) customAsioAudioSource.AudioFilePath = String.Copy(customAsioAudioSource.AudioFilePathOriginal);
                customAsioAudioSource.SetAudioSamplesFromFileName(reinitialiseSamples, reinitialiseSamples, true);

                ISampleProvider offsetSampleProvider = new OffsetSampleProvider(customAsioAudioSource.SourceWaveProvider.ToSampleProvider())
                {
                    SkipOver = TimeSpan.FromSeconds(customAsioAudioSource.CurrentTimestamp)
                };

                if (TargetBitsPerSample == BitsPerSample.Bits16) customAsioAudioSource.SourceWaveProvider = offsetSampleProvider.ToWaveProvider16();
                if (TargetBitsPerSample == BitsPerSample.Bits32) customAsioAudioSource.SourceWaveProvider = offsetSampleProvider.ToWaveProvider();
            }
        }

        /// <summary>
        /// Connect to the referenced ASIO driver, set the global multiplexing provider from all ASIO Audio Sources, and play the ASIO Audio Sources marked as Playing.
        /// </summary>
        public void ConnectMixAndPlay()
        {
            try
            {
                if (AsioOutPlayer != null)
                {
                    AsioOutPlayer.Stop();
                    AsioOutPlayer.Dispose();
                    AsioOutPlayer = null;
                }

                ConnectToAsioDriver();

                SetAllAsioAudioSourceSampleOffsets(false);

                SetGlobalMultiplexingWaveProvider();

                AsioOutPlayer.Init(GlobalMultiplexingWaveProvider);
                AsioOutPlayer.Play();
            }
            catch (Exception e)
            {
                throw e.GetBaseException();
            }
        }

        private void OnDisable()
        {
            if (AsioOutPlayer != null)
            {
                AsioOutPlayer.Stop();
                AsioOutPlayer.Dispose();
                AsioOutPlayer = null;
            }
            GlobalMultiplexingWaveProvider = null;
        }

        private void OnApplicationQuit()
        {
            if (AsioOutPlayer != null)
            {
                AsioOutPlayer.Stop();
                AsioOutPlayer.Dispose();
                AsioOutPlayer = null;
            }
            GlobalMultiplexingWaveProvider = null;
        }
    }
}