using UnityEngine;

namespace Siege
{
    // Procedurally animated siege engineer (rigid parts, no skinning): idles, walks, cranks the winch,
    // yanks the release, flinches at near arrows, cheers on hits and ragdolls when the engine blows.
    // Arrow hits are passed on to the engine he crews: double damage on the body, x2.5 on a head shot.
    public class Knight : MonoBehaviour, IDamageable
    {
        public enum State { Idle, Walk, Crank, Pull, Cheer, Flinch, Dead }

        const float HeadMultiplier = 2.5f;

        public Transform hips, torso, head, armL, armR, legL, legR, cape;
        [HideInInspector] public Trebuchet owner;
        [HideInInspector] public float crankPhase;
        public Transform lookTarget;

        State m_State = State.Idle, m_Resume = State.Idle;
        float m_StateTime;
        Quaternion m_HipsR, m_TorsoR, m_HeadR, m_ArmLR, m_ArmRR, m_LegLR, m_LegRR, m_CapeR;
        Vector3 m_HipsP;
        float m_HeadYaw;

        void Awake()
        {
            m_HipsR = hips.localRotation; m_HipsP = hips.localPosition;
            m_TorsoR = torso.localRotation; m_HeadR = head.localRotation;
            m_ArmLR = armL.localRotation; m_ArmRR = armR.localRotation;
            m_LegLR = legL.localRotation; m_LegRR = legR.localRotation;
            if (cape) m_CapeR = cape.localRotation;
        }

        public void SetState(State s)
        {
            if (m_State == State.Dead) return;
            if (m_State == State.Flinch && s != State.Dead) { m_Resume = s; return; }
            m_State = s;
            m_StateTime = 0;
        }

        public void Cheer()
        {
            if (m_State == State.Idle) SetState(State.Cheer);
        }

        public void Flinch()
        {
            if (m_State == State.Dead || m_State == State.Flinch) return;
            m_Resume = m_State == State.Cheer ? State.Idle : m_State;
            m_State = State.Flinch;
            m_StateTime = 0;
        }

        public void TakeHit(HitInfo hit)
        {
            if (m_State == State.Dead) return;
            bool headShot = hit.part && head && hit.part.IsChildOf(head);
            Sfx.Play(Sound.KnightGrunt, head.position, headShot ? 1f : 0.9f, 0.12f);
            Flinch();
            var h = hit;
            h.damage = hit.damage * (headShot ? HeadMultiplier : Tune.KnightHitDamage / Tune.ArrowDamage);
            if (owner) owner.ApplyDamage(h, true);
        }

        void Update()
        {
            if (m_State == State.Dead) return;
            m_StateTime += Time.deltaTime;
            float t = Time.time;
            float breathe = Mathf.Sin(t * 2.2f);

            Quaternion hipsR = m_HipsR, torsoR = m_TorsoR, armLR = m_ArmLR, armRR = m_ArmRR, legLR = m_LegLR, legRR = m_LegRR;
            Vector3 hipsP = m_HipsP + Vector3.up * breathe * 0.015f;
            float capeSwing = Mathf.Sin(t * 1.7f) * 4f;
            bool look = true;

            switch (m_State)
            {
                case State.Idle:
                    torsoR *= Quaternion.Euler(breathe * 1.5f, 0, 0);
                    armLR *= Quaternion.Euler(breathe * 3f, 0, -6f);
                    armRR *= Quaternion.Euler(-breathe * 3f, 0, 6f);
                    break;
                case State.Walk:
                {
                    float w = Mathf.Sin(t * 9f);
                    legLR *= Quaternion.Euler(w * 32f, 0, 0);
                    legRR *= Quaternion.Euler(-w * 32f, 0, 0);
                    armLR *= Quaternion.Euler(-w * 26f, 0, -6f);
                    armRR *= Quaternion.Euler(w * 26f, 0, 6f);
                    hipsP += Vector3.up * Mathf.Abs(Mathf.Cos(t * 9f)) * 0.08f;
                    torsoR *= Quaternion.Euler(6f, w * 4f, 0);
                    capeSwing = w * 10f + 10f;
                    look = false;
                    break;
                }
                case State.Crank:
                {
                    float ph = crankPhase * Mathf.PI * 2f;
                    torsoR *= Quaternion.Euler(14f + Mathf.Sin(ph) * 6f, 0, 0);
                    armRR *= Quaternion.Euler(-70f + Mathf.Sin(ph) * 32f, 0, 8f + Mathf.Cos(ph) * 10f);
                    armLR *= Quaternion.Euler(-70f + Mathf.Sin(ph + Mathf.PI) * 32f, 0, -8f - Mathf.Cos(ph) * 10f);
                    legLR *= Quaternion.Euler(-12f, 0, 0);
                    legRR *= Quaternion.Euler(16f, 0, 0);
                    hipsP += Vector3.down * 0.08f + Vector3.up * Mathf.Abs(Mathf.Sin(ph)) * 0.04f;
                    look = false;
                    break;
                }
                case State.Pull:
                {
                    float k = Mathf.Clamp01(m_StateTime / 0.35f);
                    float yank = k < 0.7f ? Mathf.SmoothStep(0, -150f, k / 0.7f) : Mathf.Lerp(-150f, -40f, (k - 0.7f) / 0.3f);
                    armRR *= Quaternion.Euler(yank, 0, 10f);
                    torsoR *= Quaternion.Euler(k < 0.7f ? -4f : -16f, 0, 0);
                    armLR *= Quaternion.Euler(-20f, 0, -14f);
                    break;
                }
                case State.Cheer:
                {
                    float bounce = Mathf.Abs(Mathf.Sin(m_StateTime * 10f));
                    armLR *= Quaternion.Euler(-165f + bounce * 20f, 0, -14f);
                    armRR *= Quaternion.Euler(-165f + bounce * 20f, 0, 14f);
                    hipsP += Vector3.up * bounce * 0.22f;
                    torsoR *= Quaternion.Euler(-8f, 0, 0);
                    if (m_StateTime > 1.4f) SetState(State.Idle);
                    break;
                }
                case State.Flinch:
                {
                    float k = Mathf.Sin(Mathf.Clamp01(m_StateTime / 0.7f) * Mathf.PI);
                    torsoR *= Quaternion.Euler(28f * k, 0, 0);
                    armLR *= Quaternion.Euler(-140f * k, 0, -30f * k);
                    armRR *= Quaternion.Euler(-140f * k, 0, 30f * k);
                    hipsP += Vector3.down * 0.25f * k;
                    legLR *= Quaternion.Euler(-30f * k, 0, 0);
                    legRR *= Quaternion.Euler(-30f * k, 0, 0);
                    capeSwing = 30f * k;
                    if (m_StateTime > 0.7f) { m_State = m_Resume; m_StateTime = 0; }
                    break;
                }
            }

            float sp = 18f * Time.deltaTime;
            hips.localPosition = Vector3.Lerp(hips.localPosition, hipsP, sp);
            hips.localRotation = Quaternion.Slerp(hips.localRotation, hipsR, sp);
            torso.localRotation = Quaternion.Slerp(torso.localRotation, torsoR, sp);
            armL.localRotation = Quaternion.Slerp(armL.localRotation, armLR, sp);
            armR.localRotation = Quaternion.Slerp(armR.localRotation, armRR, sp);
            legL.localRotation = Quaternion.Slerp(legL.localRotation, legLR, sp);
            legR.localRotation = Quaternion.Slerp(legR.localRotation, legRR, sp);
            if (cape) cape.localRotation = Quaternion.Slerp(cape.localRotation, m_CapeR * Quaternion.Euler(-Mathf.Abs(capeSwing) - 3f, 0, Mathf.Sin(t * 1.3f) * 3f), sp * 0.5f);

            float yaw = 0;
            if (look && lookTarget)
            {
                var dir = head.parent.InverseTransformDirection(lookTarget.position - head.position);
                yaw = Mathf.Clamp(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, -65f, 65f);
            }
            m_HeadYaw = MathExt.Damp(m_HeadYaw, yaw, 4f, Time.deltaTime);
            head.localRotation = m_HeadR * Quaternion.Euler(m_State == State.Crank ? 12f : 0f, m_HeadYaw, 0);
        }

        public void Ragdoll(Vector3 blast, float force)
        {
            if (m_State == State.Dead) return;
            m_State = State.Dead;
            foreach (var a in GetComponentsInChildren<AimTarget>()) a.enabled = false;
            Sfx.Play(Sound.KnightGrunt, head.position, 1f, 0.05f);
            foreach (var part in new[] { head, armL, armR, legL, legR, cape, torso, hips })
            {
                if (!part) continue;
                part.SetParent(null, true);
                part.gameObject.layer = Layers.Debris;
                foreach (var c in part.GetComponents<Collider>()) Destroy(c);
                var mf = part.GetComponent<MeshFilter>();
                var box = part.gameObject.AddComponent<BoxCollider>();
                if (mf && mf.sharedMesh) { box.center = mf.sharedMesh.bounds.center; box.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.1f); }
                var rb = part.gameObject.AddComponent<Rigidbody>();
                rb.mass = 1.5f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var dir = (part.position - blast).normalized + Vector3.up * 1.5f + Random.insideUnitSphere * 0.3f;
                rb.linearVelocity = dir.normalized * force * Random.Range(0.7f, 1.1f);
                rb.angularVelocity = Random.insideUnitSphere * 10f;
            }
        }
    }
}
