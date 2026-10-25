using System.Collections;
using UnityEngine;

namespace Siege
{
    // Hooks the bow glove into the game without scene changes: starts itself when a scene with a
    // GameManager loads, finds (or creates) the GloveLink, adds the glove bow input and its status UI,
    // and drives the bow from glove and touch together (touch stays as a fallback). Leaves the bow alone
    // when the AutoPilot drives it.
    public class GloveSetup : MonoBehaviour
    {
        public bool showArcWithGlove = true;   // dotted arc while the glove drives the bow (shows short, weak shots)

        GloveLink m_Link;
        GloveBowInput m_Glove;
        bool m_UsingGlove, m_Ready;
        AutoPilot m_AutoPilot;
        CombinedBowInput m_Combined;

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

            // Glove and touch both drive the bow (CombinedBowInput); the arc helps only when the glove has the shot.
            if (!m_Combined)
            {
                m_Combined = gameObject.AddComponent<CombinedBowInput>();
                m_Combined.glove = m_Glove;
                m_Combined.touch = gm.bowInput;
                gm.bow.SetInput(m_Combined);
                Debug.Log("[Glove] bow driven by glove + touch");
            }
            m_UsingGlove = m_Combined.Owner == (IBowInput)m_Glove || (m_Glove.Connected && m_Combined.Owner == null);
            gm.bow.showArc = m_UsingGlove && showArcWithGlove;
        }
    }
}
