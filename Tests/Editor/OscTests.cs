using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace AsioAudioUnity.Tests
{
    /// <summary>Tests of the OSC sender and receiver used by the REAPER and Spat controllers (on the loopback, no other application needed).</summary>
    public class OscTests
    {
        [Test]
        public void Receiver_GetsTheMessagesOfTheSender()
        {
            const int port = 39123;
            using (OscUdpReceiver receiver = new OscUdpReceiver())
            using (OscUdpSender sender = new OscUdpSender())
            {
                Assert.IsTrue(receiver.Open(port), receiver.LastError);
                sender.SetDestination("127.0.0.1", port);
                Assert.IsTrue(sender.Send("/play", 1));
                Assert.IsTrue(sender.Send("/source/3/xyz", 1.5f, 2f, -3f));

                OscMessage play = null, position = null;
                Stopwatch timeout = Stopwatch.StartNew();
                while ((play == null || position == null) && timeout.ElapsedMilliseconds < 2000)
                {
                    receiver.Poll(message =>
                    {
                        if (message.address == "/play") play = message;
                        if (message.address == "/source/3/xyz") position = message;
                    });
                    Thread.Sleep(10);
                }

                Assert.IsNotNull(play, "/play was received.");
                Assert.AreEqual(1, play.GetInt(0));
                Assert.IsNotNull(position, "/source/3/xyz was received.");
                Assert.AreEqual(3, position.values.Count);
                Assert.AreEqual(-3f, position.GetFloat(2));
            }
        }

        [Test]
        public void Receiver_ReportsAPortThatIsAlreadyUsed()
        {
            const int port = 39124;
            using (OscUdpReceiver first = new OscUdpReceiver())
            using (OscUdpReceiver second = new OscUdpReceiver())
            {
                Assert.IsTrue(first.Open(port));
                Assert.IsFalse(second.Open(port));
                Assert.IsNotNull(second.LastError);
                Assert.IsFalse(second.IsOpen);
            }
        }
    }
}
