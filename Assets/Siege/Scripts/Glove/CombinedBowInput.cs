using UnityEngine;

namespace Siege
{
    // Glove and touch together: whichever starts a draw owns that shot until it is loosed or dropped, so the
    // screen always works as a fallback even while the glove is connected.
    [DefaultExecutionOrder(-40)]   // after both sources (touch -50, glove -70) have updated this frame
    public class CombinedBowInput : MonoBehaviour, IBowInput
    {
        public GloveBowInput glove;
        public TouchBowInput touch;

        IBowInput m_Owner;

        public IBowInput Owner => m_Owner;
        public float Draw => m_Owner?.Draw ?? 0f;
        public bool Drawing => m_Owner != null && m_Owner.Drawing;
        public bool ReleasedThisFrame => m_Owner != null && m_Owner.ReleasedThisFrame;
        public Vector3? AimDirection => m_Owner?.AimDirection;
        // Exact release power when the glove fired the shot (its smoothed draw lags behind).
        public float ReleasePower => m_Owner == (IBowInput)glove && glove ? glove.ReleasePower : Draw;

        void Update()
        {
            // Keep the owner through its release frame; pick a new one only once it's idle.
            if (m_Owner != null && (m_Owner.Drawing || m_Owner.ReleasedThisFrame)) return;
            m_Owner = null;
            if (glove && glove.Connected && glove.Drawing) m_Owner = glove;
            else if (touch && touch.Drawing) m_Owner = touch;
        }
    }
}
