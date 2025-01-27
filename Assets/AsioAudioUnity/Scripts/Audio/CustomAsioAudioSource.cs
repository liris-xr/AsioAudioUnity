using UnityEngine;
using UnityEditor;
using NAudio.Wave;
using System.IO;
using NAudio.Wave.SampleProviders;
using System.Collections;
using UnityEngine.Events;
using System;
using System.Reflection;

namespace AsioAudioUnity
{
    [CustomEditor(typeof(CustomAsioAudioSource))]
    public class CustomAsioAudioSourceEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            CustomAsioAudioSource myScript = (CustomAsioAudioSource)target;

            // Create a field in the editor to allow drag-and-drop of the audio file
            GUILayout.Label("Drag an audio file here");

            // Display a field for drag-and-drop
            Rect dropArea = GUILayoutUtility.GetRect(0f, 50f, GUILayout.ExpandWidth(true));
            GUI.Box(dropArea, "Put an audio file here");

            // Check if a file has been dropped in this area
            if (dropArea.Contains(Event.current.mousePosition) && Event.current.type == EventType.DragUpdated)
            {
                DragAndDrop.AcceptDrag();
                Event.current.Use();
            }

            if (DragAndDrop.paths.Length > 0)
            {
                string filePath = DragAndDrop.paths[0];

                // Check that the file is indeed an audio file
                if (IsAudioFile(filePath))
                {
                    string fileName = Path.GetFileName(filePath); // File name
                    string fileExtension = Path.GetExtension(filePath); // File extension
                    myScript.AudioFilePath = filePath; // Store the full path in the script
                    Debug.Log("File name: " + fileName);
                    Debug.Log("File extension: " + fileExtension);
                }
                else
                {
                    Debug.LogWarning("This file is not a valid audio file.");
                }
            }

            // Display the usual inspector
            DrawDefaultInspector();
        }

        // Check if the file is an audio file based on its extension
        bool IsAudioFile(string filePath)
        {
            string[] audioExtensions = { ".mp3", ".wav", ".ogg", ".aiff", ".flac" };
            string extension = Path.GetExtension(filePath).ToLower();
            return System.Array.Exists(audioExtensions, ext => ext == extension);
        }
    }

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

        [SerializeField][ReadOnly] private float _audioFileTotalLength;
        public float AudioFileTotalLength
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

        [SerializeField][ReadOnly] private float _actualTimestamp = 0;
        public float ActualTimestamp
        {
            get { return _actualTimestamp; }
            set { _actualTimestamp = value; }
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

        /// <summary>
        /// Setup the ASIO Audio Source with the audio file specified in the Audio File Name field.
        /// </summary>
        /// <param name="convertToMono">Convert the audio file to mono if it has more than one channel.</param>
        /// <param name="convertSampleRateToNewFile">Convert the audio file to the target sample rate specified in the ASIO Audio Manager to a new file.</param>
        public void GetAudioSamplesFromFileName(bool convertSampleRateToNewFile = false, bool getAudioFileLength= false, bool convertToMono = false)
        {
            if (string.IsNullOrEmpty(AudioFilePath))
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + gameObject.name + "\" doesn't have any Audio File Name specified. It will be ignored.");
                return;
            }

            SourceSampleProvider = new AudioFileReader(AudioFilePath);
            
            if (convertSampleRateToNewFile)
            {
                if (ReferencedAsioAudioManager) ConvertToTargetedSampleRate(ReferencedAsioAudioManager.TargetSampleRate);
                else Debug.LogWarning("The argument convertSampleRate is set to true, but the ASIO Audio Manager is not referenced. The sample rate will not be converted.");
            }

            if (getAudioFileLength) GetAudioFileTotalLength();

            if (convertToMono) ConvertToMono();

            GetWaveFormatProperties();
        }

        /// <summary>
        /// Convert audio samples to the target sample rate specified in argument, and store them in a new audio file.
        /// </summary>
        /// <param name="sampleRate">The targeted sample rate.</param>
        /// <exception cref="TargetParameterCountException"></exception>
        public void ConvertToTargetedSampleRate(int sampleRate)
        {
            if(SourceSampleProvider == null)
            {
                throw new NullReferenceException("The audio samples are not set.");
            }
            if (AudioFilePath == null)
            {
                throw new DirectoryNotFoundException("The audio file path is not set.");
            }
            if (sampleRate == SourceSampleProvider.WaveFormat.SampleRate)
            {
                Debug.LogWarning("The sample rate of the file is already " + sampleRate + " Hz. No conversion needed.");
                return;
            }

            string audioFilePathConverted = Path.GetDirectoryName(AudioFilePath) + "\\" + Path.GetFileNameWithoutExtension(AudioFilePath) + "_" + sampleRate + Path.GetExtension(AudioFilePath);
            if (File.Exists(audioFilePathConverted))
            {
                Debug.LogWarning("The converted file " + audioFilePathConverted + " seems to already exist. This file will be selected instead without sample rate conversion applied.");
                if (new AudioFileReader(audioFilePathConverted).WaveFormat.SampleRate != sampleRate)
                {
                    throw new TargetParameterCountException("The sample rate of the file is not the same as the target sample rate.");
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

        /// <summary>
        /// Convert audio samples to be mixed into one channel (only works for stereo samples).
        /// </summary>
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

            AudioFileTotalLength = (float)((AudioFileReader)SourceSampleProvider).TotalTime.TotalSeconds;
        }

        /// <summary>
        /// Will send a signal to the referenced ASIO Audio Manager to start playing the audio file, and triggers OnPlay.
        /// </summary>
        public void Play()
        {
            if (ReferencedAsioAudioManager == null)
            {
                Debug.LogError("Can't play the file, because the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                return;
            }
            if (ReferencedAsioAudioManager.AsioOutPlayer == null)
            {
                Debug.LogError("Can't play the file, because the ASIO driver from the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                return;
            }
            ReferencedAsioAudioManager.AsioOutPlayer.Stop();
            ReferencedAsioAudioManager.AsioOutPlayer.Dispose();
            ReferencedAsioAudioManager.RedefineAllSamplesAsioAudioSourcesWithRequest(this, AsioAudioRequest.Play);
            AudioStatus = AsioAudioStatus.Playing;

            ReferencedAsioAudioManager.ConnectToAsioMixAndPlay();
            OnPlay.Invoke();
        }

        /// <summary>
        /// Will send a signal to the referenced ASIO Audio Manager to stop playing the audio file, and triggers OnStop.
        /// </summary>
        public void Stop()
        {
            if (ReferencedAsioAudioManager == null)
            {
                Debug.LogError("Can't stop the file, because the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                return;
            }
            if (ReferencedAsioAudioManager.AsioOutPlayer == null)
            {
                Debug.LogError("Can't stop the file, because the ASIO driver from the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                return;
            }
            ReferencedAsioAudioManager.AsioOutPlayer.Stop();
            ReferencedAsioAudioManager.AsioOutPlayer.Dispose();
            ReferencedAsioAudioManager.RedefineAllSamplesAsioAudioSourcesWithRequest(this, AsioAudioRequest.Stop);
            AudioStatus = AsioAudioStatus.Stopped;

            ReferencedAsioAudioManager.ConnectToAsioMixAndPlay();
            OnStop.Invoke();
        }

        /// <summary>
        /// Will send a signal to the referenced ASIO Audio Manager to pause the current audio file, and triggers OnPause.
        /// </summary>
        public void Pause()
        {
            if (ReferencedAsioAudioManager == null)
            {
                Debug.LogError("Can't pause the file, because the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                return;
            }
            if (ReferencedAsioAudioManager.AsioOutPlayer == null)
            {
                Debug.LogError("Can't pause the file, because the ASIO driver from the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                return;
            }
            ReferencedAsioAudioManager.AsioOutPlayer.Stop();
            ReferencedAsioAudioManager.AsioOutPlayer.Dispose();
            ReferencedAsioAudioManager.RedefineAllSamplesAsioAudioSourcesWithRequest(this, AsioAudioRequest.Pause);
            AudioStatus = AsioAudioStatus.Paused;

            ReferencedAsioAudioManager.ConnectToAsioMixAndPlay();
            OnPause.Invoke();
        }


        public void StopAndPlay()
        {
            if (ReferencedAsioAudioManager == null)
            {
                Debug.LogError("Can't loop the file, because the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                return;
            }
            if (ReferencedAsioAudioManager.AsioOutPlayer == null)
            {
                Debug.LogError("Can't loop the file, because the ASIO driver from the referenced ASIO Audio Manager from this source (" + gameObject.name + ") is not set.");
                return;
            }
            ReferencedAsioAudioManager.AsioOutPlayer.Stop();
            ReferencedAsioAudioManager.AsioOutPlayer.Dispose();
            ReferencedAsioAudioManager.RedefineAllSamplesAsioAudioSourcesWithRequest(this, AsioAudioRequest.Stop);
            AudioStatus = AsioAudioStatus.Playing;

            ReferencedAsioAudioManager.ConnectToAsioMixAndPlay();
        }
    }
}

