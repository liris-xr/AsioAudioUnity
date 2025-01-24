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

        [SerializeField] private bool _playOnInitialize = true;
        public bool PlayOnInitialize
        {
            get { return _playOnInitialize; }
            set { _playOnInitialize = value; }
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

        private float _timeWhenUpdated = 0;
        public float TimeWhenUpdated
        {
            get { return _timeWhenUpdated; }
            set { _timeWhenUpdated = value; }
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

        private void Awake()
        {

        }

        /// <summary>
        /// Setup the ASIO Audio Source with the audio file specified in the Audio File Name field.
        /// </summary>
        /// <param name="convertToMono">Convert the audio file to mono if it has more than one channel.</param>
        /// <param name="convertSampleRateToNewFile">Convert the audio file to the target sample rate specified in the ASIO Audio Manager to a new file.</param>
        public void InitializeAudioSource(bool convertSampleRateToNewFile = false, bool convertToMono = false)
        {
            if (string.IsNullOrEmpty(AudioFilePath))
            {
                Debug.LogError("The ASIO Audio Source attached to GameObject \"" + gameObject.name + "\" doesn't have any Audio File Name specified. It will be ignored.");
                return;
            }

            Debug.Log("Audio File selected : " + AudioFilePath);

            SourceSampleProvider = new AudioFileReader(AudioFilePath);

            if (convertSampleRateToNewFile)
            {
                if (ReferencedAsioAudioManager) ConvertToTargetedSampleRate(ReferencedAsioAudioManager.TargetSampleRate);
                else Debug.LogWarning("The argument convertSampleRate is set to true, but the ASIO Audio Manager is not referenced. The sample rate will not be converted.");
            }
            if (convertToMono)
            {
                if (SourceSampleProvider.WaveFormat.Channels != 1) ConvertToMono();
                else Debug.LogWarning("The argument convertToMono is set to true, but the audio file is already mono. No conversion needed.");
            }

            GetWaveFormatProperties();
        }

        /// <summary>
        /// Convert the audio file (using AudioFilePath property) to the target sample rate specified in argument.
        /// </summary>
        /// <param name="sampleRate">The targeted sample rate.</param>
        /// <exception cref="TargetParameterCountException"></exception>
        public void ConvertToTargetedSampleRate(int sampleRate)
        {
            if(AudioFilePath == null)
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

        public void ConvertToMono()
        {
            if (SourceSampleProvider.WaveFormat.Channels != 1)
            {
                SourceSampleProvider = SourceSampleProvider.ToMono();
            }
        }

        private void GetWaveFormatProperties()
        {
            Channels = SourceSampleProvider.WaveFormat.Channels;
            SampleRate = SourceSampleProvider.WaveFormat.SampleRate;
            AverageBytesPerSecond = SourceSampleProvider.WaveFormat.AverageBytesPerSecond;
            BlockAlign = SourceSampleProvider.WaveFormat.BlockAlign;
            BitsPerSample = SourceSampleProvider.WaveFormat.BitsPerSample;
            ExtraSize = SourceSampleProvider.WaveFormat.ExtraSize;
        }
        /// <summary>
        /// Will send a signal to the ASIO Audio Manager to start playing the audio file, through UnityEvent OnPlay.
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
            ReferencedAsioAudioManager.SetAsioAudioSourcesStatusWithRequest(this, AsioAudioRequest.Play);
            AudioStatus = AsioAudioStatus.Playing;

            ReferencedAsioAudioManager.ConnectToAsioAndPlay();
            OnPlay.Invoke();
        }

        /// <summary>
        /// Will send a signal to the ASIO Audio Manager to stop playing the audio file, through UnityEvent OnStop.
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
            ReferencedAsioAudioManager.SetAsioAudioSourcesStatusWithRequest(this, AsioAudioRequest.Stop);
            AudioStatus = AsioAudioStatus.Stopped;

            ReferencedAsioAudioManager.ConnectToAsioAndPlay();
            OnStop.Invoke();
        }

        /// <summary>
        /// Will send a signal to the ASIO Audio Manager to stop playing the audio file, through UnityEvent OnStop.
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
            ReferencedAsioAudioManager.SetAsioAudioSourcesStatusWithRequest(this, AsioAudioRequest.Pause);
            AudioStatus = AsioAudioStatus.Paused;

            ReferencedAsioAudioManager.ConnectToAsioAndPlay();
            OnPause.Invoke();
        }
    }
}

