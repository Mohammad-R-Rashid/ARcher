using System;
using System.Net;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Siege
{
    // Unity side of the glove link: owns a GloveUdp, hands its messages to the game on the main thread
    // (Message event, early in the frame so inputs see them the same frame), and closes the socket while
    // the app is suspended (iOS can reclaim it; it's recreated on resume).
    // Standalone: add it to any GameObject. Consumers subscribe to Message and call SendCommand.
    [DefaultExecutionOrder(-60)]
    public class GloveLink : MonoBehaviour
    {
        public int port = GloveUdp.DefaultPort;
        [Tooltip("Leave empty: the glove is found from its first datagram (phone hotspot). Set it (e.g. 192.168.4.1) only when the glove hosts its own WiFi.")]
        public string gloveAddress = "";
        public float connectedSeconds = 1f;     // the glove heartbeats 4x/s; this much silence = not connected
        public bool logMessages;

        public event Action<JObject> Message;

        public bool Connected => m_Udp != null && m_Udp.SilenceMs >= 0 && m_Udp.SilenceMs < connectedSeconds * 1000f;
        public IPEndPoint Glove => m_Udp?.Glove;
        public long PingMs => m_Udp?.LastPingRoundTripMs ?? -1;

        GloveUdp m_Udp;

        void Awake()
        {
            m_Udp = new GloveUdp(port, msg => Debug.Log("[Glove] " + msg));
            if (!string.IsNullOrWhiteSpace(gloveAddress))
            {
                if (IPAddress.TryParse(gloveAddress.Trim(), out var ip)) m_Udp.SetGlove(new IPEndPoint(ip, port));
                else Debug.LogWarning($"[Glove] gloveAddress '{gloveAddress}' is not an IP address; ignoring it");
            }
        }

        void OnEnable() => m_Udp?.Start();
        void OnDisable() => m_Udp?.Stop();
        void OnDestroy() { m_Udp?.Dispose(); m_Udp = null; }

        void OnApplicationPause(bool paused)
        {
            if (m_Udp == null || !isActiveAndEnabled) return;
            if (paused) m_Udp.Stop();
            else m_Udp.Start();
        }

        // Resent until the glove answers; returns the command id (its reply carries it).
        public long SendCommand(string cmd) => m_Udp?.SendCommand(cmd) ?? -1;

        public bool Send(JObject msg) => m_Udp != null && m_Udp.Send(msg);

        void Update()
        {
            if (m_Udp == null) return;
            m_Udp.Tick();
            while (m_Udp.Inbox.TryDequeue(out var msg))
            {
                if (logMessages) Debug.Log("[Glove] " + msg.ToString(Newtonsoft.Json.Formatting.None));
                try { Message?.Invoke(msg); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
