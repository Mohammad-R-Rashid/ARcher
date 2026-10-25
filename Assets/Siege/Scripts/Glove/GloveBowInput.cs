using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Siege
{
    // The bow glove as a bow input: fist = draw (with the glove's own power), open hand = loose.
    // Reads GloveLink messages (draw_start / draw / release / cancel / hb, each tagged with a shot number).
    // GloveUdp already drops the glove's repeated copies, so each release arrives here once.
    // Aim: Phone mode leaves aiming to the phone (crosshair + aim assist). GlovePitch mode takes elevation
    // from the glove's IMU and heading from where the phone points (no assist; the arc shows the landing).
    [DefaultExecutionOrder(-70)]   // clears last frame's release before GloveLink (-60) delivers this frame's messages
    public class GloveBowInput : MonoBehaviour, IBowInput
    {
        public enum AimMode { Phone, GlovePitch }

        public AimMode aimMode = AimMode.Phone;
        public bool invertPitch;               // if tilting the glove up aims down
        public float pitchSmoothing = 12f;     // preview only: shots use the exact release pitch
        public bool enabledForInput = true;
        [HideInInspector] public Transform cam;

        public float Draw { get; private set; }
        public bool ReleasedThisFrame { get; private set; }
        public bool Drawing { get; private set; }
        public float ReleasePower { get; private set; }   // exact power the glove reported at release
        public Vector3? AimDirection =>
            aimMode == AimMode.GlovePitch && m_HasPitch && cam ? Direction(ReleasedThisFrame ? m_ReleasePitch : m_SmoothPitch) : (Vector3?)null;

        public GloveLink Link { get; private set; }
        public bool Connected => Link && Link.Connected;
        public bool HasImu { get; private set; }
        public string GloveState { get; private set; } = "";
        public string Prompt { get; private set; } = "";
        public bool Recentering { get; private set; }

        long m_Shot = -1, m_LastEndedShot = -1;
        int m_DrawFrame = -1;
        float m_DrawStartTime, m_LastMsgTime, m_PromptUntil;
        bool m_PendingRelease, m_HasPitch;
        float m_Pitch, m_SmoothPitch, m_ReleasePitch;

        public void Bind(GloveLink link)
        {
            if (Link) Link.Message -= OnMessage;
            Link = link;
            if (Link && isActiveAndEnabled) Link.Message += OnMessage;
        }

        void OnEnable() { if (Link) { Link.Message -= OnMessage; Link.Message += OnMessage; } }
        void OnDisable() { if (Link) Link.Message -= OnMessage; }

        public void Recenter()
        {
            if (!Link) return;
            Link.SendCommand("zero");
            Recentering = true;
            ShowPrompt("Hold the bow level and still", 6f);
        }

        void Update()
        {
            ReleasedThisFrame = false;
            // A release that arrived in the same frame as its draw: the bow must see the draw for a frame first.
            if (m_PendingRelease) { m_PendingRelease = false; Loose(); }
            if (Drawing && (!enabledForInput || (!Connected && Time.unscaledTime - m_LastMsgTime > 1.5f))) EndShot();
            m_SmoothPitch = MathExt.Damp(m_SmoothPitch, m_Pitch, pitchSmoothing, Time.deltaTime);
            bool busy = GloveState == "calibrating" || GloveState == "zeroing";
            if (!busy && Time.unscaledTime > m_PromptUntil) Prompt = "";
        }

        void OnMessage(JObject m)
        {
            m_LastMsgTime = Time.unscaledTime;
            var t = (string)m["t"] ?? "";
            long shot = m["shot"] != null && m["shot"].Type == JTokenType.Integer ? (long)m["shot"] : -1;
            switch (t)
            {
                case "draw_start":
                    if (enabledForInput) StartShot(shot);
                    break;
                case "draw":
                    if (!enabledForInput) break;
                    if (!Drawing || shot != m_Shot)
                    {
                        if (shot == m_LastEndedShot) break;      // a late packet from a shot that already ended
                        StartShot(shot);                         // its draw_start was lost
                    }
                    Draw = Power(m);
                    ReadPitch(m);
                    break;
                case "release":
                    if (!Drawing || shot != m_Shot) break;
                    Draw = ReleasePower = Power(m);
                    ReadPitch(m);
                    m_ReleasePitch = m_Pitch;
                    if (Time.frameCount == m_DrawFrame) m_PendingRelease = true;
                    else Loose();
                    break;
                case "cancel":                                   // too weak a pull: no arrow
                    if (Drawing && shot == m_Shot) EndShot();
                    break;
                case "hb":
                    HasImu = (bool?)m["imu"] ?? false;
                    GloveState = (string)m["state"] ?? "";
                    // Glove open again on the shot we think is still drawn: every copy of its release was lost.
                    if (Drawing && shot == m_Shot && GloveState == "open" && Time.unscaledTime - m_DrawStartTime > 0.4f) EndShot();
                    if (!Drawing) ReadPitch(m);
                    break;
                case "log":
                    var msg = (string)m["msg"] ?? "";
                    if (msg.StartsWith("CALIBRATE") || msg.StartsWith("ZERO AIM") || msg.StartsWith("Calibration failed"))
                        ShowPrompt(msg.Replace("CALIBRATE: ", "").Replace("ZERO AIM: ", ""), 4f);
                    break;
                case "zero":
                    Recentering = false;
                    ShowPrompt((bool?)m["ok"] == false ? "Recenter failed: try again" : "Recentered", 1.5f);
                    break;
                case "calib":
                    ShowPrompt("Glove calibrated", 1.5f);
                    break;
                case "cmd_timeout":
                    if ((string)m["cmd"] == "zero") { Recentering = false; ShowPrompt("The glove didn't answer: try again", 2f); }
                    break;
            }
        }

        void StartShot(long shot)
        {
            Drawing = true;
            Draw = 0f;
            m_Shot = shot;
            m_DrawFrame = Time.frameCount;
            m_DrawStartTime = Time.unscaledTime;
            m_PendingRelease = false;
        }

        void Loose()
        {
            ReleasedThisFrame = true;
            Drawing = false;
            m_LastEndedShot = m_Shot;
        }

        void EndShot()
        {
            Drawing = false;
            Draw = 0f;
            m_PendingRelease = false;
            m_LastEndedShot = m_Shot;
        }

        float Power(JObject m) => Mathf.Clamp01((float?)m["power"] ?? Draw);

        void ReadPitch(JObject m)
        {
            var p = m["pitch"];
            if (p == null || p.Type == JTokenType.Null) return;
            float v = Mathf.Clamp((float)p * (invertPitch ? -1f : 1f), -30f, 80f);
            if (!m_HasPitch) m_SmoothPitch = v;
            m_HasPitch = true;
            m_Pitch = v;
        }

        // Heading from where the phone points, elevation from the glove.
        Vector3 Direction(float pitchDeg)
        {
            var flat = cam.forward;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) flat = cam.up;
            flat.Normalize();
            float r = pitchDeg * Mathf.Deg2Rad;
            return (flat * Mathf.Cos(r) + Vector3.up * Mathf.Sin(r)).normalized;
        }

        void ShowPrompt(string text, float seconds)
        {
            Prompt = text;
            m_PromptUntil = Time.unscaledTime + seconds;
        }
    }
}
