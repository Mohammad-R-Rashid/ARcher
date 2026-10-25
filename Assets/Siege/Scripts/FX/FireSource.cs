using UnityEngine;

namespace Siege
{
    // A permanent fire placed in a prefab (siege campfire): spawns the pooled fire effect and crackle.
    public class FireSource : MonoBehaviour
    {
        public float scale = 1f;
        GameObject m_Fx;

        void Start()
        {
            m_Fx = VFX.Fire(transform, transform.position, scale);
            Sfx.Loop(Sound.FireCrackle, transform, 0.3f);
        }

        void OnDestroy() { if (m_Fx) VFX.Release(m_Fx, 0.5f); }
    }
}
