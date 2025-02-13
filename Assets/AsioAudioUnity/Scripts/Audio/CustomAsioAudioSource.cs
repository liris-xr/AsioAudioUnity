using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Diagnostics;

namespace AsioAudioUnity
{
    public class CustomAsioAudioSource : MonoBehaviour
    {
        [SerializeField] private string _audiofilePath;
        public string AudioFilePath
        {
            get { return _audiofilePath; }
            set { _audiofilePath = value; }
        }

        [SerializeField] private int _targetOutputChannel = 0;
        public int TargetOutputChannel
        {
            get { return _targetOutputChannel; }
            set { _targetOutputChannel = value; }
        }

        [SerializeField] private AsioAudioManager _referencedAsioAudioManager;
        public AsioAudioManager ReferencedAsioAudioManager
        {
            get { return _referencedAsioAudioManager; }
            set { _referencedAsioAudioManager = value; }
        }

        [SerializeField] private bool _playOnAwake = true;
        public bool PlayOnAwake
        {
            get { return _playOnAwake; }
            set { _playOnAwake = value; }
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

        private ISampleProvider _sourceSampleProvider;
        public ISampleProvider SourceSampleProvider
        {
            get { return _sourceSampleProvider; }
            set { _sourceSampleProvider = value; }
        }

        [SerializeField][ReadOnly] private AsioAudioStatus _audioStatus = AsioAudioStatus.Stopped;
        public AsioAudioStatus AudioStatus
        {
            get { return _audioStatus; }
            private set { _audioStatus = value; }
        }

        [SerializeField][ReadOnly] private double _actualTimestamp = 0;
        public double ActualTimestamp
        {
            get { return _actualTimestamp; }
            private set { _actualTimestamp = value; }
        }

        private Stopwatch _internalStopwatch = new Stopwatch();
        public Stopwatch InternalStopwatch
        {
            get { return _internalStopwatch; }
            private set { _internalStopwatch = value; }
        }

        [SerializeField] private UnityEvent _onPlay = new UnityEvent();
        public UnityEvent OnPlay
        {
            get { return _onPlay; }
            set { _onPlay = value; }
        }

        [SerializeField] private UnityEvent _onStop = new UnityEvent();
        public UnityEvent OnStop
        {
            get { return _onStop; }
            set { _onStop = value; }
        }

        [SerializeField] private UnityEvent _onPause = new UnityEvent();
        public UnityEvent OnPause
        {
            get { return _onPause; }
            set { _onPause = value; }
        }


        private void Update()
        {
            UpdateTimestamp();
            CheckForAudioEnd();
        }

        private void UpdateTimestamp()
        {
            if (ReferencedAsioAudioManager != null && AudioStatus == AsioAudioStatus.Playing) ActualTimestamp = InternalStopwatch.Elapsed.TotalSeconds;
        }

        private void CheckForAudioEnd()
        {
            if (ActualTimestamp >= AudioFileTotalLength)
            {
                if (Loop) Restart();
                else Stop();
            }
        }

        /// <summary>
        /// Get the ASIO Audio Source samples with the audio file specified in the Audio File Name field.
        /// </summary>
        /// <param name="convertToMono">Convert audio samples to be mixed into one channel (only works for stereo samples).</param>
        /// <param name="getAudioFileLength">Get the total length of the audio file and put the value in AudioFileTotalLength.</param>
        /// <param name="convertSampleRateToNewFile">Convert the audio file to the target sample rate specified in the ASIO Audio Manager to a new file.</param>
        public void GetAudioSamplesFromFileName(bool convertSampleRateToNewFile = false, bool getAudioFileLength = false, bool convertToMono = false)
        {
            if (string.IsNullOrEmpty(AudioFilePath))
            {
                UnityEngine.Debug.LogError("The ASIO Audio Source attached to GameObject \"" + gameObject.name + "\" doesn't have any Audio File Name specified. It will be ignored.");
                return;
            }

            SourceSampleProvider = new AudioFileReader(AudioFilePath);

            if (convertSampleRateToNewFile)
            {
                if (ReferencedAsioAudioManager) ConvertToTargetedSampleRate(ReferencedAsioAudioManager.TargetSampleRate);
                else UnityEngine.Debug.LogWarning("The argument convertSampleRate is set to true, but the ASIO Audio Manager is not referenced. The sample rate will not be converted.");
            }

            if (getAudioFileLength) GetAudioFileTotalLength();

            if (convertToMono) ConvertToMono();

            GetWaveFormatProperties();
        }

        /// <summary>
        /// Convert audio samples to the target sample rate specified in argument, and store them in a new audio file.
        /// </summary>
        /// <param name="sampleRate">The targeted sample rate.</param>
        public void ConvertToTargetedSampleRate(int sampleRate)
        {
            if (SourceSampleProvider == null)
            {
                throw new NullReferenceException("The audio samples in SourceSampleProvider are not set.");
            }
            if (AudioFilePath == null || !File.Exists(AudioFilePath))
            {
                throw new NullReferenceException("The audio file path is either undefined or invalid.");
            }
            if (sampleRate == SourceSampleProvider.WaveFormat.SampleRate)
            {
                UnityEngine.Debug.LogWarning("The sample rate of the file is already " + sampleRate + " Hz. No conversion needed.");
                return;
            }

            string audioFilePathConverted = Path.GetDirectoryName(AudioFilePath) + "\\" + Path.GetFileNameWithoutExtension(AudioFilePath) + "_" + sampleRate + Path.GetExtension(AudioFilePath);
            if (File.Exists(audioFilePathConverted))
            {
                UnityEngine.Debug.LogWarning("The converted file " + audioFilePathConverted + " seems to already exist. This file will be selected instead without sample rate conversion applied.");
                if (new AudioFileReader(audioFilePathConverted).WaveFormat.SampleRate != sampleRate)
                {
                    throw new TargetParameterCountException("The sample rate of the existing file is not the same as the target sample rate.");
                }
            }
            else
            {
                ISampleProvider resampler = new WdlResamplingSampleProvider(SourceSampleProvider, sampleRate);
                WaveFileWriter.CreateWaveFile16(audioFilePathConverted, resampler);
            }
            AudioFilePath = audioFilePathConverted;
            SourceSampleProvider = new AudioFileReader(AudioFilePath);
        }

        private void ConvertToMono()
        {
            if (SourceSampleProvider == null)
            {
                throw new NullReferenceException("The audio samples are not set.");
            }

            if (SourceSampleProvider.WaveFormat.Channels == 2)
            {
                SourceSampleProvider = SourceSampleProvider.ToMono();
            }
        }

        private void GetWaveFormatProperties()
        {
            if (SourceSampleProvider == null)
            {
                throw new NullReferenceException("The audio samples are not set.");
            }

            Channels = SourceSampleProvider.WaveFormat.Channels;
            SampleRate = SourceSampleProvider.WaveFormat.SampleRate;
            AverageBytesPerSecond = SourceSampleProvider.WaveFormat.AverageBytesPerSecond;
            BlockAlign = SourceSampleProvider.WaveFormat.BlockAlign;
            BitsPerSample = SourceSampleProvider.WaveFormat.BitsPerSample;
            ExtraSize = SourceSampleProvider.WaveFormat.ExtraSize;
        }

        private void GetAudioFileTotalLength()
        {
            if (SourceSampleProvider == null)
            {
                throw new NullReferenceException("The audio samples are not set.");
            }
            if (SourceSampleProvider.GetType() != typeof(AudioFileReader))
            {
                throw new InvalidCastException("The audio samples are not from an AudioFileReader.");
            }

            AudioFileTotalLength = ((AudioFileReader)SourceSampleProvider).TotalTime.TotalSeconds;
        }

        /// <summary>
        /// Will send a signal to the referenced ASIO Audio Manager to play the current audio file, and triggers OnPlay.
        /// </summary>
        public void Play()
        {
            InternalStopwatch.Start();
            SendRequestAndReset(AsioAudioStatus.Playing);
            OnPlay.Invoke();
        }

        /// <summary>
        /// Will send a signal to the referenced ASIO Audio Manager to stop the current audio file, and triggers OnStop.
        /// </summary>
        public void Stop()
        {
            ActualTimestamp = 0;
            InternalStopwatch.Reset();
            SendRequestAndReset(AsioAudioStatus.Stopped);
            OnStop.Invoke();
        }

        /// <summary>
        /// Will send a signal to the referenced ASIO Audio Manager to pause the current audio file, and triggers OnPause.
        /// </summary>
        public void Pause()
        {
            InternalStopwatch.Stop();
            SendRequestAndReset(AsioAudioStatus.Paused);
            OnPause.Invoke();
        }

        /// <summary>
        /// Will send a signal to the referenced ASIO Audio Manager to restart the current audio file. (This function is used for loops, and will not trigger events).
        /// </summary>
        public void Restart()
        {
            ActualTimestamp = 0;
            InternalStopwatch.Restart();
            SendRequestAndReset(AsioAudioStatus.Playing);
        }

        private void SendRequestAndReset(AsioAudioStatus newAsioAudioStatus)
        {
            if (ReferencedAsioAudioManager == null)
            {
                UnityEngine.Debug.LogError("Can't update status " + newAsioAudioStatus + ", because the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                return;
            }
            if (ReferencedAsioAudioManager.AsioOutPlayer == null)
            {
                UnityEngine.Debug.LogError("Can't update status " + newAsioAudioStatus + ", because the ASIO driver from the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                return;
            }

            ReferencedAsioAudioManager.AsioOutPlayer.Stop();
            ReferencedAsioAudioManager.AsioOutPlayer.Dispose();

            ReferencedAsioAudioManager.SetAllAsioAudioSourceSampleOffsets();

            AudioStatus = newAsioAudioStatus;

            ReferencedAsioAudioManager.ConnectMixAndPlay();
        }
    }
}


