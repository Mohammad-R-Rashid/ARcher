using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Siege
{
    // The enemy raid. Waves pour out of the siege camp in staggered groups, cross the floor in loose ranks and
    // storm the castle gate: foot soldiers crowd its front and hack at it, ladder teams scale the wall beside
    // the arch (SiegeLadder), the ram team batters the doors (BatteringRam). The gate falling, or enough men
    // getting over the wall, is a breach; beating every wave wins.
    public class WaveManager : MonoBehaviour
    {
        public Soldier soldierPrefab;
        public Soldier spearmanPrefab;            // optional, mixed into the ranks
        public GameObject ladderPrefab, ramPrefab;
        public Transform camp;
        public int WaveCount = 3;

        [Header("Pacing (game metres, seconds)")]
        public float marchSpeed = 2.1f;
        public float chargeSpeed = 4.2f;
        public float chargeDistance = 14f;        // break into a run this far from the gate
        public float carrySpeed = 1.7f;           // ladder and ram teams
        public float firstWaveDelay = 1.5f;
        public float breather = 6f;
        public float maxWaveTime = 100f;          // the next wave comes anyway after this
        public int breachInfiltrators = 6;

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

        class Slot { public float x, outward; public bool centre; public Soldier owner; }

        Castle m_Castle;
        CastlePiece m_Gate;
        Vector3 m_Face, m_Fwd = Vector3.forward, m_Right = Vector3.right, m_CampPos;
        float m_HalfW = 3.5f, m_Height = 5.5f, m_Depth = 3f, m_LastHackSound;
        bool m_Running, m_HasCamp;
        readonly List<Slot> m_Slots = new List<Slot>();
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
            if (!Breached && ((m_Gate && !m_Gate.Alive) || Infiltrated >= breachInfiltrators)) Breach();
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

        // Two loose rows of hacking positions along the gate front; the middle is the ram's lane.
        void BuildSlots()
        {
            m_Slots.Clear();
            float w = Mathf.Max(1f, m_HalfW * 0.85f);
            const float gap = 0.75f;
            int n = Mathf.Max(1, Mathf.FloorToInt(2f * w / gap) + 1);
            for (int i = 0; i < n; i++) AddSlot((i - (n - 1) * 0.5f) * gap, 0.65f);
            for (int i = 0; i < n - 1; i++) AddSlot((i - (n - 2) * 0.5f) * gap, 1.4f);
        }

        void AddSlot(float x, float outward) => m_Slots.Add(new Slot { x = x, outward = outward, centre = Mathf.Abs(x) < 1.2f });

        Vector3 SlotPoint(Slot s) => GatePoint(s.x, s.outward);

        Slot ClaimSlot(Soldier s)
        {
            Slot best = null;
            float bestScore = float.MaxValue;
            var pos = s.transform.position;
            bool ramLane = RamLaneBusy;
            foreach (var sl in m_Slots)
            {
                if (sl.owner && sl.owner.Alive && sl.owner != s) continue;
                if (sl.centre && ramLane) continue;
                // spearmen reach over the front row
                float score = FlatDistance(SlotPoint(sl), pos) + (sl.outward > 1f ? (s.Spearman ? -0.6f : 0.8f) : 0f);
                if (score < bestScore) { bestScore = score; best = sl; }
            }
            if (best != null) best.owner = s;
            return best;
        }

        void FreeSlot(Soldier s)
        {
            foreach (var sl in m_Slots) if (sl.owner == s) sl.owner = null;
        }

        Vector3 StagePoint() => GatePoint(Random.Range(-m_HalfW, m_HalfW) * 0.9f, Random.Range(3.2f, 5f));

        Vector3 WaitPoint()
        {
            float w = Mathf.Max(1.6f, m_HalfW * 0.7f);
            float x = Random.Range(-w, w);
            if (RamLaneBusy && Mathf.Abs(x) < 1.4f) x = (x < 0f ? -1f : 1f) * Random.Range(1.4f, w);
            return GatePoint(x, Random.Range(2.2f, 3.4f));
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
            int foot = w == 1 ? 8 : w == 2 ? 10 : 14 + (w - 3) * 3;
            int ladders = w <= 2 ? 1 : 2;
            bool ram = w >= 2;
            var plan = new List<int>();
            int vanguard = Mathf.CeilToInt(foot * 0.45f);
            for (int i = 0; i < vanguard; i++) plan.Add(0);
            for (int i = 0; i < ladders; i++) plan.Add(1);
            if (ram) plan.Insert(Mathf.Min(plan.Count, vanguard + 1), 2);
            for (int i = vanguard; i < foot; i++) plan.Add(0);

            int ladderIndex = 0;
            foreach (var kind in plan)
            {
                if (!m_Running || Over) yield break;
                switch (kind)
                {
                    case 0:
                        SpawnFoot();
                        // little clusters: sometimes two step out together
                        yield return new WaitForSeconds(Random.value < 0.3f ? 0.12f : Random.Range(0.35f, 0.85f));
                        break;
                    case 1:
                        yield return new WaitForSeconds(0.5f);
                        SpawnLadderTeam(ladderIndex++, ladders);
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
            return Ground(c + lat * Random.Range(-spread, spread) + dir * Random.Range(-1.5f, 1f));
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
            var s = SpawnSoldier(prefab, SpawnPoint(3.5f));
            StartBrain(s, FootBrain(s, true));
        }

        void SpawnLadderTeam(int index, int of)
        {
            if (!ladderPrefab) { SpawnFoot(); SpawnFoot(); return; }
            float side = of > 1 ? (index % 2 == 0 ? -1f : 1f) : FreeLadderSide();
            var pos = SpawnPoint(2.5f);
            var crew = new[] { SpawnSoldier(soldierPrefab, pos), SpawnSoldier(soldierPrefab, pos) };
            var go = Instantiate(ladderPrefab, pos, Quaternion.identity);
            go.name = "Ladder";
            var ladder = go.AddComponent<SiegeLadder>();
            m_Ladders.Add(ladder);
            ladder.Begin(this, crew, side * Mathf.Max(1.6f, m_HalfW * 0.62f));
        }

        float FreeLadderSide()
        {
            float left = 0f, right = 0f;
            foreach (var l in m_Ladders)
            {
                if (!l || l.Fallen) continue;
                if (l.Offset < 0f) left++; else right++;
            }
            return left == right ? (Random.value < 0.5f ? -1f : 1f) : left < right ? -1f : 1f;
        }

        void SpawnRamTeam()
        {
            if (!ramPrefab) { for (int i = 0; i < 4; i++) SpawnFoot(); return; }
            var pos = SpawnPoint(1.5f);
            var crew = new Soldier[4];
            for (int i = 0; i < 4; i++) crew[i] = SpawnSoldier(soldierPrefab, pos);
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
            m_Brains[s] = StartCoroutine(brain);
        }

        void StopBrain(Soldier s)
        {
            if (m_Brains.TryGetValue(s, out var c))
            {
                m_Brains.Remove(s);
                if (c != null) StopCoroutine(c);
            }
            FreeSlot(s);
        }

        IEnumerator Walk(Soldier s, Vector3 target, float speed, float within, float timeout = -1f)
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

        IEnumerator FootBrain(Soldier s, bool march)
        {
            if (march && s)
            {
                // straight across the floor, weaving a little, breaking into a charge near the gate
                var start = s.transform.position;
                var stage = StagePoint();
                var path = stage - start;
                path.y = 0f;
                float len = path.magnitude;
                var dir = len > 0.1f ? path / len : -m_Fwd;
                var lat = Vector3.Cross(Vector3.up, dir);
                s.SetState(Soldier.State.March);
                if (len > chargeDistance + 6f)
                {
                    var weave = Vector3.Lerp(start, stage, Random.Range(0.3f, 0.5f)) + lat * Random.Range(-3f, 3f);
                    yield return Walk(s, weave, marchSpeed, 2.5f);
                    var charge = stage - dir * chargeDistance + lat * Random.Range(-1.5f, 1.5f);
                    yield return Walk(s, charge, marchSpeed, 2f);
                }
                yield return Walk(s, stage, chargeSpeed, 1.8f);
            }
            while (s && s.Alive)
            {
                if (Over) { yield return Storm(s); yield break; }
                var slot = ClaimSlot(s);
                if (slot != null)
                {
                    s.SetState(Soldier.State.March);
                    yield return Walk(s, SlotPoint(slot), Mathf.Max(marchSpeed, chargeSpeed * 0.7f), 0.3f, 10f);
                    if (!s || !s.Alive) yield break;
                    if (slot.owner == s && !Over)
                    {
                        s.Stop();
                        s.Face(-m_Fwd);
                        s.SetState(Soldier.State.Attack);
                        while (s && s.Alive && slot.owner == s && !Over && GateStanding && !(slot.centre && RamLaneBusy)) yield return null;
                    }
                    if (slot.owner == s) slot.owner = null;
                }
                else
                {
                    // no room at the gate: take a raised ladder, or mill about behind the front line jeering
                    var ladder = OpenLadder();
                    if (ladder && Random.value < 0.6f && ladder.Join(s)) { m_Brains.Remove(s); yield break; }
                    s.SetState(Soldier.State.March);
                    yield return Walk(s, WaitPoint(), marchSpeed, 0.6f, 8f);
                    if (!s || !s.Alive) yield break;
                    s.Stop();
                    s.Face(-m_Fwd);
                    if (Random.value < 0.35f) s.Cheer(Random.Range(1f, 1.8f));
                    yield return new WaitForSeconds(Random.Range(1f, 2.2f));
                }
                yield return null;
            }
        }

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
                yield return Walk(s, GatePoint(Random.Range(-1.5f, 1.5f), 1.2f), chargeSpeed, 1f);
                yield return Walk(s, GatePoint(Random.Range(-1.3f, 1.3f), -m_Depth - 1.2f), chargeSpeed, 1f);
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

        // A hack lands on the gate (Soldier.Struck while attacking).
        void OnStruck(Soldier s)
        {
            if (!m_Running || Breached || !m_Gate || !m_Gate.Alive || !s) return;
            var rel = s.transform.position - m_Face;
            float outward = Vector3.Dot(rel, m_Fwd);
            if (outward > 2.4f || outward < -0.5f) return;
            float x = Mathf.Clamp(Vector3.Dot(rel, m_Right), -m_HalfW, m_HalfW);
            var point = m_Face + m_Right * x + Vector3.up * (rel.y + Random.Range(0.9f, 1.5f));
            m_Gate.TakeHit(new HitInfo { damage = 0.04f, point = point, direction = -m_Fwd, force = 1f, source = HitSource.Fireball });
            if (Time.time - m_LastHackSound > 0.14f)
            {
                m_LastHackSound = Time.time;
                Sfx.Play(Sound.ArrowHitWood, point, 0.3f, 0.25f);
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
                if (!s || !s.Alive) continue;
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
