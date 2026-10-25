using System.Collections;
using UnityEngine;

namespace Siege
{
    // Hooks the bow glove into the game without scene changes: starts itself when a scene with a
    // GameManager loads, finds (or creates) the GloveLink, adds the glove bow input and its status UI,
    // and hands the bow to the glove while one is connected (touch otherwise). Never switches mid-draw,
    // and leaves the bow alone when the AutoPilot drives it.
    public class GloveSetup : MonoBehaviour
    {
        public bool showArcWithGlove = true;   // dotted arc while the glove drives the bow (shows short, weak shots)

        GloveLink m_Link;
        GloveBowInput m_Glove;
        bool m_UsingGlove, m_Ready;
        AutoPilot m_AutoPilot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!FindAnyObjectByType<GameManager>() || FindAnyObjectByType<GloveSetup>()) return;
            new GameObject("GloveSetup").AddComponent<GloveSetup>();
        }

        IEnumerator Start()
        {
            GameManager gm;
            while (!(gm = GameManager.Instance) || !gm.bow) yield return null;
            m_Link = FindAnyObjectByType<GloveLink>();
            if (!m_Link) m_Link = gameObject.AddComponent<GloveLink>();
            m_Glove = gameObject.AddComponent<GloveBowInput>();
            m_Glove.Bind(m_Link);
            gameObject.AddComponent<GloveStatusUI>().Bind(m_Glove);
            m_AutoPilot = FindAnyObjectByType<AutoPilot>();
            m_Ready = true;
        }

        void Update()
        {
            if (!m_Ready) return;
            var gm = GameManager.Instance;
            if (!gm || !gm.bow) return;
            m_Glove.cam = gm.bow.cam;
            m_Glove.enabledForInput = gm.State == GameManager.Phase.Battle || gm.State == GameManager.Phase.Intro;
            if (m_AutoPilot && m_AutoPilot.isActiveAndEnabled) return;

            bool touchDrawing = gm.bowInput && gm.bowInput.Drawing;
            if (m_Glove.Connected && !m_UsingGlove && !touchDrawing)
            {
                gm.bow.SetInput(m_Glove);
                m_UsingGlove = true;
                Debug.Log("[Glove] glove now drives the bow");
            }
            else if (!m_Glove.Connected && m_UsingGlove && !m_Glove.Drawing)
            {
                gm.bow.SetInput(gm.bowInput);
                m_UsingGlove = false;
                Debug.Log("[Glove] glove lost: back to touch");
            }
            gm.bow.showArc = m_UsingGlove && showArcWithGlove;
        }
    }
}
