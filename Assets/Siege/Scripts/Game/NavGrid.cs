using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // Walkability map of the real room from the LiDAR mesh / detected planes (Environment layer) plus the
    // town (Castle layer), used for placement, spawning and routing:
    //  - heights are sampled with rays that start ~1.6 m above the floor (below the ceiling the LiDAR also meshes)
    //  - a cell is blocked if it holds a building, has no scanned surface, sits on furniture, or steps sharply
    //  - moving between neighbouring cells is blocked if a knee-height line between them hits a surface
    //    (catches thin scanned walls and the sides of sofas/tables that height samples miss)
    //  - blocked space is padded by a margin so nobody brushes or clips a wall
    //  - a flood fill from the gate marks what is actually reachable, so other rooms / under tables never count
    public class NavGrid
    {
        public static NavGrid Current { get; private set; }

        readonly Vector3 m_Origin;
        readonly float m_Cell;
        readonly int m_W, m_H;
        readonly float[] m_Y;
        readonly bool[] m_Blocked;
        readonly byte[] m_Edges;      // bit d set = can't step from this cell in direction d
        bool[] m_Reach;
        readonly float m_Floor;

        static readonly int[] DX = { 1, 1, 0, -1, -1, -1, 0, 1 };
        static readonly int[] DZ = { 0, 1, 1, 1, 0, -1, -1, -1 };

        // a, b: points the grid must cover (castle, enemy camp). Distances in game units; real-world limits in metres.
        public static NavGrid Build(Vector3 a, Vector3 b, float margin, float cell, float unitsPerMeter,
                                    float floorY, Vector3 reachFrom, float maxStepM = 0.08f, float clearanceM = 0.12f)
        {
            Current = new NavGrid(a, b, margin, cell, unitsPerMeter, floorY, maxStepM, clearanceM);
            Current.Flood(reachFrom);
            return Current;
        }

        NavGrid(Vector3 a, Vector3 b, float margin, float cell, float upm, float floorY, float maxStepM, float clearanceM)
        {
            m_Cell = cell;
            m_Floor = floorY;
            var min = Vector3.Min(a, b) - new Vector3(margin, 0, margin);
            var max = Vector3.Max(a, b) + new Vector3(margin, 0, margin);
            m_W = Mathf.Clamp(Mathf.CeilToInt((max.x - min.x) / cell), 2, 180);
            m_H = Mathf.Clamp(Mathf.CeilToInt((max.z - min.z) / cell), 2, 180);
            m_Origin = new Vector3(min.x, floorY, min.z);
            int n = m_W * m_H;
            m_Y = new float[n];
            m_Blocked = new bool[n];
            m_Edges = new byte[n];

            float maxStep = maxStepM * upm, tallObstacle = 0.25f * upm, knee = 0.22f * upm;
            float rayTop = floorY + 1.6f * upm;   // below a normal ceiling, above furniture and the castle
            int env = Layers.Mask(Layers.Environment);
            int envCastle = Layers.Mask(Layers.Environment, Layers.Castle);

            for (int z = 0; z < m_H; z++)
                for (int x = 0; x < m_W; x++)
                {
                    int i = z * m_W + x;
                    var c = CellCenter(x, z);
                    if (Physics.Raycast(new Vector3(c.x, rayTop, c.z), Vector3.down, out var hit, 2.4f * upm, envCastle, QueryTriggerInteraction.Ignore))
                    {
                        m_Y[i] = hit.point.y;
                        var piece = hit.collider.gameObject.layer == Layers.Castle ? hit.collider.GetComponentInParent<CastlePiece>() : null;
                        bool building = piece && piece.kind != PieceKind.Gate;
                        bool island = hit.collider.gameObject.layer == Layers.Environment && hit.collider.transform.root.GetComponent<Castle>();
                        // furniture tops: well above the floor and not the castle mound
                        m_Blocked[i] = building || (!island && hit.point.y - floorY > tallObstacle);
                        // Something over this patch of floor (chair seat, table top) blocks it too - tall
                        // engines and men can't stand under furniture.
                        if (!m_Blocked[i] && !island && Physics.Raycast(new Vector3(c.x, hit.point.y + 0.04f * upm, c.z), Vector3.up, 1.5f * upm, env, QueryTriggerInteraction.Ignore))
                            m_Blocked[i] = true;
                    }
                    else m_Y[i] = floorY;   // not scanned yet: assume plain floor (only what the LiDAR sees blocks)
                }

            // Steps and walls between neighbours (4 directions; the opposite edges mirror).
            for (int z = 0; z < m_H; z++)
                for (int x = 0; x < m_W; x++)
                {
                    int i = z * m_W + x;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + DX[d], nz = z + DZ[d];
                        if (nx < 0 || nz < 0 || nx >= m_W || nz >= m_H) continue;
                        int j = nz * m_W + nx;
                        bool cut = float.IsNaN(m_Y[i]) || float.IsNaN(m_Y[j]) || Mathf.Abs(m_Y[i] - m_Y[j]) > maxStep;
                        if (!cut)
                        {
                            float y = Mathf.Max(m_Y[i], m_Y[j]) + knee;
                            var p = CellCenter(x, z); p.y = y;
                            var q = CellCenter(nx, nz); q.y = y;
                            cut = Physics.Linecast(p, q, env, QueryTriggerInteraction.Ignore);
                            if (!cut) { p.y = q.y = Mathf.Max(m_Y[i], m_Y[j]) + 0.55f * upm; cut = Physics.Linecast(p, q, env, QueryTriggerInteraction.Ignore); }   // seat / table edge height
                        }
                        if (cut)
                        {
                            m_Edges[i] |= (byte)(1 << d);
                            m_Edges[j] |= (byte)(1 << (d + 4));
                        }
                    }
                }

            // A cell with a wall on any side counts as blocked (that's where the wall actually is), then pad by clearance.
            var wallish = new bool[n];
            for (int i = 0; i < n; i++) wallish[i] = m_Blocked[i] || (m_Edges[i] & 0x55) != 0;
            int pad = Mathf.Max(0, Mathf.RoundToInt(clearanceM * upm / cell));
            for (int z = 0; z < m_H; z++)
                for (int x = 0; x < m_W; x++)
                {
                    if (!wallish[z * m_W + x]) continue;
                    for (int dz = -pad; dz <= pad; dz++)
                        for (int dx = -pad; dx <= pad; dx++)
                        {
                            int nx = x + dx, nz = z + dz;
                            if (nx >= 0 && nz >= 0 && nx < m_W && nz < m_H) m_Blocked[nz * m_W + nx] = true;
                        }
                }
        }

        Vector3 CellCenter(int x, int z) => m_Origin + new Vector3((x + 0.5f) * m_Cell, 0, (z + 0.5f) * m_Cell);

        Vector3 CellPoint(int i)
        {
            var c = CellCenter(i % m_W, i / m_W);
            c.y = float.IsNaN(m_Y[i]) ? m_Floor : m_Y[i];
            return c;
        }

        bool ToCell(Vector3 p, out int x, out int z)
        {
            x = Mathf.FloorToInt((p.x - m_Origin.x) / m_Cell);
            z = Mathf.FloorToInt((p.z - m_Origin.z) / m_Cell);
            return x >= 0 && z >= 0 && x < m_W && z < m_H;
        }

        bool In(int x, int z) => x >= 0 && z >= 0 && x < m_W && z < m_H;
        bool Free(int x, int z) => In(x, z) && !m_Blocked[z * m_W + x];
        bool Reachable(int i) => m_Reach == null || m_Reach[i];

        bool CanStep(int x, int z, int d)
        {
            int nx = x + DX[d], nz = z + DZ[d];
            if (!Free(nx, nz)) return false;
            if (d % 2 == 0) return (m_Edges[z * m_W + x] & (1 << d)) == 0;
            // diagonal: both orthogonal moves around the corner must be open
            int a = d - 1, b = (d + 1) % 8;
            return Free(x + DX[a], z + DZ[a]) && Free(x + DX[b], z + DZ[b])
                && (m_Edges[z * m_W + x] & (1 << a)) == 0 && (m_Edges[z * m_W + x] & (1 << b)) == 0;
        }

        // Marks every cell connected to the open floor nearest `from` (the gate approach).
        void Flood(Vector3 from)
        {
            m_Reach = new bool[m_W * m_H];
            if (!NearestFreeCell(from, 40, out int start)) { m_Reach = null; return; }
            var q = new Queue<int>();
            q.Enqueue(start);
            m_Reach[start] = true;
            while (q.Count > 0)
            {
                int c = q.Dequeue(), cx = c % m_W, cz = c / m_W;
                for (int d = 0; d < 8; d++)
                {
                    if (!CanStep(cx, cz, d)) continue;
                    int j = (cz + DZ[d]) * m_W + cx + DX[d];
                    if (m_Reach[j]) continue;
                    m_Reach[j] = true;
                    q.Enqueue(j);
                }
            }
        }

        bool NearestFreeCell(Vector3 p, int maxRing, out int cell, bool reachable = false, int openRadius = 0)
        {
            cell = -1;
            ToCell(p, out int px, out int pz);
            px = Mathf.Clamp(px, 0, m_W - 1); pz = Mathf.Clamp(pz, 0, m_H - 1);
            float best = float.MaxValue;
            for (int r = 0; r <= maxRing; r++)
            {
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        int x = px + dx, z = pz + dz;
                        if (!Free(x, z)) continue;
                        int i = z * m_W + x;
                        if (reachable && !Reachable(i)) continue;
                        if (openRadius > 0 && !OpenAround(x, z, openRadius, reachable)) continue;
                        if (m_Accept != null && !m_Accept(CellPoint(i))) continue;
                        float dd = (CellCenter(x, z) - new Vector3(p.x, m_Floor, p.z)).sqrMagnitude;
                        if (dd < best) { best = dd; cell = i; }
                    }
                if (cell >= 0) return true;   // nearest ring with a hit
            }
            return false;
        }

        bool OpenAround(int x, int z, int radius, bool reachable)
        {
            for (int dz = -radius; dz <= radius; dz++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dz * dz > radius * radius) continue;
                    if (!Free(x + dx, z + dz)) return false;
                    if (reachable && !Reachable((z + dz) * m_W + x + dx)) return false;
                }
            return true;
        }

        // ---------------------------------------------------------------- queries
        public bool IsFree(Vector3 p) => ToCell(p, out var x, out var z) && Free(x, z);

        public bool IsReachable(Vector3 p) => ToCell(p, out var x, out var z) && Free(x, z) && Reachable(z * m_W + x);

        // Open, reachable floor within `radius` (game units) of every point of a disc at p.
        public bool AreaOpen(Vector3 p, float radius)
        {
            if (!ToCell(p, out var x, out var z)) return false;
            return Free(x, z) && Reachable(z * m_W + x) && OpenAround(x, z, Mathf.CeilToInt(radius / m_Cell), true);
        }

        // Nearest point to p that is open floor, reachable from the gate, with `clear` game units of room around it.
        // `accept` filters candidates (e.g. "in front of the castle") so a fallback never lands somewhere absurd.
        public bool TryOpenSpot(Vector3 p, float searchRadius, float clear, out Vector3 spot, System.Func<Vector3, bool> accept = null)
        {
            spot = p;
            int rings = Mathf.CeilToInt(searchRadius / m_Cell);
            int open = Mathf.Max(0, Mathf.CeilToInt(clear / m_Cell));
            m_Accept = accept;
            bool ok = NearestFreeCell(p, rings, out int c, true, open) || NearestFreeCell(p, rings, out c, true);
            m_Accept = null;
            if (!ok) return false;
            spot = CellPoint(c);
            return true;
        }

        System.Func<Vector3, bool> m_Accept;

        // Path of world waypoints from a to b around obstacles (straight line if clear or no route).
        public List<Vector3> FindPath(Vector3 a, Vector3 b)
        {
            var result = new List<Vector3>();
            if (!ToCell(a, out int sx, out int sz) || !ToCell(b, out int tx, out int tz) || LineFree(a, b)) { result.Add(b); return result; }
            // start from the nearest walkable cell if a raider was pushed into a padded zone
            int start = sz * m_W + sx;
            if (!Free(sx, sz) && NearestFreeCell(a, 6, out int s2, true)) { start = s2; sx = s2 % m_W; sz = s2 / m_W; }
            int goal = tz * m_W + tx;
            int n = m_W * m_H;
            var g = new float[n];
            var came = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.MaxValue; came[i] = -1; }
            var open = new List<int> { start };
            g[start] = 0;
            int guard = 0;
            while (open.Count > 0 && guard++ < 30000)
            {
                int bi = 0; float bf = float.MaxValue;
                for (int k = 0; k < open.Count; k++)
                {
                    float f = g[open[k]] + Heuristic(open[k], goal);
                    if (f < bf) { bf = f; bi = k; }
                }
                int cur = open[bi];
                open.RemoveAt(bi);
                if (cur == goal) break;
                closed[cur] = true;
                int cx = cur % m_W, cz = cur / m_W;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DX[d], nz = cz + DZ[d];
                    if (!In(nx, nz)) continue;
                    int ni = nz * m_W + nx;
                    if (closed[ni]) continue;
                    // the goal (gate face, ladder foot) may sit inside the padded zone: always enterable
                    if (ni != goal && !CanStep(cx, cz, d)) continue;
                    float ng = g[cur] + (d % 2 == 1 ? 1.414f : 1f);
                    if (ng < g[ni]) { g[ni] = ng; came[ni] = cur; if (!open.Contains(ni)) open.Add(ni); }
                }
            }
            if (came[goal] < 0) { result.Add(b); return result; }
            var cells = new List<Vector3>();
            for (int c = goal; c >= 0 && c != start; c = came[c]) cells.Add(CellPoint(c));
            cells.Reverse();
            // string-pull: keep only the corners we can't see past
            var from = a;
            for (int k = 0; k < cells.Count; k++)
            {
                bool last = k == cells.Count - 1;
                if (last || !LineFree(from, cells[k + 1])) { result.Add(last ? b : cells[k]); from = cells[k]; }
            }
            if (result.Count == 0 || result[result.Count - 1] != b) result.Add(b);
            return result;
        }

        float Heuristic(int a, int b)
        {
            int ax = a % m_W, az = a / m_W, bx = b % m_W, bz = b / m_W;
            return Mathf.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
        }

        bool LineFree(Vector3 a, Vector3 b)
        {
            float d = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
            int steps = Mathf.Max(1, Mathf.CeilToInt(d / (m_Cell * 0.5f)));
            int px = int.MinValue, pz = int.MinValue;
            for (int i = 1; i < steps; i++)
            {
                var p = Vector3.Lerp(a, b, i / (float)steps);
                if (!ToCell(p, out var x, out var z)) continue;
                if (!Free(x, z)) return false;
                if (px != int.MinValue && (x != px || z != pz))
                {
                    // crossing a wall edge between two free cells
                    for (int dd = 0; dd < 8; dd++)
                        if (px + DX[dd] == x && pz + DZ[dd] == z && !CanStep(px, pz, dd)) return false;
                }
                px = x; pz = z;
            }
            return true;
        }

        // Quick pre-grid flatness probe (kept for callers without a grid).
        public static bool AreaClear(Vector3 p, float radius, float maxStep, float rayHeight, int samples = 24)
        {
            int mask = Layers.Mask(Layers.Environment);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i <= samples; i++)
            {
                var q = p;
                if (i > 0)
                {
                    float a = i * 2.39996f;
                    float r = radius * Mathf.Sqrt(i / (float)samples);
                    q += new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
                }
                // start below the ceiling (the LiDAR meshes it too)
                if (!Physics.Raycast(q + Vector3.up * rayHeight, Vector3.down, out var hit, rayHeight * 2f, mask, QueryTriggerInteraction.Ignore)) return false;
                lo = Mathf.Min(lo, hit.point.y); hi = Mathf.Max(hi, hit.point.y);
                if (hi - lo > maxStep) return false;
            }
            return true;
        }
    }
}
