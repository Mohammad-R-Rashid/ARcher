using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // A siege ladder and its team. Two men carry it overhead to the gate wall beside the arch and walk it up
    // (one braces the foot, the other pushes it up hand over hand) until it leans at ~72 degrees with its rails
    // on the parapet's edge, then climb. A man who reaches the top gets over the parapet; shot climbers fall
    // off and other raiders take their place.
    [DefaultExecutionOrder(-50)]
    public class SiegeLadder : MonoBehaviour
    {
        enum Phase { Carry, Raise, Raised, Fallen }

        class Climber { public Soldier s; public int stage; public float along, wait, speed, t; public Vector3 from; }

        const float Hands = 2.02f;        // overhead grip on a 1.8 m man
        const float RaiseTime = 1.3f;
        const float ClimbSpeed = 0.75f;   // metres of ladder per second
        const float Angle = 72f;          // raised, from the horizontal
        const float RailOff = 0.18f;      // ladder axis to the rails' wall side
        const float Overhang = 1f;        // ladder standing proud of the parapet
        const float ClimbOff = 0.56f;     // a climber's root off the ladder (fits Soldier's Climb pose)

        WaveManager m_Raid;
        Phase m_Phase = Phase.Carry;
        readonly List<Soldier> m_Carriers = new List<Soldier>();
        readonly List<Climber> m_Climbers = new List<Climber>();
        readonly List<Vector3> m_Path = new List<Vector3>();
        int m_PathIndex, m_LastCount;
        float m_Length = 7.5f, m_X, m_PhaseTime, m_GroundY, m_PusherAlong, m_FooterAlong, m_Solo, m_Pop = 1f;
        float m_TopRise, m_TopOut, m_TopY, m_ClimbTop;
        Vector3 m_Center, m_Dir = Vector3.forward, m_Foot, m_Up = Vector3.up, m_RaiseFoot0, m_RaiseTop0, m_Walk, m_Scale = Vector3.one;
        bool m_Halted;

        public bool Fallen => m_Phase == Phase.Fallen;
        public bool Raised => m_Phase == Phase.Raised;
        public float Offset => m_X;

        public bool CanJoin
        {
            get
            {
                if (m_Phase != Phase.Raised || m_Halted || !m_Raid || m_Climbers.Count >= 2) return false;
                foreach (var c in m_Climbers) if (c.stage < 2 || (c.stage == 2 && c.along < 2f)) return false;
                return true;
            }
        }

        static float Pitch => 90f - Angle - 5f;   // climbers lean in a little less than the ladder

        public void Begin(WaveManager raid, IList<Soldier> carriers, float x)
        {
            m_Raid = raid;
            m_X = x;
            // A solid slab collider would shield the men under it from arrows and catch the ones shot off it.
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            m_Length = MeasureLength();
            FitToWall();
            m_Pop = 0f;
            transform.localScale = m_Scale * 0.02f;
            m_Center = Vector3.zero;
            int n = 0;
            foreach (var c in carriers)
            {
                if (!c) continue;
                m_Carriers.Add(c);
                c.SetState(Soldier.State.Carry);
                c.carryHeight = 1f;
                c.SyncStride(0f);
                m_Center += c.transform.position;
                n++;
            }
            m_Center = n > 0 ? m_Center / n : transform.position;
            m_LastCount = m_Carriers.Count;
            // straight in at the wall, top end first
            float stop = 0.05f + m_Length * 0.5f;
            if (Vector3.Dot(m_Center - raid.GateFace, raid.GateForward) > stop + 7f) m_Path.Add(raid.GatePoint(x, stop + 7f));
            m_Path.Add(raid.GatePoint(x, stop));
            var to = m_Path[0] - m_Center;
            to.y = 0f;
            m_Dir = to.sqrMagnitude > 0.01f ? to.normalized : -raid.GateForward;
            PlaceCarriers();
            PoseCarried();
        }

        float MeasureLength()
        {
            var mf = GetComponentInChildren<MeshFilter>();
            if (!mf || !mf.sharedMesh) return 7.5f;
            var b = mf.sharedMesh.bounds;
            var top = mf.transform.TransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            float len = transform.InverseTransformPoint(top).y * transform.lossyScale.y;
            return len > 2f && len < 15f ? len : 7.5f;
        }

        // Where the raised ladder will stand: rails on the parapet's front edge (found by probing the real
        // masonry), foot out on the ground. A ladder longer than the wall needs is cut down to ~1 m proud of
        // the edge; the rungs just sit a little closer.
        void FitToWall()
        {
            float sin = Mathf.Sin(Angle * Mathf.Deg2Rad), cos = Mathf.Cos(Angle * Mathf.Deg2Rad);
            m_Raid.Parapet(m_X, out float top, out float edgeOut, out m_Walk);
            m_TopY = m_Raid.GateFace.y + top;
            float axisOut = edgeOut + RailOff * sin, axisY = m_TopY + RailOff * cos;
            m_GroundY = WaveManager.Ground(m_Raid.GatePoint(m_X, axisOut + 1.5f)).y;
            float along = 0f, footOut = axisOut;
            for (int i = 0; i < 2; i++)
            {
                along = Mathf.Max(1f, axisY - m_GroundY) / sin;
                footOut = axisOut + along * cos;
                m_GroundY = WaveManager.Ground(m_Raid.GatePoint(m_X, footOut)).y;
            }
            float k = Mathf.Clamp((along + Overhang) / m_Length, 0.75f, 1f);
            m_Length *= k;
            m_Scale = transform.localScale;
            m_Scale.y *= k;
            m_TopRise = m_Length * sin;
            m_TopOut = footOut - m_Length * cos;
            m_ClimbTop = Mathf.Clamp((m_TopY - 0.4f - m_GroundY) / sin, 1f, m_Length - 0.6f);
        }

        public bool Join(Soldier s)
        {
            if (!CanJoin || !s || !s.Alive) return false;
            AddClimber(s, 0f, false);
            return true;
        }

        public void OnBreach()
        {
            if (m_Phase != Phase.Carry) return;
            ReleaseCarriers();
            Fall();
        }

        public void Halt()
        {
            m_Halted = true;
            foreach (var c in m_Carriers) if (c && c.Alive) c.Stop();
            foreach (var c in m_Climbers) if (c.s && c.s.Alive) c.s.Stop();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (m_Pop < 1f)
            {
                // grows in with its carriers as they step out of the camp
                m_Pop = Mathf.Min(1f, m_Pop + dt / 0.3f);
                transform.localScale = m_Scale * Mathf.Max(0.02f, Ease.OutBack(m_Pop));
            }
            if (m_Halted) return;
            if (!m_Raid) { Halt(); return; }
            m_PhaseTime += dt;
            m_Carriers.RemoveAll(c => !c || !c.Alive);
            if (m_Carriers.Count == 1 && m_LastCount > 1) LostCarrier();
            m_LastCount = m_Carriers.Count;
            if (m_Phase == Phase.Carry) UpdateCarry(dt);
            else if (m_Phase == Phase.Raise) UpdateRaise(dt);
            UpdateClimbers(dt);
        }

        void LateUpdate()
        {
            if (!m_Halted && m_Phase == Phase.Carry) PoseCarried();
        }

        // ---------------------------------------------------------------- carrying
        void UpdateCarry(float dt)
        {
            if (m_Carriers.Count == 0) { Fall(); return; }
            if (m_Raid.Over) { ReleaseCarriers(); Fall(); return; }
            var to = m_Path[m_PathIndex] - m_Center;
            to.y = 0f;
            float d = to.magnitude;
            if (d < 0.25f)
            {
                if (m_PathIndex < m_Path.Count - 1) m_PathIndex++;
                else { StartRaise(); return; }
            }
            bool slow = false;
            foreach (var c in m_Carriers) slow |= c.Clambering;
            float speed = m_Raid.carrySpeed * (m_Carriers.Count < 2 ? 0.75f : 1f) * (slow && m_PhaseTime < 90f ? 0.3f : 1f);
            if (d > 1e-3f)
            {
                var dir = to / d;
                m_Dir = Vector3.Slerp(m_Dir, dir, 1f - Mathf.Exp(-3f * dt));
                m_Dir.y = 0f;
                m_Dir = m_Dir.sqrMagnitude > 1e-4f ? m_Dir.normalized : dir;
                m_Center += dir * Mathf.Min(speed * dt, d);
            }
            m_Solo = Mathf.MoveTowards(m_Solo, 0f, 1.2f * dt);
            PlaceCarriers();
        }

        // One man left: he keeps hold where he is and shuffles to the middle, rather than snapping there.
        void LostCarrier()
        {
            var surv = m_Carriers[0].transform.position;
            if (m_Phase == Phase.Carry)
            {
                var rel = surv - m_Center;
                rel.y = 0f;
                m_Solo = Mathf.Clamp(Vector3.Dot(rel, m_Dir), -m_Length * 0.4f, m_Length * 0.4f);
            }
            else if (m_Phase == Phase.Raise)
            {
                var flatUp = m_Up;
                flatUp.y = 0f;
                var rel = surv - m_Foot;
                rel.y = 0f;
                float run = flatUp.magnitude;
                m_PusherAlong = run > 0.05f ? Vector3.Dot(rel, flatUp / run) / run : m_PusherAlong;
            }
        }

        void PlaceCarriers()
        {
            int n = m_Carriers.Count;
            for (int i = 0; i < n; i++)
            {
                float off = n == 1 ? m_Solo : (i == 0 ? 0.3f : -0.3f) * m_Length;
                m_Carriers[i].Place(m_Center + m_Dir * off, m_Dir);
            }
        }

        // The ladder rides on the carriers' raised hands, so it tilts as they climb onto the island.
        void PoseCarried()
        {
            int n = m_Carriers.Count;
            if (n == 0) return;
            var a = m_Carriers[0].transform.position + Vector3.up * Hands;
            Vector3 mid = a - m_Dir * m_Solo, axis = m_Dir;
            if (n > 1)
            {
                var b = m_Carriers[1].transform.position + Vector3.up * Hands;
                mid = (a + b) * 0.5f;
                axis = a - b;
                axis = axis.sqrMagnitude > 1e-4f ? axis.normalized : m_Dir;
            }
            SetPose(mid - axis * m_Length * 0.5f, axis);
        }

        // Foot at 'foot', rails along 'along' (the ladder model stands along its local +Y).
        void SetPose(Vector3 foot, Vector3 along)
        {
            var up = along.normalized;
            var side = Vector3.Cross(Vector3.up, up);
            if (side.sqrMagnitude < 1e-4f) side = m_Raid ? m_Raid.GateRight : Vector3.right;
            side.Normalize();
            transform.SetPositionAndRotation(foot, Quaternion.LookRotation(Vector3.Cross(side, up), up));
        }

        // ---------------------------------------------------------------- raising
        void StartRaise()
        {
            m_Phase = Phase.Raise;
            m_PhaseTime = 0f;
            m_RaiseFoot0 = transform.position;
            m_RaiseTop0 = transform.position + transform.up * m_Length;
            // pick up from where the men actually stand along the (still level) ladder
            m_PusherAlong = AlongFromFoot(m_Carriers[0]);
            m_FooterAlong = m_Carriers.Count > 1 ? AlongFromFoot(m_Carriers[1]) : 0f;
            Sfx.Play(Sound.Thud, m_RaiseTop0, 0.35f, 0.1f);
        }

        float AlongFromFoot(Soldier s)
        {
            var rel = s.transform.position - m_RaiseFoot0;
            rel.y = 0f;
            var flat = m_RaiseTop0 - m_RaiseFoot0;
            flat.y = 0f;
            return flat.sqrMagnitude > 1e-4f ? Vector3.Dot(rel, flat.normalized) : m_Length * 0.5f;
        }

        void UpdateRaise(float dt)
        {
            if (m_Carriers.Count == 0) { Fall(); return; }
            float r = Mathf.Clamp01(m_PhaseTime / RaiseTime);
            var fwd = m_Raid.GateForward;
            // the foot comes down to the ground first, then is pushed in while the top slides up the wall
            // (it is battered, so the top keeps clear of it) and over onto the parapet
            float footY = Hands * (1f - Mathf.SmoothStep(0f, 1f, r / 0.35f));
            float u = Ease.InOutCubic(Mathf.Clamp01((r - 0.2f) / 0.8f));
            float h = Mathf.Lerp(Hands, m_TopRise, u);
            float topOut = Mathf.Lerp(0f, m_TopOut, u);
            float run = Mathf.Sqrt(Mathf.Max(0.01f, m_Length * m_Length - (h - footY) * (h - footY)));
            var top = m_Raid.GatePoint(m_X, topOut);
            top.y = m_GroundY + h;
            var foot = m_Raid.GatePoint(m_X, topOut + run);
            foot.y = m_GroundY + footY;
            float blend = Mathf.Clamp01(m_PhaseTime / 0.25f);
            m_Foot = Vector3.Lerp(m_RaiseFoot0, foot, blend);
            var tip = Vector3.Lerp(m_RaiseTop0, top, blend);
            m_Up = (tip - m_Foot).normalized;
            SetPose(m_Foot, m_Up);

            // the footer braces the foot; the pusher walks in under the rails where they're at hand height
            var toWall = -fwd;
            var flatUp = m_Up;
            flatUp.y = 0f;
            float slope = m_Up.y;
            float wantPusher = slope < 0.12f ? m_PusherAlong : Mathf.Clamp((Hands - footY) / slope, 0.8f, m_Length - 1f);
            m_PusherAlong = Mathf.MoveTowards(m_PusherAlong, wantPusher, 3.5f * dt);
            var pusher = m_Carriers[0];
            pusher.carryHeight = 1f;
            pusher.Place(m_Foot + flatUp * m_PusherAlong, toWall);
            if (m_Carriers.Count > 1)
            {
                m_FooterAlong = Mathf.MoveTowards(m_FooterAlong, -0.55f, 3.5f * dt);
                var footer = m_Carriers[1];
                footer.carryHeight = 0.15f;
                footer.Place(m_Foot + flatUp.normalized * m_FooterAlong, toWall);
            }
            if (r >= 1f) FinishRaise();
        }

        void FinishRaise()
        {
            m_Phase = Phase.Raised;
            var edge = m_Foot + m_Up * ((m_TopY - m_GroundY) / Mathf.Max(0.2f, m_Up.y));
            Sfx.Play(Sound.Thud, edge, 0.6f, 0.1f);
            VFX.StoneChips(edge, m_Raid.GateForward, 0.4f);
            VFX.Dust(m_Foot, 0.4f);
            // both carriers go up: the pusher first, the footer right behind him
            for (int i = 0; i < m_Carriers.Count; i++)
            {
                m_Carriers[i].carryHeight = 1f;
                AddClimber(m_Carriers[i], i * 1.1f, true);
            }
            m_Carriers.Clear();
        }

        // ---------------------------------------------------------------- climbing
        // The ladder's face on the climbers' side.
        Vector3 Normal
        {
            get
            {
                var f = m_Raid.GateForward;
                var n = f - m_Up * Vector3.Dot(f, m_Up);
                return n.sqrMagnitude > 1e-4f ? n.normalized : f;
            }
        }

        // Toes on the rungs, hands on the rails: the root hangs off the ladder face, the body pitched in.
        Vector3 ClimbPoint(float along) => m_Foot + m_Up * along + Normal * ClimbOff;

        // Bottom of the climb, level with the foot of the ladder.
        Vector3 FootStand => ClimbPoint(-ClimbOff * Normal.y / Mathf.Max(0.2f, m_Up.y));

        void AddClimber(Soldier s, float delay, bool nearFoot)
        {
            s.followGround = true;
            if (nearFoot)
            {
                s.SetState(Soldier.State.March);
                s.MoveTo(m_Foot + m_Raid.GateForward * 1.3f, m_Raid.marchSpeed);
            }
            else
            {
                // a volunteer waits where he is, out on the rally line, until the ladder is clear
                s.Stop();
                s.Face(-m_Raid.GateForward);
            }
            m_Climbers.Add(new Climber { s = s, wait = delay, speed = Random.Range(0.85f, 1.1f) });
        }

        // Stages: 0 wait for a clear ladder, 1 walk to the foot, 2 climb, 3 over the parapet.
        void UpdateClimbers(float dt)
        {
            if (m_Phase != Phase.Raised) return;
            var toWall = -m_Raid.GateForward;
            float start = -ClimbOff * Normal.y / Mathf.Max(0.2f, m_Up.y);
            for (int i = 0; i < m_Climbers.Count; i++)
            {
                var c = m_Climbers[i];
                if (!c.s || !c.s.Alive)
                {
                    // a shot climber falls off on his own (Soldier topples with physics)
                    m_Climbers.RemoveAt(i--);
                    continue;
                }
                c.t += dt;
                switch (c.stage)
                {
                    case 0:
                    {
                        if (c.wait > 0f) c.wait -= dt;
                        bool clear = c.wait <= 0f;
                        for (int j = 0; j < i; j++)
                            if (m_Climbers[j].stage < 2 || m_Climbers[j].along < 1.8f) clear = false;
                        if (clear)
                        {
                            c.stage = 1;
                            c.t = 0f;
                            c.s.SetState(Soldier.State.March);
                            c.s.MoveTo(FootStand, m_Raid.marchSpeed * 1.4f);
                        }
                        else if (c.s.Arrived) c.s.Face(toWall);
                        break;
                    }
                    case 1:
                        if (c.s.Arrived || WaveManager.FlatDistance(c.s.transform.position, FootStand) < 0.4f || c.t > 6f)
                        {
                            c.stage = 2;
                            c.along = start;
                            c.s.Stop();
                            c.s.followGround = false;
                            c.s.bodyPitch = Pitch;
                            c.s.SetState(Soldier.State.Climb);
                        }
                        break;
                    case 2:
                    {
                        // keep a body length below the man above
                        float cap = m_ClimbTop;
                        for (int j = 0; j < i; j++)
                            if (m_Climbers[j].stage == 2) cap = Mathf.Min(cap, m_Climbers[j].along - 1.7f);
                        c.along = Mathf.Max(c.along, Mathf.Min(c.along + ClimbSpeed * c.speed * dt, cap));
                        c.s.Place(ClimbPoint(c.along), toWall);
                        if (c.along >= m_ClimbTop - 1e-3f)
                        {
                            c.stage = 3;
                            c.t = 0f;
                            c.from = c.s.transform.position;
                        }
                        break;
                    }
                    case 3:
                    {
                        // a leg over the parapet and down onto the wall walk behind it
                        float k = Mathf.Clamp01(c.t / 0.8f);
                        float arc = Mathf.Max(0.6f, m_TopY + 0.3f - (c.from.y + m_Walk.y) * 0.5f);
                        var p = Vector3.Lerp(c.from, m_Walk, Ease.InOutCubic(k)) + Vector3.up * Mathf.Sin(k * Mathf.PI) * arc;
                        c.s.bodyPitch = Mathf.Lerp(Pitch, 0f, k);
                        c.s.Place(p, toWall);
                        if (k >= 1f)
                        {
                            c.s.bodyPitch = 0f;
                            c.s.Cheer(1.5f);
                            m_Raid.Infiltrate(c.s);
                            c.s.Vanish(0.8f);
                            m_Climbers.RemoveAt(i--);
                        }
                        break;
                    }
                }
            }
        }

        // ---------------------------------------------------------------- failure
        void ReleaseCarriers()
        {
            foreach (var c in m_Carriers) if (c && c.Alive) m_Raid.Release(c);
            m_Carriers.Clear();
        }

        void Fall()
        {
            if (m_Phase == Phase.Fallen || m_Phase == Phase.Raised) return;
            bool againstWall = m_Phase == Phase.Raise;
            m_Phase = Phase.Fallen;
            StartCoroutine(Topple(againstWall));
        }

        IEnumerator Topple(bool againstWall)
        {
            var fromPos = transform.position;
            var fromRot = transform.rotation;
            var flat = transform.up;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f) flat = m_Raid ? -m_Raid.GateForward : Vector3.forward;
            flat.Normalize();
            var foot = againstWall && m_Raid ? m_Raid.GatePoint(m_X, m_Length + 0.1f) : fromPos;
            foot = WaveManager.Ground(foot);
            foot.y += 0.06f;
            var toRot = Quaternion.LookRotation(Vector3.down, flat);
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                float k = t / 0.5f;
                k *= k;
                transform.SetPositionAndRotation(Vector3.Lerp(fromPos, foot, k), Quaternion.Slerp(fromRot, toRot, k));
                yield return null;
            }
            transform.SetPositionAndRotation(foot, toRot);
            var mid = foot + flat * m_Length * 0.5f;
            Sfx.Play(Sound.Thud, mid, 0.5f, 0.15f);
            VFX.Dust(mid, 0.6f);
        }
    }
}
