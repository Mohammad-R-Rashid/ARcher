using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // The player's castle: owns the destructible pieces, plays the "builds itself" intro, picks
    // targets for the catapult and reports when the keep falls.
    public class Castle : MonoBehaviour
    {
        public CastlePiece keep;
        public Transform island;            // the grassy diorama base
        public Transform treasure;          // chest + coins inside the courtyard
        public GameObject ruinPrefab;
        public GameObject coinPrefab;
        public GameObject[] rubblePrefabs;   // solid stones thrown out when a section falls
        public float keepTopHeight;          // keep battlements above the castle root (set by the builder)

        public event Action<CastlePiece> PieceDestroyed;
        public event Action KeepDestroyed;

        public readonly List<CastlePiece> pieces = new List<CastlePiece>();
        public bool Fallen { get; private set; }

        float m_TotalHealth;

        void Awake()
        {
            GetComponentsInChildren(true, pieces);
            foreach (var p in pieces)
            {
                p.Owner = this;
                p.CaptureRestPose();
                m_TotalHealth += p.maxHealth;
            }
        }

        public float Integrity
        {
            get
            {
                float h = 0;
                foreach (var p in pieces) h += p.Health;
                return m_TotalHealth > 0 ? h / m_TotalHealth : 0;
            }
        }

        public Vector3 Center => keep ? keep.WorldBounds.center : transform.position;
        public CastlePiece Gate => pieces.Find(p => p.kind == PieceKind.Gate);

        // ---------------------------------------------------------------- targeting
        public CastlePiece PickTarget()
        {
            float keepWeight = Mathf.Lerp(0.35f, 2.5f, 1f - Integrity);
            float total = 0;
            foreach (var p in pieces)
                if (p.Alive) total += Weight(p, keepWeight);
            float r = UnityEngine.Random.value * total;
            foreach (var p in pieces)
            {
                if (!p.Alive) continue;
                r -= Weight(p, keepWeight);
                if (r <= 0) return p;
            }
            return keep;
        }

        static float Weight(CastlePiece p, float keepWeight) => p.kind switch
        {
            PieceKind.Keep => keepWeight,
            PieceKind.Tower => 1.2f,
            PieceKind.Gate => 1.3f,
            PieceKind.House => 0.45f,
            _ => 1f,
        };

        public Vector3 AimPoint(CastlePiece p, Vector3 from)
        {
            var b = p.WorldBounds;
            if (p.kind == PieceKind.Keep)
            {
                // The player stands on the keep: hit its front face, below and ahead of them, not their face.
                var toward = from - b.center; toward.y = 0;
                toward = toward.sqrMagnitude > 0.01f ? toward.normalized : transform.forward;
                return new Vector3(b.center.x, b.min.y + b.size.y * UnityEngine.Random.Range(0.35f, 0.6f), b.center.z) + toward * b.extents.x * 0.8f;
            }
            var off = UnityEngine.Random.insideUnitCircle * Mathf.Min(b.extents.x, b.extents.z) * 0.5f;
            return new Vector3(b.center.x + off.x, b.center.y + b.extents.y * UnityEngine.Random.Range(0.1f, 0.6f), b.center.z + off.y);
        }

        // ---------------------------------------------------------------- damage
        public void Blast(Vector3 center, float radius, float damage, float force, CastlePiece direct)
        {
            var hits = Physics.OverlapSphere(center, radius, 1 << Layers.Castle, QueryTriggerInteraction.Ignore);
            var done = new HashSet<CastlePiece>();
            if (direct && direct.Alive)
            {
                done.Add(direct);
                direct.TakeHit(new HitInfo { damage = damage, point = center, direction = (direct.WorldBounds.center - center).normalized, force = force, source = HitSource.Fireball });
            }
            foreach (var h in hits)
            {
                var p = h.GetComponentInParent<CastlePiece>();
                if (!p || !p.Alive || !done.Add(p)) continue;
                var cp = h.ClosestPoint(center);
                float falloff = 1f - Mathf.Clamp01(Vector3.Distance(cp, center) / radius);
                if (falloff <= 0.05f) continue;
                p.TakeHit(new HitInfo { damage = damage * 0.55f * falloff, point = cp, direction = (cp - center).normalized, force = force * falloff, source = HitSource.Explosion });
            }
        }

        public void OnPieceDestroyed(CastlePiece p)
        {
            PieceDestroyed?.Invoke(p);
            if (p == keep && !Fallen)
            {
                Fallen = true;
                SpillTreasure();
                KeepDestroyed?.Invoke();
            }
        }

        public void SpawnRuin(Bounds b)
        {
            if (!ruinPrefab) return;
            var r = Instantiate(ruinPrefab, new Vector3(b.center.x, b.min.y, b.center.z), Quaternion.Euler(0, UnityEngine.Random.Range(0, 360f), 0), transform);
            r.transform.localScale = Vector3.one * Mathf.Max(b.size.x, b.size.z) / 2.4f;
            foreach (var t in r.GetComponentsInChildren<Transform>()) t.gameObject.layer = Layers.Environment;
            StartCoroutine(PopIn(r.transform));
        }

        static IEnumerator PopIn(Transform t)
        {
            var s = t.localScale;
            for (float e = 0; e < 0.35f; e += Time.deltaTime)
            {
                if (!t) yield break;
                t.localScale = new Vector3(s.x, s.y * Ease.OutBack(e / 0.35f), s.z);
                yield return null;
            }
            if (t) t.localScale = s;
        }

        public void SpawnRubble(Bounds b, int count, Vector3 impact)
        {
            if (rubblePrefabs == null || rubblePrefabs.Length == 0) return;
            for (int i = 0; i < count; i++)
            {
                var p = b.center + Vector3.Scale(UnityEngine.Random.insideUnitSphere, b.extents * 0.8f) + Vector3.up * b.extents.y * 0.3f;
                var r = Instantiate(rubblePrefabs[UnityEngine.Random.Range(0, rubblePrefabs.Length)], p, UnityEngine.Random.rotation);
                r.transform.localScale *= UnityEngine.Random.Range(0.6f, 1.3f);
                var rb = r.GetComponent<Rigidbody>();
                var dir = ((p - impact).normalized + Vector3.up * 0.8f).normalized;
                rb.linearVelocity = dir * UnityEngine.Random.Range(6f, 14f);
                rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 6f;
            }
        }

        void SpillTreasure()
        {
            if (!coinPrefab) return;
            Sfx.Play(Sound.Coins, Center, 0.8f);
            var origin = keep.WorldBounds.center;
            for (int i = 0; i < 14; i++)
            {
                var c = Instantiate(coinPrefab, origin + UnityEngine.Random.insideUnitSphere * 1.5f, UnityEngine.Random.rotation);
                c.layer = Layers.Debris;
                var rb = c.GetComponent<Rigidbody>();
                if (!rb) rb = c.AddComponent<Rigidbody>();
                rb.linearVelocity = (UnityEngine.Random.insideUnitSphere + Vector3.up * 1.6f) * 9f;
                rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 12f;
                c.AddComponent<Debris>();
            }
        }

        // Domino collapse of everything still standing (defeat finale).
        public IEnumerator CollapseAll()
        {
            var alive = pieces.FindAll(p => p.Alive);
            alive.Sort((a, b) => Vector3.Distance(a.transform.position, Center).CompareTo(Vector3.Distance(b.transform.position, Center)));
            foreach (var p in alive)
            {
                p.Collapse(Center + Vector3.down * 2f, 10f);
                yield return new WaitForSeconds(0.18f);
            }
        }

        // ---------------------------------------------------------------- intro
        public IEnumerator BuildIntro()
        {
            // Island pops up, then pieces drop in one by one with dust and a haptic tap each.
            var islandScale = island ? island.localScale : Vector3.one;
            var order = new List<CastlePiece>(pieces);
            // Your own tower (the keep) rises first under your feet, then the castle and town build outward.
            order.Sort((a, b) => a.kind == PieceKind.Keep ? -1 : b.kind == PieceKind.Keep ? 1 :
                Vector3.Distance(a.transform.position, keep.transform.position).CompareTo(Vector3.Distance(b.transform.position, keep.transform.position)));
            var props = new List<Transform>();
            foreach (var p in pieces) p.gameObject.SetActive(false);
            if (treasure) treasure.gameObject.SetActive(false);

            if (island)
            {
                for (float t = 0; t < 0.6f; t += Time.deltaTime)
                {
                    island.localScale = islandScale * Ease.OutBack(t / 0.6f);
                    yield return null;
                }
                island.localScale = islandScale;
                VFX.Dust(transform.position, 3f);
                Sfx.Play(Sound.Thud, transform.position, 0.8f);
                Haptics.PieceLand(0.6f);
            }
            yield return new WaitForSeconds(0.15f);
            foreach (var p in order)
            {
                StartCoroutine(Drop(p));
                yield return new WaitForSeconds(p.kind == PieceKind.Keep ? 0.35f : p.kind == PieceKind.House ? 0.05f : 0.14f);
            }
            if (treasure) treasure.gameObject.SetActive(true);
            yield return new WaitForSeconds(0.7f);
        }

        IEnumerator Drop(CastlePiece p)
        {
            var t = p.transform;
            var rest = t.localPosition;
            // The keep rises out of the ground (the player stands on top of it); everything else drops in.
            float h = p.kind == PieceKind.Keep ? -12f : 18f;
            t.localPosition = rest + Vector3.up * h;
            p.gameObject.SetActive(true);
            float dur = p.kind == PieceKind.Keep ? 1.1f : 0.42f;
            for (float e = 0; e < dur; e += Time.deltaTime)
            {
                float k = e / dur;
                float f = p.kind == PieceKind.Keep ? 1f - Ease.OutBack(k) : 1f - k * k;
                t.localPosition = rest + Vector3.up * h * f;
                if (p.kind == PieceKind.Keep && Time.frameCount % 6 == 0) { Haptics.PieceLand(0.25f); VFX.Dust(new Vector3(t.position.x, transform.position.y, t.position.z), 1.6f); }
                yield return null;
            }
            t.localPosition = rest;
            var b = p.WorldBounds;
            VFX.Dust(new Vector3(b.center.x, b.min.y, b.center.z), p.kind == PieceKind.Keep ? 2.2f : 1.4f);
            Sfx.Play(Sound.Thud, b.center, p.kind == PieceKind.Keep ? 1f : 0.6f);
            Haptics.PieceLand(p.kind == PieceKind.Keep ? 1f : 0.45f);
            if (p.kind == PieceKind.Keep) ScreenFX.Shake(0.25f);
            // Squash and settle
            var s = t.localScale;
            for (float e = 0; e < 0.25f; e += Time.deltaTime)
            {
                float k = Mathf.Sin(e / 0.25f * Mathf.PI) * 0.08f;
                t.localScale = new Vector3(s.x * (1 + k), s.y * (1 - k), s.z * (1 + k));
                yield return null;
            }
            t.localScale = s;
        }
    }

    public static class Ease
    {
        public static float OutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3) + c1 * Mathf.Pow(t - 1f, 2);
        }

        public static float InOutCubic(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
        }

        public static float OutCubic(float t) => 1 - Mathf.Pow(1 - Mathf.Clamp01(t), 3);
    }
}
