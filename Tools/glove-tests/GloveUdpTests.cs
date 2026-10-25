using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Siege;

namespace GloveTests
{
    // GloveUdp (the phone's end) against a fake glove that behaves like glove.ino, over real UDP on localhost.
    [NonParallelizable]
    public class GloveUdpTests
    {
        static int s_NextPort = 19100;
        GloveUdp m_Udp;
        FakeGlove m_Glove;
        readonly List<JObject> m_Got = new List<JObject>();

        [SetUp]
        public void SetUp()
        {
            int port = Interlocked.Increment(ref s_NextPort);   // fresh port per test: no cross-talk
            m_Udp = new GloveUdp(port) { PingRetryMs = 150, ZeroRetryMs = 150, CalRetryMs = 150, MaxAttempts = 3 };
            m_Udp.Start();
            m_Glove = new FakeGlove(port);
            m_Got.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            m_Glove.Dispose();
            m_Udp.Dispose();
        }

        // ---------------------------------------------------------------- receiving
        [Test]
        public void Receives_ParsesNulls_AndLearnsGloveAddress()
        {
            m_Glove.Send("{\"t\":\"draw\",\"p\":1,\"src\":\"wifi\",\"shot\":4,\"power\":0.42,\"pitch\":null}");
            var m = Next("draw");

            Assert.That(m.Value<float?>("power"), Is.EqualTo(0.42f));
            Assert.That(m.Value<float?>("pitch"), Is.Null);
            Assert.That(m_Udp.Glove, Is.EqualTo(m_Glove.LocalEndPoint));
            Assert.That(m_Udp.SilenceMs, Is.InRange(0, 1000));
        }

        [Test]
        public void FirstContact_PingsTheGlove_AndMeasuresRoundTrip()
        {
            m_Glove.Send(Hb(0));
            Assert.That(WaitFor(() => m_Glove.Commands.Any(c => (string)c["cmd"] == "ping")), "no ping sent back");
            Next("pong");
            Assert.That(m_Udp.LastPingRoundTripMs, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ReleaseCopies_AreDeliveredOnce_NewShotPasses()
        {
            m_Glove.SendCritical(Release(1));
            m_Glove.SendCritical(Release(2));
            m_Glove.Send(Hb(2));   // marker: everything before it has been processed once this arrives
            Next("hb");

            var shots = m_Got.Where(x => (string)x["t"] == "release").Select(x => (long)x["shot"]).ToList();
            Assert.That(shots, Is.EqualTo(new long[] { 1, 2 }));
        }

        [Test]
        public void CancelCopies_AreDeliveredOnce()
        {
            m_Glove.SendCritical("{\"t\":\"cancel\",\"p\":1,\"shot\":3}");
            m_Glove.Send(Hb(3));
            Next("hb");
            Assert.That(m_Got.Count(x => (string)x["t"] == "cancel"), Is.EqualTo(1));
        }

        [Test]
        public void GloveReboot_ShotNumbersRestart_ReleaseIsNotMistakenForARepeat()
        {
            m_Glove.SendCritical(Release(1));
            m_Glove.Send(Hb(1));
            Next("hb");
            // reboot: heartbeat shot goes back to 0, then the first shot is 1 again
            m_Glove.Send(Hb(0));
            Next("hb");
            m_Glove.SendCritical(Release(1));
            m_Glove.Send(Hb(1));
            Next("hb");

            Assert.That(m_Got.Count(x => (string)x["t"] == "release"), Is.EqualTo(2));
        }

        [Test]
        public void MessagesWithoutShot_AreNotDeduped()
        {
            m_Glove.Send("{\"t\":\"release\",\"p\":1,\"power\":0.5}");
            m_Glove.Send("{\"t\":\"release\",\"p\":1,\"power\":0.6}");
            m_Glove.Send(Hb(0));
            Next("hb");
            Assert.That(m_Got.Count(x => (string)x["t"] == "release"), Is.EqualTo(2));
        }

        [Test]
        public void GarbageAndNonObjects_AreIgnored()
        {
            m_Glove.Send("not json");
            m_Glove.Send("[1,2,3]");
            m_Glove.Send("{\"p\":1}");   // no "t"
            m_Glove.Send(Hb(0));
            Next("hb");
            Assert.That(m_Got.Select(x => (string)x["t"]), Is.All.EqualTo("hb").Or.EqualTo("pong"));
        }

        // ---------------------------------------------------------------- commands
        [Test]
        public void Command_GetsAnId_AndOneReplyDespiteThreeCopies()
        {
            Connect();
            long id = m_Udp.SendCommand("zero");
            var reply = Next("zero");
            Assert.That((long)reply["id"], Is.EqualTo(id));
            Assert.That(WaitFor(() => !m_Udp.IsPending(id)), "still pending after its reply");

            m_Glove.Send(Hb(0));
            Next("hb");
            Assert.That(m_Got.Count(x => (string)x["t"] == "zero"), Is.EqualTo(1));
        }

        [Test]
        public void LostCommand_IsResentWithTheSameId_UntilAnswered()
        {
            Connect();
            int zeros = 0;
            m_Glove.AnswerCommand = c => (string)c["cmd"] != "zero" || ++zeros >= 2;   // first zero "lost"
            long id = m_Udp.SendCommand("zero");
            Next("zero", 3000);

            var sent = m_Glove.Commands.Where(c => (string)c["cmd"] == "zero").ToList();
            Assert.That(sent.Count, Is.EqualTo(2));
            Assert.That(sent.Select(c => (long)c["id"]), Is.All.EqualTo(id));
            Assert.That(WaitFor(() => !m_Udp.IsPending(id)));
        }

        [Test]
        public void UnansweredCommand_TimesOutAfterMaxAttempts()
        {
            Connect();
            m_Glove.AnswerCommand = c => (string)c["cmd"] != "zero";
            long id = m_Udp.SendCommand("zero");
            var t = Next("cmd_timeout", 3000);

            Assert.That((long)t["id"], Is.EqualTo(id));
            Assert.That((string)t["cmd"], Is.EqualTo("zero"));
            Assert.That(m_Glove.Commands.Count(c => (string)c["cmd"] == "zero"), Is.EqualTo(3));
            Assert.That(m_Udp.IsPending(id), Is.False);
        }

        [Test]
        public void CommandBeforeGloveIsKnown_GoesOutOnceItIsHeard()
        {
            long id = m_Udp.SendCommand("cal");
            Pump(200);
            Assert.That(m_Glove.Commands.Count(c => (string)c["cmd"] == "cal"), Is.EqualTo(0));

            m_Glove.Send(Hb(0));
            var reply = Next("calib");
            Assert.That((long)reply["id"], Is.EqualTo(id));
        }

        [Test]
        public void CommandIds_DoNotRestartAtOneAcrossLaunches()
        {
            using (var a = new GloveUdp(19099))
            using (var b = new GloveUdp(19098))
            {
                long ia = a.SendCommand("ping"), ib = b.SendCommand("ping");
                Assert.That(ia, Is.Not.EqualTo(ib), "two app launches would reuse ids");
                Assert.That(ia, Is.LessThan(1_000_000_000L), "glove reads ids as 32-bit");
            }
        }

        // ---------------------------------------------------------------- app suspension
        [Test]
        public void StopThenStart_ReceivesAgain()
        {
            Connect();
            m_Udp.Stop();
            Assert.That(m_Udp.Running, Is.False);
            m_Udp.Start();
            Thread.Sleep(100);   // socket rebinds on the receive thread
            m_Glove.Send(Release(9));
            var m = Next("release");
            Assert.That((long)m["shot"], Is.EqualTo(9));
        }

        [Test]
        public void CommandsSurviveSuspension()
        {
            Connect();
            m_Glove.AnswerCommand = c => (string)c["cmd"] != "zero" || m_Glove.CommandsReceived > 3;
            m_Udp.Stop();
            long id = m_Udp.SendCommand("zero");   // made while suspended
            m_Udp.Start();
            var reply = Next("zero", 3000);
            Assert.That((long)reply["id"], Is.EqualTo(id));
        }

        // ---------------------------------------------------------------- helpers
        static string Hb(long shot) => $"{{\"t\":\"hb\",\"p\":1,\"src\":\"wifi\",\"bend\":0.1,\"state\":\"open\",\"shot\":{shot},\"pitch\":2.5,\"roll\":0.1,\"imu\":true}}";
        static string Release(long shot) => $"{{\"t\":\"release\",\"p\":1,\"src\":\"wifi\",\"shot\":{shot},\"power\":0.8,\"held_ms\":960,\"pitch\":3.1,\"roll\":null}}";

        // Glove heard and first-contact ping answered.
        void Connect()
        {
            m_Glove.Send(Hb(0));
            Next("pong");
        }

        // Ticks like Update() and collects everything delivered until a message of this type arrives.
        JObject Next(string type, int timeoutMs = 2000)
        {
            var sw = Stopwatch.StartNew();
            int from = m_Got.Count;
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                m_Udp.Tick();
                while (m_Udp.Inbox.TryDequeue(out var m)) m_Got.Add(m);
                for (int i = from; i < m_Got.Count; i++)
                    if ((string)m_Got[i]["t"] == type) return m_Got[i];
                Thread.Sleep(5);
            }
            Assert.Fail($"no '{type}' within {timeoutMs} ms; got: {string.Join(", ", m_Got.Skip(from).Select(x => (string)x["t"]))}");
            return null;
        }

        void Pump(int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                m_Udp.Tick();
                while (m_Udp.Inbox.TryDequeue(out var m)) m_Got.Add(m);
                Thread.Sleep(5);
            }
        }

        bool WaitFor(Func<bool> cond, int timeoutMs = 2000)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (cond()) return true;
                Pump(5);
            }
            return cond();
        }
    }
}
