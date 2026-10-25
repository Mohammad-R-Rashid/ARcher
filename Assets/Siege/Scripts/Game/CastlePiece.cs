using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    public enum PieceKind { Wall, Gate, Tower, Keep, House }

    // One destructible section of the castle. Shows escalating damage (smoke, then fire), shudders
    // on every hit and finally shatters into physics rubble, dropping whatever sits on top of it.
    public class CastlePiece : MonoBehaviour, IDamageable
    {
        public PieceKind kind;
        public float maxHealth = 3f;
        public int shatterCells = 9;
        public Transform[] dropOnDestroy;     // flags, crown... become physics props
        public Material debrisMaterial;

        public float Health { get; private set; }
        public bool Alive => Health > 0f;
        public Castle Owner { get; set; }
        public Bounds WorldBounds => m_Renderer ? m_Renderer.bounds : new Bounds(transform.position, Vector3.one);

        MeshRenderer m_Renderer;
        MeshFilter m_Filter;
        Collider m_Collider;
        GameObject m_Smoke, m_Fire;
        Vector3 m_BasePos;
        Quaternion m_BaseRot;
        Coroutine m_Shudder;
        readonly List<GameObject> m_Fires = new List<GameObject>();

        void Awake()
        {
            m_Renderer = GetComponent<MeshRenderer>();
            m_Filter = GetComponent<MeshFilter>();
            m_Collider = GetComponent<Collider>();
            Health = maxHealth;
        }

        public void CaptureRestPose()
        {
            m_BasePos = transform.localPosition;
            m_BaseRot = transform.localRotation;
        }

        public void TakeHit(HitInfo hit)
        {
            if (!Alive || hit.source == HitSource.Arrow) return;
            float before = Health;
            Health = Mathf.Max(0f, Health - hit.damage);
            float frac = Health / maxHealth;
            var top = WorldBounds.center + Vector3.up * WorldBounds.extents.y * 0.75f;

            VFX.StoneChips(hit.point, -hit.direction + Vector3.up * 0.5f, 1.2f);
            if (Health <= 0f)
            {
                Collapse(hit.point, hit.force);
                return;
            }
            if (m_Shudder != null) StopCoroutine(m_Shudder);
            m_Shudder = StartCoroutine(Shudder(hit.damage));

            if (frac <= 0.67f && before / maxHealth > 0.67f)
                m_Smoke = VFX.SmokeColumn(transform, top, 0.8f);
            if (frac <= 0.34f && before / maxHealth > 0.34f)
            {
                m_Fire = VFX.Fire(transform, NearestSurface(hit.point), 1.1f);
                m_Fires.Add(m_Fire);
                var loop = Sfx.Loop(Sound.FireCrackle, m_Fire ? m_Fire.transform : transform, 0.35f);
            }
            if (hit.damage >= 0.75f && m_Fires.Count < 3 && Random.value < 0.5f)
                m_Fires.Add(VFX.Fire(transform, NearestSurface(hit.point), 0.7f));
        }

        Vector3 NearestSurface(Vector3 p)
        {
            var c = m_Collider ? m_Collider.ClosestPointOnBounds(p) : p;
            return Vector3.Lerp(c, p, 0.3f);
        }

        IEnumerator Shudder(float amount)
        {
            float mag = Mathf.Clamp(amount, 0.3f, 1.2f);
            for (float t = 0; t < 0.45f; t += Time.deltaTime)
            {
                float k = (1f - t / 0.45f);
                float s = Mathf.Sin(t * 70f) * k * mag;
                transform.localRotation = m_BaseRot * Quaternion.Euler(s * 1.6f, 0, s * 1.2f);
                transform.localPosition = m_BasePos + new Vector3(s * 0.06f, -Mathf.Abs(s) * 0.04f, 0);
                yield return null;
            }
            transform.localRotation = m_BaseRot;
            transform.localPosition = m_BasePos;
        }

        public void Collapse(Vector3 impact, float force)
        {
            if (m_Renderer == null || !m_Renderer.enabled) return;
            Health = 0f;
            var b = WorldBounds;
            float size = b.extents.magnitude;

            foreach (var t in dropOnDestroy)
            {
                if (!t) continue;
                t.SetParent(null, true);
                t.gameObject.layer = Layers.Debris;
                var col = t.GetComponent<Collider>();
                if (col is MeshCollider mc) mc.convex = true;
                else if (!col) t.gameObject.AddComponent<BoxCollider>();
                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.mass = 2f;
                rb.linearVelocity = (t.position - impact).normalized * force * 0.4f + Vector3.up * 10f;
                rb.angularVelocity = Random.insideUnitSphere * 5f;
                var d = t.gameObject.AddComponent<Debris>();
            }

            if (m_Collider) m_Collider.enabled = false;
            if (m_Shudder != null) StopCoroutine(m_Shudder);
            StartCoroutine(CrumbleDown(impact));
            // Solid chunks of masonry (Kingdom ruin/stone meshes) burst out of the falling section.
            Owner?.SpawnRubble(b, kind == PieceKind.Wall || kind == PieceKind.Gate ? 7 : 11, impact);
            var ground = new Vector3(b.center.x, b.min.y, b.center.z);
            VFX.Dust(ground, size * 0.4f);
            VFX.Dust(b.center, size * 0.25f);
            VFX.StoneChips(b.center, Vector3.up, size * 0.3f);
            Sfx.Play(Sound.StoneCrumble, b.center, 1f);
            if (kind == PieceKind.Tower || kind == PieceKind.Keep)
            {
                Haptics.Collapse();
                ScreenFX.Shake(0.5f);
            }
            if (m_Smoke) VFX.Release(m_Smoke, 6f);
            foreach (var f in m_Fires) if (f) VFX.Release(f, 3f);
            m_Fires.Clear();
            // A lingering fire in the rubble
            VFX.Fire(Owner ? Owner.transform : null, ground + Vector3.up * 0.3f, 1.4f);
            Owner?.OnPieceDestroyed(this);
        }

        // The section shudders, leans away from the blast and sinks into its own dust cloud,
        // leaving a broken stump. Cheap, readable, and suits the toy-diorama style.
        System.Collections.IEnumerator CrumbleDown(Vector3 impact)
        {
            var b = WorldBounds;
            float h = b.size.y;
            var startPos = transform.localPosition;
            var startRot = transform.localRotation;
            var away = transform.parent ? transform.parent.InverseTransformDirection(b.center - impact) : (b.center - impact);
            away.y = 0;
            var axis = Vector3.Cross(Vector3.up, away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward);
            float lean = Random.Range(10f, 22f);
            float dur = kind == PieceKind.Keep ? 1.5f : 1.1f;
            var ground = new Vector3(b.center.x, b.min.y, b.center.z);
            float nextDust = 0f;
            bool ruined = false;
            for (float t = 0; t < dur; t += Time.deltaTime)
            {
                float k = t / dur;
                float sink = k * k;
                transform.localPosition = startPos + Vector3.down * h * 0.95f * sink / Mathf.Max(0.001f, transform.parent ? transform.parent.lossyScale.y : 1f)
                                          + Random.insideUnitSphere * 0.08f * (1f - k);
                transform.localRotation = Quaternion.AngleAxis(lean * Mathf.Sin(k * Mathf.PI * 0.5f), axis) * startRot;
                if (t >= nextDust)
                {
                    nextDust += 0.18f;
                    VFX.Dust(ground + Random.insideUnitSphere * b.extents.x * 0.6f, b.extents.magnitude * 0.22f);
                }
                if (!ruined && k > 0.55f && (kind == PieceKind.Tower || kind == PieceKind.Keep))
                {
                    ruined = true;
                    Owner?.SpawnRuin(b);
                }
                yield return null;
            }
            m_Renderer.enabled = false;
            transform.localPosition = startPos;
            transform.localRotation = startRot;
        }
    }
}
