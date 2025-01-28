using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UnityEngine;

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

        private MultiplexingWaveProvider _globalWaveProvider;
        public MultiplexingWaveProvider GlobalWaveProvider
        {
            get { return _globalWaveProvider; }
            private set { _globalWaveProvider = value; }
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
            AsioOutPlayer = ConnectToAsioDriver(AsioDriverName);
            GetAllValidAsioAudioSources();
            GetAllSamplesAsioAudioSources();
        }

        private void Update()
        {
            UpdateAllAsioAudioSourcesTimestamps();
            CheckForAudioSourceEnd();
        }

        /// <summary>
        /// Establish the connection to a specified ASIO driver.
        /// </summary>
        /// <param name="name">The name of the ASIO driver.</param>
        private AsioOut ConnectToAsioDriver(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                Debug.LogError("The ASIO driver was not specified.");
                return null;
            }

            AsioOut asioOutPlayer;
            string[] driverNames = AsioOut.GetDriverNames();
            foreach (var driverName in driverNames)
            {
                if (driverName == name)
                {
                    try
                    {
                        asioOutPlayer = new AsioOut(driverName);
                        AsioDriverInputChannelCount = asioOutPlayer.DriverInputChannelCount;

                        return asioOutPlayer;
                    }
                    catch (Exception e)
                    {
                        throw e.GetBaseException();
                    }
                }
            }
            return null;
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

        private void UpdateAllAsioAudioSourcesTimestamps()
        {
            foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources)
            {
                if (customAsioAudioSource.AudioStatus == AsioAudioStatus.Playing)
                {
                    customAsioAudioSource.ActualTimestamp += Time.deltaTime;
                }
            }
        }

        private void CheckForAudioSourceEnd()
        {
            foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources)
            {
                if (customAsioAudioSource.ActualTimestamp >= customAsioAudioSource.AudioFileTotalLength)
                {
                    if (customAsioAudioSource.Loop) customAsioAudioSource.StopAndPlay();
                    else customAsioAudioSource.Stop();
                }
            }
        }

        /// <summary>
        /// Setup the global provider by associating each ASIO Audio Source to a specific output channel on the ASIO driver.
        /// </summary>
        private void MixAllAudioSamples()
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
            GlobalWaveProvider = new MultiplexingWaveProvider(asioSourcesWaveProviders.Values.ToList(), CustomAsioAudioSources.Count);

            for (int i = 0; i < asioSourcesWaveProviders.Count; i ++)
            {
                GlobalWaveProvider.ConnectInputToOutput(i, asioSourcesWaveProviders.ElementAt(i).Key.TargetOutputChannel- 1);
            }
        }

        /// <summary>
        /// Redefine all ASIO Audio Sources audio samples according to the request of the ASIO Audio Source set in argument.
        /// </summary>
        /// <param name="customAsioAudioSourceToUpdate">The ASIO Audio Source to update.</param>
        /// <param name="asioAudioRequest">The requested updaye on the ASIO Audio Source.</param>
        public void RedefineAllSamplesAsioAudioSourcesWithRequest(CustomAsioAudioSource customAsioAudioSourceRequested, AsioAudioRequest asioAudioRequest)
        {
            Debug.Log("Requesting an update on ASIO Audio Source : " + customAsioAudioSourceRequested.gameObject.name + " with request : " + asioAudioRequest);

            // Get the data before updating the status of sounds to play
            foreach (CustomAsioAudioSource customAsioAudioSource in CustomAsioAudioSources)
            {
                customAsioAudioSource.GetAudioSamplesFromFileName(false, false, true);

                if (customAsioAudioSource == customAsioAudioSourceRequested)
                {
                    if ((customAsioAudioSource.AudioStatus == AsioAudioStatus.Playing || customAsioAudioSource.AudioStatus == AsioAudioStatus.Paused) && asioAudioRequest == AsioAudioRequest.Stop)
                    {
                        customAsioAudioSource.ActualTimestamp = 0;
                    }
                }
                
                customAsioAudioSource.SourceSampleProvider = new OffsetSampleProvider(customAsioAudioSource.SourceSampleProvider)
                {
                    SkipOver = TimeSpan.FromSeconds(customAsioAudioSource.ActualTimestamp)
                };
            }
        }

        /// <summary>
        /// Connect to the referenced ASIO driver, set the global audio samples provider and play the ASIO Audio Sources marked as Playing.
        /// </summary>
        public void ConnectToAsioMixAndPlay()
        {
            TimeSpentPlayingBetweenUpdates = 0;

            AsioOutPlayer = ConnectToAsioDriver(AsioDriverName);
            MixAllAudioSamples();

            AsioOutPlayer.Init(GlobalWaveProvider);
            AsioOutPlayer.Play();
        }

        private void OnDisable()
        {
            AsioOutPlayer?.Dispose();
        }
    }
}