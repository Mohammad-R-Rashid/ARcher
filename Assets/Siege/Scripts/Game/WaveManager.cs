using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Siege
{
    // The enemy raid. Waves pour out of the siege camp in staggered groups, cross the floor in loose ranks and
    // storm the castle gate: foot soldiers gather on a rally line out where the keep can see them and take
    // turns at the gate (a few blows each, a handful of men at a time, since right under the wall they are in
    // dead ground), ladder teams scale the wall beside the arch (SiegeLadder), the ram team batters the doors
    // (BatteringRam). Gate surfaces come from probing the real masonry. The gate falling, or enough men getting
    // over the wall, is a breach; beating every wave wins.
    public class WaveManager : MonoBehaviour
    {
        public Soldier soldierPrefab;
        public Soldier spearmanPrefab;            // optional, mixed into the ranks
        public GameObject ladderPrefab, ramPrefab;
        public Transform camp;
        public int WaveCount = 1;   // one intense raid; beat it and you win

        [Header("Pacing (game metres, seconds)")]
        public float marchSpeed = 3.6f;
        public float chargeSpeed = 6.8f;
        public float chargeDistance = 14f;        // break into a run this far from the gate
        public float carrySpeed = 2.9f;           // ladder and ram teams
        public float firstWaveDelay = 0.5f;
        public float breather = 3f;
        public float maxWaveTime = 100f;          // the next wave comes anyway after this
        public int breachInfiltrators = 6;

        [Header("Gate assault")]
        public int maxHackers = 4;                // men at the gate face at once; the rest wait on the rally line
        public float rallyDistance = 4.6f;        // out from the gate face, clear of the wall's dead ground
        public float hackStandoff = 0.7f;         // a swordsman's reach from the masonry (spearmen stand further back)

        public int Wave { get; private set; }
        public int Infiltrated { get; private set; }
        public bool Breached { get; private set; }
        public bool Running => m_Running;

        public int AliveCount
        {
            get
            {
                int n = 0;
                foreach (var s in m_Alive) if (s && s.Alive) n++;
                return n;
            }
        }

        public IEnumerable<Soldier> Soldiers
        {
            get
            {
                for (int i = 0; i < m_Alive.Count; i++)
                {
                    var s = m_Alive[i];
                    if (s && s.Alive) yield return s;
                }
            }
        }

        public event Action<int> WaveStarted;
        public event Action AllWavesCleared;
        public event Action GateBreached;

        // Gate frame for the squads: the gate's outer face at its base, outward = toward the enemy.
        public Vector3 GateFace => m_Face;
        public Vector3 GateForward => m_Fwd;
        public Vector3 GateRight => m_Right;
        public float GateHalfWidth => m_HalfW;
        public float GateHeight => m_Height;
        public float GateDepth => m_Depth;
        public bool GateStanding => !m_Gate || m_Gate.Alive;
        public bool Over => Breached || !m_Castle || m_Castle.Fallen;

        public bool RamLaneBusy
        {
            get
            {
                foreach (var r in m_Rams) if (r && r.Committed) return true;
                return false;
            }
        }

        class Slot { public float x, surface; public bool centre; public Soldier owner; }

        const int CastleMask = 1 << Layers.Castle;
        static readonly float[] s_WallProbe = { 0.12f, 0.3f, 0.5f, 0.7f };   // fractions of the gate height

        Castle m_Castle;
        CastlePiece m_Gate;
        Vector3 m_Face, m_Fwd = Vector3.forward, m_Right = Vector3.right, m_CampPos;
        float m_HalfW = 3.5f, m_Height = 5.5f, m_Depth = 3f, m_LastHackSound;
        bool m_Running, m_HasCamp;
        readonly List<Slot> m_Slots = new List<Slot>();
        readonly List<float> m_LadderSpots = new List<float>();
        readonly List<float> m_Free = new List<float>();
        readonly Dictionary<Soldier, int> m_Strikes = new Dictionary<Soldier, int>();
        readonly List<Soldier> m_Spawned = new List<Soldier>();
        readonly List<Soldier> m_Alive = new List<Soldier>();
        readonly Dictionary<Soldier, Coroutine> m_Brains = new Dictionary<Soldier, Coroutine>();
        readonly List<SiegeLadder> m_Ladders = new List<SiegeLadder>();
        readonly List<BatteringRam> m_Rams = new List<BatteringRam>();

        // ---------------------------------------------------------------- lifecycle
        public void Begin(Castle castle, Transform campTransform)
        {
            Stop();
            DestroyLeftovers();
            m_Castle = castle;
            if (campTransform) camp = campTransform;
            m_HasCamp = false;
            Wave = 0;
            Infiltrated = 0;
            Breached = false;
            if (!castle || !soldierPrefab)
            {
                Debug.LogWarning("[Siege] WaveManager.Begin needs a castle and a soldier prefab");
                return;
            }
            m_Gate = null;
            foreach (var p in castle.pieces)
                if (p && p.kind == PieceKind.Gate) { m_Gate = p; break; }
            MeasureGate();
            HangDoor();
            FindLadderSpots();
            BuildSlots();
            m_Running = true;
            StartCoroutine(RaidLoop());
        }

        // Halts the raid (no more spawns, no more events); survivors stand where they are.
        public void Stop()
        {
            m_Running = false;
            StopAllCoroutines();
            m_Brains.Clear();
            m_Strikes.Clear();
            foreach (var sl in m_Slots) sl.owner = null;
            foreach (var l in m_Ladders) if (l) l.Halt();
            foreach (var r in m_Rams) if (r) r.Halt();
            foreach (var s in m_Alive)
                if (s && s.Alive) { s.Stop(); s.SetState(Soldier.State.Idle); }
        }

        void DestroyLeftovers()
        {
            foreach (var s in m_Spawned) if (s) Destroy(s.gameObject);
            foreach (var l in m_Ladders) if (l) Destroy(l.gameObject);
            foreach (var r in m_Rams) if (r) Destroy(r.gameObject);
            m_Spawned.Clear();
            m_Alive.Clear();
            m_Ladders.Clear();
            m_Rams.Clear();
        }

        void Update()
        {
            if (!m_Running) return;
            if (!m_Castle) { Stop(); return; }   // the round was torn down under us
            m_Alive.RemoveAll(s => !s || !s.Alive);
            m_Spawned.RemoveAll(s => !s);
            if (!Tune.DemoNoLoss && !Breached && ((m_Gate && !m_Gate.Alive) || Infiltrated >= breachInfiltrators)) Breach();
        }

        void Breach()
        {
            Breached = true;
            // men over the wall throw the gate open from inside
            if (m_Gate && m_Gate.Alive)
                m_Gate.TakeHit(new HitInfo { damage = m_Gate.Health + 1f, point = GatePoint(0f, 0f, 2f), direction = -m_Fwd, force = 10f, source = HitSource.Fireball });
            foreach (var l in m_Ladders) if (l) l.OnBreach();
            foreach (var r in m_Rams) if (r) r.OnBreach();
            GateBreached?.Invoke();
        }

        // ---------------------------------------------------------------- gate geometry
        void MeasureGate()
        {
            var fwd = m_Castle.transform.forward;
            fwd.y = 0f;
            m_Fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            m_Right = Vector3.Cross(Vector3.up, m_Fwd);
            if (!m_Gate)
            {
                m_HalfW = 3.5f; m_Depth = 3f; m_Height = 5.5f;
                m_Face = m_Castle.transform.position + m_Fwd * 7.6f;
                return;
            }
            // Tight box in the castle's frame from the gate mesh's local bounds.
            var origin = m_Gate.transform.position;
            float minR = float.MaxValue, maxR = float.MinValue, minF = float.MaxValue, maxF = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            void Extend(Vector3 w)
            {
                var d = w - origin;
                float r = Vector3.Dot(d, m_Right), f = Vector3.Dot(d, m_Fwd);
                minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
                minF = Mathf.Min(minF, f); maxF = Mathf.Max(maxF, f);
                minY = Mathf.Min(minY, w.y); maxY = Mathf.Max(maxY, w.y);
            }
            var mf = m_Gate.GetComponentInChildren<MeshFilter>();
            bool local = mf && mf.sharedMesh;
            var b = local ? mf.sharedMesh.bounds : m_Gate.WorldBounds;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Extend(local ? mf.transform.TransformPoint(c) : c);
            }
            m_HalfW = Mathf.Max(1f, (maxR - minR) * 0.5f);
            m_Depth = Mathf.Max(0.5f, maxF - minF);
            m_Height = Mathf.Max(2f, maxY - minY);
            m_Face = origin + m_Right * ((minR + maxR) * 0.5f) + m_Fwd * maxF;
            m_Face.y = minY;
        }

        public Vector3 GatePoint(float x, float outward, float up = 0f) => m_Face + m_Right * x + m_Fwd * outward + Vector3.up * up;

        // The real surface of the gate at (x, up above its base), as an outward offset from the face plane
        // (negative = behind it: the wall is battered and the bounds' front is the arch ring). NaN where the
        // probe passes through: an open arch, a crenel, past the end of the wall.
        public float SurfaceOut(float x, float up)
        {
            if (!m_Gate) return float.NaN;
            if (Physics.Raycast(GatePoint(x, 4f, up), -m_Fwd, out var hit, 4f + m_Depth, CastleMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<CastlePiece>() == m_Gate)
                return 4f - hit.distance;
            return float.NaN;
        }

        // The top front edge of the wall at x (height above the gate base, outward offset) and a point on the
        // wall walk behind it, for ladders.
        public void Parapet(float x, out float top, out float edgeOut, out Vector3 walk)
        {
            top = m_Height;
            edgeOut = 0f;
            bool found = false;
            for (float h = m_Height * 0.4f; h <= m_Height + 0.5f; h += 0.1f)
            {
                float o = SurfaceOut(x, h);
                if (float.IsNaN(o)) { if (found) break; continue; }
                found = true;
                top = h;
                edgeOut = o;
            }
            walk = GatePoint(x, edgeOut - 0.65f, top - 0.9f);
            if (m_Gate && Physics.Raycast(GatePoint(x, edgeOut - 0.65f, top + 2f), Vector3.down, out var hit, 4f, CastleMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<CastlePiece>() == m_Gate)
                walk = hit.point;
        }

        // The Kingdom kit's gate is an open archway: hang doors in it so the ram and the axes have something
        // to batter (a modelled door closing the arch makes this a no-op).
        void HangDoor()
        {
            if (!m_Gate || m_Gate.GetComponentInChildren<GateDoor>() || !float.IsNaN(SurfaceOut(0f, 1.2f))) return;
            if (GateDoor.Hang(m_Gate, m_Face, m_Fwd, m_Right, 0.25f))
            {
                Physics.SyncTransforms();
                return;
            }
            Debug.LogWarning("[Siege] The gate arch is open and no door could be fitted: the ram and the men in the arch strike at its mouth");
        }

        // Ladder positions: plain stretches of wall each side of the arch (no arch ring or buttress standing
        // proud of it, no gaps) wide enough for a ladder, one per 1.3 m.
        void FindLadderSpots()
        {
            m_LadderSpots.Clear();
            if (m_Gate)
            {
                int n = Mathf.FloorToInt(m_HalfW / 0.1f);
                var back = new float[s_WallProbe.Length];
                for (int k = 0; k < back.Length; k++)
                {
                    back[k] = float.MaxValue;
                    for (int i = -n; i <= n; i++)
                    {
                        float o = SurfaceOut(i * 0.1f, s_WallProbe[k] * m_Height);
                        if (!float.IsNaN(o)) back[k] = Mathf.Min(back[k], o);
                    }
                }
                bool Plain(float x)
                {
                    for (int k = 0; k < back.Length; k++)
                        for (int j = -1; j <= 1; j++)
                        {
                            float o = SurfaceOut(x + j * 0.42f, s_WallProbe[k] * m_Height);
                            if (float.IsNaN(o) || o > back[k] + 0.25f) return false;
                        }
                    return true;
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    float runA = 0f, runB = -1f, a = float.NaN;
                    for (int i = 6; i <= n; i++)
                    {
                        float x = i * 0.1f;
                        bool ok = Plain(side * x);
                        if (ok && float.IsNaN(a)) a = x;
                        if ((!ok || i == n) && !float.IsNaN(a))
                        {
                            float b = ok ? x : x - 0.1f;
                            if (b - a > runB - runA) { runA = a; runB = b; }
                            a = float.NaN;
                        }
                    }
                    if (runB < runA) continue;
                    int count = 1 + Mathf.FloorToInt((runB - runA) / 1.3f);
                    for (int c = 0; c < count; c++)
                        m_LadderSpots.Add(side * (count == 1 ? (runA + runB) * 0.5f : Mathf.Lerp(runA, runB, c / (count - 1f))));
                }
            }
            if (m_LadderSpots.Count == 0)
            {
                float x = Mathf.Max(1.6f, m_HalfW * 0.62f);
                m_LadderSpots.Add(-x);
                m_LadderSpots.Add(x);
            }
        }

        // One row of places along the gate front, a weapon's reach from the masonry (or the doors), each with a
        // clear lane back out to the rally line. The middle is the ram's lane.
        void BuildSlots()
        {
            m_Slots.Clear();
            float w = Mathf.Max(0.5f, m_HalfW - 0.5f);
            const float gap = 0.85f;
            int n = Mathf.Max(1, Mathf.FloorToInt(2f * w / gap) + 1);
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) * 0.5f) * gap;
                float o = SurfaceOut(x, 1.2f);
                if (float.IsNaN(o) && !m_Gate) o = 0f;
                if (float.IsNaN(o) || !LaneClear(x, o + hackStandoff, rallyDistance)) continue;
                m_Slots.Add(new Slot { x = x, surface = o, centre = Mathf.Abs(x) < 1.3f });
            }
        }

        Vector3 SlotPoint(Slot sl, Soldier s) => Ground(GatePoint(sl.x, sl.surface + (s && s.Spearman ? 1.25f : hackStandoff)));

        // Not in the ram's lane while it's at work, not under a ladder.
        bool SlotOpen(Slot sl)
        {
            if (sl.centre && RamLaneBusy) return false;
            foreach (var l in m_Ladders) if (l && !l.Fallen && Mathf.Abs(l.Offset - sl.x) < 0.75f) return false;
            return true;
        }

        int Hackers
        {
            get
            {
                int n = 0;
                foreach (var sl in m_Slots) if (sl.owner && sl.owner.Alive) n++;
                return n;
            }
        }

        // A turn at the gate, if fewer than maxHackers are at it: the free place nearest along the line.
        Slot ClaimSlot(Soldier s)
        {
            if (Hackers >= maxHackers) return null;
            Slot best = null;
            float bestScore = float.MaxValue;
            float x = Lateral(s.transform.position);
            foreach (var sl in m_Slots)
            {
                if ((sl.owner && sl.owner.Alive) || !SlotOpen(sl)) continue;
                float score = Mathf.Abs(sl.x - x) + Random.value;
                if (score < bestScore) { bestScore = score; best = sl; }
            }
            if (best != null) best.owner = s;
            return best;
        }

        void FreeSlot(Soldier s)
        {
            foreach (var sl in m_Slots) if (sl.owner == s) sl.owner = null;
        }

        float Lateral(Vector3 p) => Vector3.Dot(p - m_Face, m_Right);

        // A place on the rally line: out of the wall's dead ground (the keep sees, and shoots, the men waiting
        // for a turn), off the ram's lane, not on a barrel or crate.
        Vector3 RallyPoint(float nearX = float.NaN)
        {
            float w = m_HalfW + 0.6f;
            var p = GatePoint(0f, rallyDistance);
            for (int i = 0; i < 8; i++)
            {
                float x = float.IsNaN(nearX) ? Random.Range(-w, w) : Mathf.Clamp(nearX + Random.Range(-1f, 1f), -w, w);
                if (RamLaneBusy && Mathf.Abs(x) < 1.5f) x = (x < 0f ? -1f : 1f) * Random.Range(1.5f, w);
                p = Ground(GatePoint(x, rallyDistance + Random.Range(0f, 0.7f)));
                if (Clear(p)) break;
            }
            return p;
        }

        bool OnRallyLine(Soldier s)
        {
            float o = Vector3.Dot(s.transform.position - m_Face, m_Fwd);
            return o > rallyDistance - 0.6f && o < rallyDistance + 1.6f && Mathf.Abs(Lateral(s.transform.position)) < m_HalfW + 1f;
        }

        // Flat footing: no barrel, crate or ledge under a man standing here.
        bool Clear(Vector3 p)
        {
            float floor = m_Face.y + 0.4f;
            if (Ground(p).y > floor) return false;
            for (int i = 0; i < 4; i++)
            {
                var d = (i < 2 ? m_Right : m_Fwd) * (i % 2 == 0 ? 0.45f : -0.45f);
                if (Ground(p + d).y > floor) return false;
            }
            return true;
        }

        // Nothing to trip over walking straight out from the gate between two offsets.
        bool LaneClear(float x, float from, float to)
        {
            float floor = m_Face.y + 0.4f;
            for (float o = from; o <= to; o += 0.4f)
                for (int j = -1; j <= 1; j++)
                    if (Ground(GatePoint(x + j * 0.3f, o)).y > floor) return false;
            return true;
        }

        public static Vector3 Ground(Vector3 p, float above = 4f)
        {
            if (Physics.Raycast(p + Vector3.up * above, Vector3.down, out var hit, above + 10f, 1 << Layers.Environment, QueryTriggerInteraction.Ignore))
                p.y = hit.point.y;
            return p;
        }

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ---------------------------------------------------------------- waves
        IEnumerator RaidLoop()
        {
            yield return new WaitForSeconds(firstWaveDelay);
            int count = Mathf.Max(1, WaveCount);
            for (int w = 1; w <= count; w++)
            {
                if (Over) yield break;
                Wave = w;
                MeasureGate();
                WaveStarted?.Invoke(w);
                float started = Time.time;
                yield return SpawnWave(w);
                while (!Over && AliveCount > 0 && Time.time - started < maxWaveTime) yield return null;
                if (Over) yield break;
                if (w < count) yield return new WaitForSeconds(breather);
            }
            while (!Over && AliveCount > 0) yield return null;
            if (!Over && m_Running) AllWavesCleared?.Invoke();
        }

        // Vanguard first, the siege teams in the middle of the column, stragglers last.
        IEnumerator SpawnWave(int w)
        {
            // Wave 1 is a quick two-man probe; then the raid escalates fast.
            int foot = WaveCount == 1 ? 30 : w == 1 ? 2 : w == 2 ? 8 : 12 + (w - 3) * 3;
            int ladders = WaveCount == 1 ? 4 : w == 1 ? 0 : w == 2 ? 1 : 2;
            bool ram = w >= 2 || WaveCount == 1;
            var plan = new List<int>();
            int vanguard = Mathf.CeilToInt(foot * 0.45f);
            for (int i = 0; i < vanguard; i++) plan.Add(0);
            for (int i = 0; i < ladders; i++) plan.Add(1);
            if (ram) plan.Insert(Mathf.Min(plan.Count, vanguard + 1), 2);
            for (int i = vanguard; i < foot; i++) plan.Add(0);

            foreach (var kind in plan)
            {
                if (!m_Running || Over) yield break;
                switch (kind)
                {
                    case 0:
                        SpawnFoot();
                        // little clusters: sometimes two step out together
                        yield return new WaitForSeconds(Random.value < 0.3f ? 0.1f : Random.Range(0.2f, 0.45f));
                        break;
                    case 1:
                        yield return new WaitForSeconds(0.5f);
                        SpawnLadderTeam();
                        yield return new WaitForSeconds(1.2f);
                        break;
                    default:
                        yield return new WaitForSeconds(0.5f);
                        SpawnRamTeam();
                        yield return new WaitForSeconds(1.6f);
                        break;
                }
            }
        }

        Vector3 CampPoint()
        {
            if (camp)
            {
                var sp = camp.Find("SpawnPoint");
                if (sp) m_CampPos = sp.position;
                else
                {
                    var toGate = m_Face - camp.position;
                    toGate.y = 0f;
                    m_CampPos = camp.position + (toGate.sqrMagnitude > 1f ? toGate.normalized : -m_Fwd) * 8f;
                }
                m_HasCamp = true;
            }
            else if (!m_HasCamp)
            {
                m_CampPos = Ground(m_Face + m_Fwd * 50f, 20f);
                m_HasCamp = true;
            }
            return m_CampPos;
        }

        Vector3 SpawnPoint(float spread)
        {
            var c = CampPoint();
            var dir = m_Face - c;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1f ? dir.normalized : -m_Fwd;
            var lat = Vector3.Cross(Vector3.up, dir);
            var p = Ground(c + lat * Random.Range(-spread, spread) + dir * Random.Range(-3f, 2f));
            // LiDAR: never inside a wall or furniture - nearest open floor that can reach the gate.
            var grid = NavGrid.Current;
            // Only ever out in front of the castle, at least about half way to the camp - never beside or behind the player.
            float minAhead = Vector3.Dot(c - m_Face, m_Fwd) * 0.5f;
            if (grid != null && !grid.AreaOpen(p, 1f) && grid.TryOpenSpot(p, 30f, 1.5f, out var open, q => Vector3.Dot(q - m_Face, m_Fwd) > minAhead)) p = Ground(open);
            return p;
        }

        Soldier SpawnSoldier(Soldier prefab, Vector3 pos)
        {
            var face = m_Face - pos;
            face.y = 0f;
            var s = Instantiate(prefab, pos, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face.normalized : -m_Fwd));
            s.name = "Soldier";
            s.Died += OnDied;
            s.Struck += OnStruck;
            s.Appear(0.3f);
            m_Spawned.Add(s);
            m_Alive.Add(s);
            return s;
        }

        void SpawnFoot()
        {
            var prefab = spearmanPrefab && Random.value < 0.35f ? spearmanPrefab : soldierPrefab;
            var s = SpawnSoldier(prefab, SpawnPoint(8f));
            StartBrain(s, FootBrain(s, true));
        }

        void SpawnLadderTeam()
        {
            float x = ladderPrefab ? FreeLadderSpot() : float.NaN;
            if (float.IsNaN(x))
            {
                // no room at the wall for another ladder: these two go up one that's already standing
                for (int i = 0; i < 2; i++)
                {
                    var s = SpawnSoldier(soldierPrefab, SpawnPoint(2.5f));
                    StartBrain(s, FootBrain(s, true, true));
                }
                return;
            }
            var pos = SpawnPoint(2.5f);
            var crew = new[] { SpawnSoldier(soldierPrefab, pos), SpawnSoldier(soldierPrefab, pos) };
            var go = Instantiate(ladderPrefab, pos, Quaternion.identity);
            go.name = "Ladder";
            var ladder = go.AddComponent<SiegeLadder>();
            m_Ladders.Add(ladder);
            ladder.Begin(this, crew, x);
        }

        // A ladder spot with no standing (or incoming) ladder within 1.2 m; NaN when the wall is full.
        float FreeLadderSpot()
        {
            m_Free.Clear();
            foreach (var spot in m_LadderSpots)
            {
                bool taken = false;
                foreach (var l in m_Ladders) taken |= l && !l.Fallen && Mathf.Abs(l.Offset - spot) < 1.2f;
                if (!taken) m_Free.Add(spot);
            }
            return m_Free.Count > 0 ? m_Free[Random.Range(0, m_Free.Count)] : float.NaN;
        }

        void SpawnRamTeam()
        {
            // One ram at the gate at a time: a working one takes these men as its reserve, one the last crew
            // dropped is picked up again rather than a second log landing on top of it.
            BatteringRam live = null, spare = null;
            foreach (var r in m_Rams)
            {
                if (!r) continue;
                if (r.Abandoned) spare = r; else live = r;
            }
            var existing = live ? live : spare;
            if (!existing && !ramPrefab) { for (int i = 0; i < 4; i++) SpawnFoot(); return; }
            var pos = SpawnPoint(1.5f);
            var crew = new Soldier[4];
            for (int i = 0; i < 4; i++) crew[i] = SpawnSoldier(soldierPrefab, pos);
            if (existing)
            {
                foreach (var s in crew) if (!existing.Enlist(s)) StartBrain(s, FootBrain(s, true));
                return;
            }
            var go = Instantiate(ramPrefab, pos, Quaternion.identity);
            go.name = "Ram";
            var ram = go.AddComponent<BatteringRam>();
            m_Rams.Add(ram);
            ram.Begin(this, crew);
        }

        // ---------------------------------------------------------------- foot soldiers
        void StartBrain(Soldier s, IEnumerator brain)
        {
            StopBrain(s);
            // a brain can run straight through to handing him to a ladder before StartCoroutine returns
            m_Brains[s] = null;
            var c = StartCoroutine(brain);
            if (m_Brains.ContainsKey(s)) m_Brains[s] = c;
        }

        void StopBrain(Soldier s)
        {
            if (m_Brains.TryGetValue(s, out var c))
            {
                m_Brains.Remove(s);
                if (c != null) StopCoroutine(c);
            }
            m_Strikes.Remove(s);
            FreeSlot(s);
        }

        // Long legs go round real furniture: the LiDAR NavGrid supplies waypoints, then the last leg is direct.
        IEnumerator Walk(Soldier s, Vector3 target, float speed, float within, float timeout = -1f)
        {
            if (!s || !s.Alive) yield break;
            var grid = NavGrid.Current;
            if (grid != null && FlatDistance(s.transform.position, target) > 6f)
            {
                var pts = grid.FindPath(s.transform.position, target);
                for (int k = 0; k < pts.Count - 1; k++)
                {
                    yield return WalkDirect(s, Ground(pts[k]), speed, 1.5f, -1f);
                    if (!s || !s.Alive) yield break;
                }
            }
            yield return WalkDirect(s, target, speed, within, timeout);
        }

        IEnumerator WalkDirect(Soldier s, Vector3 target, float speed, float within, float timeout)
        {
            if (!s || !s.Alive) yield break;
            s.MoveTo(target, speed);
            float limit = timeout > 0f ? timeout : FlatDistance(s.transform.position, target) / Mathf.Max(0.5f, speed) * 1.8f + 4f;
            for (float t = 0f; t < limit; t += Time.deltaTime)
            {
                if (!s || !s.Alive) yield break;
                if (s.Arrived || FlatDistance(s.transform.position, target) <= within) yield break;
                yield return null;
            }
        }

        IEnumerator FootBrain(Soldier s, bool march, bool ladderBound = false)
        {
            if (march && s)
            {
                // straight across the floor, weaving a little, breaking into a charge near the gate
                var start = s.transform.position;
                var stage = RallyPoint();
                var path = stage - start;
                path.y = 0f;
                float len = path.magnitude;
                var dir = len > 0.1f ? path / len : -m_Fwd;
                var lat = Vector3.Cross(Vector3.up, dir);
                s.SetState(Soldier.State.March);
                if (len > chargeDistance + 6f)
                {
                    var weave = Vector3.Lerp(start, stage, Random.Range(0.3f, 0.5f)) + lat * Random.Range(-9f, 9f);   // a wide, loose front
                    yield return Walk(s, weave, marchSpeed, 2.5f);
                    var charge = stage - dir * chargeDistance + lat * Random.Range(-4f, 4f);
                    yield return Walk(s, charge, marchSpeed, 2f);
                }
                yield return Walk(s, stage, chargeSpeed, 1.8f);
            }
            float rest = 0f;
            while (s && s.Alive)
            {
                if (Over) { yield return Storm(s); yield break; }
                // climbers wanted on a raised ladder
                var ladder = OpenLadder();
                if (ladder && Random.value < (ladderBound ? 1f : 0.4f) && ladder.Join(s)) { m_Brains.Remove(s); yield break; }
                var slot = Time.time >= rest ? ClaimSlot(s) : null;
                if (slot != null)
                {
                    // his turn: in along the slot's lane, a few blows at the gate, then back out for the next man
                    s.SetState(Soldier.State.March);
                    var lane = Ground(GatePoint(slot.x, rallyDistance));
                    if (FlatDistance(s.transform.position, lane) > 1.2f) yield return Walk(s, lane, marchSpeed, 0.8f, 6f);
                    if (s && s.Alive && slot.owner == s && !Over && SlotOpen(slot))
                        yield return Walk(s, SlotPoint(slot, s), Mathf.Max(marchSpeed, chargeSpeed * 0.7f), 0.3f, 8f);
                    if (!s || !s.Alive) yield break;
                    if (slot.owner == s && !Over && SlotOpen(slot))
                    {
                        s.Stop();
                        s.Face(-m_Fwd);
                        s.SetState(Soldier.State.Attack);
                        m_Strikes[s] = 0;
                        int blows = Random.Range(2, 4);
                        float until = Time.time + blows * 1.6f + 1f;
                        while (s && s.Alive && slot.owner == s && !Over && GateStanding && SlotOpen(slot) && Strikes(s) < blows && Time.time < until)
                            yield return null;
                    }
                    if (slot.owner == s) slot.owner = null;
                    if (!s || !s.Alive) yield break;
                    if (Over) continue;
                    s.SetState(Soldier.State.March);
                    yield return Walk(s, RallyPoint(slot.x), marchSpeed, 0.6f, 8f);
                    if (!s || !s.Alive) yield break;
                    s.Stop();
                    s.Face(-m_Fwd);
                    if (Random.value < 0.4f) s.Cheer(Random.Range(0.8f, 1.5f));
                    rest = Time.time + Random.Range(1.5f, 3.5f);
                }
                else
                {
                    // waiting for a turn: hold the rally line, jeering, shuffling along it now and then
                    if (!OnRallyLine(s) || Random.value < 0.2f)
                    {
                        s.SetState(Soldier.State.March);
                        yield return Walk(s, RallyPoint(Lateral(s.transform.position)), marchSpeed, 0.6f, 8f);
                        if (!s || !s.Alive) yield break;
                    }
                    s.Stop();
                    s.Face(-m_Fwd);
                    if (Random.value < 0.3f) s.Cheer(Random.Range(1f, 1.8f));
                    yield return new WaitForSeconds(Random.Range(0.6f, 1.6f));
                }
                yield return null;
            }
        }

        int Strikes(Soldier s) => m_Strikes.TryGetValue(s, out int n) ? n : 0;

        // The gate is down (or the castle has fallen): cheer, then pour through the gap into the courtyard.
        IEnumerator Storm(Soldier s)
        {
            FreeSlot(s);
            if (!s || !s.Alive) yield break;
            s.Stop();
            s.Face(-m_Fwd);
            s.Cheer(Random.Range(1.2f, 2.2f));
            yield return new WaitForSeconds(Random.Range(0.8f, 2f));
            for (float t = 0f; t < 30f && s && s.Alive && GateStanding && m_Gate; t += Time.deltaTime) yield return null;
            if (!s || !s.Alive) yield break;
            if (!GateStanding)
            {
                s.SetState(Soldier.State.March);
                yield return Walk(s, GatePoint(Random.Range(-1.2f, 1.2f), 1.2f), chargeSpeed, 1f);
                yield return Walk(s, GatePoint(Random.Range(-0.8f, 0.8f), -m_Depth - 1.2f), chargeSpeed, 1f);
                yield return Walk(s, GatePoint(Random.Range(-m_HalfW, m_HalfW) * 0.8f, -m_Depth - Random.Range(3f, 7f)), marchSpeed, 0.6f);
            }
            while (s && s.Alive)
            {
                s.Stop();
                s.Cheer(Random.Range(1.5f, 3f));
                yield return new WaitForSeconds(Random.Range(2f, 4.5f));
            }
        }

        SiegeLadder OpenLadder()
        {
            foreach (var l in m_Ladders) if (l && l.CanJoin) return l;
            return null;
        }

        void OnDied(Soldier s)
        {
            m_Alive.Remove(s);
            StopBrain(s);
        }

        // A hack lands on the gate (Soldier.Struck while attacking), on the real masonry or doors in front of him.
        void OnStruck(Soldier s)
        {
            if (!m_Running || Breached || !m_Gate || !m_Gate.Alive || !s) return;
            var rel = s.transform.position - m_Face;
            float x = Mathf.Clamp(Vector3.Dot(rel, m_Right), -m_HalfW, m_HalfW), up = rel.y + Random.Range(0.9f, 1.5f);
            float o = SurfaceOut(x, up);
            if (float.IsNaN(o)) o = 0f;
            float reach = Vector3.Dot(rel, m_Fwd) - o;
            if (reach > 2f || reach < -0.5f) return;
            var point = GatePoint(x, o, up);
            m_Strikes[s] = Strikes(s) + 1;
            m_Gate.TakeHit(new HitInfo { damage = 0.04f, point = point, direction = -m_Fwd, force = 1f, source = HitSource.Fireball });
            // spaced out so a crowd of axes doesn't churn through the shared Sfx pool
            if (Time.time - m_LastHackSound > 0.35f)
            {
                m_LastHackSound = Time.time;
                Sfx.Play(Sound.ArrowHitWood, point, Random.Range(0.18f, 0.32f), 0.25f);
            }
        }

        // ---------------------------------------------------------------- squad services
        public void HitGate(float damage, Vector3 point, float force)
        {
            if (!m_Gate || !m_Gate.Alive) return;
            m_Gate.TakeHit(new HitInfo { damage = damage, point = point, direction = -m_Fwd, force = force, source = HitSource.Fireball });
        }

        // A climber made it over the parapet.
        public void Infiltrate(Soldier s)
        {
            if (!s) return;
            m_Alive.Remove(s);
            Infiltrated++;
            HitGate(0.3f, s.transform.position, 2f);
        }

        // Pulls the nearest foot soldier off gate duty for a squad (picking up a dropped ram).
        public Soldier Recruit(Vector3 near, float radius)
        {
            if (!m_Running || Over) return null;
            Soldier best = null;
            float bd = radius;
            foreach (var kv in m_Brains)
            {
                var s = kv.Key;
                if (!s || !s.Alive || kv.Value == null) continue;
                float d = FlatDistance(s.transform.position, near);
                if (d < bd) { bd = d; best = s; }
            }
            if (best) StopBrain(best);
            return best;
        }

        // A squad member goes back to fighting on foot.
        public void Release(Soldier s)
        {
            if (!s || !s.Alive) return;
            s.followGround = true;
            s.bodyPitch = 0f;
            s.squadEffort = 0f;
            s.carryHeight = 1f;
            s.Stop();
            s.SetState(Soldier.State.Idle);
            if (!m_Running) return;
            bool far = Vector3.Dot(s.transform.position - m_Face, m_Fwd) > chargeDistance + 6f;
            StartBrain(s, FootBrain(s, far));
        }
    }
}
