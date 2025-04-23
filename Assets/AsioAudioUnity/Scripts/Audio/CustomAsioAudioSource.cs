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
                if (OnAudioFilePathChanged != null)
                    OnAudioFilePathChanged.Invoke();
            }
        }

        private string _originalAudioFilePath;
        public string OriginalAudioFilePath
        {
            get { return _originalAudioFilePath; }
            private set { _originalAudioFilePath = value; }
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

        private UnityEvent _onVolumeChanged;
        public UnityEvent OnVolumeChanged
        {
            get { return _onVolumeChanged; }
            private set { _onVolumeChanged = value; }
        }

        [SerializeField][Range(0f, 1f)] private float _volume = 1f;
        public float Volume
        {
            get { return _volume; }
            set 
            {
                if (_volume == value) return;
                _volume = value; 
                if (OnVolumeChanged != null)
                    OnVolumeChanged.Invoke();
            }
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
            OnVolumeChanged = new UnityEvent();

            OnPlay = new UnityEvent();
            OnStop = new UnityEvent();
            OnPause = new UnityEvent();

            InternalStopwatch = new Stopwatch();
        }

        private void OnEnable()
        {
            if (SourceWaveProvider == null)
            {
                bool audioSourceIsValid = AddThisAsValidAsioAudioSource(ReferencedAsioAudioManager, true);
                if (audioSourceIsValid && PlayOnEnable) StartCoroutine(PlayOnEnableCoroutine());
            }
        }

        private void Start()
        {
            OnAudioFilePathChanged.AddListener(delegate { AddThisAsValidAsioAudioSource(ReferencedAsioAudioManager, true); });
            OnTargetOutputChannelChanged.AddListener(delegate { AddThisAsValidAsioAudioSource(ReferencedAsioAudioManager, false); });
            OnVolumeChanged.AddListener(delegate { AddThisAsValidAsioAudioSource(ReferencedAsioAudioManager, false); });
        }

        private bool AddThisAsValidAsioAudioSource(AsioAudioManager asioAudioManager = null, bool reinitializeSamples = false)
        {
            if (asioAudioManager != null) 
            {
                if (asioAudioManager.RequestAddAsioAudioSource(this))
                {
                    bool returnValue = SetSourceWaveProviderFromFileName(reinitializeSamples, true, true);
                    if (AudioStatus == AsioAudioStatus.Playing || AudioStatus == AsioAudioStatus.Paused) Stop();
                    return returnValue;
                }
            }
            else
            {
                AsioAudioManager asioAudioManagerInScene = FindFirstObjectByType<AsioAudioManager>();
                if (asioAudioManagerInScene != null && asioAudioManagerInScene.RequestAddAsioAudioSource(this))
                {
                    bool returnValue = SetSourceWaveProviderFromFileName(reinitializeSamples, true, true);
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
        /// Set the SourceWaveProvider from the audio file path specified in the AudioFilePath field, returns true if correctly set.
        /// </summary>
        /// <param name="convertSampleRateAndBitsPerSampleToNewFile">Convert the audio file to the target sample rate specified in the ASIO Audio Manager to a new file.</param>
        /// <param name="setOffsetTime">Set the offset time to the current timestamp.</param>
        /// <param name="convertToMono">Convert audio samples to be mixed into one channel (only works for stereo samples).</param>
        public bool SetSourceWaveProviderFromFileName(bool convertSampleRateAndBitsPerSampleToNewFile = false, bool setOffsetTime = false, bool convertToMono = false)
        {
            if (string.IsNullOrEmpty(AudioFilePath))
            {
                UnityEngine.Debug.LogError("The ASIO Audio Source attached to GameObject " + gameObject.name + " doesn't have any Audio File Name specified. It will be ignored.");
                return false;
            }

            if (!File.Exists(AudioFilePath))
            {
                UnityEngine.Debug.LogError("The ASIO Audio Source attached to GameObject " + gameObject.name + " has an invalid Audio File Name specified. It will be ignored.");
                return false;
            }

            if (ReferencedAsioAudioManager == null)
            {
                UnityEngine.Debug.LogError("The ASIO Audio Source attached to GameObject " + gameObject.name + " doesn't have any ASIO Audio Manager referenced. It will be ignored.");
                return false;
            }

            string notConvertedPath = GetNotConvertedPath(AudioFilePath);

            if (OriginalAudioFilePath == null || OriginalAudioFilePath != notConvertedPath) OriginalAudioFilePath = String.Copy(notConvertedPath);

            SourceWaveProvider = new AudioFileReader(AudioFilePath);

            if (convertSampleRateAndBitsPerSampleToNewFile)
            {
                if (ReferencedAsioAudioManager) SourceWaveProvider = ConvertSamplesAndCreateNewAudioFile(SourceWaveProvider, ReferencedAsioAudioManager.TargetSampleRate, (int)ReferencedAsioAudioManager.TargetBitsPerSample);
                else UnityEngine.Debug.LogWarning("The argument convertSampleRateAndBitsPerSampleToNewFile is set to true, but the ASIO Audio Manager (where the TargetSampleRate and TargetBitsPerSample properties are defined) is not referenced. The sample rate and bits per sample will not be converted.");
            }

            GetAudioFileTotalLength();

            if (setOffsetTime && CurrentTimestamp != 0) ((AudioFileReader)SourceWaveProvider).Position = (long)(CurrentTimestamp * SourceWaveProvider.WaveFormat.AverageBytesPerSecond);

            if (convertToMono)
            {
                if (ReferencedAsioAudioManager) SourceWaveProvider = ConvertSamplesToMono(SourceWaveProvider, (int)ReferencedAsioAudioManager.TargetBitsPerSample);
                else UnityEngine.Debug.LogWarning("The argument convertToMono is set to true, but the ASIO Audio Manager (where the TargetBitsPerSample property is defined) is not referenced. The audio samples will not be converted to mono.");
            }

            SourceWaveProvider = SetSamplesVolume(SourceWaveProvider, Volume);

            GetWaveFormatProperties(SourceWaveProvider);

            return true;
        }

        /// <summary>
        /// Convert audio samples to the target sample rate specified in argument, and store them in a new audio file.
        /// </summary>
        /// <param name="waveProvider">The audio samples to convert.</param>
        /// <param name="sampleRate">The targeted sample rate.</param>
        /// <param name="bitsPerSample">The targeted bits per sample.</param>
        public IWaveProvider ConvertSamplesAndCreateNewAudioFile(IWaveProvider waveProvider, int sampleRate, int bitsPerSample)
        {
            if (OriginalAudioFilePath == null || !File.Exists(OriginalAudioFilePath))
            {
                throw new Exception("The audio file path is either undefined or invalid.");
            }

            if (waveProvider == null)
            {
                throw new Exception("The audio samples in SourceWaveProvider are not set.");
            }

            if (waveProvider.WaveFormat.SampleRate == sampleRate && waveProvider.WaveFormat.BitsPerSample == bitsPerSample)
            {
                UnityEngine.Debug.LogWarning("The registered samples have a sample rate that is already " + sampleRate + " Hz and bits per sample of " + bitsPerSample + " bits. No conversion needed.");
                return waveProvider;
            }

            string audioFilePathConverted = Path.GetDirectoryName(OriginalAudioFilePath) + "\\" + Path.GetFileNameWithoutExtension(OriginalAudioFilePath) + "_" + sampleRate + "_" + bitsPerSample + Path.GetExtension(OriginalAudioFilePath);

            if(audioFilePathConverted == AudioFilePath)
            {
                UnityEngine.Debug.LogWarning("The file path used for conversion is the same as the original file path. No conversion will be applied.");
                return waveProvider;
            }

            if (File.Exists(audioFilePathConverted))
            {
                IWaveProvider existingWaveProvider = new AudioFileReader(audioFilePathConverted);

                if (existingWaveProvider.WaveFormat.SampleRate == sampleRate && existingWaveProvider.WaveFormat.BitsPerSample == bitsPerSample)
                {
                    UnityEngine.Debug.LogWarning("The converted file \"" + audioFilePathConverted + "\" seems to already exist and has the same sample rate and bits per sample as specified. This file will be used instead.");
                    ((AudioFileReader)existingWaveProvider).Dispose();
                    existingWaveProvider = null;
                }
                else
                {
                    UnityEngine.Debug.LogWarning("The converted file \"" + audioFilePathConverted + "\" seems to already exist but has a different sample rate and/or bits per sample than the one specified. The file will be replaced.");
                    ((AudioFileReader)existingWaveProvider).Dispose();
                    existingWaveProvider = null;

                    File.Delete(audioFilePathConverted);
                    WriteWaveProviderToFile(waveProvider, audioFilePathConverted, sampleRate, bitsPerSample);
                }
            }
            else WriteWaveProviderToFile(waveProvider, audioFilePathConverted, sampleRate, bitsPerSample);
            
            AudioFilePath = audioFilePathConverted;
            return new AudioFileReader(AudioFilePath);
        }

        private void GetAudioFileTotalLength()
        {
            if (SourceWaveProvider == null)
            {
                UnityEngine.Debug.LogError("The audio samples are not set.");
                return;
            }

            if (SourceWaveProvider is not AudioFileReader)
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
            if (parts.Length < 3) return audioFilePath; // No sample rate and bits per sample found

            string[] partsWithoutSampleRateAndBitsPerSample = parts.Take(parts.Length - 2).ToArray();
            string originalAudioFilePath = Path.Combine(Path.GetDirectoryName(audioFilePath), string.Join("_", partsWithoutSampleRateAndBitsPerSample) + Path.GetExtension(audioFilePath));

            if (File.Exists(originalAudioFilePath) && int.TryParse(parts[^2], out _) && int.TryParse(parts[^1], out _)) 
            {
                var audioFileCheck = new AudioFileReader(audioFilePath);
                if (audioFileCheck.WaveFormat.SampleRate == int.Parse(parts[^2]) && audioFileCheck.WaveFormat.BitsPerSample == int.Parse(parts[^1]))
                {
                    audioFileCheck.Dispose();
                    return originalAudioFilePath;
                }
            }

            return audioFilePath;
        }

        private void WriteWaveProviderToFile(IWaveProvider originalWaveProvider, string targetFilePath, int sampleRate = 0, int bitsPerSample = 0)
        {
            if (originalWaveProvider == null)
            {
                throw new Exception("No wave provider is passed as an argument for conversion.");
            }

            IWaveProvider convertedWaveProvider = null;

            if (sampleRate == 0)
            {
                if (bitsPerSample == 0) bitsPerSample = originalWaveProvider.WaveFormat.BitsPerSample;

                if (bitsPerSample == 16)
                {
                    if (originalWaveProvider.WaveFormat.BitsPerSample != 16) convertedWaveProvider = originalWaveProvider.ToSampleProvider().ToWaveProvider16();
                    else convertedWaveProvider = originalWaveProvider;
                    WaveFileWriter.CreateWaveFile16(targetFilePath, convertedWaveProvider.ToSampleProvider());
                }
                else if (bitsPerSample == 32)
                {
                    if (originalWaveProvider.WaveFormat.BitsPerSample != 32) convertedWaveProvider = originalWaveProvider.ToSampleProvider().ToWaveProvider();
                    else convertedWaveProvider = originalWaveProvider;
                    WaveFileWriter.CreateWaveFile(targetFilePath, convertedWaveProvider);
                }
                else
                {
                    throw new Exception("The bits per sample value is not supported. Please use 16 or 32.");
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
                    throw new Exception("The bits per sample value is not supported. Please use 16 or 32.");
                }
            }
        }

        private IWaveProvider ConvertSamplesToMono(IWaveProvider waveProvider, int bitsPerSample)
        {
            if (waveProvider == null)
            {
                UnityEngine.Debug.LogError("The audio samples are not set.");
                return waveProvider;
            }

            if (waveProvider.WaveFormat.Channels == 1) return waveProvider;

            if (waveProvider.WaveFormat.Channels != 2)
            {
                UnityEngine.Debug.LogError("The audio samples are not stereo. No conversion to mono will be applied.");
                return waveProvider;
            }

            if (bitsPerSample == 16) return waveProvider.ToSampleProvider().ToMono().ToWaveProvider16();

            else if (bitsPerSample == 32) return waveProvider.ToSampleProvider().ToMono().ToWaveProvider();

            else throw new Exception("The bits per sample value is not supported. Please use 16 or 32.");
        }

        private void GetWaveFormatProperties(IWaveProvider waveProvider)
        {
            if (waveProvider == null)
            {
                UnityEngine.Debug.LogError("The audio samples are not set.");
                return;
            }

            Channels = waveProvider.WaveFormat.Channels;
            SampleRate = waveProvider.WaveFormat.SampleRate;
            AverageBytesPerSecond = waveProvider.WaveFormat.AverageBytesPerSecond;
            BlockAlign = waveProvider.WaveFormat.BlockAlign;
            BitsPerSample = waveProvider.WaveFormat.BitsPerSample;
            ExtraSize = waveProvider.WaveFormat.ExtraSize;
        }

        private IWaveProvider SetSamplesVolume(IWaveProvider waveProvider, float volume)
        {
            if (volume < 0f || volume > 1f)
            {
                UnityEngine.Debug.LogError("Volume must be between 0 and 1.");
                return waveProvider;
            }
            if (SourceWaveProvider == null)
            {
                UnityEngine.Debug.LogError("Cannot set the volume because the audio samples are not set.");
                return waveProvider;
            }
            if (ReferencedAsioAudioManager == null)
            {
                UnityEngine.Debug.LogError("Cannot set the volume because the ASIO Audio Manager is not referenced.");
                return waveProvider;
            }

            if (ReferencedAsioAudioManager.TargetBitsPerSample == AsioAudioUnity.BitsPerSample.Bits16) 
                return new VolumeWaveProvider16(SourceWaveProvider) { Volume = volume };

            else if (ReferencedAsioAudioManager.TargetBitsPerSample == AsioAudioUnity.BitsPerSample.Bits32)
                return new VolumeSampleProvider(SourceWaveProvider.ToSampleProvider()) { Volume = volume }.ToWaveProvider();

            else throw new Exception("The bits per sample value is not supported. Please use 16 or 32.");
            
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
            while (ReferencedAsioAudioManager == null) yield return null;
            while (SourceWaveProvider.WaveFormat.SampleRate != ReferencedAsioAudioManager.TargetSampleRate && SourceWaveProvider.WaveFormat.BitsPerSample != (int)ReferencedAsioAudioManager.TargetBitsPerSample) yield return null;
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
                ReferencedAsioAudioManager.ConnectMixAndPlay(false);
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


