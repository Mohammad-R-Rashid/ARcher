using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // Plank doors for the gate arch. The Kingdom kit's gate is an open archway, so without them the ram and the
    // men at the gate would batter thin air. Built at runtime to the arch's measured profile (palette-atlas UVs,
    // the castle's own material), hung on the gate piece so they shudder with it, and burst into splinters when
    // the gate falls.
    public class GateDoor : MonoBehaviour
    {
        const float Thickness = 0.12f;
        const float Tuck = 0.1f;          // planks run this far into the jambs and the arch
        const float Cell = 0.05f;         // one swatch of the 19 x 10 palette atlas

        CastlePiece m_Gate;
        MeshRenderer m_Renderer;
        BoxCollider m_Collider;
        Vector3 m_Fwd;

        // Probes the arch at 'inset' metres behind the gate face (face = the face plane at the gate base, centred)
        // and hangs a door there. Null when there is no open arch to fill.
        public static GateDoor Hang(CastlePiece gate, Vector3 face, Vector3 fwd, Vector3 right, float inset)
        {
            var renderer = gate ? gate.GetComponent<MeshRenderer>() : null;
            if (!renderer || !renderer.sharedMaterial) return null;
            const int mask = 1 << Layers.Castle;
            bool Cast(Vector3 o, Vector3 d, float dist, out float hitDist)
            {
                hitDist = dist;
                if (!Physics.Raycast(o, d, out var hit, dist, mask, QueryTriggerInteraction.Ignore)) return false;
                if (hit.collider.GetComponentInParent<CastlePiece>() != gate) return false;
                hitDist = hit.distance;
                return true;
            }
            var plane = face - fwd * inset;
            // jambs: the narrowest span at knee and chest height
            float left = float.MaxValue, right_ = float.MaxValue;
            foreach (var h in new[] { 0.6f, 1.4f })
            {
                if (!Cast(plane + Vector3.up * h, -right, 4f, out float l) || !Cast(plane + Vector3.up * h, right, 4f, out float r)) return null;
                left = Mathf.Min(left, l);
                right_ = Mathf.Min(right_, r);
            }
            float hw = (left + right_) * 0.5f, xc = (right_ - left) * 0.5f;
            if (hw < 0.4f || hw > 3f) return null;
            // soffit profile across the opening
            const int n = 9;
            var tops = new float[n];
            float maxTop = 0f;
            for (int i = 0; i < n; i++)
            {
                float x = xc - hw + 0.05f + (2f * hw - 0.1f) * i / (n - 1);
                tops[i] = Cast(plane + right * x + Vector3.up * 0.5f, Vector3.up, 8f, out float up) ? up + 0.5f : -1f;
                maxTop = Mathf.Max(maxTop, tops[i]);
            }
            if (maxTop < 1.6f) return null;
            for (int i = 0; i < n; i++) if (tops[i] < 0f) tops[i] = maxTop;

            var go = new GameObject("GateDoor");
            go.layer = Layers.Castle;
            go.transform.SetPositionAndRotation(plane + right * xc - fwd * (Thickness * 0.5f), Quaternion.LookRotation(fwd));
            go.transform.SetParent(gate.transform, true);
            var door = go.AddComponent<GateDoor>();
            door.Build(gate, renderer.sharedMaterial, hw, tops);
            return door;
        }

        void Build(CastlePiece gate, Material material, float hw, float[] tops)
        {
            m_Gate = gate;
            m_Fwd = transform.forward;
            float Top(float x)
            {
                // soffit height at x (door-local), from the probe samples
                float u = Mathf.Clamp01((x + hw - 0.05f) / (2f * hw - 0.1f)) * (tops.Length - 1);
                int i = Mathf.Min(tops.Length - 2, Mathf.FloorToInt(u));
                return Mathf.Lerp(tops[i], tops[i + 1], u - i);
            }

            var v = new List<Vector3>();
            var nrm = new List<Vector3>();
            var uv = new List<Vector2>();
            var tri = new List<int>();
            float ext = hw + Tuck, half = Thickness * 0.5f;
            int planks = Mathf.Clamp(Mathf.RoundToInt(2f * ext / 0.36f), 4, 9);
            float pw = 2f * ext / planks, lowest = float.MaxValue;
            for (int i = 0; i < planks; i++)
            {
                float x0 = -ext + i * pw, x1 = x0 + pw;
                float top = Mathf.Max(Top(x0), Top((x0 + x1) * 0.5f), Top(x1)) + Tuck;
                lowest = Mathf.Min(lowest, top);
                var col = i % 2 == 0 ? Swatch(16, 5) : Swatch(16, 6);
                Box(v, nrm, uv, tri, new Vector3(x0 + 0.012f, -0.05f, -half), new Vector3(x1 - 0.012f, top, half), col);
            }
            // iron straps with a stud on every plank
            float b2 = Mathf.Min(1.95f, lowest - Tuck - 0.6f);
            foreach (var y in new[] { 0.45f, b2 })
            {
                Box(v, nrm, uv, tri, new Vector3(-ext, y, half), new Vector3(ext, y + 0.13f, half + 0.03f), Swatch(17, 8));
                for (int i = 0; i < planks; i++)
                {
                    float x = -ext + (i + 0.5f) * pw;
                    Box(v, nrm, uv, tri, new Vector3(x - 0.035f, y + 0.03f, half + 0.03f), new Vector3(x + 0.035f, y + 0.1f, half + 0.055f), Swatch(17, 6));
                }
            }
            var mesh = new Mesh { name = "GateDoor" };
            mesh.SetVertices(v);
            mesh.SetNormals(nrm);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0);
            mesh.RecalculateBounds();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            m_Renderer = gameObject.AddComponent<MeshRenderer>();
            m_Renderer.sharedMaterial = material;
            m_Collider = gameObject.AddComponent<BoxCollider>();
            m_Collider.center = mesh.bounds.center;
            m_Collider.size = mesh.bounds.size;
        }

        static Vector2 Swatch(int col, int row) => new Vector2((col + 0.5f) * Cell, 1f - (row + 0.5f) * Cell);

        static void Box(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 a, Vector3 b, Vector2 col)
        {
            void Face(Vector3 normal, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
            {
                int s = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                for (int i = 0; i < 4; i++) { n.Add(normal); uv.Add(col); }
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
                t.Add(s); t.Add(s + 2); t.Add(s + 3);
            }
            Face(Vector3.forward, new Vector3(b.x, a.y, b.z), new Vector3(b.x, b.y, b.z), new Vector3(a.x, b.y, b.z), new Vector3(a.x, a.y, b.z));
            Face(Vector3.back, new Vector3(a.x, a.y, a.z), new Vector3(a.x, b.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(b.x, a.y, a.z));
            Face(Vector3.right, new Vector3(b.x, a.y, a.z), new Vector3(b.x, b.y, a.z), new Vector3(b.x, b.y, b.z), new Vector3(b.x, a.y, b.z));
            Face(Vector3.left, new Vector3(a.x, a.y, b.z), new Vector3(a.x, b.y, b.z), new Vector3(a.x, b.y, a.z), new Vector3(a.x, a.y, a.z));
            Face(Vector3.up, new Vector3(a.x, b.y, a.z), new Vector3(a.x, b.y, b.z), new Vector3(b.x, b.y, b.z), new Vector3(b.x, b.y, a.z));
            Face(Vector3.down, new Vector3(a.x, a.y, b.z), new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z), new Vector3(b.x, a.y, b.z));
        }

        void Update()
        {
            if (!m_Renderer.enabled || (m_Gate && m_Gate.Alive)) return;
            // the gate is down: the doors burst
            m_Renderer.enabled = false;
            m_Collider.enabled = false;
            var c = m_Renderer.bounds.center;
            VFX.Splinters(c, m_Fwd, 2.2f);
            VFX.Splinters(c + Vector3.up * 0.8f, m_Fwd, 1.6f);
            VFX.Dust(c, 1.1f);
            Sfx.Play(Sound.WoodBreak, c, 1f, 0.1f);
        }
    }
}
