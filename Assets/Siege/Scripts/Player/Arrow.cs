using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // Arrow flight is integrated by hand with sphere-casts each step: reliable at high speed against
    // thin geometry, and it lets arrows stick exactly where they hit (and ride along on moving parts).
    // Damage scales with the draw; an arrow passing close to an incoming fireball bursts it mid-air.
    // Wooden targets splinter; men take a dull thwack (they grunt and react themselves).
    public class Arrow : MonoBehaviour
    {
        static readonly Queue<Arrow> s_Live = new Queue<Arrow>();
        public static bool DebugLog;

        public TrailRenderer trail;
        public float radius = 0.1f;            // sweep radius at model scale (about 8 cm on a 0.9 m arrow)
        public float tipLength = 1.05f;
        public float shootDownRadius = 1.4f;   // passing this close to a fireball's centre bursts it

        Vector3 m_Vel;
        float m_Power = 1f;
        bool m_Flying;
        float m_Born;
        float m_Quiver;
        Quaternion m_StuckRot;
        int m_Mask;
        float m_Tip;
        float m_Radius;
        Transform m_Mesh;

        public void Launch(Vector3 velocity) => Launch(velocity, 1f);

        public void Launch(Vector3 velocity, float power)
        {
            m_Vel = velocity;
            m_Power = Mathf.Clamp01(power);
            m_Flying = true;
            m_Born = Time.time;
            m_Tip = tipLength * transform.lossyScale.z;
            m_Radius = radius * transform.lossyScale.z;
            m_Mesh = transform.Find("Mesh");
            m_Mask = Layers.Mask(Layers.Castle, Layers.Enemy, Layers.Environment, Layers.Debris);
            transform.rotation = Quaternion.LookRotation(velocity);
            if (trail) { trail.Clear(); trail.emitting = true; }
            s_Live.Enqueue(this);
            while (s_Live.Count > Tune.MaxArrows)
            {
                var old = s_Live.Dequeue();
                if (old) Destroy(old.gameObject);
            }
        }

        void Update()
        {
            if (Time.time - m_Born > Tune.ArrowLifetime) { Destroy(gameObject); return; }
            if (m_Flying) Fly(Time.deltaTime);
            else if (m_Quiver > 0.01f)
            {
                m_Quiver = MathExt.Damp(m_Quiver, 0f, 5f, Time.deltaTime);
                float a = Mathf.Sin(Time.time * 70f) * m_Quiver * 3f;
                transform.localRotation = m_StuckRot * Quaternion.Euler(a, a * 0.6f, 0);
            }
        }

        void Fly(float dt)
        {
            if (dt <= 0f) return;
            var pos = transform.position;
            var vel = m_Vel + Physics.gravity * dt;
            var step = (m_Vel + vel) * 0.5f * dt;
            var tip = pos + transform.forward * m_Tip;
            float len = Mathf.Max(step.magnitude, 1e-5f);
            var dir = step / len;
            var from = tip - dir * 0.05f;
            bool blocked = Physics.SphereCast(from, m_Radius, dir, out var hit, len + 0.05f, m_Mask, QueryTriggerInteraction.Ignore);
            // Hit zones need the exact spot: the sweep can graze a shoulder before the shaft reaches the head.
            if (blocked && hit.collider.gameObject.layer == Layers.Enemy
                && Physics.Raycast(from, dir, out var exact, hit.distance + m_Radius * 2f + 0.05f, m_Mask, QueryTriggerInteraction.Ignore)
                && exact.collider.gameObject.layer == Layers.Enemy)
                hit = exact;
            // A fireball in the way before the surface is hit takes the arrow instead.
            if (TryShootDown(tip, step, dt, blocked ? Mathf.Clamp01((hit.distance - 0.05f) / len) : 1f)) return;
            if (blocked)
            {
                Stick(hit);
                return;
            }
            m_Vel = vel;
            transform.position = pos + step;
            if (m_Vel.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(m_Vel);
            // Fletching spins the arrow in flight.
            if (m_Mesh) m_Mesh.localRotation = Quaternion.Euler(0, 0, (Time.time - m_Born) * 900f);
            if (transform.position.y < -3000f) Destroy(gameObject);
        }

        // Tests this step's tip segment against incoming fireballs, swept in the fireball's moving frame so
        // fast head-on passes can't skip through. reach (0-1) limits the segment to before any surface hit.
        bool TryShootDown(Vector3 tip, Vector3 step, float dt, float reach)
        {
            var list = Fireball.Active;
            float r2 = shootDownRadius * shootDownRadius;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var f = list[i];
                if (!f) continue;
                var a = tip - f.transform.position;
                var ab = step - f.Velocity * dt;
                float s = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp(-Vector3.Dot(a, ab) / ab.sqrMagnitude, 0f, reach) : 0f;
                if ((a + ab * s).sqrMagnitude > r2) continue;
                var point = tip + step * s;
                if (DebugLog) Debug.Log($"[ARROW] shot down fireball at {point} flight={Time.time - m_Born:F2}s");
                f.ShootDown(point);
                Destroy(gameObject);
                return true;
            }
            return false;
        }

        void Stick(RaycastHit hit)
        {
            var dmg = hit.collider.GetComponentInParent<IDamageable>();
            // A fireball caught by the sweep itself bursts (ShootDown notifies the bow) and takes the arrow with it.
            if (dmg is Fireball fireball) { fireball.ShootDown(hit.point); Destroy(gameObject); return; }
            m_Flying = false;
            var dir = m_Vel.normalized;
            float speed = m_Vel.magnitude;
            // Bury the head a little way into the surface.
            transform.position = hit.point - dir * (m_Tip * 0.86f);
            transform.rotation = Quaternion.LookRotation(dir);
            if (trail) trail.emitting = false;

            int layer = hit.collider.gameObject.layer;
            if (DebugLog) Debug.Log($"[ARROW] hit {hit.collider.name} layer={layer} at {hit.point} power={m_Power:F2} flight={Time.time - m_Born:F2}s");
            // Hit zones (knight head etc.) are resolved by the target from `part`.
            var info = new HitInfo
            {
                damage = Mathf.Lerp(Tune.ArrowDamageMin, Tune.ArrowDamageMax, m_Power),
                point = hit.point,
                direction = dir,
                force = speed * 0.1f,
                source = HitSource.Arrow,
                part = hit.collider.transform,
                power = m_Power,
            };

            if (layer == Layers.Enemy || (dmg != null && !(dmg is CastlePiece)))
            {
                if (dmg is Knight || dmg is Soldier)
                {
                    // Padding and mail: a dull thwack, no splinters.
                    var src = Sfx.Play(Sound.Thud, hit.point, 0.45f, 0.1f);
                    if (src) src.pitch *= 1.7f;
                }
                else
                {
                    Sfx.Play(Sound.ArrowHitWood, hit.point, 1f, 0.12f);
                    Haptics.ArrowHitWood();
                    VFX.Splinters(hit.point, hit.normal, 0.9f);
                }
                if (dmg != null)
                {
                    Sfx.Play(Sound.HitMarker, hit.point, 0.5f, 0.05f, 0.3f);
                    if (!(dmg is Soldier)) Haptics.HitConfirm();   // soldiers confirm their own kills
                    dmg.TakeHit(info);
                    BowController.NotifyHit(dmg);
                }
            }
            else if (layer == Layers.Castle)
            {
                Sfx.Play(Sound.ArrowHitStone, hit.point, 0.7f, 0.15f);
                Haptics.ArrowHitStone();
                VFX.StoneChips(hit.point, hit.normal, 0.5f);
            }
            else
            {
                Sfx.Play(Sound.ArrowHitWood, hit.point, 0.5f, 0.2f);
                VFX.Dust(hit.point, 0.25f);
                if (hit.rigidbody) hit.rigidbody.AddForceAtPosition(dir * speed * 0.5f, hit.point, ForceMode.Impulse);
            }

            // Ride along with whatever we hit (trebuchet arm, soldiers, falling debris...).
            if (hit.collider && hit.collider.gameObject.activeInHierarchy)
                transform.SetParent(hit.collider.transform, true);
            m_StuckRot = transform.localRotation;
            m_Quiver = 1f;
        }
    }
}
