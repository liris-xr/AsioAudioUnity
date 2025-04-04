using System;
using System.IO;
using System.Diagnostics;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AsioAudioUnity
{
    [System.Serializable]
    public class CustomAsioAudioSource : MonoBehaviour
    {
        private UnityEvent _onAudioFilePathChanged;
        public UnityEvent OnAudioFilePathChanged
        {
            get { return _onAudioFilePathChanged; }
            private set { _onAudioFilePathChanged = value; }
        }

        [SerializeField][ReadOnly] private string _audioFilePath;
        public string AudioFilePath
        {
            get { return _audioFilePath; }
            set 
            {
                if (_audioFilePath == value) return;
                _audioFilePath = value;
                _audioFilePathOriginal = null;
                if (OnAudioFilePathChanged != null)
                    OnAudioFilePathChanged.Invoke();
            }
        }

        private string _audioFilePathOriginal;
        public string AudioFilePathOriginal
        {
            get { return _audioFilePathOriginal; }
            private set { _audioFilePathOriginal = value; }
        }

        private UnityEvent _onTargetOutputChannelChanged;
        public UnityEvent OnTargetOutputChannelChanged
        {
            get { return _onTargetOutputChannelChanged; }
            private set { _onTargetOutputChannelChanged = value; }
        }

        [SerializeField] private int _targetOutputChannel = 0;
        public int TargetOutputChannel
        {
            get { return _targetOutputChannel; }
            set 
            {
                if (_targetOutputChannel == value) return;
                _targetOutputChannel = value;
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

        [SerializeField] private bool _playOnEnable = true;
        public bool PlayOnEnable
        {
            get { return _playOnEnable; }
            set { _playOnEnable = value; }
        }

        [SerializeField] private bool _loop = false;
        public bool Loop
        {
            get { return _loop; }
            set { _loop = value; }
        }

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
        public double AudioFileTotalLength
        {
            get { return _audioFileTotalLength; }
            private set { _audioFileTotalLength = value; }
        }

        private IWaveProvider _sourceWaveProvider;
        public IWaveProvider SourceWaveProvider
        {
            get { return _sourceWaveProvider; }
            set { _sourceWaveProvider = value; }
        }

        [SerializeField][ReadOnly] private AsioAudioStatus _audioStatus = AsioAudioStatus.Stopped;
        public AsioAudioStatus AudioStatus
        {
            get { return _audioStatus; }
            private set { _audioStatus = value; }
        }

        [SerializeField][ReadOnly] private double _currentTimestamp = 0;
        public double CurrentTimestamp
        {
            get { return _currentTimestamp; }
            private set { _currentTimestamp = value; }
        }

        private Stopwatch _internalStopwatch;
        public Stopwatch InternalStopwatch
        {
            get { return _internalStopwatch; }
            private set { _internalStopwatch = value; }
        }

        [SerializeField] private UnityEvent _onPlay;
        public UnityEvent OnPlay
        {
            get { return _onPlay; }
            set { _onPlay = value; }
        }

        [SerializeField] private UnityEvent _onStop;
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

        private void Awake()
        {
            OnAudioFilePathChanged = new UnityEvent();
            OnTargetOutputChannelChanged = new UnityEvent();

            OnPlay = new UnityEvent();
            OnStop = new UnityEvent();
            OnPause = new UnityEvent();

            InternalStopwatch = new Stopwatch();
        }

        private void OnEnable()
        {
            if (SourceWaveProvider == null)
            {
                bool audioSourceIsValid = AddThisAsValidAsioAudioSource(ReferencedAsioAudioManager);
                if (audioSourceIsValid && PlayOnEnable) StartCoroutine(PlayOnEnableCoroutine());
            }
        }

        private void Start()
        {
            OnAudioFilePathChanged.AddListener(delegate { AddThisAsValidAsioAudioSource(ReferencedAsioAudioManager); });
            OnTargetOutputChannelChanged.AddListener(delegate { AddThisAsValidAsioAudioSource(ReferencedAsioAudioManager); });
        }

        private bool AddThisAsValidAsioAudioSource(AsioAudioManager asioAudioManager = null)
        {
            if (asioAudioManager != null) 
            {
                if (asioAudioManager.RequestAddAsioAudioSource(this))
                {
                    bool returnValue = SetAudioSamplesFromFileName(true, true, true);
                    if (AudioStatus == AsioAudioStatus.Playing || AudioStatus == AsioAudioStatus.Paused) Stop();
                    return returnValue;
                }
            }
            else
            {
                AsioAudioManager asioAudioManagerInScene = FindFirstObjectByType<AsioAudioManager>();
                if (asioAudioManagerInScene != null && asioAudioManagerInScene.RequestAddAsioAudioSource(this))
                {
                    bool returnValue = SetAudioSamplesFromFileName(true, true, true);
                    if (AudioStatus == AsioAudioStatus.Playing || AudioStatus == AsioAudioStatus.Paused) Stop();
                    return returnValue;
                }
            }
            return false;
        }

        private void Update()
        {
            if (!ReferencedAsioAudioManager) return;

            UpdateTimestamp();
            CheckForAudioEnd();
        }

        private void UpdateTimestamp()
        {
            if (ReferencedAsioAudioManager != null && AudioStatus == AsioAudioStatus.Playing) CurrentTimestamp = InternalStopwatch.Elapsed.TotalSeconds;
        }

        private void CheckForAudioEnd()
        {
            if (CurrentTimestamp >= AudioFileTotalLength)
            {
                if (Loop) Restart();
                else
                {
                    AudioStatus = AsioAudioStatus.Stopped;
                    CurrentTimestamp = 0;
                    InternalStopwatch.Reset();
                }
            }
        }

        /// <summary>
        /// Get the ASIO Audio Source samples with the audio file specified in the Audio File Name field, retruns true if correctly get.
        /// </summary>
        /// <param name="convertSampleRateToNewFile">Convert the audio file to the target sample rate specified in the ASIO Audio Manager to a new file.</param>
        /// <param name="getAudioFileLength">Get the total length of the audio file and put the value in AudioFileTotalLength.</param>
        /// <param name="convertToMono">Convert audio samples to be mixed into one channel (only works for stereo samples).</param>
        public bool SetAudioSamplesFromFileName(bool convertSampleRateToNewFile = false, bool getAudioFileLength = false, bool convertToMono = false)
        {
            if (string.IsNullOrEmpty(AudioFilePath))
            {
                UnityEngine.Debug.LogError("The ASIO Audio Source attached to GameObject \"" + gameObject.name + "\" doesn't have any Audio File Name specified. It will be ignored.");
                return false;
            }

            if (AudioFilePathOriginal == null) AudioFilePathOriginal = String.Copy(GetNotConvertedPath(AudioFilePath));
            else if (GetNotConvertedPath(AudioFilePath) != AudioFilePathOriginal) AudioFilePathOriginal = String.Copy(GetNotConvertedPath(AudioFilePath));

            SourceWaveProvider = new AudioFileReader(AudioFilePath);

            if (convertSampleRateToNewFile)
            {
                if (ReferencedAsioAudioManager) ConvertAudioSamples(ReferencedAsioAudioManager.TargetSampleRate, (int)ReferencedAsioAudioManager.TargetBitsPerSample);
                else UnityEngine.Debug.LogWarning("The argument convertSampleRate is set to true, but the ASIO Audio Manager (where the TargetSampleRate property is defined) is not referenced. The sample rate will not be converted.");
            }

            if (getAudioFileLength) GetAudioFileTotalLength();

            if (convertToMono)
            {
                if (ReferencedAsioAudioManager) ConvertToMono((int)ReferencedAsioAudioManager.TargetBitsPerSample);
                else UnityEngine.Debug.LogWarning("The argument convertToMono is set to true, but the ASIO Audio Manager (where the TargetBitsPerSample property is defined) is not referenced. The audio samples will not be converted to mono.");
            }

            GetWaveFormatProperties();

            return true;
        }

        /// <summary>
        /// Convert audio samples to the target sample rate specified in argument, and store them in a new audio file.
        /// </summary>
        /// <param name="sampleRate">The targeted sample rate.</param>
        public void ConvertAudioSamples(int sampleRate, int bitsPerSample)
        {
            if (AudioFilePathOriginal == null || !File.Exists(AudioFilePathOriginal))
            {
                UnityEngine.Debug.LogError("The audio file path is either undefined or invalid.");
                return;
            }

            if (SourceWaveProvider == null)
            {
                UnityEngine.Debug.LogError("The audio samples in SourceWaveProvider are not set.");
                return;
            }

            if (SourceWaveProvider.WaveFormat.SampleRate == sampleRate)
            {
                UnityEngine.Debug.LogWarning("The sample rate of the file is already " + sampleRate + " Hz. No conversion needed.");
                return;
            }

            string audioFilePathConverted = Path.GetDirectoryName(AudioFilePathOriginal) + "\\" + Path.GetFileNameWithoutExtension(AudioFilePathOriginal) + "_" + sampleRate + Path.GetExtension(AudioFilePathOriginal);
            if (File.Exists(audioFilePathConverted))
            {
                UnityEngine.Debug.LogWarning("The converted file " + audioFilePathConverted + " seems to already exist. This file will be selected instead without sample rate conversion applied.");

                IWaveProvider existingWaveProvider = new AudioFileReader(audioFilePathConverted);
                if (existingWaveProvider.WaveFormat.SampleRate != sampleRate)
                {
                    UnityEngine.Debug.LogWarning("The converted file " + audioFilePathConverted + " has a different sample rate than the one specified. The file will be erased.");
                    existingWaveProvider = null;
                    File.Delete(audioFilePathConverted);

                    ConvertWaveProviderToFile(SourceWaveProvider, audioFilePathConverted, sampleRate, bitsPerSample);
                }
                if (existingWaveProvider.WaveFormat.BitsPerSample != bitsPerSample)
                {
                    UnityEngine.Debug.LogWarning("The converted file " + audioFilePathConverted + " has a different bits per sample than the one specified. The file will be erased.");
                    existingWaveProvider = null;
                    File.Delete(audioFilePathConverted);

                    ConvertWaveProviderToFile(SourceWaveProvider, audioFilePathConverted, sampleRate, bitsPerSample);
                }
            }
            else ConvertWaveProviderToFile(SourceWaveProvider, audioFilePathConverted, sampleRate, bitsPerSample);
            
            AudioFilePath = audioFilePathConverted;
            SourceWaveProvider = new AudioFileReader(AudioFilePath);
        }

        private void GetAudioFileTotalLength()
        {
            if (SourceWaveProvider == null)
            {
                UnityEngine.Debug.LogError("The audio samples are not set.");
                return;
            }

            if (SourceWaveProvider.GetType() != typeof(AudioFileReader))
            {
                UnityEngine.Debug.LogError("The audio samples are not from an AudioFileReader.");
                return;
            }

            AudioFileTotalLength = ((AudioFileReader)SourceWaveProvider).TotalTime.TotalSeconds;
        }

        private string GetNotConvertedPath(string audioFilePath)
        {
            if (string.IsNullOrEmpty(audioFilePath)) return null;

            string fileName = Path.GetFileNameWithoutExtension(audioFilePath);
            string[] parts = fileName.Split("_");
            string[] partsWithoutSampleRate = parts.Take(parts.Length - 1).ToArray();

            string originalAudioFilePath = Path.Combine(Path.GetDirectoryName(audioFilePath), string.Join("_", partsWithoutSampleRate) + Path.GetExtension(audioFilePath));

            if (File.Exists(originalAudioFilePath) && int.TryParse(parts[^1], out _)) 
            {
                if (new AudioFileReader(audioFilePath).WaveFormat.SampleRate == int.Parse(parts[^1]))
                {
                    return originalAudioFilePath;
                }
            }

            return audioFilePath;
        }

        private void ConvertWaveProviderToFile(IWaveProvider originalWaveProvider, string targetFilePath, int sampleRate = 0, int bitsPerSample = 0)
        {
            if (originalWaveProvider == null)
            {
                UnityEngine.Debug.LogError("No wave provider is passed as an argument for conversion.");
                return;
            }

            IWaveProvider convertedWaveProvider = null;

            if (sampleRate == 0)
            {
                if (bitsPerSample == 0) bitsPerSample = originalWaveProvider.WaveFormat.BitsPerSample;

                if (bitsPerSample == 16)
                {
                    convertedWaveProvider = originalWaveProvider.ToSampleProvider().ToWaveProvider16();
                    WaveFileWriter.CreateWaveFile16(targetFilePath, convertedWaveProvider.ToSampleProvider());
                }
                else if (bitsPerSample == 32)
                {
                    convertedWaveProvider = originalWaveProvider.ToSampleProvider().ToWaveProvider();
                    WaveFileWriter.CreateWaveFile(targetFilePath, convertedWaveProvider);
                }
                else
                {
                    UnityEngine.Debug.LogError("The bits per sample value is not supported. Please use 16 or 32.");
                    return;
                }
            }

            else /*if (sampleRate != 0)*/
            {
                if (bitsPerSample == 0) bitsPerSample = originalWaveProvider.WaveFormat.BitsPerSample;

                if (bitsPerSample == 16)
                {
                    convertedWaveProvider = new WdlResamplingSampleProvider(originalWaveProvider.ToSampleProvider(), sampleRate).ToWaveProvider16();
                    WaveFileWriter.CreateWaveFile16(targetFilePath, convertedWaveProvider.ToSampleProvider());
                }
                else if (bitsPerSample == 32)
                {
                    convertedWaveProvider = new WdlResamplingSampleProvider(originalWaveProvider.ToSampleProvider(), sampleRate).ToWaveProvider();
                    WaveFileWriter.CreateWaveFile(targetFilePath, convertedWaveProvider);
                }
                else
                {
                    UnityEngine.Debug.LogError("The bits per sample value is not supported. Please use 16 or 32.");
                    return;
                }
            }

            return;
        }

        private void ConvertToMono(int bitsPerSample)
        {
            if (SourceWaveProvider == null)
            {
                UnityEngine.Debug.LogError("The audio samples are not set.");
                return;
            }
            if (SourceWaveProvider.WaveFormat.Channels != 2)
            {
                UnityEngine.Debug.LogError("The audio samples are not stereo. No conversion to mono will be applied.");
                return;
            }
            else
            {
                SourceWaveProvider = SourceWaveProvider.ToSampleProvider().ToMono().ToWaveProvider();
            }
        }

        private void GetWaveFormatProperties()
        {
            if (SourceWaveProvider == null)
            {
                UnityEngine.Debug.LogError("The audio samples are not set.");
                return;
            }

            Channels = SourceWaveProvider.WaveFormat.Channels;
            SampleRate = SourceWaveProvider.WaveFormat.SampleRate;
            AverageBytesPerSecond = SourceWaveProvider.WaveFormat.AverageBytesPerSecond;
            BlockAlign = SourceWaveProvider.WaveFormat.BlockAlign;
            BitsPerSample = SourceWaveProvider.WaveFormat.BitsPerSample;
            ExtraSize = SourceWaveProvider.WaveFormat.ExtraSize;
        }

        /// <summary>
        /// Will send a signal to the referenced ASIO Audio Manager to play the current audio file, and triggers OnPlay.
        /// </summary>
        public void Play()
        {
            if (AudioStatus == AsioAudioStatus.Playing) return;
            InternalStopwatch.Start();
            SendRequestAndReset(AsioAudioStatus.Playing);
            OnPlay.Invoke();
        }

        /// <summary>
        /// Will send a signal to the referenced ASIO Audio Manager to stop the current audio file, and triggers OnStop.
        /// </summary>
        public void Stop()
        {
            if (AudioStatus == AsioAudioStatus.Stopped) return;
            CurrentTimestamp = 0;
            InternalStopwatch.Reset();
            SendRequestAndReset(AsioAudioStatus.Stopped);
            OnStop.Invoke();
        }

        /// <summary>
        /// Will send a signal to the referenced ASIO Audio Manager to pause the current audio file, and triggers OnPause.
        /// </summary>
        public void Pause()
        {
            if (AudioStatus == AsioAudioStatus.Paused || AudioStatus == AsioAudioStatus.Stopped) return;
            InternalStopwatch.Stop();
            SendRequestAndReset(AsioAudioStatus.Paused);
            OnPause.Invoke();
        }

        /// <summary>
        /// Will send a signal to the referenced ASIO Audio Manager to restart the current audio file. (This function is used for loops, and will not trigger events).
        /// </summary>
        public void Restart()
        {
            CurrentTimestamp = 0;
            InternalStopwatch.Restart();
            SendRequestAndReset(AsioAudioStatus.Playing);
        }

        private IEnumerator PlayOnEnableCoroutine()
        {
            while (SourceWaveProvider.WaveFormat.SampleRate != ReferencedAsioAudioManager.TargetSampleRate) yield return null;
            while (ReferencedAsioAudioManager && ReferencedAsioAudioManager.AsioOutPlayer == null) yield return null;
            Play();
        }

        private void SendRequestAndReset(AsioAudioStatus newAsioAudioStatus)
        {
            if (ReferencedAsioAudioManager == null)
            {
                UnityEngine.Debug.LogError("Can't update status " + newAsioAudioStatus + ", because the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                if (AudioStatus == AsioAudioStatus.Playing && newAsioAudioStatus != AsioAudioStatus.Playing) AudioStatus = newAsioAudioStatus;
                return;
            }
            if (ReferencedAsioAudioManager.AsioOutPlayer == null)
            {
                UnityEngine.Debug.LogError("Can't update status " + newAsioAudioStatus + ", because the ASIO driver from the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                if (AudioStatus == AsioAudioStatus.Playing && newAsioAudioStatus != AsioAudioStatus.Playing) AudioStatus = newAsioAudioStatus;
                return;
            }

            AsioAudioStatus audioStatusTemp = (AsioAudioStatus)(int)AudioStatus;

            try
            {
                AudioStatus = newAsioAudioStatus;
                ReferencedAsioAudioManager.ConnectMixAndPlay();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("An error occurred while trying to update to status " + newAsioAudioStatus + " from this source (" + gameObject.name + "): " + e.Message);
                AudioStatus = audioStatusTemp;
            }
        }

        private void OnDisable()
        {
            if (ReferencedAsioAudioManager)
            {
                Stop();
                ReferencedAsioAudioManager.RequestRemoveAsioAudioSource(this);
            }
            SourceWaveProvider = null;
        }

        private void OnApplicationQuit()
        {
            if (ReferencedAsioAudioManager)
            {
                ReferencedAsioAudioManager.RequestRemoveAsioAudioSource(this);
            }
            SourceWaveProvider = null;
        }
    }
}


