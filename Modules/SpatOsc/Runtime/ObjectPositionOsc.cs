using System;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Sends the position of this GameObject over OSC, to "/[OscObject]/[Index]/xyz" (as x, z, y: Unity's Y-up axes converted to Z-up).
    /// The position is sent when the object moves (at most MaxSendRate times per second), and re-sent every KeepAliveInterval seconds
    /// so a receiver that restarts gets it back.
    /// Obsolete: replaced by the <see cref="SpatController"/>.
    /// </summary>
    [Obsolete("Replaced by the Spat Controller, which sends the positions of every ASIO Audio Source and of rooms from a single component. Use its Inspector button to replace this component. Will be removed in version 4.0.")]
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
            if (Osc != null)
            {
                SubscribeToClosing();
                return true;
            }

            if (!_missingOscLogged)
            {
                Debug.LogError("The " + GetType().Name + " on GameObject \"" + name + "\" can't find any OSC Manager in the scene: its position won't be sent.");
                _missingOscLogged = true;
            }
            return false;
        }

        // When the OSC Manager closes (end of Play mode, scene unloaded), the position is reset to (0, 0, 0) in Spat Revolution.
        private OSC _subscribedOsc;

        private void SubscribeToClosing()
        {
            if (_subscribedOsc == Osc) return;
            UnsubscribeFromClosing();
            Osc.Closing += OnOscClosing;
            _subscribedOsc = Osc;
        }

        private void UnsubscribeFromClosing()
        {
            if (_subscribedOsc != null) _subscribedOsc.Closing -= OnOscClosing;
            _subscribedOsc = null;
        }

        private void OnOscClosing(OSC osc)
        {
            if (osc == Osc) SendResetPositionOsc();
        }

        private void OnEnable()
        {
            // Send the position right away when (re)enabled.
            _hasSent = false;
            if (Osc != null) SubscribeToClosing();
        }

        private void OnDestroy()
        {
            UnsubscribeFromClosing();
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
