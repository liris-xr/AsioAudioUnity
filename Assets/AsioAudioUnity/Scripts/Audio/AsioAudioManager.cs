using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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

        private void Awake()
        {
            ConnectToAsioDriver();
            GetAllValidAsioAudioSources();
            GetAllSamplesAsioAudioSources();
        }

        /// <summary>
        /// Establish the connection to a specified ASIO driver, whose name is set on AsioDriverName property.
        /// </summary>
        private void ConnectToAsioDriver()
        {
            if (string.IsNullOrEmpty(AsioDriverName))
            {
                Debug.LogError("The ASIO driver was not specified.");
                return;
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
                        throw e.GetBaseException();
                    }
                }
            }
            Debug.LogError("The ASIO driver \"" + AsioDriverName + "\" was not found on the system.");
            return;
        }

        /// <summary>
        /// Get all the ASIO Audio Sources from the scene, and check if they are valid.
        /// </summary>
        private void GetAllValidAsioAudioSources()
        {
            CustomAsioAudioSource[] allCustomAsioAudioSources = FindObjectsOfType<CustomAsioAudioSource>();
            List<int> channelOffsets = new List<int>();
            for (int i = 0; i < allCustomAsioAudioSources.Length; i++)
            {
                // Check if file name is empty
                if (string.IsNullOrEmpty(allCustomAsioAudioSources[i].AudioFilePath))
                {
                    Debug.LogError("The ASIO Audio Source attached to GameObject \"" + allCustomAsioAudioSources[i].gameObject.name + "\" doesn't have any Audio File Name specified. It will be ignored.");
                }
                // Check if target output channel is specified on another ASIO Audio Source
                else if (channelOffsets.Contains(allCustomAsioAudioSources[i].TargetOutputChannel))
                {
                    Debug.LogError("The targeted output channel of the ASIO Audio Source attached to GameObject \"" + allCustomAsioAudioSources[i].gameObject.name + "\" is already specified on another ASIO Audio Source, and needs to be unique. It will be ignored.");
                }
                else if (!allCustomAsioAudioSources[i].ReferencedAsioAudioManager || allCustomAsioAudioSources[i].ReferencedAsioAudioManager == this)
                {
                    CustomAsioAudioSources.Add(allCustomAsioAudioSources[i]);
                    allCustomAsioAudioSources[i].ReferencedAsioAudioManager = this;
                    channelOffsets.Add(allCustomAsioAudioSources[i].TargetOutputChannel);
                }
            }
        }

        private void GetAllSamplesAsioAudioSources()
        {
            foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources)
            {
                customAsioAudioSource.GetAudioSamplesFromFileName(true, true, true);
            }
            PlayAllAsioAudioSourcesOnAwake();
        }

        private void PlayAllAsioAudioSourcesOnAwake()
        {
            foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources)
            {
                if (customAsioAudioSource.PlayOnAwake) customAsioAudioSource.Play();
            }
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

            // Step 1: Get all the Sample Providers from the ASIO Audio Sources, convert them to Wave Providers (to manage silenced sources), and store them in a dictionary
            Dictionary<CustomAsioAudioSource, IWaveProvider> asioSourcesWaveProviders = new Dictionary<CustomAsioAudioSource, IWaveProvider>();

            foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources)
            {
                if (customAsioAudioSource.SourceSampleProvider == null)
                {
                    throw new NullReferenceException("Cannot create the Global Sample Provider because the Sample Provider of the ASIO Audio Source attached to GameObject \"" + customAsioAudioSource.gameObject.name + "\" is not yet set. The process will be aborted.");
                }
                else  
                {
                    if (customAsioAudioSource.AudioStatus == AsioAudioStatus.Playing)
                    {
                        if (TargetBitsPerSample == BitsPerSample.Bits32) asioSourcesWaveProviders.Add(customAsioAudioSource, customAsioAudioSource.SourceSampleProvider.ToWaveProvider());
                        else if (TargetBitsPerSample == BitsPerSample.Bits16) asioSourcesWaveProviders.Add(customAsioAudioSource, customAsioAudioSource.SourceSampleProvider.ToWaveProvider16());
                    }
                    else
                    {
                        asioSourcesWaveProviders.Add(customAsioAudioSource, new SilenceProvider(customAsioAudioSource.SourceSampleProvider.WaveFormat));
                    }
                }
            }

            // Step 2: Use the dictionary to connect the input channels of the ASIO driver to the output channels of the ASIO Audio Sources
            GlobalMultiplexingWaveProvider = new MultiplexingWaveProvider(asioSourcesWaveProviders.Values.ToList(), CustomAsioAudioSources.Count);

            for (int i = 0; i < asioSourcesWaveProviders.Count; i ++)
            {
                GlobalMultiplexingWaveProvider.ConnectInputToOutput(i, asioSourcesWaveProviders.ElementAt(i).Key.TargetOutputChannel- 1);
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

            ConnectToAsioDriver();
            SetGlobalMultiplexingWaveProvider();

            AsioOutPlayer.Init(GlobalMultiplexingWaveProvider);
            AsioOutPlayer.Play();
        }

        private void OnDisable()
        {
            AsioOutPlayer?.Dispose();
        }
    }
}