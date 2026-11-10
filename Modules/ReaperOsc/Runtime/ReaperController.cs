using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Controls REAPER with OSC messages, from Unity: mirrors the ASIO Audio Manager's transport (PlayAll, PauseAll, StopAll, RestartAll),
    /// and sends commands (play, go to a region or marker, run an action or a Lua script...) by name or on Unity events.
    ///
    /// REAPER must have an OSC control surface listening on ReaperPort (Preferences > Control/OSC/web > Add > OSC, "Local listen port").
    /// The messages are encoded by the shared OSC library (AsioAudioUnity.Osc), and sent directly to REAPER (no OSC Manager needed, so the scene's OSC Manager keeps sending to Spat Revolution).
    /// </summary>
    [AddComponentMenu("AsioAudioUnity/REAPER Controller")]
    public class ReaperController : MonoBehaviour
    {
        [SerializeField] private string _reaperAddress = "127.0.0.1";
        /// <summary>IP address of the computer running REAPER (127.0.0.1: this computer).</summary>
        public string ReaperAddress
        {
            get { return _reaperAddress; }
            set { _reaperAddress = value; }
        }

        [SerializeField] private int _reaperPort = 8000;
        /// <summary>The port REAPER's OSC control surface listens on ("Local listen port", 8000 by default).</summary>
        public int ReaperPort
        {
            get { return _reaperPort; }
            set { _reaperPort = value; }
        }

        [SerializeField] private bool _followTransport = true;
        /// <summary>
        /// Mirrors the ASIO Audio Manager's transport in REAPER: PlayAll plays REAPER, PauseAll pauses it, StopAll stops it,
        /// and RestartAll plays it from the start of the project. When the scene starts with sources playing (Play On Enable),
        /// REAPER plays from the start of the project too.
        /// </summary>
        public bool FollowTransport
        {
            get { return _followTransport; }
            set { _followTransport = value; }
        }

        [SerializeField] private bool _stopReaperOnQuit = true;
        /// <summary>Stops REAPER when the application quits (or when leaving Play mode).</summary>
        public bool StopReaperOnQuit
        {
            get { return _stopReaperOnQuit; }
            set { _stopReaperOnQuit = value; }
        }

        [SerializeField] private string _playRegionScriptId = "";
        /// <summary>
        /// Command ID of the PlayRegion.lua script in REAPER (e.g. "_RS7d3c..."), used by PlayRegion: the script receives the region number
        /// and plays the region in loop. Empty: PlayRegion goes to the region and plays from there, without looping.
        /// </summary>
        public string PlayRegionScriptId
        {
            get { return _playRegionScriptId; }
            set { _playRegionScriptId = value; }
        }

        [SerializeField] private List<ReaperCommand> _commands = new List<ReaperCommand>();
        /// <summary>The commands, sent by name (Trigger) or automatically on their Trigger event.</summary>
        public List<ReaperCommand> Commands => _commands;

        [SerializeField] private ReaperOscAddresses _addresses = new ReaperOscAddresses();
        /// <summary>The OSC addresses REAPER answers to (REAPER's Default OSC pattern configuration).</summary>
        public ReaperOscAddresses Addresses => _addresses;

        [SerializeField] private bool _logMessages = false;
        /// <summary>Logs every message sent to REAPER.</summary>
        public bool LogMessages
        {
            get { return _logMessages; }
            set { _logMessages = value; }
        }

        private readonly OscUdpSender _sender = new OscUdpSender();
        private bool _started;
        private bool _subscribed;
        private bool _quitting;

        // The source events this component listens to, to remove the listeners later.
        private readonly List<(AsioAudioSource source, ReaperCommandTrigger trigger, UnityEngine.Events.UnityAction action)> _sourceListeners
            = new List<(AsioAudioSource, ReaperCommandTrigger, UnityEngine.Events.UnityAction)>();

        // ---------- Lifecycle ----------

        private void Start()
        {
            _started = true;
            SubscribeToUnityEvents();

            // Sources with Play On Enable start by themselves when the scene starts (not through PlayAll):
            // they play from the start of their files, so REAPER plays from the start of the project too.
            // (Start runs after the OnEnable of every object of the scene, so these sources are already playing.)
            if (_followTransport && FindObjectsByType<AsioAudioSource>(FindObjectsSortMode.None).Any(source => source.AudioStatus == AsioAudioStatus.Playing))
            {
                GoToStart();
                Play();
            }

            RunCommandsFor(ReaperCommandTrigger.SceneStart, null);
        }

        private void OnEnable()
        {
            // Subscribed in Start the first time, once every object of the scene is initialised.
            if (_started) SubscribeToUnityEvents();
        }

        private void OnDisable()
        {
            UnsubscribeFromUnityEvents();
        }

        private void OnApplicationQuit()
        {
            if (_stopReaperOnQuit && isActiveAndEnabled) Stop();
            // The sources stop while the application shuts down: don't send their triggers to REAPER.
            _quitting = true;
        }

        private void OnDestroy()
        {
            _sender.Dispose();
        }

        private void SubscribeToUnityEvents()
        {
            if (_subscribed) return;
            _subscribed = true;

            AsioAudioManager.TransportCommandIssued += OnTransportCommand;

            foreach (ReaperCommand command in _commands)
            {
                if (command == null || !command.UsesSource || command.Source == null) continue;
                ReaperCommandTrigger trigger = command.Trigger;
                AsioAudioSource source = command.Source;
                if (_sourceListeners.Exists(listener => listener.source == source && listener.trigger == trigger)) continue;

                UnityEngine.Events.UnityAction action = () => RunCommandsFor(trigger, source);
                switch (trigger)
                {
                    case ReaperCommandTrigger.SourcePlay: source.OnPlay?.AddListener(action); break;
                    case ReaperCommandTrigger.SourcePause: source.OnPause?.AddListener(action); break;
                    case ReaperCommandTrigger.SourceStop: source.OnStop?.AddListener(action); break;
                }
                _sourceListeners.Add((source, trigger, action));
            }
        }

        private void UnsubscribeFromUnityEvents()
        {
            if (!_subscribed) return;
            _subscribed = false;

            AsioAudioManager.TransportCommandIssued -= OnTransportCommand;

            foreach ((AsioAudioSource source, ReaperCommandTrigger trigger, UnityEngine.Events.UnityAction action) in _sourceListeners)
            {
                if (source == null) continue;
                switch (trigger)
                {
                    case ReaperCommandTrigger.SourcePlay: source.OnPlay?.RemoveListener(action); break;
                    case ReaperCommandTrigger.SourcePause: source.OnPause?.RemoveListener(action); break;
                    case ReaperCommandTrigger.SourceStop: source.OnStop?.RemoveListener(action); break;
                }
            }
            _sourceListeners.Clear();
        }

        private void OnTransportCommand(AsioTransportCommand command)
        {
            if (_quitting) return;
            if (_followTransport)
            {
                switch (command)
                {
                    case AsioTransportCommand.Play: Play(); break;
                    case AsioTransportCommand.Pause: Pause(); break;
                    case AsioTransportCommand.Stop: Stop(); break;
                    case AsioTransportCommand.Restart: GoToStart(); Play(); break;
                }
            }

            switch (command)
            {
                case AsioTransportCommand.Play: RunCommandsFor(ReaperCommandTrigger.PlayAll, null); break;
                case AsioTransportCommand.Pause: RunCommandsFor(ReaperCommandTrigger.PauseAll, null); break;
                case AsioTransportCommand.Stop: RunCommandsFor(ReaperCommandTrigger.StopAll, null); break;
                case AsioTransportCommand.Restart: RunCommandsFor(ReaperCommandTrigger.RestartAll, null); break;
            }
        }

        private void RunCommandsFor(ReaperCommandTrigger trigger, AsioAudioSource source)
        {
            if (_quitting) return;
            foreach (ReaperCommand command in _commands.ToArray())
            {
                if (command == null || command.Trigger != trigger) continue;
                if (command.UsesSource && command.Source != source) continue;
                Execute(command);
            }
        }

        // ---------- Commands ----------

        /// <summary>Sends the command with this name (usable from a UnityEvent). Returns false if there is none.</summary>
        public bool Trigger(string commandName)
        {
            ReaperCommand command = _commands.Find(other => other != null && other.Name == commandName);
            if (command == null)
            {
                Debug.LogWarning("REAPER Controller on \"" + name + "\": no command named \"" + commandName + "\".", this);
                return false;
            }
            Execute(command);
            return true;
        }

        /// <summary>Sends the command at this index of Commands.</summary>
        public void Trigger(int index)
        {
            if (index >= 0 && index < _commands.Count && _commands[index] != null) Execute(_commands[index]);
        }

        /// <summary>Sends a command.</summary>
        public void Execute(ReaperCommand command)
        {
            switch (command.Type)
            {
                case ReaperCommandType.Play: Play(); break;
                case ReaperCommandType.Pause: Pause(); break;
                case ReaperCommandType.Stop: Stop(); break;
                case ReaperCommandType.Record: Record(); break;
                case ReaperCommandType.GoToStart: GoToStart(); break;
                case ReaperCommandType.GoToTime: GoToTime(command.Number); break;
                case ReaperCommandType.GoToMarker: GoToMarker(Mathf.RoundToInt(command.Number)); break;
                case ReaperCommandType.GoToRegion: GoToRegion(Mathf.RoundToInt(command.Number)); break;
                case ReaperCommandType.PlayRegion: PlayRegion(Mathf.RoundToInt(command.Number)); break;
                case ReaperCommandType.RunAction: RunAction(command.Text); break;
                case ReaperCommandType.RunActionWithValue: RunActionWithValue(command.Text, command.Number.ToString(CultureInfo.InvariantCulture)); break;
                case ReaperCommandType.CustomMessage: Send(command.Text, command.Number); break;
            }
        }

        // ---------- REAPER actions ----------

        public void Play() { Send(_addresses.Play, 1f); }
        public void Pause() { Send(_addresses.Pause, 1f); }
        public void Stop() { Send(_addresses.Stop, 1f); }
        public void Record() { Send(_addresses.Record, 1f); }

        /// <summary>Moves the edit cursor to the start of the project (and the playback, if playing).</summary>
        public void GoToStart() { GoToTime(0f); }

        /// <summary>Moves the edit cursor to a time, in seconds (and the playback, if playing).</summary>
        public void GoToTime(float seconds) { Send(_addresses.Time, Mathf.Max(0f, seconds)); }

        /// <summary>Moves the edit cursor to a marker, by its number (as shown in REAPER).</summary>
        public void GoToMarker(int markerNumber) { Send(_addresses.Marker, markerNumber); }

        /// <summary>Moves the edit cursor to the start of a region, by its number (as shown in REAPER).</summary>
        public void GoToRegion(int regionNumber) { Send(_addresses.Region, regionNumber); }

        /// <summary>
        /// Plays a region in loop, by its number, with the PlayRegion.lua script (PlayRegionScriptId): one OSC message runs the script,
        /// with the region number as its argument. Without the script, goes to the region and plays from there (no loop).
        /// </summary>
        public void PlayRegion(int regionNumber)
        {
            if (string.IsNullOrWhiteSpace(_playRegionScriptId))
            {
                GoToRegion(regionNumber);
                Play();
                return;
            }
            RunActionWithValue(_playRegionScriptId, regionNumber.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Runs a REAPER action or script with a value: sends the value to /action/[command ID] (ActionWithValue address).
        /// A Lua script reads it with reaper.get_action_context(): the context ends with ":s=[value]" (see PlayRegion.lua).
        /// </summary>
        public void RunActionWithValue(string commandId, string value)
        {
            if (string.IsNullOrWhiteSpace(commandId))
            {
                Debug.LogWarning("REAPER Controller on \"" + name + "\": no action command ID set.", this);
                return;
            }
            Send(string.Format(CultureInfo.InvariantCulture, _addresses.ActionWithValue, commandId.Trim()), value ?? "");
        }

        /// <summary>Runs a REAPER action by its numeric command ID (Actions > Show action list, "Command ID" column).</summary>
        public void RunAction(int commandId) { Send(_addresses.Action, commandId); }

        /// <summary>
        /// Runs a REAPER action or script by its command ID: a number ("40042"), or a named ID ("_RS7d3c..." for scripts, "_SWS_..." for extensions).
        /// In REAPER's action list, right-click an action > Copy selected action command ID.
        /// </summary>
        public void RunAction(string commandId)
        {
            if (string.IsNullOrWhiteSpace(commandId))
            {
                Debug.LogWarning("REAPER Controller on \"" + name + "\": no action command ID set.", this);
                return;
            }

            commandId = commandId.Trim();
            if (int.TryParse(commandId, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numericId)) RunAction(numericId);
            else Send(_addresses.NamedAction, commandId);
        }

        // ---------- OSC ----------

        /// <summary>Sends an OSC message to REAPER, with any number of int, float or string values.</summary>
        public void Send(string address, params object[] values)
        {
            if (string.IsNullOrEmpty(address)) return;
            _sender.SetDestination(_reaperAddress, _reaperPort);
            if (_sender.Send(address, values) && _logMessages)
            {
                Debug.Log("REAPER Controller: sent " + address + (values.Length > 0 ? " " + string.Join(" ", values) : "") + " to " + _sender, this);
            }
        }
    }
}
