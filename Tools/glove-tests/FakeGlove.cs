using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace GloveTests
{
    // Stands in for glove.ino on localhost: sends JSON datagrams to the phone's port, repeats one-off
    // messages UdpCopies times like the firmware, and answers commands with the command's id.
    sealed class FakeGlove : IDisposable
    {
        public const int UdpCopies = 3;

        readonly UdpClient m_Udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        readonly IPEndPoint m_Phone;
        readonly Thread m_Thread;
        volatile bool m_Stop;

        public ConcurrentQueue<JObject> Commands { get; } = new ConcurrentQueue<JObject>();
        public int CommandsReceived;
        public Func<JObject, bool> AnswerCommand = _ => true;   // false = pretend the command got lost
        public IPEndPoint LocalEndPoint => (IPEndPoint)m_Udp.Client.LocalEndPoint;

        public FakeGlove(int phonePort)
        {
            m_Phone = new IPEndPoint(IPAddress.Loopback, phonePort);
            m_Thread = new Thread(Listen) { IsBackground = true };
            m_Thread.Start();
        }

        public void Send(string json)
        {
            var b = Encoding.UTF8.GetBytes(json);
            m_Udp.Send(b, b.Length, m_Phone);
        }

        public void SendCritical(string json)
        {
            for (int i = 0; i < UdpCopies; i++) Send(json);
        }

        void Listen()
        {
            while (!m_Stop)
            {
                try
                {
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    var data = m_Udp.Receive(ref from);
                    var cmd = JToken.Parse(Encoding.UTF8.GetString(data)) as JObject;
                    if (cmd == null) continue;
                    Interlocked.Increment(ref CommandsReceived);
                    Commands.Enqueue(cmd);
                    if (!AnswerCommand(cmd)) continue;
                    var name = (string)cmd["cmd"];
                    var reply = name == "ping" ? "pong" : name == "cal" ? "calib" : name;
                    SendCritical($"{{\"t\":\"{reply}\",\"p\":1,\"src\":\"wifi\",\"id\":{(long)cmd["id"]},\"ok\":true}}");
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { return; }
                catch (Newtonsoft.Json.JsonException) { }
            }
        }

        public void Dispose()
        {
            m_Stop = true;
            m_Udp.Close();
            m_Thread.Join(1000);
        }
    }
}
