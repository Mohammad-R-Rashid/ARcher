using UnityEngine;

namespace Siege
{
    // Soft shadow on the ground where a fireball is going to land.
    public class LandingMarker : MonoBehaviour
    {
        float m_Born, m_Flight;
        Vector3 m_Scale;
        Renderer m_R;
        MaterialPropertyBlock m_Mpb;

        public void Init(float flightTime)
        {
            m_Born = Time.time;
            m_Flight = flightTime;
            m_Scale = transform.localScale;
            m_R = GetComponentInChildren<Renderer>();
            m_Mpb = new MaterialPropertyBlock();
        }

        void Update()
        {
            if (m_Flight <= 0) return;
            float k = Mathf.Clamp01((Time.time - m_Born) / m_Flight);
            // The incoming boulder's shadow: wide and faint while it's high, tight and dark just before impact.
            transform.localScale = m_Scale * Mathf.Lerp(2.2f, 0.8f, k * k);
            if (m_R)
            {
                m_Mpb.SetColor("_Tint", new Color(0f, 0f, 0f, Mathf.Lerp(0.05f, 0.6f, k * k)));
                m_R.SetPropertyBlock(m_Mpb);
            }
            if (k >= 1f && Time.time - m_Born > m_Flight + 1.5f) Destroy(gameObject);
        }
    }
}
