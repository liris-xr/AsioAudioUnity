using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>What a tracked object is in Spat Revolution.</summary>
    public enum SpatObjectType
    {
        Source,
        Room
    }

    /// <summary>An object whose position is sent to Spat Revolution.</summary>
    [Serializable]
    public class SpatTrackedObject
    {
        [Tooltip("The object whose position is sent.")]
        public Transform Target;

        [Tooltip("Source or room, in Spat Revolution.")]
        public SpatObjectType Type = SpatObjectType.Room;

        [Tooltip("Its number in Spat Revolution (sources and rooms are numbered from 1).")]
        [Min(1)] public int Index = 1;
    }

    /// <summary>
    /// The Spat Revolution number of an ASIO Audio Source, when it isn't its output channel (or to not send it at all).
    /// </summary>
    [Serializable]
    public class SpatSourceMapping
    {
        [Tooltip("The ASIO Audio Source.")]
        public AsioAudioSource Source;

        [Tooltip("Its source number in Spat Revolution (any number, from 1).")]
        [Min(1)] public int SpatIndex = 1;

        [Tooltip("Whether its position is sent.")]
        public bool Send = true;
    }

    /// <summary>
    /// The OSC addresses of Spat Revolution. {0} is replaced by the source or room number.
    /// </summary>
    [Serializable]
    public class SpatOscAddresses
    {
        [Tooltip("Position of a source: x, y, z (Spat's axes).")]
        public string SourcePosition = "/source/{0}/xyz";
        [Tooltip("Position of a room: x, y, z (Spat's axes).")]
        public string RoomPosition = "/room/{0}/xyz";
        [Tooltip("Yaw of a source, in degrees (sent with Send Orientation).")]
        public string SourceYaw = "/source/{0}/yaw";
        [Tooltip("Pitch of a source, in degrees (sent with Send Orientation).")]
        public string SourcePitch = "/source/{0}/pitch";
        [Tooltip("Name of a source (sent with Send Names).")]
        public string SourceName = "/source/{0}/name";
    }

    /// <summary>
    /// Sends the positions of the ASIO Audio Sources (and of rooms or other objects) to Spat Revolution over OSC.
    ///
    /// Every ASIO Audio Source of the loaded scenes is tracked automatically: its number in Spat Revolution is its output channel
    /// (the ReaRoute input / REAPER track it arrives on). Positions are sent when objects move (at most MaxSendRate times per second),
    /// re-sent every KeepAliveInterval seconds, and converted from Unity's axes (Y up) to Spat's (Z up), relative to an optional Origin.
    /// The messages go directly to Spat Revolution: no OSC Manager or per-object component is needed.
    /// </summary>
    [AddComponentMenu("AsioAudioUnity/Spat Controller")]
    public class SpatController : MonoBehaviour
    {
        [SerializeField] private string _spatAddress = "127.0.0.1";
        /// <summary>IP address of the computer running Spat Revolution (127.0.0.1: this computer).</summary>
        public string SpatAddress
        {
            get { return _spatAddress; }
            set { _spatAddress = value; }
        }

        [SerializeField] private int _spatPort = 8100;
        /// <summary>The port of Spat Revolution's OSC input (8100 in the package's setup guide).</summary>
        public int SpatPort
        {
            get { return _spatPort; }
            set { _spatPort = value; }
        }

        [SerializeField] private bool _trackAllSources = true;
        /// <summary>Sends the position of every ASIO Audio Source of the loaded scenes, as the Spat source numbered like its output channel (or like its SourceMappings entry).</summary>
        public bool TrackAllSources
        {
            get { return _trackAllSources; }
            set { _trackAllSources = value; }
        }

        [SerializeField] private List<SpatSourceMapping> _sourceMappings = new List<SpatSourceMapping>();
        /// <summary>
        /// ASIO Audio Sources sent with another Spat number than their output channel, or not sent. Sources without an entry use their output channel.
        /// </summary>
        public List<SpatSourceMapping> SourceMappings => _sourceMappings;

        /// <summary>The mapping of a source, or null if it uses its output channel.</summary>
        public SpatSourceMapping GetMapping(AsioAudioSource source) => source == null ? null : _sourceMappings.Find(mapping => mapping != null && mapping.Source == source);

        /// <summary>The Spat source number of an ASIO Audio Source: its mapping, or its output channel (0 if it has neither).</summary>
        public int GetSpatIndex(AsioAudioSource source)
        {
            SpatSourceMapping mapping = GetMapping(source);
            return mapping != null ? mapping.SpatIndex : Mathf.Max(0, source != null ? source.TargetOutputChannel : 0);
        }

        /// <summary>Sends an ASIO Audio Source as another Spat source number (replaces its mapping).</summary>
        public void SetSpatIndex(AsioAudioSource source, int spatIndex)
        {
            SpatSourceMapping mapping = GetMapping(source);
            if (mapping == null) _sourceMappings.Add(mapping = new SpatSourceMapping { Source = source });
            mapping.SpatIndex = Mathf.Max(1, spatIndex);
        }

        [SerializeField] private List<SpatTrackedObject> _trackedObjects = new List<SpatTrackedObject>();
        /// <summary>Other objects whose position is sent: rooms, or sources that aren't ASIO Audio Sources.</summary>
        public List<SpatTrackedObject> TrackedObjects => _trackedObjects;

        [SerializeField] private Transform _origin;
        /// <summary>Positions are sent relative to this object (its position, rotation and scale), e.g. the listener or the room's center. Empty: the world origin.</summary>
        public Transform Origin
        {
            get { return _origin; }
            set { _origin = value; }
        }

        [SerializeField] private float _positionScale = 1f;
        /// <summary>Multiplies the positions sent (e.g. to convert units).</summary>
        public float PositionScale
        {
            get { return _positionScale; }
            set { _positionScale = value; }
        }

        [SerializeField][Min(0f)] private float _maxSendRate = 60f;
        /// <summary>Maximum number of messages per second, per object (0: no limit, up to one per frame).</summary>
        public float MaxSendRate
        {
            get { return _maxSendRate; }
            set { _maxSendRate = Mathf.Max(0f, value); }
        }

        [SerializeField][Min(0f)] private float _keepAliveInterval = 1f;
        /// <summary>Positions are re-sent every this many seconds even if nothing moves, so a Spat session that restarts gets them back (0: only on movement).</summary>
        public float KeepAliveInterval
        {
            get { return _keepAliveInterval; }
            set { _keepAliveInterval = Mathf.Max(0f, value); }
        }

        [SerializeField][Min(0f)] private float _movementThreshold = 0.001f;
        /// <summary>Smallest movement (in Unity units) that sends a new position.</summary>
        public float MovementThreshold
        {
            get { return _movementThreshold; }
            set { _movementThreshold = Mathf.Max(0f, value); }
        }

        [SerializeField] private bool _resetPositionsOnQuit = false;
        /// <summary>Sends the position (0, 0, 0) for every tracked object when the application quits (or Play mode ends).</summary>
        public bool ResetPositionsOnQuit
        {
            get { return _resetPositionsOnQuit; }
            set { _resetPositionsOnQuit = value; }
        }

        [SerializeField] private bool _sendOrientation = false;
        /// <summary>
        /// Also sends the orientation of the sources (yaw and pitch, in degrees, relative to the Origin), for directivity in Spat Revolution.
        /// Yaw 0 faces the Origin's forward (Unity's +Z), positive turning right seen from above; pitch is positive upwards.
        /// </summary>
        public bool SendOrientation
        {
            get { return _sendOrientation; }
            set { _sendOrientation = value; _lastSent.Clear(); }
        }

        [SerializeField] private bool _invertYaw = false;
        /// <summary>Inverts the sign of the yaw sent (positive turning left), if Spat Revolution's convention is the opposite.</summary>
        public bool InvertYaw
        {
            get { return _invertYaw; }
            set { _invertYaw = value; }
        }

        [SerializeField][Min(0f)] private float _rotationThreshold = 0.5f;
        /// <summary>Smallest rotation, in degrees, that sends a new orientation (with Send Orientation).</summary>
        public float RotationThreshold
        {
            get { return _rotationThreshold; }
            set { _rotationThreshold = Mathf.Max(0f, value); }
        }

        [SerializeField] private bool _sendNames = true;
        /// <summary>
        /// Names each Spat source like its Unity object (the ASIO Audio Source's GameObject): when Play mode starts, when a name changes,
        /// and with <see cref="SendAllPositions"/>.
        /// </summary>
        public bool SendNames
        {
            get { return _sendNames; }
            set { _sendNames = value; _lastNames.Clear(); }
        }

        [SerializeField] private SpatOscAddresses _addresses = new SpatOscAddresses();
        /// <summary>The OSC addresses of Spat Revolution.</summary>
        public SpatOscAddresses Addresses => _addresses;

        [SerializeField] private bool _logMessages = false;
        /// <summary>Logs every message sent.</summary>
        public bool LogMessages
        {
            get { return _logMessages; }
            set { _logMessages = value; }
        }

        [SerializeField] private bool _listenToSpat = false;
        /// <summary>
        /// Receives the OSC messages Spat Revolution sends to <see cref="ListenPort"/> (in Play mode), with an OSC output connection in Spat Revolution:
        /// shows that Spat Revolution answers, and lists what it sends (<see cref="ReceivedAddresses"/>, <see cref="MessageReceived"/>).
        /// </summary>
        public bool ListenToSpat
        {
            get { return _listenToSpat; }
            set { _listenToSpat = value; }
        }

        [SerializeField] private int _listenPort = 9100;
        /// <summary>The port Spat Revolution sends its messages to: the port of its OSC output connection (default: 9100).</summary>
        public int ListenPort
        {
            get { return _listenPort; }
            set { _listenPort = value; }
        }

        [SerializeField] private bool _followSpat = false;
        /// <summary>
        /// With <see cref="ListenToSpat"/>: a source or room moved in Spat Revolution moves in Unity too (only objects whose position Unity sends),
        /// in Play mode and in Edit mode (with Undo). While it is dragged in Spat Revolution, Unity doesn't send its position back.
        /// Spat Revolution's echoes of the positions Unity sends are recognised and ignored.
        /// </summary>
        public bool FollowSpat
        {
            get { return _followSpat; }
            set { _followSpat = value; }
        }

        [SerializeField] private bool _invertAzimuth = false;
        /// <summary>
        /// Inverts the sign of the azimuth received from Spat Revolution (<c>/source/N/aed</c>): by default, azimuth 0 is the Origin's forward,
        /// positive to the right seen from above. <see cref="AzimuthLooksInverted"/> tells whether Spat's echoes suggest the opposite.
        /// </summary>
        public bool InvertAzimuth
        {
            get { return _invertAzimuth; }
            set { _invertAzimuth = value; }
        }

        [SerializeField] private bool _logReceivedMessages = true;
        /// <summary>Logs the messages received from Spat Revolution: each address the first time, then at most once per second.</summary>
        public bool LogReceivedMessages
        {
            get { return _logReceivedMessages; }
            set { _logReceivedMessages = value; }
        }

        private readonly OscUdpSender _sender = new OscUdpSender();

        // Last position and rotation sent for each Spat object ("source/3", "room/1").
        private readonly Dictionary<string, (Vector3 position, Quaternion rotation, float time)> _lastSent = new Dictionary<string, (Vector3, Quaternion, float)>();

        // Last name sent for each Spat source number.
        private readonly Dictionary<int, string> _lastNames = new Dictionary<int, string>();

        private AsioAudioSource[] _sources = new AsioAudioSource[0];
        private float _nextSourceRefresh;

        // Gives AsioAudioSource.SpatNumber the Spat Controller's numbering (used to name REAPER tracks), in Edit mode and in Play mode.
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void RegisterSpatNumbers()
        {
            AsioAudioSource.SpatNumberProvider = FindSpatNumber;
        }

        // The Spat number of a source, from the first Spat Controller that sends it (enabled ones first); 0 if none does.
        private static int FindSpatNumber(AsioAudioSource source)
        {
            if (source == null) return 0;
            foreach (SpatController controller in FindObjectsByType<SpatController>(FindObjectsInactive.Include, FindObjectsSortMode.None).OrderByDescending(c => c.isActiveAndEnabled))
            {
                if (!controller.TrackAllSources) continue;
                SpatSourceMapping mapping = controller.GetMapping(source);
                if (mapping != null && !mapping.Send) continue;
                return controller.GetSpatIndex(source);
            }
            return 0;
        }

        // ---------- Tracked objects ----------

        /// <summary>A tracked object of the current frame: what it is in Spat, and where it is in Unity.</summary>
        public struct TrackedPosition
        {
            public SpatObjectType Type;
            public int Index;
            public Transform Target;
            public string Name;
            /// <summary>The ASIO Audio Source, for tracked sources (null for other objects).</summary>
            public AsioAudioSource Source;
        }

        /// <summary>Everything that is tracked now: the ASIO Audio Sources (if TrackAllSources) and the TrackedObjects.</summary>
        public List<TrackedPosition> GetTrackedPositions()
        {
            List<TrackedPosition> tracked = new List<TrackedPosition>();
            if (_trackAllSources)
            {
                foreach (AsioAudioSource source in _sources)
                {
                    if (source == null || !source.isActiveAndEnabled) continue;
                    SpatSourceMapping mapping = GetMapping(source);
                    if (mapping != null && !mapping.Send) continue;
                    int index = GetSpatIndex(source);
                    if (index <= 0) continue; // no output channel and no mapping
                    tracked.Add(new TrackedPosition { Type = SpatObjectType.Source, Index = index, Target = source.transform, Name = source.name, Source = source });
                }
            }
            foreach (SpatTrackedObject trackedObject in _trackedObjects)
            {
                if (trackedObject == null || trackedObject.Target == null || trackedObject.Index < 1) continue;
                // An explicit entry replaces the automatic one with the same number.
                tracked.RemoveAll(other => other.Type == trackedObject.Type && other.Index == trackedObject.Index);
                tracked.Add(new TrackedPosition { Type = trackedObject.Type, Index = trackedObject.Index, Target = trackedObject.Target, Name = trackedObject.Target.name });
            }
            return tracked;
        }

        /// <summary>Spat's coordinates of a Unity position: relative to Origin, scaled, and with Z up (Unity's x, z, y).</summary>
        public Vector3 ToSpatPosition(Vector3 worldPosition)
        {
            Vector3 local = _origin != null ? _origin.InverseTransformPoint(worldPosition) : worldPosition;
            local *= _positionScale;
            return new Vector3(local.x, local.z, local.y);
        }

        /// <summary>The Unity position of Spat coordinates: the inverse of <see cref="ToSpatPosition"/>.</summary>
        public Vector3 FromSpatPosition(Vector3 spatPosition)
        {
            Vector3 local = new Vector3(spatPosition.x, spatPosition.z, spatPosition.y);
            if (_positionScale != 0f) local /= _positionScale;
            return _origin != null ? _origin.TransformPoint(local) : local;
        }

        /// <summary>
        /// Spat's x, y, z (x right, y front, z up) of an azimuth, elevation and distance as Spat Revolution sends them (<c>/source/N/aed</c>):
        /// azimuth 0 in front, positive to the right (to the left with <see cref="InvertAzimuth"/>), elevation positive upwards, in degrees.
        /// </summary>
        public Vector3 AedToSpatPosition(Vector3 aed) => AedToXyz(aed, _invertAzimuth);

        private static Vector3 AedToXyz(Vector3 aed, bool invertAzimuth)
        {
            float azimuth = (invertAzimuth ? -aed.x : aed.x) * Mathf.Deg2Rad;
            float elevation = aed.y * Mathf.Deg2Rad;
            float horizontal = aed.z * Mathf.Cos(elevation);
            return new Vector3(horizontal * Mathf.Sin(azimuth), horizontal * Mathf.Cos(azimuth), aed.z * Mathf.Sin(elevation));
        }

        /// <summary>
        /// Spat's orientation of a Unity rotation, in degrees, relative to the Origin: yaw (0 facing the Origin's forward, positive turning right
        /// seen from above, or left with Invert Yaw) and pitch (positive upwards).
        /// </summary>
        public Vector2 ToSpatOrientation(Quaternion worldRotation)
        {
            Quaternion local = _origin != null ? Quaternion.Inverse(_origin.rotation) * worldRotation : worldRotation;
            Vector3 forward = local * Vector3.forward;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            return new Vector2(_invertYaw ? -yaw : yaw, pitch);
        }

        // ---------- Lifecycle ----------

        private void OnEnable()
        {
            _lastSent.Clear(); // send everything right away
            _lastNames.Clear();
            RefreshSources();
            _nextSourceRefresh = 0f; // names are sent on the first Update
        }

        private void Update()
        {
            UpdateListening();
            ReleaseStaleSpatMoves();

            if (Time.unscaledTime >= _nextSourceRefresh)
            {
                RefreshSources();
                if (_sendNames) SendSourceNames(false);
            }

            float now = Time.unscaledTime;
            foreach (TrackedPosition tracked in GetTrackedPositions())
            {
                string key = Key(tracked.Type, tracked.Index);
                if (IsHeldBySpat(key)) continue; // being moved in Spat Revolution: don't send it back
                Vector3 position = tracked.Target.position;
                Quaternion rotation = tracked.Target.rotation;
                bool withOrientation = _sendOrientation && tracked.Type == SpatObjectType.Source;

                bool rotated = true;
                if (_lastSent.TryGetValue(key, out (Vector3 position, Quaternion rotation, float time) last))
                {
                    float elapsed = now - last.time;
                    if (_maxSendRate > 0f && elapsed < 1f / _maxSendRate) continue;
                    bool moved = (position - last.position).sqrMagnitude > _movementThreshold * _movementThreshold;
                    rotated = withOrientation && Quaternion.Angle(rotation, last.rotation) > _rotationThreshold;
                    bool keepAliveDue = _keepAliveInterval > 0f && elapsed >= _keepAliveInterval;
                    if (!moved && !rotated && !keepAliveDue) continue;
                    if (keepAliveDue) rotated = true;
                    if (!moved && !keepAliveDue)
                    {
                        // Only the rotation changed: the position isn't sent again
                        SendSourceOrientation(tracked.Index, ToSpatOrientation(rotation));
                        _lastSent[key] = (last.position, rotation, now);
                        continue;
                    }
                }

                SendPosition(tracked.Type, tracked.Index, ToSpatPosition(position));
                if (withOrientation && rotated) SendSourceOrientation(tracked.Index, ToSpatOrientation(rotation));
                _lastSent[key] = (position, rotation, now);
            }
        }

        private void OnApplicationQuit()
        {
            if (!_resetPositionsOnQuit || !isActiveAndEnabled) return;
            foreach (TrackedPosition tracked in GetTrackedPositions()) SendPosition(tracked.Type, tracked.Index, Vector3.zero);
        }

        private void OnDisable()
        {
            StopListening();
        }

        /// <summary>Closes the listen port (it opens again on the next update with Listen To Spat). Called by the editor before Play mode and script reloads.</summary>
        public void StopListening()
        {
            _receiver.Close(); // frees the port
            EndAllSpatMoves();
        }

        /// <summary>
        /// Listens to Spat Revolution outside Play mode (called by the editor, so Follow Spat can place objects in Edit mode).
        /// Does nothing in Play mode: Update does it.
        /// </summary>
        public void EditModeUpdate()
        {
            if (Application.isPlaying) return;
            if (!isActiveAndEnabled) { if (_receiver.IsOpen) StopListening(); return; }
            UpdateListening();
            ReleaseStaleSpatMoves();
        }

        // ---------- Following Spat Revolution ----------

        // The Spat positions Unity sent recently, per object ("source/3"), to recognise Spat Revolution's echoes of them.
        private readonly Dictionary<string, List<(Vector3 position, float time)>> _sentHistory = new Dictionary<string, List<(Vector3, float)>>();
        // Objects being moved in Spat Revolution: when the last move arrived (Time.realtimeSinceStartup).
        private readonly Dictionary<string, float> _heldBySpat = new Dictionary<string, float>();
#if UNITY_EDITOR
        private readonly Dictionary<string, int> _undoGroups = new Dictionary<string, int>();
#endif
        private const float EchoWindow = 1f;     // seconds an echo can arrive after Unity sent the position
        private const float HoldTimeout = 0.5f;  // seconds without a move (and without "release") that end a drag

        /// <summary>How many positions received from Spat Revolution were echoes of positions Unity sent (ignored).</summary>
        public int EchoCount { get; private set; }

        /// <summary>How many moves made in Spat Revolution were applied to Unity objects (Follow Spat).</summary>
        public int AppliedMoveCount { get; private set; }

        /// <summary>
        /// Whether Spat Revolution's echoes only match the positions Unity sent with the azimuth's sign inverted: then tick <see cref="InvertAzimuth"/>.
        /// </summary>
        public bool AzimuthLooksInverted { get; private set; }

        /// <summary>Whether an object is being moved in Spat Revolution now (Unity doesn't send its position meanwhile).</summary>
        public bool IsHeldBySpat(SpatObjectType type, int index) => IsHeldBySpat(Key(type, index));

        private bool IsHeldBySpat(string key) => _heldBySpat.TryGetValue(key, out float time) && Time.realtimeSinceStartup - time < HoldTimeout;

        private void RememberSent(string key, Vector3 spatPosition)
        {
            float now = Time.realtimeSinceStartup;
            if (!_sentHistory.TryGetValue(key, out List<(Vector3 position, float time)> history)) _sentHistory[key] = history = new List<(Vector3, float)>();
            history.RemoveAll(item => now - item.time > EchoWindow);
            if (history.Count >= 64) history.RemoveAt(0);
            history.Add((spatPosition, now));
        }

        // Whether a received position is one Unity sent in the last second (within 1 cm, or 0.5 % of the distance).
        private bool MatchesSent(string key, Vector3 spatPosition, out bool recentlySent)
        {
            recentlySent = false;
            if (!_sentHistory.TryGetValue(key, out List<(Vector3 position, float time)> history)) return false;
            float now = Time.realtimeSinceStartup;
            float tolerance = Mathf.Max(0.01f, spatPosition.magnitude * 0.005f);
            bool match = false;
            foreach ((Vector3 position, float time) in history)
            {
                if (now - time > EchoWindow) continue;
                if (now - time < 0.25f) recentlySent = true;
                if ((position - spatPosition).sqrMagnitude <= tolerance * tolerance) match = true;
            }
            return match;
        }

        // /source/3/aed, /source/3/xyz, /room/1/xyz...: a position of a Spat object.
        private static readonly System.Text.RegularExpressions.Regex PositionAddress =
            new System.Text.RegularExpressions.Regex(@"^/(source|room)/(\d+)/(aed|xyz)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        private void HandlePositionMessage(OscMessage message)
        {
            System.Text.RegularExpressions.Match match = PositionAddress.Match(message.address);
            if (!match.Success) return;
            SpatObjectType type = match.Groups[1].Value == "source" ? SpatObjectType.Source : SpatObjectType.Room;
            int index = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            string key = Key(type, index);

            // "release": the drag in Spat Revolution is over
            if (message.values.Count == 1 && message.values[0] is string text)
            {
                if (text == "release") EndSpatMove(key);
                return;
            }
            if (message.values.Count < 3 || !TryGetFloat(message.values[0], out float a) || !TryGetFloat(message.values[1], out float b) || !TryGetFloat(message.values[2], out float c)) return;

            bool aed = match.Groups[3].Value == "aed";
            Vector3 spatPosition = aed ? AedToXyz(new Vector3(a, b, c), _invertAzimuth) : new Vector3(a, b, c);

            // An echo of what Unity sent? (Also with the azimuth mirrored: then the convention is the opposite, and it is still an echo.)
            if (MatchesSent(key, spatPosition, out bool recentlySent))
            {
                EchoCount++;
                return;
            }
            if (aed && Mathf.Abs(spatPosition.x) > 0.01f && MatchesSent(key, AedToXyz(new Vector3(a, b, c), !_invertAzimuth), out _))
            {
                EchoCount++;
                if (!AzimuthLooksInverted)
                {
                    AzimuthLooksInverted = true;
                    Debug.LogWarning("Spat Controller: Spat Revolution's positions only match Unity's with the azimuth inverted: tick Invert Azimuth (Listening card).", this);
                }
                return;
            }
            if (!_followSpat) return;
            if (recentlySent && !IsHeldBySpat(key)) return; // Unity is moving it: probably a late echo; Unity keeps control

            Transform target = FindTarget(type, index);
            if (target == null) return;
            ApplySpatMove(key, target, FromSpatPosition(spatPosition));
        }

        private Transform FindTarget(SpatObjectType type, int index)
        {
            foreach (TrackedPosition tracked in GetTrackedPositions())
            {
                if (tracked.Type == type && tracked.Index == index) return tracked.Target;
            }
            return null;
        }

        private void ApplySpatMove(string key, Transform target, Vector3 position)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (!_undoGroups.ContainsKey(key))
                {
                    UnityEditor.Undo.IncrementCurrentGroup();
                    UnityEditor.Undo.SetCurrentGroupName("Move " + target.name + " from Spat Revolution");
                    _undoGroups[key] = UnityEditor.Undo.GetCurrentGroup();
                }
                UnityEditor.Undo.RecordObject(target, "Move " + target.name + " from Spat Revolution");
            }
#endif
            target.position = position;
            _heldBySpat[key] = Time.realtimeSinceStartup;
            // Unity doesn't send this position back: it is where Spat Revolution already is.
            _lastSent[key] = (position, target.rotation, Time.unscaledTime);
            AppliedMoveCount++;
        }

        private void EndSpatMove(string key)
        {
            _heldBySpat.Remove(key);
#if UNITY_EDITOR
            if (_undoGroups.TryGetValue(key, out int group))
            {
                UnityEditor.Undo.CollapseUndoOperations(group);
                _undoGroups.Remove(key);
            }
#endif
        }

        private void EndAllSpatMoves()
        {
            foreach (string key in _heldBySpat.Keys.ToList()) EndSpatMove(key);
        }

        // Drags whose "release" didn't arrive end after HoldTimeout.
        private void ReleaseStaleSpatMoves()
        {
            if (_heldBySpat.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            foreach (KeyValuePair<string, float> held in _heldBySpat.ToList())
            {
                if (now - held.Value >= HoldTimeout) EndSpatMove(held.Key);
            }
        }

        private static bool TryGetFloat(object value, out float result)
        {
            switch (value)
            {
                case float f: result = f; return true;
                case double d: result = (float)d; return true;
                case int i: result = i; return true;
                default: result = 0f; return false;
            }
        }

        private void OnDestroy()
        {
            _sender.Dispose();
            _receiver.Dispose();
        }

        // ---------- Listening to Spat Revolution ----------

        /// <summary>An address received from Spat Revolution: how many messages, the last values, and the latest values of each kind.</summary>
        public struct ReceivedAddress
        {
            public string Address;
            public string LastValues;
            /// <summary>
            /// The latest values of each kind received on this address (up to 4 kinds, by value types: f float, i int, s string...),
            /// e.g. <c>f f f: 30.5 0 2 | s: "release"</c>: a "release" message doesn't hide the numbers sent before it.
            /// </summary>
            public string Variants;
            public int Count;
            /// <summary>When the last message arrived (Time.unscaledTime).</summary>
            public float LastTime;
        }

        private readonly OscUdpReceiver _receiver = new OscUdpReceiver();
        private readonly List<ReceivedAddress> _receivedAddresses = new List<ReceivedAddress>();
        private readonly Dictionary<string, int> _receivedIndex = new Dictionary<string, int>();
        // Per address: the latest values of each kind (value types), in the order the kinds first arrived.
        private readonly Dictionary<string, List<(string types, string values)>> _receivedVariants = new Dictionary<string, List<(string types, string values)>>();
        private const int MaxVariants = 4;
        private readonly Dictionary<string, float> _lastLogged = new Dictionary<string, float>();
        private string _loggedListenError;
        private const int MaxLoggedMessages = 300; // Logged in a session: past that, the Inspector's list still shows everything

        /// <summary>Whether Unity listens to Spat Revolution (Listen To Spat, in Play mode).</summary>
        public bool IsListening => _receiver.IsOpen;

        /// <summary>Why the listen port couldn't be opened, or null.</summary>
        public string ListenError => _receiver.LastError;

        /// <summary>Whether Spat Revolution has sent a message since Unity started listening.</summary>
        public bool HasHeardFromSpat { get; private set; }

        /// <summary>The number of messages received from Spat Revolution since Unity started listening (or since ClearReceivedMessages).</summary>
        public int ReceivedMessageCount { get; private set; }

        /// <summary>The addresses received from Spat Revolution, in the order they first arrived.</summary>
        public IReadOnlyList<ReceivedAddress> ReceivedAddresses => _receivedAddresses;

        /// <summary>Raised on the main thread for each message received from Spat Revolution (Listen To Spat).</summary>
        public event Action<OscMessage> MessageReceived;

        /// <summary>Forgets the messages received so far (the list, the count and the log limit).</summary>
        public void ClearReceivedMessages()
        {
            _receivedAddresses.Clear();
            _receivedIndex.Clear();
            _receivedVariants.Clear();
            _lastLogged.Clear();
            ReceivedMessageCount = 0;
            _loggedCount = 0;
            EchoCount = 0;
            AppliedMoveCount = 0;
            AzimuthLooksInverted = false;
        }

        /// <summary>The received addresses as text, one per line ("address  ×count  values of each kind"), e.g. to copy them.</summary>
        public string DescribeReceivedMessages()
        {
            return string.Join("\n", _receivedAddresses.Select(item => item.Address + "  ×" + item.Count + "  " + item.Variants));
        }

        // The latest values of each kind received on an address, as text ("f f f: 30.5 0 2 | s: \"release\"").
        private string UpdateVariants(string address, OscMessage message, string values)
        {
            string types = message.values == null || message.values.Count == 0 ? "-" : string.Join(" ", message.values.Cast<object>().Select(TypeTag));
            if (!_receivedVariants.TryGetValue(address, out List<(string types, string values)> variants)) _receivedVariants[address] = variants = new List<(string, string)>();
            int index = variants.FindIndex(variant => variant.types == types);
            if (index >= 0) variants[index] = (types, values);
            else if (variants.Count < MaxVariants) variants.Add((types, values));
            return string.Join(" | ", variants.Select(variant => variant.types + ": " + variant.values));
        }

        private static string TypeTag(object value)
        {
            switch (value)
            {
                case float _: return "f";
                case double _: return "d";
                case int _: return "i";
                case long _: return "h";
                case string _: return "s";
                case byte[] _: return "b";
                case null: return "N";
                default: return value.GetType().Name;
            }
        }

        private int _loggedCount;

        private void UpdateListening()
        {
            if (!_listenToSpat)
            {
                if (_receiver.IsOpen) _receiver.Close();
                return;
            }
            if (!_receiver.IsOpen || _receiver.Port != _listenPort)
            {
                HasHeardFromSpat = false;
                if (_receiver.Open(_listenPort)) _loggedListenError = null;
                else if (_receiver.LastError != _loggedListenError)
                {
                    _loggedListenError = _receiver.LastError;
                    Debug.LogWarning("Spat Controller: can't listen to Spat Revolution on port " + _listenPort + ": " + _receiver.LastError, this);
                }
            }
            _receiver.Poll(OnSpatMessage);
        }

        private void OnSpatMessage(OscMessage message)
        {
            if (message == null || string.IsNullOrEmpty(message.address)) return;
            HasHeardFromSpat = true;
            ReceivedMessageCount++;
            string values = FormatValues(message);
            string variants = UpdateVariants(message.address, message, values);
            float now = Time.unscaledTime;

            if (_receivedIndex.TryGetValue(message.address, out int index))
            {
                ReceivedAddress item = _receivedAddresses[index];
                item.Count++;
                item.LastValues = values;
                item.Variants = variants;
                item.LastTime = now;
                _receivedAddresses[index] = item;
            }
            else
            {
                _receivedIndex[message.address] = _receivedAddresses.Count;
                _receivedAddresses.Add(new ReceivedAddress { Address = message.address, LastValues = values, Variants = variants, Count = 1, LastTime = now });
            }

            if (_logReceivedMessages && _loggedCount < MaxLoggedMessages && (!_lastLogged.TryGetValue(message.address, out float last) || now - last >= 1f))
            {
                _lastLogged[message.address] = now;
                _loggedCount++;
                Debug.Log("Spat Controller: received " + message.address + " " + values
                    + (_loggedCount == MaxLoggedMessages ? "   (log limit reached: the Spat Controller's Inspector lists the rest)" : ""), this);
            }

            HandlePositionMessage(message);

            try { MessageReceived?.Invoke(message); }
            catch (Exception e) { Debug.LogException(e, this); }
        }

        private static string FormatValues(OscMessage message)
        {
            if (message.values == null || message.values.Count == 0) return "";
            List<string> parts = new List<string>();
            foreach (object value in message.values)
            {
                switch (value)
                {
                    case float f: parts.Add(f.ToString("0.####", CultureInfo.InvariantCulture)); break;
                    case double d: parts.Add(d.ToString("0.####", CultureInfo.InvariantCulture)); break;
                    case string s: parts.Add("\"" + s + "\""); break;
                    case byte[] blob: parts.Add("<blob " + blob.Length + " bytes>"); break;
                    case null: parts.Add("null"); break;
                    default: parts.Add(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
                }
            }
            return string.Join(" ", parts);
        }

        /// <summary>The ASIO Audio Sources found in the loaded scenes (see RefreshSources).</summary>
        public IReadOnlyList<AsioAudioSource> Sources => _sources;

        /// <summary>Looks for the ASIO Audio Sources of the loaded scenes (done twice per second while playing, and when enabled).</summary>
        public void RefreshSources()
        {
            _sources = _trackAllSources ? FindObjectsByType<AsioAudioSource>(FindObjectsSortMode.None) : new AsioAudioSource[0];
            _nextSourceRefresh = Time.unscaledTime + 0.5f;
        }

        // ---------- OSC ----------

        /// <summary>Sends the positions of everything tracked now (also works in Edit mode, e.g. from the Inspector).</summary>
        public void SendAllPositions()
        {
            RefreshSources();
            foreach (TrackedPosition tracked in GetTrackedPositions())
            {
                SendPosition(tracked.Type, tracked.Index, ToSpatPosition(tracked.Target.position));
                if (_sendOrientation && tracked.Type == SpatObjectType.Source) SendSourceOrientation(tracked.Index, ToSpatOrientation(tracked.Target.rotation));
                _lastSent[Key(tracked.Type, tracked.Index)] = (tracked.Target.position, tracked.Target.rotation, Time.unscaledTime);
            }
            if (_sendNames) SendSourceNames(true);
        }

        /// <summary>Sends the name of every tracked source (all of them, or only those whose name changed since it was last sent).</summary>
        public void SendSourceNames(bool all = true)
        {
            foreach (TrackedPosition tracked in GetTrackedPositions())
            {
                if (tracked.Type != SpatObjectType.Source || string.IsNullOrEmpty(tracked.Name)) continue;
                if (!all && _lastNames.TryGetValue(tracked.Index, out string last) && last == tracked.Name) continue;
                SendSourceName(tracked.Index, tracked.Name);
            }
        }

        /// <summary>Sends the name of a Spat source.</summary>
        public void SendSourceName(int index, string name)
        {
            Send(string.Format(CultureInfo.InvariantCulture, _addresses.SourceName, index), name ?? "");
            _lastNames[index] = name;
        }

        /// <summary>Sends a position (in Spat's coordinates) for a source or a room.</summary>
        public void SendPosition(SpatObjectType type, int index, Vector3 spatPosition)
        {
            string template = type == SpatObjectType.Source ? _addresses.SourcePosition : _addresses.RoomPosition;
            Send(string.Format(CultureInfo.InvariantCulture, template, index), spatPosition.x, spatPosition.y, spatPosition.z);
            RememberSent(Key(type, index), spatPosition);
        }

        /// <summary>Sends the orientation of a source: yaw and pitch, in degrees (see <see cref="ToSpatOrientation"/>).</summary>
        public void SendSourceOrientation(int index, Vector2 yawPitch)
        {
            Send(string.Format(CultureInfo.InvariantCulture, _addresses.SourceYaw, index), yawPitch.x);
            Send(string.Format(CultureInfo.InvariantCulture, _addresses.SourcePitch, index), yawPitch.y);
        }

        /// <summary>Sends any OSC message to Spat Revolution (int, float or string values).</summary>
        public void Send(string address, params object[] values)
        {
            if (string.IsNullOrEmpty(address)) return;
            _sender.SetDestination(_spatAddress, _spatPort);
            if (_sender.Send(address, values) && _logMessages)
            {
                Debug.Log("Spat Controller: sent " + address + " " + string.Join(" ", values.Select(value => value is float number ? number.ToString("0.###", CultureInfo.InvariantCulture) : value.ToString())) + " to " + _sender, this);
            }
        }

        private static string Key(SpatObjectType type, int index) => (type == SpatObjectType.Source ? "source/" : "room/") + index;
    }
}
