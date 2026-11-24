using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;

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
    public class ReaperController : MonoBehaviour, INotificationReceiver
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

        [SerializeField] private bool _followReaper = false;
        /// <summary>
        /// Two-way sync: when REAPER is played, paused or stopped (by its operator, a control surface…), Unity's sources follow
        /// (<c>PlayAll</c>, <c>PauseAll</c>, <c>StopAll</c>). REAPER must send its feedback to <see cref="ListenPort"/>
        /// (control surface mode "Configure device IP + local port", device port = this port).
        /// </summary>
        public bool FollowReaper
        {
            get { return _followReaper; }
            set { _followReaper = value; }
        }

        [SerializeField] private int _listenPort = 9000;
        /// <summary>The port REAPER sends its feedback to: the "Device port" of its OSC control surface (default: 9000).</summary>
        public int ListenPort
        {
            get { return _listenPort; }
            set { _listenPort = value; }
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

        [SerializeField] private string _setupTracksScriptId = "";
        /// <summary>
        /// Command ID of the SetupTracks.lua script in REAPER (e.g. "_RS1a2b..."), used by <see cref="SetUpReaperTracks()"/>:
        /// the script creates or updates one REAPER track per ASIO Audio Source.
        /// </summary>
        public string SetupTracksScriptId
        {
            get { return _setupTracksScriptId; }
            set { _setupTracksScriptId = value; }
        }

        [SerializeField] private bool _numberTrackNames = true;
        /// <summary>
        /// SetUpReaperTracks starts each track's name with the number of its Spat Revolution source (its output channel without a Spat Controller), e.g. "3 · Violin".
        /// </summary>
        public bool NumberTrackNames
        {
            get { return _numberTrackNames; }
            set { _numberTrackNames = value; }
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
            _receiver.Dispose();
        }

        // ---------- Following REAPER ----------

        private enum ReaperTransport { Unknown, Stopped, Playing, Paused }

        private readonly OscUdpReceiver _receiver = new OscUdpReceiver();
        private ReaperTransport _reaperTransport = ReaperTransport.Unknown; // As last reported by REAPER, or as last asked by Unity
        private bool _applyingReaperTransport; // Unity is following REAPER: don't send the command back to it

        /// <summary>Whether REAPER's feedback is being received (Follow REAPER, in Play mode).</summary>
        public bool IsListening => _receiver.IsOpen;

        /// <summary>Why the listen port couldn't be opened, or null.</summary>
        public string ListenError => _receiver.LastError;

        /// <summary>Whether REAPER has sent a message since Unity started listening (Follow REAPER, in Play mode).</summary>
        public bool HasHeardFromReaper { get; private set; }

        private void Update()
        {
            if (!_followReaper || _quitting)
            {
                if (_receiver.IsOpen) _receiver.Close();
                return;
            }
            if (!_receiver.IsOpen || _receiver.Port != _listenPort)
            {
                _reaperTransport = ReaperTransport.Unknown;
                HasHeardFromReaper = false;
                if (!_receiver.Open(_listenPort) && _receiver.LastError != _loggedListenError)
                {
                    _loggedListenError = _receiver.LastError;
                    Debug.LogWarning("REAPER Controller: can't listen to REAPER's feedback on port " + _listenPort + ": " + _receiver.LastError, this);
                }
            }
            _receiver.Poll(OnReaperMessage);
        }

        private string _loggedListenError;

        // REAPER reports its transport with /play, /pause and /stop (1: in that state). The first report only tells the current state;
        // afterwards, each change is applied to Unity's sources, unless Unity asked for it (then REAPER is only confirming it).
        private void OnReaperMessage(OscMessage message)
        {
            HasHeardFromReaper = true;
            if (_logMessages) Debug.Log("REAPER Controller: received " + message, this);
            if (message.values.Count == 0 || !IsOn(message.values[0])) return;

            ReaperTransport reported = message.address == _addresses.Play ? ReaperTransport.Playing
                : message.address == _addresses.Pause ? ReaperTransport.Paused
                : message.address == _addresses.Stop ? ReaperTransport.Stopped
                : ReaperTransport.Unknown;
            if (reported == ReaperTransport.Unknown || reported == _reaperTransport) return;

            bool firstReport = _reaperTransport == ReaperTransport.Unknown;
            _reaperTransport = reported;
            if (firstReport) return;

            AsioAudioManager manager = AsioAudioManager.Existing;
            if (manager == null) return;

            _applyingReaperTransport = true;
            try
            {
                switch (reported)
                {
                    case ReaperTransport.Playing: manager.PlayAll(); break;
                    case ReaperTransport.Paused: manager.PauseAll(); break;
                    case ReaperTransport.Stopped: manager.StopAll(); break;
                }
            }
            finally
            {
                _applyingReaperTransport = false;
            }
        }

        private static bool IsOn(object value)
        {
            switch (value)
            {
                case int number: return number != 0;
                case float number: return number > 0.5f;
                case string text: return text == "1";
                default: return false;
            }
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
            if (_followTransport && !_applyingReaperTransport)
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
        /// <summary>Receives Timeline markers (REAPER Command markers, on a REAPER Command track bound to this controller), and sends their command.</summary>
        public void OnNotify(Playable origin, INotification notification, object context)
        {
            if (!(notification is IReaperCommandNotification reaperNotification) || _quitting) return;
            if (!string.IsNullOrEmpty(reaperNotification.CommandName))
            {
                if (!Trigger(reaperNotification.CommandName)) Debug.LogWarning("REAPER Controller: no command named \"" + reaperNotification.CommandName + "\" (from a Timeline marker).", this);
            }
            else if (reaperNotification.Command != null)
            {
                Execute(reaperNotification.Command);
            }
        }

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

        public void Play() { _reaperTransport = ReaperTransport.Playing; Send(_addresses.Play, 1f); }
        public void Pause() { if (_reaperTransport == ReaperTransport.Playing) _reaperTransport = ReaperTransport.Paused; Send(_addresses.Pause, 1f); }
        public void Stop() { _reaperTransport = ReaperTransport.Stopped; Send(_addresses.Stop, 1f); }
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
        /// Creates or updates one REAPER track per ASIO Audio Source of the loaded scenes, with SetupTracks.lua (see <see cref="SetupTracksScriptId"/>):
        /// named like the source (after its Spat number, with <see cref="NumberTrackNames"/>), on its ReaRoute input, armed with monitoring,
        /// without a master send, with the Spat Revolution Send plugin. Works in Edit mode too.
        /// </summary>
        public void SetUpReaperTracks()
        {
            SetUpReaperTracks(FindObjectsByType<AsioAudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(source => source.TargetOutputChannel > 0)
                .Select(source => (source.TargetOutputChannel, source.OutputChannelCount, source.SpatNumber, source.name)));
        }

        /// <summary>Creates or updates REAPER tracks for these outputs (first channel, channel count, name), with SetupTracks.lua. Their names aren't numbered.</summary>
        public void SetUpReaperTracks(IEnumerable<(int channel, int channelCount, string name)> tracks)
        {
            SetUpReaperTracks(tracks.Select(track => (track.channel, track.channelCount, 0, track.name)));
        }

        /// <summary>
        /// Creates or updates REAPER tracks for these outputs (first channel, channel count, Spat number, name), with SetupTracks.lua.
        /// With <see cref="NumberTrackNames"/>, a track's name starts with its number (0: not numbered).
        /// </summary>
        public void SetUpReaperTracks(IEnumerable<(int channel, int channelCount, int number, string name)> tracks)
        {
            if (string.IsNullOrWhiteSpace(_setupTracksScriptId))
            {
                Debug.LogWarning("REAPER Controller: set the Set Up Tracks Script ID first (install SetupTracks.lua in REAPER, and paste its command ID).", this);
                return;
            }
            // "channel,count,number,name|..." (the script reads the value up to the first ']'; names can't contain the separators)
            string list = string.Join("|", tracks.OrderBy(track => track.channel)
                .Select(track => track.channel + "," + Math.Max(1, track.channelCount) + "," + (_numberTrackNames ? Math.Max(0, track.number) : 0) + "," + Sanitize(track.name)));
            if (list.Length == 0)
            {
                Debug.LogWarning("REAPER Controller: no ASIO Audio Source with an output channel: no track to set up.", this);
                return;
            }
            // Long OSC values don't reach REAPER's scripts: the list goes through a file (Unity and REAPER are on the same computer with ReaRoute),
            // and the OSC message only runs the script.
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SetupTracksListPath));
                File.WriteAllText(SetupTracksListPath, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + "\n" + list,
                    new System.Text.UTF8Encoding(false));
                RunActionWithValue(_setupTracksScriptId, "file");
            }
            catch (Exception e)
            {
                Debug.LogWarning("REAPER Controller: couldn't write " + SetupTracksListPath + " (" + e.Message + "): the list is sent in the OSC message instead.", this);
                RunActionWithValue(_setupTracksScriptId, list);
            }
        }

        /// <summary>The file SetUpReaperTracks writes the track list to, read by SetupTracks.lua: %APPDATA%\AsioAudioUnity\SetupTracks.txt.</summary>
        public static string SetupTracksListPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AsioAudioUnity", "SetupTracks.txt");

        /// <summary>
        /// The file RegisterScripts.lua writes the command IDs of the scripts it loaded in REAPER to ("PlayRegion.lua=_RS..." lines):
        /// %APPDATA%\AsioAudioUnity\ScriptIds.txt. See <see cref="FillScriptIdsFromReaper"/>.
        /// </summary>
        public static string RegisteredScriptIdsPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AsioAudioUnity", "ScriptIds.txt");

        /// <summary>The command IDs written by RegisterScripts.lua, by script file name ("PlayRegion.lua"); empty if it hasn't run.</summary>
        public static Dictionary<string, string> ReadRegisteredScriptIds()
        {
            Dictionary<string, string> ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(RegisteredScriptIdsPath)) return ids;
                foreach (string line in File.ReadAllLines(RegisteredScriptIdsPath))
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    string id = line.Substring(separator + 1).Trim();
                    if (id.Length > 0) ids[line.Substring(0, separator).Trim()] = id;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("REAPER Controller: couldn't read " + RegisteredScriptIdsPath + ": " + e.Message);
            }
            return ids;
        }

        /// <summary>The script file name of <see cref="PlayRegionScriptId"/>, as RegisterScripts.lua writes it.</summary>
        public const string PlayRegionScriptFile = "PlayRegion.lua";
        /// <summary>The script file name of <see cref="SetupTracksScriptId"/>, as RegisterScripts.lua writes it.</summary>
        public const string SetupTracksScriptFile = "SetupTracks.lua";

        /// <summary>
        /// Sets PlayRegionScriptId and SetupTracksScriptId from the IDs written by RegisterScripts.lua (only the empty ones with <paramref name="onlyEmpty"/>).
        /// Returns how many changed.
        /// </summary>
        public int FillScriptIdsFromReaper(bool onlyEmpty = false)
        {
            Dictionary<string, string> ids = ReadRegisteredScriptIds();
            int changed = 0;
            if (ids.TryGetValue(PlayRegionScriptFile, out string playRegion) && playRegion != _playRegionScriptId && (!onlyEmpty || string.IsNullOrWhiteSpace(_playRegionScriptId)))
            {
                _playRegionScriptId = playRegion;
                changed++;
            }
            if (ids.TryGetValue(SetupTracksScriptFile, out string setupTracks) && setupTracks != _setupTracksScriptId && (!onlyEmpty || string.IsNullOrWhiteSpace(_setupTracksScriptId)))
            {
                _setupTracksScriptId = setupTracks;
                changed++;
            }
            return changed;
        }

        /// <summary>Whether RegisterScripts.lua gave other command IDs than this controller's (or ones it doesn't have yet).</summary>
        public bool ScriptIdsDifferFromReaper()
        {
            Dictionary<string, string> ids = ReadRegisteredScriptIds();
            return (ids.TryGetValue(PlayRegionScriptFile, out string playRegion) && playRegion != _playRegionScriptId)
                || (ids.TryGetValue(SetupTracksScriptFile, out string setupTracks) && setupTracks != _setupTracksScriptId);
        }

        private static string Sanitize(string name)
        {
            char[] chars = (name ?? "").ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (chars[i] == '|' || chars[i] == ',' || chars[i] == ']' || chars[i] < ' ') chars[i] = ' ';
            return new string(chars).Trim();
        }

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
