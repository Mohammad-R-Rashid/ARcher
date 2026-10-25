using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Siege
{
    // The phone's end of the glove link (protocol: glove/HANDOFF.md). The glove joins the phone's hotspot
    // and sends one JSON object per UDP datagram to port 9000; we answer to whatever address it sent from.
    //  - A background thread receives and parses; messages queue in Inbox for the main thread to drain.
    //  - The glove sends release/cancel and command replies 3 times: repeats are dropped here (by shot/id).
    //  - Commands get an id and are resent until the glove's reply arrives (or a cmd_timeout is queued).
    //  - The socket is recreated after errors, and can be closed/reopened around app suspension.
    // Plain C# (no Unity types) so it can be tested against a fake glove from Tools/glove-tests.
    public sealed class GloveUdp : IDisposable
    {
        public const int DefaultPort = 9000;
        const int MaxQueued = 512;
        const int ErrorBackoffMs = 500;

        // Command retry timing (ms). A recenter takes ~4 s and a flex calibration ~7 s on the glove, and it
        // ignores repeats of an id it's still running, so wait that long before resending.
        public int PingRetryMs = 1000;
        public int ZeroRetryMs = 5000;
        public int CalRetryMs = 10000;
        public int MaxAttempts = 3;

        public ConcurrentQueue<JObject> Inbox { get; } = new ConcurrentQueue<JObject>();
        public int Port { get; }
        public bool Running => m_Thread != null;
        public IPEndPoint Glove => m_Glove;            // where the glove's datagrams come from (null: not heard yet)
        public long LastPingRoundTripMs { get; private set; } = -1;

        // Milliseconds since the last datagram (-1: none yet).
        public long SilenceMs
        {
            get
            {
                long last = Interlocked.Read(ref m_LastReceiveMs);
                return last < 0 ? -1 : m_Clock.ElapsedMilliseconds - last;
            }
        }

        class Pending
        {
            public long id;
            public string cmd;
            public byte[] bytes;
            public int attempts;
            public long nextMs;
            public long firstSentMs = -1;
        }

        readonly Action<string> m_Log;
        readonly Stopwatch m_Clock = Stopwatch.StartNew();
        readonly object m_Lock = new object();
        readonly List<Pending> m_Pending = new List<Pending>();
        // Dedupe state, receive thread only: last key seen per message type.
        readonly Dictionary<string, long> m_LastKey = new Dictionary<string, long>();
        long m_LastShot = -1;
        volatile UdpClient m_Client;
        volatile IPEndPoint m_Glove;
        volatile bool m_Stopping;
        Thread m_Thread;
        long m_LastReceiveMs = -1;
        long m_NextId;
        string m_LastError;

        public GloveUdp(int port = DefaultPort, Action<string> log = null)
        {
            Port = port;
            m_Log = log ?? (_ => { });
            // Ids must not repeat across app launches: the glove answers a repeated id with its stored reply
            // instead of running the command again. A random start makes that practically impossible.
            // (Kept below 1e9: the glove reads ids as a 32-bit long.)
            m_NextId = new Random().Next(1, 900_000_000);
        }

        // Opens the socket and starts receiving (again, after Stop).
        public void Start()
        {
            if (m_Thread != null) return;
            m_Stopping = false;
            m_Thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "GloveUdp" };
            m_Thread.Start();
        }

        // Closes the socket (app going to the background: iOS may reclaim it anyway). Pending commands are kept.
        public void Stop()
        {
            if (m_Thread == null) return;
            m_Stopping = true;
            CloseClient();
            m_Thread.Join(1000);
            m_Thread = null;
        }

        public void Dispose() => Stop();

        // Use a known glove address instead of waiting to hear from it (a glove hosting its own WiFi).
        public void SetGlove(IPEndPoint glove) => m_Glove = glove;

        // Sends a message to the glove as is (no id, no retry). False if the glove hasn't been heard from yet.
        public bool Send(JObject msg) => SendBytes(Encoding.UTF8.GetBytes(msg.ToString(Formatting.None)));

        // Sends a command ("zero", "cal", "ping") with a fresh id and resends it until the glove's reply
        // ("zero" / "calib" / "pong" with that id) arrives. If the glove isn't known yet it goes out as soon
        // as it is. Returns the id; after MaxAttempts unanswered sends a {"t":"cmd_timeout"} is queued.
        public long SendCommand(string cmd)
        {
            var p = new Pending { id = Interlocked.Increment(ref m_NextId), cmd = cmd };
            var json = new JObject { ["t"] = "cmd", ["cmd"] = cmd, ["id"] = p.id };
            p.bytes = Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
            lock (m_Lock) m_Pending.Add(p);
            Tick();
            return p.id;
        }

        // Call regularly (every frame): sends and resends pending commands.
        public void Tick()
        {
            long now = m_Clock.ElapsedMilliseconds;
            List<Pending> expired = null;
            lock (m_Lock)
            {
                for (int i = m_Pending.Count - 1; i >= 0; i--)
                {
                    var p = m_Pending[i];
                    if (now < p.nextMs) continue;
                    if (p.attempts >= MaxAttempts)
                    {
                        m_Pending.RemoveAt(i);
                        (expired ??= new List<Pending>()).Add(p);
                        continue;
                    }
                    if (!SendBytes(p.bytes)) continue;   // glove not heard from yet: try again next tick
                    p.attempts++;
                    if (p.firstSentMs < 0) p.firstSentMs = now;
                    p.nextMs = now + RetryMs(p.cmd);
                }
            }
            if (expired == null) return;
            foreach (var p in expired)
            {
                m_Log($"no reply to '{p.cmd}' (id {p.id}) after {p.attempts} tries");
                Enqueue(new JObject { ["t"] = "cmd_timeout", ["cmd"] = p.cmd, ["id"] = p.id });
            }
        }

        public bool IsPending(long id)
        {
            lock (m_Lock) return m_Pending.Exists(p => p.id == id);
        }

        int RetryMs(string cmd) => cmd == "zero" ? ZeroRetryMs : cmd == "cal" ? CalRetryMs : PingRetryMs;

        bool SendBytes(byte[] bytes)
        {
            var glove = m_Glove;
            var client = m_Client;
            if (glove == null || client == null) return false;
            try
            {
                client.Send(bytes, bytes.Length, glove);
                return true;
            }
            catch (Exception e) when (e is SocketException || e is ObjectDisposedException)
            {
                return false;   // the receive loop rebuilds a broken socket
            }
        }

        // ---------------------------------------------------------------- receive thread
        void ReceiveLoop()
        {
            while (!m_Stopping)
            {
                try
                {
                    var client = m_Client ?? OpenClient();
                    var from = new IPEndPoint(IPAddress.Any, 0);
                    var data = client.Receive(ref from);
                    OnDatagram(data, from);
                }
                catch (SocketException e) when (e.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // Windows reports an earlier send to a closed port this way; the socket is fine.
                }
                catch (Exception e) when (!m_Stopping)
                {
                    var error = $"UDP {Port}: {e.GetType().Name}: {e.Message}";
                    if (error != m_LastError) m_Log(error + " (reopening)");
                    m_LastError = error;
                    CloseClient();
                    Thread.Sleep(ErrorBackoffMs);
                }
                catch
                {
                    // stopping: the socket was closed under Receive
                }
            }
        }

        UdpClient OpenClient()
        {
            var c = new UdpClient(AddressFamily.InterNetwork);
            c.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            try { c.Client.IOControl(unchecked((IOControlCode)(-1744830452)), new byte[] { 0, 0, 0, 0 }, null); }   // Windows: no SIO_UDP_CONNRESET
            catch (Exception) { }   // not Windows
            c.Client.Bind(new IPEndPoint(IPAddress.Any, Port));
            m_Client = c;
            m_LastError = null;
            m_Log($"listening for the glove on UDP {Port}");
            return c;
        }

        void CloseClient()
        {
            var c = m_Client;
            m_Client = null;
            try { c?.Close(); } catch (Exception) { }
        }

        void OnDatagram(byte[] data, IPEndPoint from)
        {
            JObject msg;
            try { msg = JToken.Parse(Encoding.UTF8.GetString(data)) as JObject; }
            catch (JsonException) { return; }
            if (msg == null) return;
            Interlocked.Exchange(ref m_LastReceiveMs, m_Clock.ElapsedMilliseconds);

            var known = m_Glove;
            bool first = known == null;
            if (first || !known.Equals(from))
            {
                m_Glove = from;
                m_Log($"glove at {from}");
            }
            if (first) SendCommand("ping");   // confirms the way back, and asks iOS for local-network access early

            var t = (string)msg["t"];
            if (t == null || IsRepeat(t, msg)) return;
            if (t == "zero" || t == "calib" || t == "pong") OnReply(t, msg);
            Enqueue(msg);
        }

        // The glove sends one-off messages 3 times. release/cancel repeat their "shot", replies their "id".
        bool IsRepeat(string t, JObject msg)
        {
            if (t == "hb")
            {
                // shot numbers restart at 1 when the glove reboots; the heartbeat carries the latest one
                long hbShot = msg.Value<long?>("shot") ?? -1;
                if (hbShot >= 0 && hbShot < m_LastShot) { m_LastKey.Clear(); m_LastShot = hbShot; }
                return false;
            }
            string field = t == "release" || t == "cancel" ? "shot" : t == "zero" || t == "calib" || t == "pong" ? "id" : null;
            if (field == null) return false;
            long? key = msg.Value<long?>(field);
            if (!key.HasValue) return false;   // older firmware / no id: nothing to dedupe on
            if (m_LastKey.TryGetValue(t, out var last) && last == key.Value) return true;
            m_LastKey[t] = key.Value;
            if (field == "shot") m_LastShot = key.Value;
            return false;
        }

        void OnReply(string t, JObject msg)
        {
            long? id = msg.Value<long?>("id");
            if (!id.HasValue) return;
            long now = m_Clock.ElapsedMilliseconds;
            lock (m_Lock)
            {
                int i = m_Pending.FindIndex(p => p.id == id.Value);
                if (i < 0) return;
                if (t == "pong" && m_Pending[i].firstSentMs >= 0) LastPingRoundTripMs = now - m_Pending[i].firstSentMs;
                m_Pending.RemoveAt(i);
            }
        }

        void Enqueue(JObject msg)
        {
            if (Inbox.Count >= MaxQueued) Inbox.TryDequeue(out _);
            Inbox.Enqueue(msg);
        }
    }
}
