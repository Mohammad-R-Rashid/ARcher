using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Siege
{
    // Life-size enemy footman built from rigid parts (no skinning), animated procedurally like the Knight.
    // The stride is driven by the distance actually covered, so feet never skate, and every man gets his own
    // height, pace and rhythm. Marches, carries ladders and the ram, hauls, climbs, hacks at the gate and
    // cheers. Any hit kills: the body topples away from the blow with limp limbs, then sinks into the ground.
    public class Soldier : MonoBehaviour, IDamageable
    {
        public enum State { Idle, March, Carry, Haul, Climb, Attack, Ram, Cheer, Dead }

        public Transform hips, torso, head, armL, armR, legL, legR, weapon, shield;
        public float health = 1f;

        // Driven by squads (WaveManager, SiegeLadder, BatteringRam).
        [HideInInspector] public float squadPhase;        // ram swing cycle 0-1, the head meets the gate at 0.66
        [HideInInspector] public float squadEffort;       // 0 = just carrying the ram, 1 = swinging it
        [HideInInspector] public float squadSide;         // -1 = the load is on this man's right, +1 = on his left
        [HideInInspector] public float carryHeight = 1f;  // ladder grip: 1 = overhead, 0 = low (bracing the foot)
        [HideInInspector] public float bodyPitch;         // whole-body forward lean (climbing)
        [HideInInspector] public bool followGround = true;
        [HideInInspector] public Transform lookTarget;

        public event Action<Soldier> Died;
        public event Action<Soldier> Struck;              // the weapon connects (Attack)

        public State CurrentState => m_State;
        public bool Alive => m_State != State.Dead && !m_Gone;
        public bool Arrived { get; private set; } = true;
        public bool Moving => m_Moving;
        public bool Clambering { get; private set; }
        public bool Spearman => m_Spear;
        public static IReadOnlyList<Soldier> Living => s_Living;

        const int GroundMask = 1 << Layers.Environment;
        static readonly List<Soldier> s_Living = new List<Soldier>();

        State m_State = State.Idle;
        float m_StateTime, m_CheerUntil = -1f;
        bool m_Gone, m_Rigged, m_Spear;
        AimTarget m_Aim;

        Quaternion m_HipsR, m_TorsoR, m_HeadR, m_ArmLR, m_ArmRR, m_LegLR, m_LegRR, m_WeaponR, m_ShieldR;
        Vector3 m_HipsP, m_BaseScale = Vector3.one;
        float m_LegLen = 0.9f;

        // per-man variation
        float m_Pace, m_Stride, m_Swing, m_Lean, m_Rate, m_Offset, m_AttackEvery, m_AttackPhase;

        // locomotion
        Vector3 m_Target, m_Vel, m_FaceLocal = Vector3.forward, m_LastPos;
        float m_Speed, m_Best, m_ProgressAt, m_FallVel;
        bool m_Moving, m_Steer;   // steer: we own the facing (else whoever placed us, e.g. the trebuchet, does)

        // animation
        float m_Cycle, m_Climbed, m_GroundSpeed, m_HeadYaw, m_HeadPitch, m_Pop = 1f, m_PopTime = 0.3f;
        int m_StrikeIndex = -1;

        float Scale
        {
            get
            {
                float ly = transform.localScale.y;
                float ratio = Mathf.Abs(ly) > 1e-4f ? m_BaseScale.y / ly : 1f;
                return Mathf.Max(0.05f, transform.lossyScale.y * ratio);
            }
        }

        Vector3 FaceWorld => transform.parent ? transform.parent.TransformDirection(m_FaceLocal) : m_FaceLocal;

        void Awake()
        {
            m_Rigged = hips && torso && head && armL && armR && legL && legR;
            if (m_Rigged)
            {
                Attach(torso, hips); Attach(legL, hips); Attach(legR, hips);
                Attach(head, torso); Attach(armL, torso); Attach(armR, torso);
                Attach(weapon, armR); Attach(shield, armL);
                m_HipsR = hips.localRotation; m_HipsP = hips.localPosition;
                m_TorsoR = torso.localRotation; m_HeadR = head.localRotation;
                m_ArmLR = armL.localRotation; m_ArmRR = armR.localRotation;
                m_LegLR = legL.localRotation; m_LegRR = legR.localRotation;
                if (weapon) m_WeaponR = weapon.localRotation;
                if (shield) m_ShieldR = shield.localRotation;
                var space = hips.parent ? hips.parent : transform;
                float hipY = space.InverseTransformPoint(legL.position).y - space.InverseTransformPoint(transform.position).y;
                if (hipY > 0.3f && hipY < 3f) m_LegLen = hipY;
            }
            m_Spear = weapon && (weapon.name.IndexOf("Spear", StringComparison.OrdinalIgnoreCase) >= 0 || TallMesh(weapon));

            m_Pace = Random.Range(0.9f, 1.1f);
            m_Stride = Random.Range(0.92f, 1.08f);
            m_Swing = Random.Range(0.85f, 1.15f);
            m_Lean = Random.Range(-2f, 4f);
            m_Rate = Random.Range(0.9f, 1.12f);
            m_Offset = Random.Range(0f, 100f);
            m_Cycle = Random.value;
            m_AttackEvery = Random.Range(1.05f, 1.35f);
            transform.localScale *= Random.Range(0.96f, 1.04f);
            m_BaseScale = transform.localScale;

            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layers.Enemy;
            if (!GetComponentInChildren<Collider>(true))
            {
                var cap = gameObject.AddComponent<CapsuleCollider>();
                cap.center = new Vector3(0f, 0.9f, 0f);
                cap.height = 1.8f;
                cap.radius = 0.3f;
            }
            m_Aim = gameObject.AddComponent<AimTarget>();
            m_Aim.offset = new Vector3(0f, 1.2f, 0f);
            m_FaceLocal = transform.parent ? transform.parent.InverseTransformDirection(transform.forward) : transform.forward;
            m_LastPos = transform.position;
        }

        static void Attach(Transform child, Transform parent)
        {
            if (child && parent && child != parent && child.parent != parent) child.SetParent(parent, true);
        }

        static bool TallMesh(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            return mf && mf.sharedMesh && mf.sharedMesh.bounds.size.y > 1.5f;
        }

        void OnEnable()
        {
            if (Alive && !s_Living.Contains(this)) s_Living.Add(this);
            m_LastPos = transform.position;
        }

        void OnDisable() => s_Living.Remove(this);

        // ---------------------------------------------------------------- control
        public void SetState(State s)
        {
            if (m_State == State.Dead || m_Gone) return;
            if (s == State.Dead) { Kill(transform.position + transform.forward, 2f); return; }
            if (s == m_State) return;
            m_State = s;
            m_StateTime = 0f;
            m_CheerUntil = s == State.Cheer ? Time.time + Random.Range(2.2f, 3f) : -1f;
            if (s == State.Attack) { m_AttackPhase = Random.Range(0f, 0.45f); m_StrikeIndex = -1; }
            bool stow = s == State.Carry || s == State.Haul || s == State.Ram || s == State.Climb;
            if (weapon) weapon.gameObject.SetActive(!stow);
            if (shield) shield.gameObject.SetActive(!stow);
        }

        public void Cheer(float duration)
        {
            if (!Alive) return;
            SetState(State.Cheer);
            m_CheerUntil = Time.time + duration;
        }

        public void MoveTo(Vector3 worldTarget, float speed)
        {
            if (!Alive) return;
            m_Target = worldTarget;
            m_Speed = speed;
            var to = worldTarget - transform.position;
            to.y = 0f;
            m_Best = to.magnitude;
            m_ProgressAt = Time.time;
            m_Moving = true;
            m_Steer = true;
            Arrived = false;
            if (m_State == State.Idle) SetState(State.March);
        }

        public void Stop()
        {
            m_Moving = false;
            Arrived = true;
            m_Vel = Vector3.zero;
            if (m_State == State.March) SetState(State.Idle);
        }

        // Squads drive their men directly (formation carrying, climbing); the ground still follows if enabled.
        public void Place(Vector3 worldPos, Vector3 facing)
        {
            if (m_State == State.Dead) return;
            m_Moving = false;
            Arrived = true;
            transform.position = followGround ? new Vector3(worldPos.x, transform.position.y, worldPos.z) : worldPos;
            Face(facing);
        }

        public void Face(Vector3 worldDir)
        {
            worldDir.y = 0f;
            if (worldDir.sqrMagnitude < 1e-6f) return;
            worldDir.Normalize();
            m_Steer = true;
            m_FaceLocal = transform.parent ? transform.parent.InverseTransformDirection(worldDir) : worldDir;
        }

        public void SyncStride(float cycle) => m_Cycle = cycle;

        // Scale-in when stepping out of the camp.
        public void Appear(float duration)
        {
            m_PopTime = Mathf.Max(0.01f, duration);
            m_Pop = 0f;
            transform.localScale = m_BaseScale * 0.02f;
        }

        // Gone over the wall: no longer a target, shrinks away after a moment.
        public void Vanish(float delay)
        {
            if (!Alive) return;
            m_Gone = true;
            m_Moving = false;
            s_Living.Remove(this);
            if (m_Aim) m_Aim.enabled = false;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            StartCoroutine(ShrinkAway(delay));
        }

        IEnumerator ShrinkAway(float delay)
        {
            yield return new WaitForSeconds(delay);
            var from = transform.localScale;
            for (float t = 0f; t < 0.3f; t += Time.deltaTime)
            {
                transform.localScale = from * (1f - t / 0.3f);
                yield return null;
            }
            Destroy(gameObject);
        }

        // ---------------------------------------------------------------- damage
        public void TakeHit(HitInfo hit)
        {
            if (!Alive) return;
            health = 0f;   // a footman goes down to any arrow, fireball or blast
            bool arrow = hit.source == HitSource.Arrow;
            var dir = hit.direction.sqrMagnitude > 1e-4f ? hit.direction.normalized : -transform.forward;
            if (arrow) Haptics.HitConfirm();
            Die(hit.point - dir * 2f, arrow ? Mathf.Clamp(hit.force, 2.5f, 6f) : Mathf.Max(hit.force, 9f));
        }

        public void Kill(Vector3 from, float force) => Die(from, force);

        void Die(Vector3 from, float force)
        {
            if (m_State == State.Dead || m_Gone) return;
            m_State = State.Dead;
            m_StateTime = 0f;
            health = 0f;
            m_Moving = false;
            s_Living.Remove(this);
            if (m_Aim) m_Aim.enabled = false;
            var src = Sfx.Play(Sound.KnightGrunt, head ? head.position : transform.position, 0.75f, 0.08f);
            if (src) src.pitch *= Random.Range(1.25f, 1.5f);
            if (transform.parent) transform.SetParent(null, true);
            DropWeapon(from, force);
            Topple(from, force);
            Died?.Invoke(this);
            StartCoroutine(SinkAway());
        }

        void DropWeapon(Vector3 from, float force)
        {
            if (!weapon || !weapon.gameObject.activeInHierarchy) return;
            var w = weapon;
            weapon = null;
            w.SetParent(null, true);
            w.gameObject.layer = Layers.Debris;
            var mf = w.GetComponent<MeshFilter>();
            var box = w.gameObject.AddComponent<BoxCollider>();
            if (mf && mf.sharedMesh) { box.center = mf.sharedMesh.bounds.center; box.size = Vector3.Max(mf.sharedMesh.bounds.size, Vector3.one * 0.05f); }
            var rb = w.gameObject.AddComponent<Rigidbody>();
            rb.mass = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            var away = w.position - from;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : -transform.forward;
            rb.linearVelocity = away * force * 0.3f + Vector3.up * Random.Range(2f, 3.5f) + Random.insideUnitSphere;
            rb.angularVelocity = Random.insideUnitSphere * 8f;
            w.gameObject.AddComponent<Debris>();
        }

        // One rigid body for the whole man so he falls as a unit; the limbs go limp in Animate().
        void Topple(Vector3 from, float force)
        {
            foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layers.Debris;
            var box = gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.85f, 0f);
            box.size = new Vector3(0.5f, 1.7f, 0.34f);
            var rb = GetComponent<Rigidbody>();
            if (!rb) rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.mass = 4f;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            var away = transform.position - from;
            away.y = 0f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : -transform.forward;
            bool blast = force > 8f;
            rb.linearVelocity = away * force * (blast ? 0.9f : 0.45f) + Vector3.up * (blast ? force * 0.6f : 1.2f);
            // tip over backwards, away from the blow
            rb.angularVelocity = Vector3.Cross(Vector3.up, away) * Random.Range(2.5f, 4f) * (blast ? 2.5f : 1f) + Random.insideUnitSphere * (blast ? 4f : 0.8f);
        }

        IEnumerator SinkAway()
        {
            yield return new WaitForSeconds(Random.Range(3.6f, 4.4f));
            var rb = GetComponent<Rigidbody>();
            if (rb) rb.isKinematic = true;
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            VFX.Dust(transform.position, 0.3f);
            var start = transform.position;
            float depth = 1.1f * transform.lossyScale.y;
            for (float t = 0f; t < 1.6f; t += Time.deltaTime)
            {
                float k = t / 1.6f;
                transform.position = start + Vector3.down * depth * k * k;
                yield return null;
            }
            Destroy(gameObject);
        }

        // ---------------------------------------------------------------- movement
        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || m_State == State.Dead || m_Gone) return;
            if (m_Pop < 1f)
            {
                m_Pop = Mathf.Min(1f, m_Pop + dt / m_PopTime);
                transform.localScale = m_BaseScale * Mathf.Max(0.02f, Ease.OutBack(m_Pop));
            }
            float s = Scale;
            var pos = transform.position;
            if (m_Moving) pos = Locomote(pos, dt, s);
            if (followGround) pos = FollowGround(pos, dt, s);
            else { Clambering = false; m_FallVel = 0f; }
            transform.position = pos;

            var face = FaceWorld;
            face.y = 0f;
            if (m_Steer && face.sqrMagnitude > 1e-6f)
            {
                var want = Quaternion.LookRotation(face.normalized) * Quaternion.Euler(bodyPitch, 0f, 0f);
                transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-9f * dt));
            }
        }

        Vector3 Locomote(Vector3 pos, float dt, float s)
        {
            var to = m_Target - pos;
            to.y = 0f;
            float d = to.magnitude;
            if (d < m_Best - 0.25f * s || Clambering) { m_Best = Mathf.Min(m_Best, d); m_ProgressAt = Time.time; }
            // arrived, or made no headway for a while (blocked by the crowd): give up rather than stall
            if (d <= 0.3f * s || Time.time - m_ProgressAt > 3.5f)
            {
                m_Moving = false;
                Arrived = true;
                m_Vel = Vector3.zero;
                if (m_State == State.March) SetState(State.Idle);
                return pos;
            }
            float speed = Mathf.Min(m_Speed * m_Pace, d * 2.5f + 0.5f * s);
            var want = to / d * speed + Separation(pos, s);
            m_Vel = MathExt.Damp(m_Vel, want, 6f, dt);
            if (m_Vel.sqrMagnitude > 0.04f * s * s) Face(m_Vel);
            if (Clambering) return pos;   // hands on the ledge: up first, then on
            return pos + m_Vel * dt;
        }

        Vector3 Separation(Vector3 pos, float s)
        {
            var push = Vector3.zero;
            float r = 0.75f * s;
            foreach (var o in s_Living)
            {
                if (o == this || !o) continue;
                var d = pos - o.transform.position;
                d.y = 0f;
                float m = d.sqrMagnitude;
                if (m >= r * r || m < 1e-6f) continue;
                float dist = Mathf.Sqrt(m);
                push += d / dist * (1f - dist / r);
            }
            return push * 2.2f * s;
        }

        // Raycast down onto Environment (real floor, LiDAR mesh, castle island). Ledges taller than a step
        // are clambered up, drops are fallen down, small bumps are smoothed.
        Vector3 FollowGround(Vector3 pos, float dt, float s)
        {
            bool found = Probe(pos, s, out float ground);
            var fwd = transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 1e-4f && Probe(pos + fwd.normalized * 0.35f * s, s, out float ahead) && (!found || ahead > ground))
            {
                ground = ahead;
                found = true;
            }
            if (!found) { Clambering = false; m_FallVel = 0f; return pos; }
            float dy = ground - pos.y, step = 0.5f * s;
            if (dy > step)
            {
                Clambering = true;
                m_FallVel = 0f;
                pos.y = Mathf.MoveTowards(pos.y, ground, 2.6f * s * dt);
            }
            else if (dy < -step)
            {
                Clambering = false;
                m_FallVel += Tune.Gravity * dt;
                pos.y = Mathf.Max(ground, pos.y - m_FallVel * dt);
            }
            else
            {
                Clambering = false;
                m_FallVel = 0f;
                pos.y = MathExt.Damp(pos.y, ground, 18f, dt);
            }
            return pos;
        }

        static bool Probe(Vector3 p, float s, out float y)
        {
            if (Physics.Raycast(p + Vector3.up * 3f * s, Vector3.down, out var hit, 10f * s, GroundMask, QueryTriggerInteraction.Ignore))
            {
                y = hit.point.y;
                return true;
            }
            y = p.y;
            return false;
        }

        // ---------------------------------------------------------------- animation
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float s = Scale;
            var pos = transform.position;
            var delta = pos - m_LastPos;
            m_LastPos = pos;
            m_StateTime += dt;
            if (m_State != State.Dead)
            {
                // the gait is driven by real displacement, whoever moved us (own legs, a squad, a ladder)
                float horiz = new Vector2(delta.x, delta.z).magnitude / s;
                float vert = Mathf.Abs(delta.y) / s;
                if (horiz > 2f) horiz = 0f;
                if (vert > 2f) vert = 0f;
                m_GroundSpeed = MathExt.Damp(m_GroundSpeed, horiz / dt, 8f, dt);
                float stride = Mathf.Lerp(1.6f, 2.6f, Mathf.InverseLerp(1.2f, 4.6f, m_GroundSpeed)) * m_Stride;
                m_Cycle += horiz / stride;
                m_Climbed += vert;
                if (m_State == State.Cheer && m_CheerUntil > 0f && Time.time > m_CheerUntil) SetState(m_Moving ? State.March : State.Idle);
                if (m_State == State.Attack)
                {
                    float cyc = m_StateTime / m_AttackEvery + m_AttackPhase;
                    int idx = Mathf.FloorToInt(cyc);
                    if (cyc - idx >= 0.6f && idx != m_StrikeIndex)
                    {
                        m_StrikeIndex = idx;
                        Struck?.Invoke(this);
                    }
                }
            }
            if (m_Rigged) Animate(dt);
        }

        // Extra weapon pitch that points the blade/spear at a given elevation (degrees above horizontal),
        // standing in for the wrist: the sword is modelled at 45 degrees, the spear upright.
        float Wrist(float armPitch, float elevation) => (m_Spear ? 90f : 45f) - elevation - armPitch;

        void Animate(float dt)
        {
            float t = Time.time * m_Rate + m_Offset;
            float breathe = Mathf.Sin(t * 2.2f);
            float move = Mathf.Clamp01(m_GroundSpeed / 0.8f);
            float run = Mathf.InverseLerp(2.8f, 4.6f, m_GroundSpeed);
            float ph = m_Cycle * Mathf.PI * 2f;
            float sw = Mathf.Sin(ph), cw = Mathf.Cos(ph);

            // Gait: legs swing with the stride, arms counter-swing, hips roll over the planted foot.
            float legAmp = Mathf.Lerp(26f, 40f, run) * move * m_Swing;
            float lL = -sw * legAmp, lR = sw * legAmp;
            float armAmp = Mathf.Lerp(18f, 40f, run) * move * m_Swing;
            float aLp = sw * armAmp, aLr = -5f, aRp = -sw * armAmp, aRr = 5f;
            float tP = m_Lean + (3f + 9f * run) * move, tY = -sw * 6f * move, tR = -cw * 1.5f * move;
            float hY = sw * 5f * move, hR = cw * 2.5f * move;
            var hipsOff = new Vector3(cw * 0.025f * move, Mathf.Abs(cw) * 0.05f * run, 0f);
            float wP = 0f, headP = 0f, headR = 0f;
            bool look = false, planted = true;

            switch (m_State)
            {
                case State.Idle:
                {
                    float shift = Mathf.Sin(t * 0.55f);
                    hR += shift * 1.8f;
                    hipsOff.x += shift * 0.02f;
                    tP += breathe * 1.2f;
                    tY += Mathf.Sin(t * 0.31f) * 7f;
                    aLp += -12f + breathe * 2f; aLr = -7f;
                    if (m_Spear) { aRp += -24f; aRr = 4f; wP = Wrist(aRp, 90f); }
                    else { aRp += -8f - breathe * 2f; aRr = 7f; }
                    look = true;
                    break;
                }
                case State.March:
                {
                    aLp = sw * armAmp * 0.55f - 14f; aLr = -8f;
                    if (m_Spear) { aRp = -22f - sw * armAmp * 0.25f; aRr = 5f; }
                    // the charge: shields up, swords raised, spears levelled
                    aLp = Mathf.Lerp(aLp, -52f, run);
                    aLr = Mathf.Lerp(aLr, 18f, run);
                    if (m_Spear)
                    {
                        aRp = Mathf.Lerp(aRp, -72f, run);
                        wP = Wrist(aRp, Mathf.Lerp(84f, -6f, run));
                    }
                    else
                    {
                        aRp = Mathf.Lerp(aRp, -150f + sw * 8f, run);
                        wP = Mathf.Lerp(0f, Wrist(aRp, 100f), run);
                    }
                    look = run < 0.3f && Mathf.PerlinNoise(t * 0.25f, m_Offset) > 0.7f;
                    break;
                }
                case State.Carry:
                {
                    float up = Mathf.Lerp(-58f, -168f, carryHeight);
                    aLp = aRp = up + Mathf.Abs(cw) * 4f * move;
                    aLr = Mathf.Lerp(16f, -3f, carryHeight);
                    aRr = -aLr;
                    tP = m_Lean * 0.5f + Mathf.Lerp(26f, 3f, carryHeight) + 2f * move;
                    tY *= 0.4f;
                    if (carryHeight < 0.5f) { lL = Mathf.Lerp(-24f, lL, move); lR = Mathf.Lerp(18f, lR, move); }
                    break;
                }
                case State.Haul:
                {
                    float pull = 0.5f + 0.5f * Mathf.Sin(t * 4.2f);
                    pull = pull * pull * (3f - 2f * pull);
                    tP = -10f - 18f * pull + m_Lean * 0.5f; tY = 0f; tR = 0f;
                    aLp = aRp = -80f + 34f * pull;
                    aLr = 10f; aRr = -10f;
                    lL = Mathf.Lerp(-26f, lL, move); lR = Mathf.Lerp(16f, lR, move);
                    hipsOff += new Vector3(0f, -0.03f * pull, -0.1f * pull);
                    hY = 0f; hR = 0f;
                    break;
                }
                case State.Climb:
                {
                    // alternate arm and leg, one rung pair per 0.7 m climbed
                    float sc = Mathf.Sin(m_Climbed / 0.7f * Mathf.PI * 2f + m_Offset);
                    aLp = -122f - sc * 26f; aRp = -122f + sc * 26f;
                    aLr = 6f; aRr = -6f;
                    lL = -38f + sc * 26f; lR = -38f - sc * 26f;
                    tP = 14f; tY = sc * 5f; tR = 0f; hY = 0f; hR = 0f;
                    hipsOff = new Vector3(0f, -0.04f, 0.06f);
                    headP = -18f;
                    planted = false;
                    break;
                }
                case State.Attack:
                {
                    float cyc = m_StateTime / m_AttackEvery + m_AttackPhase;
                    float k = cyc - Mathf.Floor(cyc);
                    lL = Mathf.Lerp(-20f, lL, move); lR = Mathf.Lerp(14f, lR, move);
                    aLp = -58f; aLr = 16f;   // shield up
                    tR = 0f; hY = 0f;
                    if (m_Spear)
                    {
                        // draw back, thrust, recover
                        float thrust = k < 0.5f ? -Ease.InOutCubic(k / 0.5f)
                                     : k < 0.62f ? Mathf.Lerp(-1f, 1f, Ease.OutCubic((k - 0.5f) / 0.12f))
                                     : Mathf.Lerp(1f, 0f, Ease.InOutCubic((k - 0.62f) / 0.38f));
                        aRp = -78f - 22f * thrust; aRr = 6f;
                        wP = Wrist(aRp, -6f);
                        tY = -12f * thrust;
                        tP = m_Lean + 6f + 8f * Mathf.Max(0f, thrust);
                        hipsOff.z += 0.05f * thrust;
                    }
                    else
                    {
                        // overhead chop: wind up, cut down onto the gate, recover to guard
                        float arm, el;
                        if (k < 0.5f)
                        {
                            float u = Ease.InOutCubic(k / 0.5f);
                            arm = Mathf.Lerp(-75f, -165f, u); el = Mathf.Lerp(60f, 160f, u);
                            tP = m_Lean + Mathf.Lerp(4f, -8f, u); tY = Mathf.Lerp(0f, 12f, u);
                        }
                        else if (k < 0.62f)
                        {
                            float u = Ease.OutCubic((k - 0.5f) / 0.12f);
                            arm = Mathf.Lerp(-165f, -55f, u); el = Mathf.Lerp(160f, -15f, u);
                            tP = m_Lean + Mathf.Lerp(-8f, 20f, u); tY = Mathf.Lerp(12f, -10f, u);
                        }
                        else
                        {
                            float u = Ease.InOutCubic((k - 0.62f) / 0.38f);
                            arm = Mathf.Lerp(-55f, -75f, u); el = Mathf.Lerp(-15f, 60f, u);
                            tP = m_Lean + Mathf.Lerp(20f, 4f, u); tY = Mathf.Lerp(-10f, 0f, u);
                        }
                        aRp = arm; aRr = 8f;
                        wP = Wrist(arm, el);
                        hipsOff.y -= 0.04f * Mathf.Sin(Mathf.Clamp01((k - 0.5f) / 0.3f) * Mathf.PI);
                    }
                    break;
                }
                case State.Ram:
                {
                    float p = squadPhase - Mathf.Floor(squadPhase), eff = Mathf.Clamp01(squadEffort);
                    float back, drive;
                    if (p < 0.5f) { back = Mathf.SmoothStep(0f, 1f, p / 0.5f); drive = 0f; }
                    else if (p < 0.66f) { float u = (p - 0.5f) / 0.16f; back = 1f - u * u; drive = u; }
                    else { float u = (p - 0.66f) / 0.34f; back = 0.2f * Mathf.Sin(u * Mathf.PI); drive = 1f - u; }
                    float side = squadSide < 0f ? -1f : 1f;
                    aLp = aRp = -32f + eff * (14f * back - 28f * drive);
                    if (side < 0f) { aRr = 10f; aLr = 26f; } else { aLr = -10f; aRr = -26f; }
                    tP = m_Lean + 6f + eff * (-16f * back + 24f * drive);
                    tY = -side * 10f + tY * (1f - eff);
                    lL = Mathf.Lerp(lL, -24f, eff); lR = Mathf.Lerp(lR, 18f, eff);
                    hipsOff.z += eff * (-0.12f * back + 0.1f * drive);
                    break;
                }
                case State.Cheer:
                {
                    float b = Mathf.Abs(Mathf.Sin(m_StateTime * 8.5f * m_Rate + m_Offset));
                    aRp = -158f + b * 34f; aRr = 10f;
                    aLp = -118f + b * 22f; aLr = -14f;
                    wP = Wrist(aRp, m_Spear ? 95f : 100f);
                    tP = -8f + b * 4f; tY *= 0.3f;
                    hipsOff.y += b * 0.16f * (1f - move);
                    lL = Mathf.Lerp(-6f * b, lL, move); lR = Mathf.Lerp(6f * b, lR, move);
                    headP = -10f;
                    look = true;
                    planted = move > 0.5f;
                    break;
                }
                case State.Dead:
                {
                    float jerk = Mathf.Clamp01(1f - m_StateTime / 0.25f);
                    aLp = Mathf.Lerp(-35f, -140f, jerk); aLr = -50f;
                    aRp = Mathf.Lerp(-25f, -150f, jerk); aRr = 48f;
                    lL = -18f; lR = 12f;
                    tP = -12f; tY = 8f; tR = 10f; hY = 0f; hR = 0f;
                    hipsOff = Vector3.zero;
                    headP = -15f; headR = 18f;
                    wP = 0f;
                    planted = false;
                    break;
                }
            }

            // Scrambling up a ledge: hands on the edge, legs scrabbling.
            if (Clambering && (m_State == State.Idle || m_State == State.March || m_State == State.Attack || m_State == State.Cheer))
            {
                float sc = Mathf.Sin(m_Climbed / 0.5f * Mathf.PI * 2f);
                aLp = -130f - sc * 20f; aRp = -130f + sc * 20f;
                lL = -40f + sc * 25f; lR = -40f - sc * 25f;
                tP = 20f;
                planted = false;
            }

            // Rigid legs swinging out would lift the feet off the ground: drop the hips onto the arc instead.
            if (planted)
            {
                float swing = Mathf.Max(Mathf.Abs(lL), Mathf.Abs(lR)) * Mathf.Deg2Rad;
                hipsOff.y -= m_LegLen * (1f - Mathf.Cos(swing)) * 0.65f;
            }

            // The shield stays facing forward as the arm comes up (stands in for the elbow).
            float sP = m_State == State.Dead ? 0f : -aLp * 0.8f;

            float yaw = 0f, pitch = headP;
            if (look && LookPoint(out var at))
            {
                var d = head.parent.InverseTransformDirection(at - head.position);
                yaw = Mathf.Clamp(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, -65f, 65f);
                pitch += Mathf.Clamp(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg, -40f, 20f);
            }
            m_HeadYaw = MathExt.Damp(m_HeadYaw, yaw, 5f, dt);
            m_HeadPitch = MathExt.Damp(m_HeadPitch, pitch, 5f, dt);

            float k2 = 1f - Mathf.Exp(-(m_State == State.Dead ? 7f : 24f) * dt);
            hips.localPosition = Vector3.Lerp(hips.localPosition, m_HipsP + hipsOff, k2);
            hips.localRotation = Quaternion.Slerp(hips.localRotation, m_HipsR * Quaternion.Euler(0f, hY, hR), k2);
            torso.localRotation = Quaternion.Slerp(torso.localRotation, m_TorsoR * Quaternion.Euler(tP, tY, tR), k2);
            armL.localRotation = Quaternion.Slerp(armL.localRotation, m_ArmLR * Quaternion.Euler(aLp, 0f, aLr), k2);
            armR.localRotation = Quaternion.Slerp(armR.localRotation, m_ArmRR * Quaternion.Euler(aRp, 0f, aRr), k2);
            legL.localRotation = Quaternion.Slerp(legL.localRotation, m_LegLR * Quaternion.Euler(lL, 0f, 0f), k2);
            legR.localRotation = Quaternion.Slerp(legR.localRotation, m_LegRR * Quaternion.Euler(lR, 0f, 0f), k2);
            if (weapon) weapon.localRotation = Quaternion.Slerp(weapon.localRotation, m_WeaponR * Quaternion.Euler(wP, 0f, 0f), k2);
            if (shield) shield.localRotation = Quaternion.Slerp(shield.localRotation, m_ShieldR * Quaternion.Euler(sP, 0f, 0f), k2);
            head.localRotation = m_HeadR * Quaternion.Euler(m_HeadPitch, m_HeadYaw, headR);
        }

        // Soldiers glance up at the defender on the keep.
        bool LookPoint(out Vector3 p)
        {
            if (lookTarget) { p = lookTarget.position; return true; }
            var gm = GameManager.Instance;
            var cam = gm && gm.cam ? gm.cam : Camera.main;
            if (cam) { p = cam.transform.position; return true; }
            p = default;
            return false;
        }
    }
}
