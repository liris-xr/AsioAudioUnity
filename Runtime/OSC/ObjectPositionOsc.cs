using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Sends the position of this GameObject over OSC, to "/[OscObject]/[Index]/xyz" (as x, z, y: Unity's Y-up axes converted to Z-up).
    /// The position is sent when the object moves (at most MaxSendRate times per second), and re-sent every KeepAliveInterval seconds
    /// so a receiver that restarts gets it back.
    /// </summary>
    public abstract class ObjectPositionOsc : MonoBehaviour
    {
        [SerializeField] public OSC _osc;
        public OSC Osc
        {
            get { return _osc; }
            set { _osc = value; }
        }

        [SerializeField][Min(0f)] private float _maxSendRate = 60f;
        /// <summary>
        /// Maximum number of position messages per second (0: no limit, up to one per frame).
        /// </summary>
        public float MaxSendRate
        {
            get { return _maxSendRate; }
            set { _maxSendRate = Mathf.Max(0f, value); }
        }

        [SerializeField][Min(0f)] private float _keepAliveInterval = 1f;
        /// <summary>
        /// The position is re-sent every this many seconds even when the object doesn't move (0: only send when it moves).
        /// </summary>
        public float KeepAliveInterval
        {
            get { return _keepAliveInterval; }
            set { _keepAliveInterval = Mathf.Max(0f, value); }
        }

        [SerializeField][Min(0f)] private float _movementThreshold = 0.001f;
        /// <summary>
        /// Smallest movement (in Unity units) that triggers sending the position.
        /// </summary>
        public float MovementThreshold
        {
            get { return _movementThreshold; }
            set { _movementThreshold = Mathf.Max(0f, value); }
        }

        public abstract string OscObject { get; }

        public abstract int Index { get; set; }

        private Vector3 _lastSentPosition;
        private float _lastSendTime = float.NegativeInfinity;
        private bool _hasSent;
        private bool _missingOscLogged;

        private string Address => "/" + OscObject + "/" + Index + "/xyz";

        /// <summary>
        /// Sends the current position now.
        /// </summary>
        public void SendPositionOsc()
        {
            if (!EnsureOsc()) return;

            Vector3 position = transform.position;
            OscMessage message = new OscMessage();
            message.address = Address;
            message.values.Add(position.x);
            message.values.Add(position.z);
            message.values.Add(position.y);
            Osc.Send(message);

            _lastSentPosition = position;
            _lastSendTime = Time.unscaledTime;
            _hasSent = true;
        }

        /// <summary>
        /// Sends the position (0, 0, 0).
        /// </summary>
        public void SendResetPositionOsc()
        {
            if (!EnsureOsc()) return;

            OscMessage message = new OscMessage();
            message.address = Address;
            message.values.Add(0f);
            message.values.Add(0f);
            message.values.Add(0f);
            Osc.Send(message);
        }

        private bool EnsureOsc()
        {
            if (Osc == null) Osc = FindFirstObjectByType<OSC>();
            if (Osc != null) return true;

            if (!_missingOscLogged)
            {
                Debug.LogError("The " + GetType().Name + " on GameObject \"" + name + "\" can't find any OSC Manager in the scene: its position won't be sent.");
                _missingOscLogged = true;
            }
            return false;
        }

        private void OnEnable()
        {
            // Send the position right away when (re)enabled.
            _hasSent = false;
        }

        void Update()
        {
            float now = Time.unscaledTime;
            float elapsed = now - _lastSendTime;

            if (MaxSendRate > 0f && elapsed < 1f / MaxSendRate) return;

            bool moved = (transform.position - _lastSentPosition).sqrMagnitude > MovementThreshold * MovementThreshold;
            bool keepAliveDue = KeepAliveInterval > 0f && elapsed >= KeepAliveInterval;

            if (!_hasSent || moved || keepAliveDue) SendPositionOsc();
        }
    }
}
