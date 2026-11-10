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

        private readonly OscUdpSender _sender = new OscUdpSender();

        // Last message sent for each Spat object ("source/3", "room/1").
        private readonly Dictionary<string, (Vector3 position, float time)> _lastSent = new Dictionary<string, (Vector3, float)>();

        private AsioAudioSource[] _sources = new AsioAudioSource[0];
        private float _nextSourceRefresh;

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

        // ---------- Lifecycle ----------

        private void OnEnable()
        {
            _lastSent.Clear(); // send everything right away
            RefreshSources();
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextSourceRefresh) RefreshSources();

            float now = Time.unscaledTime;
            foreach (TrackedPosition tracked in GetTrackedPositions())
            {
                string key = Key(tracked.Type, tracked.Index);
                Vector3 position = tracked.Target.position;

                if (_lastSent.TryGetValue(key, out (Vector3 position, float time) last))
                {
                    float elapsed = now - last.time;
                    if (_maxSendRate > 0f && elapsed < 1f / _maxSendRate) continue;
                    bool moved = (position - last.position).sqrMagnitude > _movementThreshold * _movementThreshold;
                    bool keepAliveDue = _keepAliveInterval > 0f && elapsed >= _keepAliveInterval;
                    if (!moved && !keepAliveDue) continue;
                }

                SendPosition(tracked.Type, tracked.Index, ToSpatPosition(position));
                _lastSent[key] = (position, now);
            }
        }

        private void OnApplicationQuit()
        {
            if (!_resetPositionsOnQuit || !isActiveAndEnabled) return;
            foreach (TrackedPosition tracked in GetTrackedPositions()) SendPosition(tracked.Type, tracked.Index, Vector3.zero);
        }

        private void OnDestroy()
        {
            _sender.Dispose();
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
                _lastSent[Key(tracked.Type, tracked.Index)] = (tracked.Target.position, Time.unscaledTime);
            }
        }

        /// <summary>Sends a position (in Spat's coordinates) for a source or a room.</summary>
        public void SendPosition(SpatObjectType type, int index, Vector3 spatPosition)
        {
            string template = type == SpatObjectType.Source ? _addresses.SourcePosition : _addresses.RoomPosition;
            Send(string.Format(CultureInfo.InvariantCulture, template, index), spatPosition.x, spatPosition.y, spatPosition.z);
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
