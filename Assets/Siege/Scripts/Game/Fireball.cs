using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // Flaming boulder. Real physics flight (ballistic velocity from the siege engine), flickering light
    // that lights up the castle as it passes, roaring loop, heartbeat haptics while incoming,
    // and a big area blast on impact. Near-miss shots skim past the camera instead.
    // An arrow can catch it mid-air (ShootDown): it bursts harmlessly in a shower of embers.
    [RequireComponent(typeof(Rigidbody))]
    public class Fireball : MonoBehaviour, IDamageable
    {
        public static readonly List<Fireball> Active = new List<Fireball>();

        public Light glow;
        public Transform core;

        public bool NearMiss { get; private set; }
        public Vector3 Target { get; private set; }
        public float TimeToImpact => Mathf.Max(0, m_Flight - (Time.time - m_Launched));
        public Vector3 Velocity => m_Rb ? m_Rb.linearVelocity : Vector3.zero;

        Rigidbody m_Rb;
        Castle m_Castle;
        CastlePiece m_TargetPiece;
        Transform m_Camera;
        GameObject m_Trail;
        GameObject m_Marker;
        AimTarget m_Aim;
        AudioSource m_Loop;
        float m_Launched, m_Flight, m_NextBeat;
        bool m_Flying, m_Done, m_Whooshed;

        void Awake()
        {
            m_Rb = GetComponent<Rigidbody>();
            m_Rb.isKinematic = true;
        }

        public void Launch(Vector3 velocity, float flightTime, Vector3 target, Castle castle, CastlePiece piece,
                           Transform cam, bool nearMiss, GameObject marker, Collider[] ignore)
        {
            transform.SetParent(null, true);
            NearMiss = nearMiss;
            Target = target;
            m_Castle = castle;
            m_TargetPiece = piece;
            m_Camera = cam;
            m_Marker = marker;
            m_Flight = flightTime;
            m_Launched = Time.time;
            m_NextBeat = Time.time + 0.4f;
            var col = GetComponent<Collider>();
            col.enabled = true;
            foreach (var c in ignore) if (c) Physics.IgnoreCollision(col, c);
            m_Rb.isKinematic = false;
            m_Rb.useGravity = true;
            m_Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            m_Rb.linearVelocity = velocity;
            m_Rb.angularVelocity = Random.insideUnitSphere * 4f;
            m_Flying = true;
            // The bow's aim assist can lock on and lead it (with gravity) to shoot it down.
            m_Aim = GetComponent<AimTarget>();
            if (!m_Aim) m_Aim = gameObject.AddComponent<AimTarget>();
            m_Aim.offset = Vector3.zero;
            m_Aim.ballistic = true;
            m_Aim.enabled = true;
            if (m_Trail) VFX.Release(m_Trail, 1f);
            m_Trail = VFX.FireballTrail(transform, 1f);
            if (!m_Loop) m_Loop = Sfx.Loop(Sound.FireballLoop, transform, 0.9f);
            Active.Add(this);
        }

        // Called while the fireball sits in the bucket so it glows before launch.
        public void Ignite()
        {
            if (!m_Trail) m_Trail = VFX.FireballTrail(transform, 0.5f);
        }

        void OnDestroy() => Active.Remove(this);

        // Arrows burst it. Arrow calls ShootDown itself (swept flight test, or a direct hit in Stick),
        // so this only covers other arrow-sourced hits.
        public void TakeHit(HitInfo hit)
        {
            if (hit.source == HitSource.Arrow) ShootDown(hit.point);
        }

        // Shot out of the air: a smaller mid-air burst and a shower of embers, no castle damage.
        public void ShootDown(Vector3 point)
        {
            if (!m_Flying || m_Done) return;
            m_Done = true;
            m_Flying = false;
            Active.Remove(this);
            if (m_Aim) m_Aim.enabled = false;
            var p = transform.position;
            float scale = 0.5f;
            if (m_Camera && Vector3.Distance(p, m_Camera.position) < 7f) scale *= 0.6f;
            VFX.Explosion(p, scale);
            VFX.Firework(p, new Color(1f, 0.45f, 0.12f), scale * 1.1f);
            Sfx.Play(Sound.Explosion, p, 0.8f, 0.12f);
            Sfx.Play(Sound.HitMarker, point, 0.6f, 0.05f, 0.3f);
            Haptics.HitConfirm();
            ScreenFX.Punch(0.12f);
            BowController.NotifyHit(this);
            if (m_Marker) Destroy(m_Marker);
            if (m_Trail) VFX.Release(m_Trail, 2f);
            m_Trail = null;
            Destroy(gameObject);
        }

        void Update()
        {
            if (glow) glow.intensity = (m_Flying ? 7f : 3f) * (0.8f + 0.2f * Mathf.PerlinNoise(Time.time * 12f, 0.3f));
            if (core) core.Rotate(Vector3.up, 120f * Time.deltaTime, Space.World);
            if (!m_Flying || m_Done) return;

            float age = Time.time - m_Launched;
            if (age > 10f || transform.position.y < -3000f) { Explode(transform.position, Vector3.up, null, false); return; }
            if (!m_Camera) return;

            float camDist = Vector3.Distance(transform.position, m_Camera.position);
            if (NearMiss)
            {
                if (!m_Whooshed && camDist < 28f)
                {
                    m_Whooshed = true;
                    Sfx.Play(Sound.FireballFlyby, m_Camera.position + (transform.position - m_Camera.position).normalized * 6f, 1f, 0.02f, 0.6f);
                    Haptics.NearMiss();
                    ScreenFX.Heat(1f);
                    ScreenFX.Shake(0.45f);
                    ScreenFX.Punch(0.4f);
                    ScreenFX.SlowMo(0.45f, 0.35f);
                }
            }
            else if (Time.time >= m_NextBeat)
            {
                float urgency = 1f - Mathf.Clamp01(TimeToImpact / m_Flight);
                Haptics.Heartbeat(urgency);
                m_NextBeat = Time.time + Mathf.Lerp(0.75f, 0.32f, urgency);
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (!m_Flying || m_Done) return;
            var contact = c.GetContact(0);
            var piece = c.collider.GetComponentInParent<CastlePiece>();
            var dmg = c.collider.GetComponentInParent<IDamageable>();
            Explode(contact.point, contact.normal, piece, true, piece == null ? dmg : null);
        }

        void Explode(Vector3 point, Vector3 normal, CastlePiece piece, bool damage, IDamageable other = null)
        {
            m_Done = true;
            m_Flying = false;
            Active.Remove(this);
            if (m_Aim) m_Aim.enabled = false;
            float scale = NearMiss ? 0.7f : 1f;
            // Right next to the player the full blast would just fill the screen; keep it readable.
            if (m_Camera && Vector3.Distance(point, m_Camera.position) < 7f) scale *= 0.6f;
            VFX.Explosion(point, scale);
            VFX.Scorch(point, normal, 7f * scale);
            Sfx.Play(Sound.Explosion, point, 1f, 0.08f);

            if (damage && !NearMiss && m_Castle)
                m_Castle.Blast(point, Tune.FireballRadius, 1f, 16f, piece);
            if (damage && !NearMiss && other != null)
                other.TakeHit(new HitInfo { damage = 1f, point = point, direction = m_Rb.linearVelocity.normalized, force = 10f, source = HitSource.Fireball });
            // Push loose rubble and props around
            foreach (var h in Physics.OverlapSphere(point, Tune.FireballRadius * 1.6f, 1 << Layers.Debris))
                if (h.attachedRigidbody) h.attachedRigidbody.AddExplosionForce(900f, point, Tune.FireballRadius * 1.6f, 0.6f);

            if (m_Camera)
            {
                float d = Vector3.Distance(point, m_Camera.position);
                float prox = Mathf.Clamp01(1f - d / 160f);
                Haptics.CastleImpact(Mathf.Lerp(0.35f, 1f, prox));
                ScreenFX.Shake(0.25f + prox * 0.55f);
                ScreenFX.Punch(0.2f + prox * 0.4f);
                if (!NearMiss) ScreenFX.Damage(piece ? 0.7f : 0.25f);
                if (d < 25f) ScreenFX.Heat(prox * 0.6f);
            }

            if (m_Marker) Destroy(m_Marker);
            if (m_Trail) VFX.Release(m_Trail, 3f);
            m_Trail = null;
            Destroy(gameObject);
        }
    }
}
