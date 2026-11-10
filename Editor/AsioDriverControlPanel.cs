using System;
using NAudio.Wave;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Opens an ASIO driver's own control panel (buffer size, clock, routing...) in Edit mode, when no ASIO Audio Manager is connected.
    /// The driver is loaded for it (which also reads its outputs, channel names and sample format), and kept loaded while the panel may be open (many drivers close their panel when unloaded).
    /// It is released when Project Settings closes, before entering Play mode, before a preview, before scripts reload and when quitting,
    /// or with <see cref="Release"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class AsioDriverControlPanel
    {
        private static AsioOut _asioOut;

        /// <summary>The driver loaded for its control panel, or null.</summary>
        public static string LoadedDriverName { get; private set; }

        /// <summary>The reason the last control panel could not open, or null.</summary>
        public static string LastError { get; private set; }

        static AsioDriverControlPanel()
        {
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Release(); };
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorApplication.quitting += Release;
        }

        public static void Show(string driverName)
        {
            LastError = null;
            if (string.IsNullOrEmpty(driverName))
            {
                LastError = "No ASIO driver is set.";
                return;
            }

            try
            {
                // A preview already holds the driver: use it (a driver can only be loaded once).
                AsioOut previewDriver = AsioEditorPreview.Driver;
                if (previewDriver != null)
                {
                    RememberDriverInfo(previewDriver, driverName);
                    previewDriver.ShowControlPanel();
                    return;
                }

                // Reloaded on each click: the driver reports its outputs and format when loaded, so changes made in its last panel are read now.
                Release();
                _asioOut = new AsioOut(driverName);
                LoadedDriverName = driverName;
                RememberDriverInfo(_asioOut, driverName);
                _asioOut.ShowControlPanel();
            }
            catch (Exception e)
            {
                Release();
                LastError = "Could not open the control panel of \"" + driverName + "\": " + e.GetBaseException().Message;
                Debug.LogWarning("ASIO Audio: " + LastError);
            }
        }

        public static void Release()
        {
            if (_asioOut == null) return;
            try { _asioOut.Dispose(); }
            catch (Exception) { }
            _asioOut = null;
            LoadedDriverName = null;
        }

        // The output count, channel names and sample format, shown by the editor outside Play mode (keys read by AsioEditorUtility, also written by the manager).
        internal static void RememberDriverInfo(AsioOut asioOut, string driverName)
        {
            string format = AsioAudioManager.DescribeSampleType(AsioAudioManager.GetDriverOutputSampleType(asioOut));
            if (!string.IsNullOrEmpty(format)) EditorPrefs.SetString("AsioAudioUnity.DriverSampleFormat." + driverName, format);

            int outputs = asioOut.DriverOutputChannelCount;
            if (outputs <= 0) return;
            EditorPrefs.SetInt("AsioAudioUnity.OutputChannelCount." + driverName, outputs);
            string[] names = new string[outputs];
            for (int channel = 0; channel < outputs; channel++)
            {
                try { names[channel] = asioOut.AsioOutputChannelName(channel) ?? ""; }
                catch (Exception) { names[channel] = ""; }
            }
            EditorPrefs.SetString("AsioAudioUnity.OutputChannelNames." + driverName, string.Join("\n", names));
        }
    }
}
