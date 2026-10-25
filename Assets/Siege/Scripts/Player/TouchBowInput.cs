using UnityEngine;

namespace Siege
{
    // Phone touch: press and hold anywhere (or drag down for a faster pull) to draw, lift to loose.
    // In the editor, the left mouse button does the same and the right button orbits the camera.
    [DefaultExecutionOrder(-50)]
    public class TouchBowInput : MonoBehaviour, IBowInput
    {
        public float fullDrawScreenFraction = 0.32f;
        public float holdToFullDraw = 0.9f;     // just holding a finger down also draws the bow
        public bool enabledForInput = true;

        public float Draw { get; private set; }
        public bool ReleasedThisFrame { get; private set; }
        public bool Drawing { get; private set; }
        public Vector3? AimDirection => null;

        Vector2 m_Start;
        float m_Held;
        int m_FingerId = -1;

        void Update()
        {
            ReleasedThisFrame = false;
            if (!enabledForInput) { Drawing = false; Draw = 0; m_FingerId = -1; return; }

            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    var t = Input.GetTouch(i);
                    if (m_FingerId < 0 && t.phase == TouchPhase.Began && !OverUI(t.fingerId))
                    {
                        m_FingerId = t.fingerId;
                        m_Start = t.position;
                        m_Held = 0f;
                        Drawing = true;
                    }
                    if (t.fingerId != m_FingerId) continue;
                    if (t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary)
                        Draw = Mathf.Max(Pull(t.position), Hold());
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                        Let();
                }
                return;
            }
            if (m_FingerId >= 0 && m_FingerId != 999) { Let(); return; }

            // Mouse (editor / simulator)
            if (Input.GetMouseButtonDown(0) && !OverUI(-1)) { m_FingerId = 999; m_Start = Input.mousePosition; m_Held = 0f; Drawing = true; }
            if (m_FingerId == 999)
            {
                if (Input.GetMouseButton(0)) Draw = Mathf.Max(Pull(Input.mousePosition), Hold());
                if (Input.GetMouseButtonUp(0)) Let();
            }
        }

        float Hold()
        {
            m_Held += Time.deltaTime;
            return Mathf.Clamp01(m_Held / holdToFullDraw);
        }

        float Pull(Vector2 pos)
        {
            var d = m_Start - pos;
            float pull = Mathf.Max(d.y, d.magnitude * 0.6f);
            return Mathf.Clamp01(pull / (Screen.height * fullDrawScreenFraction));
        }

        void Let()
        {
            ReleasedThisFrame = Drawing;
            Drawing = false;
            m_FingerId = -1;
        }

        static bool OverUI(int id)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            return es && (id >= 0 ? es.IsPointerOverGameObject(id) : es.IsPointerOverGameObject());
        }
    }
}
