using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Receives OSC messages on a UDP port, without an OSC Manager (used by the REAPER Controller to follow REAPER's transport).
    /// Messages are received on a background thread and handed to the main thread by <see cref="Poll"/>.
    /// </summary>
    public sealed class OscUdpReceiver : IDisposable
    {
        private readonly ConcurrentQueue<OscMessage> _received = new ConcurrentQueue<OscMessage>();
        private UdpClient _socket;
        private Thread _thread;
        private volatile bool _running;

        /// <summary>The port listened to, or 0 when closed.</summary>
        public int Port { get; private set; }

        /// <summary>The reason the port couldn't be opened, or null.</summary>
        public string LastError { get; private set; }

        public bool IsOpen => _socket != null;

        /// <summary>Listens on this port (from any computer). Closes the previous port if it was another one. Returns false if the port can't be opened.</summary>
        public bool Open(int port)
        {
            if (IsOpen && port == Port) return true;
            Close();
            LastError = null;
            try
            {
                _socket = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                Port = port;
                _running = true;
                _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "OSC receiver " + port };
                _thread.Start();
                return true;
            }
            catch (Exception e)
            {
                LastError = e.GetBaseException().Message;
                _socket?.Dispose();
                _socket = null;
                Port = 0;
                return false;
            }
        }

        /// <summary>[Main thread] Hands the messages received since the last call to the handler.</summary>
        public void Poll(Action<OscMessage> handler)
        {
            while (_received.TryDequeue(out OscMessage message)) handler(message);
        }

        public void Close()
        {
            _running = false;
            try { _socket?.Close(); } catch (Exception) { }
            _socket = null;
            _thread = null;
            Port = 0;
            while (_received.TryDequeue(out _)) { }
        }

        public void Dispose() => Close();

        private void ReceiveLoop()
        {
            IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
            UdpClient socket = _socket;
            while (_running && socket != null)
            {
                try
                {
                    byte[] packet = socket.Receive(ref from);
                    ArrayList messages = OSC.PacketToOscMessages(packet, packet.Length);
                    foreach (OscMessage message in messages) _received.Enqueue(message);
                }
                catch (SocketException) { break; } // Closed
                catch (ObjectDisposedException) { break; }
                catch (Exception e)
                {
                    if (_running) Debug.LogWarning("OSC receiver (port " + Port + "): " + e.GetBaseException().Message);
                }
            }
        }
    }
}
