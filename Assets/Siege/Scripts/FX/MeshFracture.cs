using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // Runtime Voronoi-style shattering: triangles are grouped by the nearest of N seed points.
    // Chunks are open shells, rendered double-sided; at the speeds they fly that reads as solid rubble.
    // Results are cached per mesh so repeated fractures are free.
    public static class MeshFracture
    {
        class Chunk { public Mesh mesh; public Vector3 center; public float volume; }
        static readonly Vector2 InnerUV = new Vector2(0.875f, 0.625f);   // dark stone swatch in the palette atlas
        static readonly Dictionary<(Mesh, int), List<Chunk>> s_Cache = new Dictionary<(Mesh, int), List<Chunk>>();

        public static List<Rigidbody> Shatter(Transform source, Mesh mesh, Material material, int cells,
                                              Vector3 impactPoint, float force, Vector3 inheritVelocity = default)
        {
            var chunks = Get(mesh, cells);
            var result = new List<Rigidbody>(chunks.Count);
            var scale = source.lossyScale;
            foreach (var c in chunks)
            {
                var go = new GameObject("Debris");
                go.layer = Layers.Debris;
                go.transform.SetPositionAndRotation(source.TransformPoint(c.center), source.rotation);
                go.transform.localScale = scale;
                go.AddComponent<MeshFilter>().sharedMesh = c.mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                var box = go.AddComponent<BoxCollider>();
                box.center = c.mesh.bounds.center;
                box.size = Vector3.Max(c.mesh.bounds.size * 0.85f, Vector3.one * 0.15f);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = Mathf.Clamp(c.volume * scale.x * scale.y * scale.z, 0.3f, 40f);
                rb.linearDamping = 0.05f;
                rb.angularDamping = 0.3f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                var dir = go.transform.position - impactPoint;
                float dist = dir.magnitude;
                dir = dist > 0.01f ? dir / dist : Random.onUnitSphere;
                dir = (dir + Vector3.up * 0.6f + Random.insideUnitSphere * 0.35f).normalized;
                float falloff = 1f / (1f + dist * 0.25f);
                rb.linearVelocity = inheritVelocity + dir * force * falloff * Random.Range(0.6f, 1.2f);
                rb.angularVelocity = Random.insideUnitSphere * Random.Range(2f, 7f);
                go.AddComponent<Debris>();
                result.Add(rb);
            }
            return result;
        }

        static List<Chunk> Get(Mesh mesh, int cells)
        {
            if (s_Cache.TryGetValue((mesh, cells), out var list)) return list;
            list = Build(mesh, cells);
            s_Cache[(mesh, cells)] = list;
            return list;
        }

        static List<Chunk> Build(Mesh mesh, int cells)
        {
            var verts = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            var tris = mesh.triangles;
            var b = mesh.bounds;
            var rng = new System.Random(mesh.vertexCount * 31 + cells);
            var seeds = new Vector3[cells];
            for (int i = 0; i < cells; i++)
                seeds[i] = new Vector3(
                    Mathf.Lerp(b.min.x, b.max.x, (float)rng.NextDouble()),
                    Mathf.Lerp(b.min.y, b.max.y, (float)rng.NextDouble()),
                    Mathf.Lerp(b.min.z, b.max.z, (float)rng.NextDouble()));

            var groups = new List<int>[cells];
            for (int i = 0; i < cells; i++) groups[i] = new List<int>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                var c = (verts[tris[t]] + verts[tris[t + 1]] + verts[tris[t + 2]]) / 3f;
                int best = 0; float bd = float.MaxValue;
                for (int s = 0; s < cells; s++)
                {
                    float d = (c - seeds[s]).sqrMagnitude;
                    if (d < bd) { bd = d; best = s; }
                }
                groups[best].Add(t);
            }

            var list = new List<Chunk>();
            var remap = new Dictionary<int, int>();
            foreach (var g in groups)
            {
                if (g.Count < 4) continue;
                remap.Clear();
                var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var idx = new List<int>(g.Count * 3);
                foreach (var t in g)
                    for (int k = 0; k < 3; k++)
                    {
                        int oi = tris[t + k];
                        if (!remap.TryGetValue(oi, out int ni))
                        {
                            ni = v.Count;
                            remap[oi] = ni;
                            v.Add(verts[oi]);
                            if (normals.Length > 0) n.Add(normals[oi]);
                            if (uvs.Length > 0) u.Add(uvs[oi]);
                        }
                        idx.Add(ni);
                    }
                var center = Vector3.zero;
                foreach (var p in v) center += p;
                center /= v.Count;
                for (int i = 0; i < v.Count; i++) v[i] -= center;
                // Inner skin: the same triangles flipped and pulled toward the centre, coloured as raw
                // stone, so fragments read as solid chunks instead of paper-thin shells.
                int outer = v.Count, tcount = idx.Count;
                for (int i = 0; i < outer; i++)
                {
                    v.Add(v[i] * 0.72f);
                    if (n.Count == outer) n.Add(-n[i]);
                    if (u.Count == outer) u.Add(InnerUV);
                }
                for (int i = 0; i < tcount; i += 3)
                {
                    idx.Add(idx[i] + outer);
                    idx.Add(idx[i + 2] + outer);
                    idx.Add(idx[i + 1] + outer);
                }
                var m = new Mesh { name = mesh.name + "_chunk" };
                m.SetVertices(v);
                if (n.Count == v.Count) m.SetNormals(n);
                if (u.Count == v.Count) m.SetUVs(0, u);
                m.SetTriangles(idx, 0);
                m.RecalculateBounds();
                var sz = m.bounds.size;
                list.Add(new Chunk { mesh = m, center = center, volume = Mathf.Max(0.05f, sz.x * sz.y * sz.z) });
            }
            return list;
        }
    }

}
