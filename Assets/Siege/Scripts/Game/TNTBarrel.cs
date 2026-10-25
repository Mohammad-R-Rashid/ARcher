using UnityEngine;

namespace Siege
{
    // Powder kegs stacked beside the trebuchet. A lucky arrow sets one off for a huge blast that
    // badly damages the engine (and the final break-up sets off whatever is left).
    public class TNTBarrel : MonoBehaviour, IDamageable
    {
        [HideInInspector] public Trebuchet owner;
        bool m_Gone;

        public void TakeHit(HitInfo hit)
        {
            if (m_Gone || hit.source != HitSource.Arrow) return;
            Detonate(true);
        }

        public void Detonate(bool damageOwner)
        {
            if (m_Gone) return;
            m_Gone = true;
            var p = transform.position + Vector3.up * 0.5f * transform.lossyScale.y;
            float s = Mathf.Max(1f, transform.lossyScale.x);
            VFX.Explosion(p, 1.3f * s);
            if (damageOwner)
            {
                Sfx.Play(Sound.ExplosionTnt, p, 1f, 0.03f);
                Haptics.TNT();
                ScreenFX.Shake(0.6f);
                ScreenFX.Punch(0.6f);
                ScreenFX.SlowMo(0.35f, 0.45f);
                if (owner)
                {
                    owner.knight?.Flinch();
                    owner.ApplyDamage(new HitInfo { damage = Tune.TNTDamage, point = p, direction = (owner.AimCenter - p).normalized, force = 20f, source = HitSource.Explosion }, false);
                }
            }
            foreach (var h in Physics.OverlapSphere(p, 8f * s, Layers.Mask(Layers.Debris)))
                if (h.attachedRigidbody) h.attachedRigidbody.AddExplosionForce(1200f, p, 8f * s, 0.8f);
            var mf = GetComponent<MeshFilter>();
            var mr = GetComponent<MeshRenderer>();
            if (mf && mr) MeshFracture.Shatter(transform, mf.sharedMesh, mr.sharedMaterial, 6, p - Vector3.up, 18f);
            Destroy(gameObject);
        }
    }
}
