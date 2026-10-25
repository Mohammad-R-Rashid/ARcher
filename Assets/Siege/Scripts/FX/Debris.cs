using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // Lifetime management for physics rubble: settles, then sinks away. Globally capped.
    public class Debris : MonoBehaviour
    {
        static readonly LinkedList<Debris> s_All = new LinkedList<Debris>();
        LinkedListNode<Debris> m_Node;
        float m_Born;
        float m_Life;
        bool m_Dying;
        float m_LastSound;

        void OnEnable()
        {
            m_Born = Time.time;
            m_Life = Tune.DebrisLifetime + Random.Range(0f, 1.5f);
            m_Node = s_All.AddLast(this);
            while (s_All.Count > Tune.MaxDebris)
            {
                var oldest = s_All.First.Value;
                s_All.RemoveFirst();
                oldest.m_Node = null;
                if (oldest) Destroy(oldest.gameObject);
            }
        }

        void OnDisable()
        {
            if (m_Node != null) s_All.Remove(m_Node);
            m_Node = null;
        }

        void Update()
        {
            if (m_Dying) return;
            if (Time.time - m_Born > m_Life) StartCoroutine(Sink());
            if (transform.position.y < -2000f) Destroy(gameObject);
        }

        System.Collections.IEnumerator Sink()
        {
            m_Dying = true;
            var rb = GetComponent<Rigidbody>();
            var start = transform.localScale;
            for (float t = 0; t < 0.7f; t += Time.deltaTime)
            {
                transform.localScale = start * (1f - t / 0.7f);
                yield return null;
            }
            Destroy(gameObject);
        }

        void OnCollisionEnter(Collision c)
        {
            if (c.relativeVelocity.sqrMagnitude < 36f || Time.time - m_LastSound < 0.4f || Time.time - m_Born > 3f) return;
            m_LastSound = Time.time;
            if (Random.value < 0.25f) Sfx.Play(Sound.Thud, transform.position, 0.25f, 0.25f);
        }
    }
}
