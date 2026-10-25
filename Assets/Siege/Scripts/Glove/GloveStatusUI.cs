using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Siege
{
    // Small overlay for the bow glove, built at runtime and independent of the HUD: a status line
    // (top-right) and the glove's own prompts (calibrate).
    public class GloveStatusUI : MonoBehaviour
    {
        static readonly Color Ok = new Color(0.55f, 1f, 0.6f), Off = new Color(1f, 1f, 1f, 0.55f), Gold = new Color(1f, 0.85f, 0.45f);

        GloveBowInput m_Glove;
        Font m_Font;
        Text m_Status, m_Prompt;

        public void Bind(GloveBowInput glove)
        {
            m_Glove = glove;
            Build();
        }

        void Build()
        {
            m_Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var go = new GameObject("GloveUI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var root = (RectTransform)go.transform;

            // top-right: the castle health bar owns the top-left
            m_Status = Label(root, "Status", 28, TextAnchor.UpperRight, new Vector2(1, 1), new Vector2(-90, -40), new Vector2(900, 50));
            m_Prompt = Label(root, "Prompt", 46, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0, -170), new Vector2(1400, 70));
            m_Prompt.color = Gold;
            m_Prompt.fontStyle = FontStyle.Bold;
            // No RECENTER / AIM buttons: the phone always aims (crosshair), the glove only draws and looses.

            if (!FindAnyObjectByType<EventSystem>())
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        void Update()
        {
            if (!m_Glove) return;
            bool on = m_Glove.Connected;
            m_Status.text = on ? $"Glove: connected   {(m_Glove.HasImu ? "IMU ok" : "no IMU")}{(m_Glove.Drawing ? "   drawing" : "")}"
                               : "Glove: not connected (phone hotspot on?)";
            m_Status.color = on ? Ok : Off;
            m_Prompt.text = m_Glove.Prompt;
        }

        Text Label(RectTransform parent, string name, int size, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 box)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = anchor;
            r.anchoredPosition = pos;
            r.sizeDelta = box;
            var t = go.GetComponent<Text>();
            t.font = m_Font;
            t.fontSize = size;
            t.alignment = align;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.7f);
            return t;
        }
    }
}
