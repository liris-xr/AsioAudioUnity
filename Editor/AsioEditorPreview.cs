using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Plays an ASIO Audio Source in Edit mode (without entering Play mode), on its output channel, through the project's ASIO driver (ASIO Audio Settings),
    /// at the source's volume and the master volume. One source at a time. Stopped before entering Play mode and before scripts reload.
    /// </summary>
    [InitializeOnLoad]
    internal static class AsioEditorPreview
    {
        private static AsioOut _asioOut;
        private static AsioMixer _mixer;
        private static AsioSourceVoice _voice;
        private static AsioAudioSettings _settings;
        private static double _lastUpdateTime;

        /// <summary>The source being previewed, or null.</summary>
        public static AsioAudioSource Source { get; private set; }

        /// <summary>The reason the last preview could not start, or null.</summary>
        public static string LastError { get; private set; }

        public static float MeterLevel { get; private set; }

        private static float[] _channelMeterLevels = new float[0];
        private static float[] _channelPeaks = new float[1];
        /// <summary>The level of each channel of the previewed source (one value when mixed down to mono).</summary>
        public static IReadOnlyList<float> ChannelMeterLevels => _channelMeterLevels;

        public static double CurrentTime => _voice?.CurrentTime ?? 0;

        public static bool IsPreviewing(AsioAudioSource source) => source != null && Source == source && _voice != null;

        /// <summary>The driver used by the running preview, or null.</summary>
        public static AsioOut Driver => _asioOut;

        static AsioEditorPreview()
        {
            // The driver is needed by the ASIO Audio Manager in Play mode, and the engine objects don't survive a script reload.
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Stop(); };
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        public static bool Start(AsioAudioSource source)
        {
            Stop();
            AsioDriverControlPanel.Release(); // A driver can only be loaded once
            LastError = null;

            AsioAudioSettings settings = AsioEditorUtility.Settings;
            if (string.IsNullOrEmpty(settings.AsioDriverName)) return Fail("no ASIO driver is set (Project Settings > ASIO Audio).");

            string path = source.ResolvedAudioFilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Fail("the audio file was not found.");

            try
            {
                _asioOut = new AsioOut(settings.AsioDriverName);
                AsioDriverControlPanel.RememberDriverInfo(_asioOut, settings.AsioDriverName);
                int channelCount = _asioOut.DriverOutputChannelCount;
                if (source.TargetOutputChannel < 1 || source.TargetOutputChannel > channelCount)
                {
                    throw new Exception("output channel " + source.TargetOutputChannel + " doesn't exist (the driver has " + channelCount + " outputs).");
                }

                _voice = new AsioSourceVoice { DownmixToMono = !source.PlayAllChannels, Loop = source.Loop, LoopCrossfadeSeconds = source.LoopCrossfadeDuration };
                _voice.Load(path, settings.TargetSampleRate, false);
                _settings = settings;
                Source = source;
                ApplySettings();

                _mixer = new AsioMixer(settings.TargetSampleRate, channelCount) { MasterVolume = settings.MasterVolume };
                _mixer.AddVoice(_voice);
                _asioOut.Init(_mixer.ToWaveProvider()); // 32-bit float: NAudio converts it to any driver format
                _asioOut.Play();
                _voice.Play();

                _lastUpdateTime = EditorApplication.timeSinceStartup;
                EditorApplication.update += Update;
                return true;
            }
            catch (Exception e)
            {
                Stop();
                return Fail("could not use the ASIO driver \"" + settings.AsioDriverName + "\" at " + settings.TargetSampleRate + " Hz: " + e.GetBaseException().Message);
            }
        }

        public static void Seek(double seconds)
        {
            _voice?.Seek(seconds);
        }

        public static void Stop()
        {
            EditorApplication.update -= Update;
            try
            {
                _asioOut?.Stop();
                _asioOut?.Dispose();
            }
            catch (Exception) { }
            _asioOut = null;
            _voice?.Dispose();
            _voice = null;
            _mixer = null;
            _settings = null;
            Source = null;
            MeterLevel = 0f;
            _channelMeterLevels = new float[0];
        }

        private static bool Fail(string reason)
        {
            LastError = "Can't preview: " + reason;
            return false;
        }

        private static void Update()
        {
            if (Source == null || _voice == null || _settings == null)
            {
                Stop();
                return;
            }

            ApplySettings();

            if (_voice.ConsumeEndReached())
            {
                Stop();
                return;
            }

            // Meter, falling by 20 dB per second
            double now = EditorApplication.timeSinceStartup;
            float elapsed = (float)(now - _lastUpdateTime);
            _lastUpdateTime = now;
            float falloff = Mathf.Pow(0.1f, elapsed);
            MeterLevel = Mathf.Max(_voice.ConsumePeak(), MeterLevel * falloff);
            int channels = _voice.ChannelCount;
            if (_channelMeterLevels.Length != channels) _channelMeterLevels = new float[channels];
            if (_channelPeaks.Length < channels) _channelPeaks = new float[channels];
            int measured = _voice.ConsumeChannelPeaks(_channelPeaks);
            for (int c = 0; c < channels; c++) _channelMeterLevels[c] = Mathf.Max(c < measured ? _channelPeaks[c] : 0f, _channelMeterLevels[c] * falloff);
        }

        // Settings edited during the preview are applied live (mute and solo are ignored: a preview is always heard).
        private static void ApplySettings()
        {
            _voice.Volume = Source.Volume;
            _voice.Loop = Source.Loop;
            _voice.OutputChannel = Source.TargetOutputChannel;
            _voice.FadeInSeconds = Source.FadeInDuration;
            _voice.FadeOutSeconds = Source.FadeOutDuration;
            _voice.FadeInCurve = Source.FadeInCurve;
            _voice.FadeOutCurve = Source.FadeOutCurve;
            _voice.LoopCrossfadeSeconds = Source.LoopCrossfadeDuration;
            if (_mixer != null) _mixer.MasterVolume = _settings.MasterVolume;
        }
    }
}
