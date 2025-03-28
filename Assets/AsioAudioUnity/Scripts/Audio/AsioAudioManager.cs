using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.IO;

namespace AsioAudioUnity
{
    public class AsioAudioManager : MonoBehaviour
    {
        [SerializeField] private string _asioDriverName;
        public string AsioDriverName
        {
            get { return _asioDriverName; }
        }

        [SerializeField] private int _targetSampleRate = 48000;
        public int TargetSampleRate
        {
            get { return _targetSampleRate; }
        }

        [SerializeField] private BitsPerSample _targetBitsPerSample = BitsPerSample.Bits32;
        public BitsPerSample TargetBitsPerSample
        {
            get { return _targetBitsPerSample; }
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
        private string _connectionStatusGUI = "";
        private int _currentAsioAudioSourceGUIIndex = 0;
        private string _currentAsioAudioSourceGUIStatus = "Stopped";

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

        private float _timeSpentPlayingBetweenUpdates;
        public float TimeSpentPlayingBetweenUpdates
        {
            get { return _timeSpentPlayingBetweenUpdates; }
            private set { _timeSpentPlayingBetweenUpdates = value; }
        }

        private void OnGUI()
        {
            if (!DisplayInfoOnGameWindow) return;

            float horizontalOffset = 20;
            float verticalOffset = 280;

            GUIStyle headerStyle = new GUIStyle();
            headerStyle.fontSize = 20;
            headerStyle.fontStyle = FontStyle.Bold;

            GUIStyle standardStyle = new GUIStyle();
            standardStyle.fontSize = 15;
            standardStyle.wordWrap = true;
            standardStyle.clipping = TextClipping.Clip;

            GUI.Box(new Rect(5, 5, 2 * verticalOffset, 14 * horizontalOffset), "");

            GUI.Label(new Rect(10, 10, 200, 20), "ASIO Audio Manager", headerStyle);

            GUI.Label(new Rect(10, 20 + horizontalOffset, verticalOffset - 10, horizontalOffset), "ASIO Driver name:", standardStyle);
            GUI.Label(new Rect(10 + verticalOffset, 20 + horizontalOffset, verticalOffset - 10, horizontalOffset), AsioDriverName, standardStyle);

            GUI.Label(new Rect(10, 20 + horizontalOffset * 2, verticalOffset - 10, horizontalOffset), "ASIO Driver connection:", standardStyle);

            GUIStyle connectionStyle = new GUIStyle();
            connectionStyle.fontSize = 15;
            connectionStyle.normal.textColor = _connectionStatusGUI == "OK" ? Color.green : Color.red;

            _connectionStatusGUI = (AsioOutPlayer != null) ? "OK" : "Error";

            GUI.Label(new Rect(10 + verticalOffset, 20 + horizontalOffset * 2, verticalOffset - 10, horizontalOffset), _connectionStatusGUI, connectionStyle);

            GUI.Label(new Rect(10, 20 + horizontalOffset * 3, verticalOffset - 10, horizontalOffset), "Number of supported channels:", standardStyle);
            GUI.Label(new Rect(10 + verticalOffset, 20 + horizontalOffset * 3, verticalOffset - 10, horizontalOffset), AsioDriverInputChannelCount.ToString(), standardStyle);

            GUI.Label(new Rect(10, 20 + horizontalOffset * 4, verticalOffset - 10, horizontalOffset), "Target Sample Rate:", standardStyle);
            GUI.Label(new Rect(10 + verticalOffset, 20 + horizontalOffset * 4, verticalOffset - 10, horizontalOffset), TargetSampleRate.ToString() + " Hz", standardStyle);

            GUI.Label(new Rect(10, 20 + horizontalOffset * 5, verticalOffset - 10, horizontalOffset), "Target Bits Per Sample:", standardStyle);
            GUI.Label(new Rect(10 + verticalOffset, 20 + horizontalOffset * 5, verticalOffset - 10, horizontalOffset), TargetBitsPerSample.ToString(), standardStyle);

            GUI.Label(new Rect(10, 20 + horizontalOffset * 6, verticalOffset - 10, horizontalOffset), "Number of Custom ASIO Audio Sources:", standardStyle);
            GUI.Label(new Rect(10 + verticalOffset, 20 + horizontalOffset * 6, verticalOffset - 10, horizontalOffset), CustomAsioAudioSources.Count.ToString(), standardStyle);

            if (CustomAsioAudioSources.Count == 0) return;
            if (_currentAsioAudioSourceGUIIndex < 0 || _currentAsioAudioSourceGUIIndex >= CustomAsioAudioSources.Count) _currentAsioAudioSourceGUIIndex = 0;

            GUI.Box(new Rect(10, 20 + horizontalOffset * 7, 2 * verticalOffset - 10, 6 * horizontalOffset), "");

            if (GUI.Button(new Rect(15, 25 + horizontalOffset * 7, 50, horizontalOffset), "<-"))
            {
                _currentAsioAudioSourceGUIIndex--;
                if (_currentAsioAudioSourceGUIIndex < 0) _currentAsioAudioSourceGUIIndex = CustomAsioAudioSources.Count - 1;
            }
            if (GUI.Button(new Rect(2 * verticalOffset - 55, 25 + horizontalOffset * 7, 50, horizontalOffset), "->"))
            {
                _currentAsioAudioSourceGUIIndex++;
                if (_currentAsioAudioSourceGUIIndex >= CustomAsioAudioSources.Count) _currentAsioAudioSourceGUIIndex = 0;
            }

            GUIStyle asioSourceTitleStyle = new GUIStyle();
            asioSourceTitleStyle.fontSize = 15;
            asioSourceTitleStyle.fontStyle = FontStyle.Bold;
            asioSourceTitleStyle.alignment = TextAnchor.MiddleCenter;

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

            if (GUI.Button(new Rect(15, 25 + horizontalOffset * 11, (2 * verticalOffset / 3) - 10, 3 * horizontalOffset / 2), "Play"))
            {
                CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex].Play();
            }

            if (GUI.Button(new Rect(10 + (2 * verticalOffset / 3), 25 + horizontalOffset * 11, (2 * verticalOffset / 3) - 10, 3 * horizontalOffset / 2), "Pause"))
            {
                CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex].Pause();
            }

            if (GUI.Button(new Rect(5 + (4 * verticalOffset / 3), 25 + horizontalOffset * 11, (2 * verticalOffset / 3) - 10, 3 * horizontalOffset / 2), "Stop"))
            {
                CustomAsioAudioSources[_currentAsioAudioSourceGUIIndex].Stop();
            }    
        }

        private void Awake()
        {
            if (!ConnectToAsioDriver()) return;
        }

        /// <summary>
        /// Establish the connection to a specified ASIO driver, whose name is set on AsioDriverName property.
        /// </summary>
        private bool ConnectToAsioDriver()
        {
            if (string.IsNullOrEmpty(AsioDriverName))
            {
                Debug.LogError("The ASIO driver was not specified.");
                return false;
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
                        return true;
                    }
                    catch (Exception e)
                    {
                        throw e.GetBaseException();
                    }
                }
            }
            Debug.LogError("The ASIO driver \"" + AsioDriverName + "\" was not found on the system.");
            return false;
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
                Debug.LogError("There is more audio channels from all ASIO Audio Sources (" + CustomAsioAudioSources.Count + ") than there is input channels on the ASIO driver (" + AsioDriverInputChannelCount + ")." +
                    "Consider removing ASIO Audio Sources or reducing the amount of input channels (TargetSampleChannelCount) per track in an ASIO Audio Source.");
                return;
            }
            if (CustomAsioAudioSources.Select((customAsioAudioSource) => customAsioAudioSource.TargetOutputChannel).Distinct().Count() != CustomAsioAudioSources.Count)
            {
                Debug.LogError("There are ASIO Audio Sources with the same Target Output Channel. Each ASIO Audio Source must have a unique Target Output Channel.");
                return;
            }

            // Step 1: Get all the Sample Providers from the ASIO Audio Sources, convert them to Wave Providers (to manage silenced sources), and store them in a dictionary
            List<IWaveProvider> asioSourcesWaveProviders = new List<IWaveProvider>();
            int numberOfOutputChannels = CustomAsioAudioSources.Max((customAsioAudioSource) => customAsioAudioSource.TargetOutputChannel);

            for (int i = 0; i < numberOfOutputChannels; i++)
            {
                CustomAsioAudioSource customAsioAudioSourceFound = CustomAsioAudioSources.Find((CustomAsioAudioSource) => CustomAsioAudioSource.TargetOutputChannel == i + 1);

                if (customAsioAudioSourceFound != null)
                {
                    if (customAsioAudioSourceFound.SourceSampleProvider == null)
                    {
                        throw new NullReferenceException("Cannot create the Global Sample Provider because the Sample Provider of the ASIO Audio Source attached to GameObject \"" + customAsioAudioSourceFound.gameObject.name + "\" is not yet set. The process will be aborted.");
                    }
                    else
                    {
                        if (customAsioAudioSourceFound.AudioStatus == AsioAudioStatus.Playing)
                        {
                            if (TargetBitsPerSample == BitsPerSample.Bits32) asioSourcesWaveProviders.Add(customAsioAudioSourceFound.SourceSampleProvider.ToWaveProvider());
                            else if (TargetBitsPerSample == BitsPerSample.Bits16) asioSourcesWaveProviders.Add(customAsioAudioSourceFound.SourceSampleProvider.ToWaveProvider16());
                        }
                        else asioSourcesWaveProviders.Add(new SilenceProvider(customAsioAudioSourceFound.SourceSampleProvider.WaveFormat));
                        
                    }
                }
                else asioSourcesWaveProviders.Add(new SilenceProvider(new WaveFormat(TargetSampleRate, (int)TargetBitsPerSample, 1)));
            }

            // Step 2: Use the dictionary to connect the input channels of the ASIO driver to the output channels of the ASIO Audio Sources
            GlobalMultiplexingWaveProvider = new MultiplexingWaveProvider(asioSourcesWaveProviders, asioSourcesWaveProviders.Count);

            for (int i = 0; i < asioSourcesWaveProviders.Count; i++)
            {
                GlobalMultiplexingWaveProvider.ConnectInputToOutput(i, i);
            }
        }

        /// <summary>
        /// Redefine all ASIO Audio Sources samples providers with offsets.
        /// </summary>
        public void SetAllAsioAudioSourceSampleOffsets()
        {
            // Get the data before updating the status of sounds to play
            foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources)
            {
                customAsioAudioSource.GetAudioSamplesFromFileName(false, false, true);

                customAsioAudioSource.SourceSampleProvider = new OffsetSampleProvider(customAsioAudioSource.SourceSampleProvider)
                {
                    SkipOver = TimeSpan.FromSeconds(customAsioAudioSource.ActualTimestamp)
                };
            }
        }

        /// <summary>
        /// Connect to the referenced ASIO driver, set the global mixed audio sample provider from all ASIO Audio Sources, and play the ASIO Audio Sources marked as Playing.
        /// </summary>
        public void ConnectMixAndPlay()
        {
            TimeSpentPlayingBetweenUpdates = 0;

            if (!ConnectToAsioDriver()) return;
            SetGlobalMultiplexingWaveProvider();

            AsioOutPlayer.Init(GlobalMultiplexingWaveProvider);
            AsioOutPlayer.Play();
        }

        private void OnDisable()
        {
            AsioOutPlayer?.Dispose();
        }

        private void OnApplicationQuit()
        {
            AsioOutPlayer?.Dispose();
        }
    }
}