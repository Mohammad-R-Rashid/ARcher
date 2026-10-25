using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // The battering ram and its team. Four men carry the iron-shod log to the gate by its rope handles and
    // swing it in unison: draw back, drive in, boom. The crew holds the rear two pairs of handles, so the back
    // pair stands out of the gate wall's dead ground where the keep can shoot them; when a man there falls, the
    // man ahead of him takes his place. Cut below two men (or with nobody at the back) the ram is dropped
    // until other raiders pick it up; if nobody does, the survivors go back to fighting on foot.
    [DefaultExecutionOrder(-50)]
    public class BatteringRam : MonoBehaviour
    {
        enum Phase { Carry, Ram, Down, Lift, Abandoned }

        const float Period = 1.6f;      // seconds per swing
        const float Impact = 0.66f;     // swing phase at which the head meets the gate
        const float Grip = 0.98f;       // log height above a carrier's feet
        const float DrawBack = 1.1f;    // metres the log is pulled back before each blow
        const float Reach = 0.3f;       // a man stands this far behind the rope loop his hands are on
        const float FeetTake = 0.8f;    // share of the swing the men step through (their arms do the rest)

        WaveManager m_Raid;
        readonly Soldier[] m_Holders = new Soldier[4];   // 0, 1 rear pair; 2, 3 the pair ahead of them
        readonly bool[] m_Holding = new bool[4];
        readonly Vector3[] m_Handles = new Vector3[4];
        readonly Vector3[] m_JoinTarget = new Vector3[4];
        Phase m_Phase = Phase.Carry;
        float m_Length = 6f, m_Radius = 0.28f, m_PhaseTime, m_Cycle, m_Swing, m_LyingY, m_NextRecruit, m_StrikeOut, m_Pop = 1f;
        int m_LastImpact = -1;
        Vector3 m_Center, m_Dir = Vector3.forward, m_Approach, m_Scale = Vector3.one;
        bool m_Aligned, m_Halted, m_Lying;

        Vector3 StrikeCenter => m_Raid.GatePoint(0f, m_StrikeOut + m_Length * 0.5f + 0.05f);

        public bool Abandoned => m_Phase == Phase.Abandoned;

        // At (or closing on) the gate: foot soldiers keep its middle clear.
        public bool Committed => !m_Halted && m_Raid && m_Phase != Phase.Abandoned &&
                                 (m_Phase == Phase.Ram || WaveManager.FlatDistance(m_Center, StrikeCenter) < 22f);

        public void Begin(WaveManager raid, IList<Soldier> crew)
        {
            m_Raid = raid;
            // Arrows pass the log to the men; dead men don't land on it.
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Measure();
            MeasureStrike();
            m_Scale = transform.localScale;
            m_Pop = 0f;
            transform.localScale = m_Scale * 0.02f;
            // rope loops hang at a third and a twelfth of the length behind the middle (Army.fbx Ram)
            float rear = -m_Length / 3f - Reach, front = -m_Length / 12f - Reach, side = 0.62f;
            m_Handles[0] = new Vector3(-side, 0f, rear);
            m_Handles[1] = new Vector3(side, 0f, rear);
            m_Handles[2] = new Vector3(-side, 0f, front);
            m_Handles[3] = new Vector3(side, 0f, front);
            m_Center = Vector3.zero;
            int n = 0;
            for (int i = 0; i < 4 && i < crew.Count; i++)
            {
                var s = crew[i];
                if (!s) continue;
                m_Holders[i] = s;
                m_Holding[i] = true;
                Grab(i, Soldier.State.Ram);
                s.SyncStride(0f);
                m_Center += s.transform.position;
                n++;
            }
            m_Center = n > 0 ? m_Center / n : transform.position;
            // line up straight in front of the gate, then march in
            m_Approach = raid.GatePoint(0f, m_Length * 0.5f + 9f);
            m_Aligned = Vector3.Dot(m_Center - raid.GateFace, raid.GateForward) < m_Length * 0.5f + 9f;
            var to = (m_Aligned ? StrikeCenter : m_Approach) - m_Center;
            to.y = 0f;
            m_Dir = to.sqrMagnitude > 0.01f ? to.normalized : -raid.GateForward;
            PlaceHolders(0f);
            PoseCarried();
        }

        void Measure()
        {
            var mf = GetComponentInChildren<MeshFilter>();
            if (!mf || !mf.sharedMesh) return;
            var b = mf.sharedMesh.bounds;
            var sc = mf.transform.lossyScale;
            float len = b.size.z * Mathf.Abs(sc.z);
            float rad = Mathf.Min(b.size.x * Mathf.Abs(sc.x), b.size.y * Mathf.Abs(sc.y)) * 0.5f;
            if (len > 2f && len < 15f) m_Length = len;
            if (rad > 0.08f && rad < 1f) m_Radius = rad;
        }

        // The head meets whatever closes the arch (the doors), probed at the ram's own height.
        void MeasureStrike()
        {
            var under = WaveManager.Ground(m_Raid.GatePoint(0f, m_Length * 0.5f));
            float o = m_Raid.SurfaceOut(0f, under.y + Grip - m_Raid.GateFace.y);
            m_StrikeOut = float.IsNaN(o) ? 0f : o;
        }

        void Grab(int i, Soldier.State state)
        {
            var s = m_Holders[i];
            s.Stop();
            s.SetState(state);
            s.squadEffort = 0f;
            s.carryHeight = 0f;
            s.squadSide = m_Handles[i].x < 0f ? -1f : 1f;
        }

        // A fresh man for the ram: he walks to a free handle and takes hold (a dropped or abandoned ram is
        // picked up again). False when every handle is manned.
        public bool Enlist(Soldier s)
        {
            if (m_Halted || !m_Raid || !s || !s.Alive) return false;
            for (int i = 0; i < 4; i++)
            {
                if (m_Holders[i]) continue;
                if (m_Phase == Phase.Abandoned)
                {
                    m_Phase = Phase.Down;
                    m_PhaseTime = 0f;
                    m_NextRecruit = Time.time + 6f;
                }
                m_Holders[i] = s;
                m_Holding[i] = false;
                m_JoinTarget[i] = Vector3.zero;
                s.squadEffort = 0f;
                s.SetState(Soldier.State.March);
                return true;
            }
            return false;
        }

        public void OnBreach() => Abandon();

        public void Halt()
        {
            m_Halted = true;
            foreach (var s in m_Holders)
                if (s && s.Alive) { s.squadEffort = 0f; s.Stop(); }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (m_Pop < 1f)
            {
                // grows in with its crew as they step out of the camp
                m_Pop = Mathf.Min(1f, m_Pop + dt / 0.3f);
                transform.localScale = m_Scale * Mathf.Max(0.02f, Ease.OutBack(m_Pop));
            }
            if (m_Halted) return;
            if (!m_Raid) { Halt(); return; }
            m_PhaseTime += dt;
            for (int i = 0; i < 4; i++)
            {
                if (m_Holders[i] && !m_Holders[i].Alive) m_Holders[i] = null;
                if (!m_Holders[i]) m_Holding[i] = false;
            }
            if (m_Phase == Phase.Abandoned) return;
            if (m_Raid.Over) { Abandon(); return; }
            ShiftBack();
            int holding = 0;
            bool rear = false;
            for (int i = 0; i < 4; i++)
                if (m_Holders[i] && m_Holding[i]) { holding++; rear |= i < 2; }
            if (m_Phase != Phase.Down && (holding < 2 || !rear)) { Drop(); return; }
            UpdateJoiners();
            switch (m_Phase)
            {
                case Phase.Carry: UpdateCarry(dt, holding); break;
                case Phase.Ram: UpdateRam(dt); break;
                case Phase.Down: UpdateDown(); break;
                case Phase.Lift: UpdateLift(); break;
            }
        }

        // A man down at the back: the one ahead of him (same side first) lets go and takes the rear loop.
        void ShiftBack()
        {
            for (int i = 0; i < 2; i++)
            {
                if (m_Holders[i]) continue;
                int from = m_Holders[i + 2] ? i + 2 : m_Holders[3 - i] ? 3 - i : -1;
                if (from < 0) continue;
                var s = m_Holders[from];
                m_Holders[from] = null;
                m_Holding[from] = false;
                m_Holders[i] = s;
                m_Holding[i] = false;
                m_JoinTarget[i] = Vector3.zero;
                s.squadEffort = 0f;
                s.carryHeight = 0f;
                s.SetState(Soldier.State.Carry);   // hands low along the log, weapon still stowed
            }
        }

        // Recruits (and survivors after a drop) walk to a free handle and take hold.
        void UpdateJoiners()
        {
            var rot = Quaternion.LookRotation(m_Dir);
            for (int i = 0; i < 4; i++)
            {
                var s = m_Holders[i];
                if (!s || m_Holding[i]) continue;
                var hp = m_Center + rot * m_Handles[i];
                if (WaveManager.FlatDistance(s.transform.position, hp) < 0.5f)
                {
                    m_Holding[i] = true;
                    Grab(i, m_Phase == Phase.Down ? Soldier.State.Carry : Soldier.State.Ram);
                }
                else if (!s.Moving || (m_JoinTarget[i] - hp).sqrMagnitude > 0.25f)
                {
                    m_JoinTarget[i] = hp;
                    s.MoveTo(hp, m_Raid.chargeSpeed * 0.8f);
                }
            }
        }

        // ---------------------------------------------------------------- carrying and ramming
        void UpdateCarry(float dt, int holding)
        {
            var target = m_Aligned ? StrikeCenter : m_Approach;
            var to = target - m_Center;
            to.y = 0f;
            float d = to.magnitude;
            if (d < 0.25f)
            {
                if (!m_Aligned) m_Aligned = true;
                else { StartRam(); return; }
            }
            bool slow = false;
            for (int i = 0; i < 4; i++) if (m_Holders[i] && m_Holding[i]) slow |= m_Holders[i].Clambering;
            float speed = m_Raid.carrySpeed * 0.9f * (holding < 4 ? 0.85f : 1f) * (slow ? 0.3f : 1f);
            if (d > 1e-3f)
            {
                var dir = to / d;
                m_Dir = Vector3.Slerp(m_Dir, dir, 1f - Mathf.Exp(-3f * dt));
                m_Dir.y = 0f;
                m_Dir = m_Dir.sqrMagnitude > 1e-4f ? m_Dir.normalized : dir;
                m_Center += dir * Mathf.Min(speed * dt, d);
            }
            PlaceHolders(0f);
        }

        void StartRam()
        {
            MeasureStrike();
            m_Phase = Phase.Ram;
            m_PhaseTime = 0f;
            m_Cycle = 0f;
            m_LastImpact = -1;
            m_Center = StrikeCenter;
        }

        void UpdateRam(float dt)
        {
            m_Dir = Vector3.Slerp(m_Dir, -m_Raid.GateForward, 1f - Mathf.Exp(-4f * dt)).normalized;
            m_Cycle += dt / Period;
            int idx = Mathf.FloorToInt(m_Cycle);
            float p = m_Cycle - idx;
            if (p < 0.5f) m_Swing = DrawBack * Mathf.SmoothStep(0f, 1f, p / 0.5f);
            else if (p < Impact) { float u = (p - 0.5f) / (Impact - 0.5f); m_Swing = DrawBack * (1f - u * u); }
            else m_Swing = 0.25f * Mathf.Sin((p - Impact) / (1f - Impact) * Mathf.PI);   // recoil
            if (p >= Impact && idx != m_LastImpact)
            {
                m_LastImpact = idx;
                Strike();
            }
            float effort = Mathf.Clamp01(m_PhaseTime / 0.6f);
            m_Center = StrikeCenter;
            for (int i = 0; i < 4; i++)
                if (m_Holders[i] && m_Holding[i]) { m_Holders[i].squadPhase = p; m_Holders[i].squadEffort = effort; }
            // the men step with the swing; their arms and backs take the rest, so the loops stay in their hands
            PlaceHolders(m_Swing * FeetTake);
        }

        void Strike()
        {
            if (!m_Raid.GateStanding) return;
            var head = transform.position + transform.forward * m_Length * 0.5f;
            float up = head.y - m_Raid.GateFace.y;
            float o = m_Raid.SurfaceOut(0f, up);
            var point = m_Raid.GatePoint(0f, float.IsNaN(o) ? m_StrikeOut : o, up);
            m_Raid.HitGate(0.35f, point, 6f);
            VFX.Dust(point, 0.9f);
            VFX.Splinters(point, m_Raid.GateForward, 1.1f);
            Sfx.Play(Sound.Thud, point, 1f, 0.08f);
            Sfx.Play(Sound.WoodBreak, point, 0.55f, 0.15f);
            ScreenFX.Shake(0.12f);
            Haptics.CastleImpact(0.4f);
        }

        void PlaceHolders(float feetBack)
        {
            var rot = Quaternion.LookRotation(m_Dir);
            var basePos = m_Center - m_Dir * feetBack;
            for (int i = 0; i < 4; i++)
                if (m_Holders[i] && m_Holding[i]) m_Holders[i].Place(basePos + rot * m_Handles[i], m_Dir);
        }

        void LateUpdate()
        {
            if (m_Halted || m_Lying || !m_Raid) return;
            if (m_Phase == Phase.Carry || m_Phase == Phase.Ram || m_Phase == Phase.Lift) PoseCarried();
        }

        // The log hangs from the carriers' hands: pitch follows the rear and front pairs' footing.
        void PoseCarried()
        {
            float yr = 0f, yf = 0f;
            int nr = 0, nf = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!m_Holders[i] || !m_Holding[i]) continue;
                float y = m_Holders[i].transform.position.y + Grip;
                if (i < 2) { yr += y; nr++; } else { yf += y; nf++; }
            }
            if (nr + nf == 0) return;
            float ar = nr > 0 ? yr / nr : 0f, af = nf > 0 ? yf / nf : 0f;
            if (nr == 0) ar = af;
            if (nf == 0) af = ar;
            float zr = m_Handles[0].z, zf = m_Handles[2].z;
            // both pairs are behind the middle: keep a man scrambling up a ledge from see-sawing the head
            float rise = Mathf.Clamp(af - ar, -0.36f * (zf - zr), 0.36f * (zf - zr));
            var center = m_Center - m_Dir * (m_Phase == Phase.Ram ? m_Swing : 0f);
            center.y = ar + rise * (-zr / (zf - zr));
            var dir = (m_Dir * (zf - zr) + Vector3.up * rise).normalized;
            if (m_Phase == Phase.Lift)
            {
                float k = Ease.OutCubic(m_PhaseTime / 0.6f);
                center.y = Mathf.Lerp(m_LyingY, center.y, k);
                dir = Vector3.Slerp(m_Dir, dir, k);
            }
            transform.SetPositionAndRotation(center, Quaternion.LookRotation(dir, Vector3.up));
        }

        // ---------------------------------------------------------------- dropped
        void Drop()
        {
            m_Phase = Phase.Down;
            m_PhaseTime = 0f;
            m_NextRecruit = Time.time + 0.8f;
            m_Center -= m_Dir * m_Swing;
            m_Swing = 0f;
            for (int i = 0; i < 4; i++)
            {
                var s = m_Holders[i];
                if (!s) continue;
                m_Holding[i] = false;
                s.squadEffort = 0f;
                s.Stop();
                s.SetState(Soldier.State.Idle);
            }
            StopAllCoroutines();
            StartCoroutine(FallFlat());
        }

        IEnumerator FallFlat()
        {
            m_Lying = true;
            var fromPos = transform.position;
            var fromRot = transform.rotation;
            var to = WaveManager.Ground(new Vector3(m_Center.x, fromPos.y, m_Center.z));
            to.y += m_Radius;
            m_LyingY = to.y;
            var flat = m_Dir;
            flat.y = 0f;
            var toRot = Quaternion.LookRotation(flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward, Vector3.up);
            for (float t = 0f; t < 0.35f; t += Time.deltaTime)
            {
                float k = t / 0.35f;
                k *= k;
                transform.SetPositionAndRotation(Vector3.Lerp(fromPos, to, k), Quaternion.Slerp(fromRot, toRot, k));
                yield return null;
            }
            transform.SetPositionAndRotation(to, toRot);
            Sfx.Play(Sound.Thud, to, 0.7f, 0.1f);
            VFX.Dust(to, 0.7f);
        }

        void UpdateDown()
        {
            var rot = Quaternion.LookRotation(m_Dir);
            int holding = 0, crew = 0;
            bool rear = false;
            for (int i = 0; i < 4; i++)
            {
                if (!m_Holders[i]) continue;
                crew++;
                if (!m_Holding[i]) continue;
                holding++;
                rear |= i < 2;
                m_Holders[i].Place(m_Center + rot * m_Handles[i], m_Dir);
            }
            if (holding >= 2 && rear && m_PhaseTime > 0.6f) { StartLift(); return; }
            if (crew < 4 && Time.time >= m_NextRecruit)
            {
                m_NextRecruit = Time.time + 1.2f;
                var r = m_Raid.Recruit(m_Center, 24f);
                if (r && !Enlist(r)) m_Raid.Release(r);
            }
            // nobody came (or the ones coming were cut down): give up on the ram
            if ((crew == 0 && m_PhaseTime > 25f) || m_PhaseTime > 60f) Abandon();
        }

        void StartLift()
        {
            StopAllCoroutines();
            m_Lying = false;
            m_Phase = Phase.Lift;
            m_PhaseTime = 0f;
            for (int i = 0; i < 4; i++)
                if (m_Holders[i] && m_Holding[i]) Grab(i, Soldier.State.Ram);
        }

        void UpdateLift()
        {
            PlaceHolders(0f);
            if (m_PhaseTime < 0.6f) return;
            if (WaveManager.FlatDistance(m_Center, StrikeCenter) < 0.5f) StartRam();
            else
            {
                m_Phase = Phase.Carry;
                m_PhaseTime = 0f;
                m_Aligned = Vector3.Dot(m_Center - m_Raid.GateFace, m_Raid.GateForward) < m_Length * 0.5f + 9f;
            }
        }

        void Abandon()
        {
            if (m_Phase == Phase.Abandoned) return;
            m_Phase = Phase.Abandoned;
            if (!m_Lying)
            {
                m_Center -= m_Dir * m_Swing;
                m_Swing = 0f;
                StopAllCoroutines();
                StartCoroutine(FallFlat());
            }
            for (int i = 0; i < 4; i++)
            {
                var s = m_Holders[i];
                m_Holders[i] = null;
                m_Holding[i] = false;
                if (s && s.Alive)
                {
                    s.squadEffort = 0f;
                    if (m_Raid) m_Raid.Release(s);
                }
            }
        }
    }
}
