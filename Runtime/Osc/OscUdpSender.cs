using System;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Sends OSC messages to one address and port over UDP, without an OSC Manager (no listening socket).
    /// Used by the REAPER and Spat Revolution controllers. Call from the main thread.
    /// </summary>
    public sealed class OscUdpSender : IDisposable
    {
        private UdpClient _socket;
        private IPEndPoint _endPoint;
        private string _host;
        private int _port;
        private readonly byte[] _packet = new byte[2048];
        private bool _errorLogged;

        /// <summary>The destination: IP address and port.</summary>
        public void SetDestination(string host, int port)
        {
            host = (host ?? "").Trim();
            if (host == _host && port == _port) return;
            _host = host;
            _port = port;
            Close();
        }

        /// <summary>Sends a message with any number of int, float or string values. Returns false (and logs once) if it couldn't be sent.</summary>
        public bool Send(string address, params object[] values)
        {
            if (string.IsNullOrEmpty(address)) return false;

            OscMessage message = new OscMessage { address = address };
            foreach (object value in values) message.values.Add(value);
            return Send(message);
        }

        /// <summary>Sends a message. Returns false (and logs once) if it couldn't be sent.</summary>
        public bool Send(OscMessage message)
        {
            try
            {
                if (_socket == null) Open();
                int length = OSC.OscMessageToPacket(message, _packet, _packet.Length);
                _socket.Send(_packet, length, _endPoint);
                return true;
            }
            catch (Exception e)
            {
                if (!_errorLogged) Debug.LogWarning("OSC: could not send to " + _host + ":" + _port + ": " + e.GetBaseException().Message);
                _errorLogged = true;
                Close();
                return false;
            }
        }

        /// <summary>The destination, as "address:port".</summary>
        public override string ToString() => _host + ":" + _port;

        private void Open()
        {
            IPAddress address = IPAddress.Parse(_host);
            _endPoint = new IPEndPoint(address, _port);
            // Sending only. Bound to the loopback address when the destination is this computer (no firewall prompt).
            _socket = IPAddress.IsLoopback(address) ? new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)) : new UdpClient();
            _errorLogged = false;
        }

        private void Close()
        {
            _socket?.Close();
            _socket = null;
        }

        public void Dispose()
        {
            Close();
        }
    }
}
