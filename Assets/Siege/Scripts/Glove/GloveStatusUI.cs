using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Siege
{
    // Small overlay for the bow glove, built at runtime and independent of the HUD: a status line
    // (top-left), the glove's prompts (calibrate / recenter) and RECENTER + AIM buttons (bottom-left).
    public class GloveStatusUI : MonoBehaviour
    {
        static readonly Color Ok = new Color(0.55f, 1f, 0.6f), Off = new Color(1f, 1f, 1f, 0.55f), Gold = new Color(1f, 0.85f, 0.45f);

        GloveBowInput m_Glove;
        Font m_Font;
        Text m_Status, m_Prompt, m_AimText;
        GameObject m_Recenter, m_Aim;

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
            m_Recenter = MakeButton(root, "RECENTER", new Vector2(0, 0), new Vector2(230, 90), () => m_Glove.Recenter(), out _);
            m_Aim = MakeButton(root, "AIM: PHONE", new Vector2(0, 0), new Vector2(230, 200), () =>
                m_Glove.aimMode = m_Glove.aimMode == GloveBowInput.AimMode.Phone ? GloveBowInput.AimMode.GlovePitch : GloveBowInput.AimMode.Phone, out m_AimText);

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
            bool aimable = on && m_Glove.HasImu;
            m_Recenter.SetActive(aimable);
            m_Aim.SetActive(aimable);
            m_AimText.text = m_Glove.aimMode == GloveBowInput.AimMode.Phone ? "AIM: PHONE" : "AIM: GLOVE";
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

        GameObject MakeButton(RectTransform parent, string text, Vector2 anchor, Vector2 pos, UnityEngine.Events.UnityAction onClick, out Text label)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = anchor;
            r.anchoredPosition = pos;
            r.sizeDelta = new Vector2(300, 90);
            go.GetComponent<Image>().color = new Color(0.45f, 0.12f, 0.06f, 0.92f);
            go.GetComponent<Button>().onClick.AddListener(onClick);
            label = Label(r, "Text", 32, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 90));
            label.text = text;
            label.color = Gold;
            label.fontStyle = FontStyle.Bold;
            return go;
        }
    }
}
