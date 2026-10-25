using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // A point the bow's aim assist can lock onto (trebuchet, knight, powder kegs, soldiers, fireballs).
    // Measures its own world velocity each frame so the bow can lead moving targets.
    public class AimTarget : MonoBehaviour
    {
        public static readonly List<AimTarget> All = new List<AimTarget>();

        public Vector3 offset;      // local-space aim point
        public bool ballistic;      // falls under gravity (fireballs)

        public Vector3 Point => transform.TransformPoint(offset);
        public Vector3 Velocity { get; private set; }

        const float MaxTrackedSpeed = 120f;   // faster than this between frames is a teleport, not motion

        Rigidbody m_Rb;
        Vector3 m_Last;
        bool m_Tracking;

        void OnEnable()
        {
            All.Add(this);
            m_Rb = GetComponent<Rigidbody>();
            m_Tracking = false;
            Velocity = Vector3.zero;
        }

        void OnDisable() => All.Remove(this);

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            var p = Point;
            if (m_Rb && !m_Rb.isKinematic) Velocity = m_Rb.linearVelocity;
            else if (m_Tracking && dt > 1e-5f)
            {
                // Smoothed so walk-cycle bob doesn't jitter the lead.
                var v = (p - m_Last) / dt;
                if (v.sqrMagnitude < MaxTrackedSpeed * MaxTrackedSpeed) Velocity = MathExt.Damp(Velocity, v, 8f, dt);
            }
            m_Last = p;
            m_Tracking = true;
        }
    }
}
