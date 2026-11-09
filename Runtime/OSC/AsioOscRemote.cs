using System;
using System.Globalization;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Controls the ASIO Audio Manager and its sources with OSC messages received by an OSC Manager
    /// (e.g. from REAPER, Spat Revolution, TouchOSC or Max). Sources are addressed by their output channel.
    ///
    /// With the default prefix "/asio":
    ///   /asio/play, /asio/pause, /asio/stop, /asio/restart        all sources (in sync)
    ///   /asio/master/volume [dB]                                   master volume
    ///   /asio/snapshot [name or index]                             recall a mixer snapshot
    ///   /asio/source/[channel]/play, /pause, /stop, /restart       one source
    ///   /asio/source/[channel]/volume [dB]                         its volume
    ///   /asio/source/[channel]/mute [0/1], /solo [0/1]             its mute / solo
    ///   /asio/source/[channel]/seek [seconds]                      its position
    /// </summary>
    [AddComponentMenu("AsioAudioUnity/ASIO OSC Remote")]
    public class AsioOscRemote : MonoBehaviour
    {
        [SerializeField] private OSC _osc;
        /// <summary>The OSC Manager receiving the messages (found in the scene if empty).</summary>
        public OSC Osc
        {
            get { return _osc; }
            set { Unsubscribe(); _osc = value; if (isActiveAndEnabled) Subscribe(); }
        }

        [SerializeField] private AsioAudioManager _asioAudioManager;
        /// <summary>The ASIO Audio Manager to control (found in the scene if empty).</summary>
        public AsioAudioManager AsioAudioManager
        {
            get { return _asioAudioManager; }
            set { _asioAudioManager = value; }
        }

        [SerializeField] private string _addressPrefix = "/asio";
        /// <summary>The start of the addresses this component answers to (default: "/asio").</summary>
        public string AddressPrefix
        {
            get { return _addressPrefix; }
            set { _addressPrefix = value; }
        }

        [SerializeField] private bool _logMessages = false;
        /// <summary>Logs every message handled (and the ones starting with the prefix that are not understood).</summary>
        public bool LogMessages
        {
            get { return _logMessages; }
            set { _logMessages = value; }
        }

        private OSC _subscribedOsc;

        private void OnEnable()
        {
            if (_osc == null) _osc = FindFirstObjectByType<OSC>();
            if (_asioAudioManager == null) _asioAudioManager = FindFirstObjectByType<AsioAudioManager>();
            if (_osc == null) Debug.LogError("ASIO OSC Remote on \"" + name + "\": there is no OSC Manager in the scene, no message will be received.");
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_osc == null || _subscribedOsc == _osc) return;
            _osc.MessageReceived += OnMessageReceived;
            _subscribedOsc = _osc;
        }

        private void Unsubscribe()
        {
            if (_subscribedOsc == null) return;
            _subscribedOsc.MessageReceived -= OnMessageReceived;
            _subscribedOsc = null;
        }

        // Called on the main thread by the OSC Manager.
        private void OnMessageReceived(OscMessage message)
        {
            string prefix = string.IsNullOrEmpty(_addressPrefix) ? "" : "/" + _addressPrefix.Trim('/');
            if (message?.address == null || !message.address.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)) return;
            if (_asioAudioManager == null) return;

            string[] parts = message.address.Substring(prefix.Length + 1).Split('/');
            bool handled = parts.Length >= 2 && parts[0].Equals("source", StringComparison.OrdinalIgnoreCase)
                ? HandleSourceMessage(parts, message)
                : HandleGlobalMessage(parts, message);

            if (_logMessages || !handled) Debug.Log("ASIO OSC Remote: " + (handled ? "" : "message not understood: ") + message);
        }

        private bool HandleGlobalMessage(string[] parts, OscMessage message)
        {
            string command = string.Join("/", parts).ToLowerInvariant();
            switch (command)
            {
                case "play": _asioAudioManager.PlayAll(); return true;
                case "pause": _asioAudioManager.PauseAll(); return true;
                case "stop": _asioAudioManager.StopAll(); return true;
                case "restart": _asioAudioManager.RestartAll(); return true;

                case "master/volume":
                    if (!TryGetFloat(message, 0, out float decibels)) return false;
                    _asioAudioManager.MasterVolumeDecibels = Mathf.Min(0f, decibels);
                    return true;

                case "snapshot":
                    if (message.values.Count == 0) return false;
                    object value = message.values[0];
                    if (value is string snapshotName) return _asioAudioManager.RecallSnapshot(snapshotName);
                    return TryGetFloat(message, 0, out float index) && _asioAudioManager.RecallSnapshot(Mathf.RoundToInt(index));

                default:
                    return false;
            }
        }

        private bool HandleSourceMessage(string[] parts, OscMessage message)
        {
            if (parts.Length != 3 || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int channel)) return false;

            AsioAudioSource source = _asioAudioManager.AsioAudioSources.Find(other => other && other.TargetOutputChannel == channel);
            if (source == null) return false;

            switch (parts[2].ToLowerInvariant())
            {
                case "play": source.Play(); return true;
                case "pause": source.Pause(); return true;
                case "stop": source.Stop(); return true;
                case "restart": source.Restart(); return true;

                case "volume":
                    if (!TryGetFloat(message, 0, out float decibels)) return false;
                    source.VolumeDecibels = Mathf.Min(0f, decibels);
                    return true;

                case "mute":
                    if (!TryGetFloat(message, 0, out float mute)) return false;
                    source.Mute = mute >= 0.5f;
                    return true;

                case "solo":
                    if (!TryGetFloat(message, 0, out float solo)) return false;
                    source.Solo = solo >= 0.5f;
                    return true;

                case "seek":
                    if (!TryGetFloat(message, 0, out float seconds)) return false;
                    source.Seek(seconds);
                    return true;

                default:
                    return false;
            }
        }

        // Reads a number sent as an int, a float, or a string ("-6", "-inf").
        private static bool TryGetFloat(OscMessage message, int index, out float value)
        {
            value = 0f;
            if (message.values == null || message.values.Count <= index) return false;

            switch (message.values[index])
            {
                case float floatValue: value = floatValue; return !float.IsNaN(value);
                case int intValue: value = intValue; return true;
                case string text:
                    text = text.Trim().Replace(',', '.');
                    if (text.ToLowerInvariant().Contains("inf") || text.Contains("∞")) { value = float.NegativeInfinity; return true; }
                    return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
                default:
                    return false;
            }
        }
    }
}
