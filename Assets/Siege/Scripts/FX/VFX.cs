using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // All particle effects are built in code from a handful of materials, then pooled.
    // Sizes are authored for "scale 1" = a catapult fireball impact (game units).
    public class VFX : MonoBehaviour
    {
        [Header("Materials (assigned by SiegeAssetBuilder)")]
        public Material glow;      // additive soft dot
        public Material flame;     // additive 2x2 flame sheet
        public Material smoke;     // alpha 2x2 smoke sheet
        public Material spark;     // additive streak
        public Material ring;      // additive ring
        public Material chip;      // alpha square chip
        public Material scorch;    // alpha decal

        static VFX s_I;
        readonly Dictionary<string, List<GameObject>> m_Pools = new Dictionary<string, List<GameObject>>();
        readonly Dictionary<string, Func<GameObject>> m_Builders = new Dictionary<string, Func<GameObject>>();
        readonly List<Light> m_Lights = new List<Light>();
        readonly Queue<GameObject> m_Decals = new Queue<GameObject>();
        Mesh m_Quad;

        static readonly Color Ember = new Color(4f, 1.6f, 0.4f);
        static readonly Color Hot = new Color(6f, 3.4f, 1.1f);

        void Awake()
        {
            s_I = this;
            m_Builders["explosion"] = BuildExplosion;
            m_Builders["dust"] = BuildDust;
            m_Builders["splinters"] = () => BuildChips("splinters", new Color(0.42f, 0.27f, 0.17f), new Color(0.62f, 0.45f, 0.3f), false);
            m_Builders["stonechips"] = () => BuildChips("stonechips", new Color(0.55f, 0.57f, 0.6f), new Color(0.82f, 0.83f, 0.86f), true);
            m_Builders["fire"] = BuildFire;
            m_Builders["smokecolumn"] = BuildSmokeColumn;
            m_Builders["firework"] = BuildFirework;
            m_Builders["trail"] = BuildFireballTrail;
            m_Builders["launchdust"] = BuildDust;
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            m_Quad = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }

        // ---------------------------------------------------------------- public API
        public static void Explosion(Vector3 pos, float scale = 1f, bool light = true)
        {
            if (!s_I) return;
            s_I.Spawn("explosion", pos, Quaternion.identity, scale);
            if (light) s_I.Flash(pos + Vector3.up * 2f * scale, new Color(1f, 0.6f, 0.25f), 9f, 22f * scale, 0.5f);
        }

        public static void Dust(Vector3 pos, float scale = 1f) { if (s_I) s_I.Spawn("dust", pos, Quaternion.identity, scale); }

        public static void Splinters(Vector3 pos, Vector3 normal, float scale = 1f)
        {
            if (s_I) s_I.Spawn("splinters", pos, Quaternion.LookRotation(normal.sqrMagnitude > 0 ? normal : Vector3.up), scale);
        }

        public static void StoneChips(Vector3 pos, Vector3 normal, float scale = 1f)
        {
            if (s_I) s_I.Spawn("stonechips", pos, Quaternion.LookRotation(normal.sqrMagnitude > 0 ? normal : Vector3.up), scale);
        }

        // Looping effects return the instance; call Release() to fade it out.
        public static GameObject Fire(Transform parent, Vector3 worldPos, float scale = 1f)
        {
            if (!s_I) return null;
            var go = s_I.Spawn("fire", worldPos, Quaternion.identity, scale);
            go.transform.SetParent(parent, true);
            return go;
        }

        public static GameObject SmokeColumn(Transform parent, Vector3 worldPos, float scale = 1f)
        {
            if (!s_I) return null;
            var go = s_I.Spawn("smokecolumn", worldPos, Quaternion.identity, scale);
            go.transform.SetParent(parent, true);
            return go;
        }

        public static GameObject FireballTrail(Transform parent, float scale = 1f)
        {
            if (!s_I) return null;
            var go = s_I.Spawn("trail", parent.position, Quaternion.identity, scale);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        public static void Release(GameObject fx, float linger = 4f)
        {
            if (!fx || !s_I) return;
            fx.transform.SetParent(s_I.transform, true);
            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>()) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            s_I.StartCoroutine(s_I.DisableLater(fx, linger));
        }

        public static void Firework(Vector3 pos, Color color, float scale = 1f)
        {
            if (!s_I) return;
            var go = s_I.Spawn("firework", pos, Quaternion.identity, scale);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
            {
                var m = ps.main;
                if (ps.name == "burst") m.startColor = new ParticleSystem.MinMaxGradient(color * 2.2f, Color.Lerp(color, Color.white, 0.3f) * 1.6f);
            }
            s_I.Flash(pos, color, 6f, 25f * scale, 0.6f);
        }

        public static void Scorch(Vector3 pos, Vector3 normal, float size)
        {
            if (!s_I || normal.y < 0.55f) return;
            var go = new GameObject("Scorch");
            go.AddComponent<MeshFilter>().sharedMesh = s_I.m_Quad;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = s_I.scorch;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.position = pos + normal * 0.06f;
            go.transform.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0, 0, UnityEngine.Random.Range(0f, 360f));
            go.transform.localScale = Vector3.one * size;
            s_I.m_Decals.Enqueue(go);
            while (s_I.m_Decals.Count > 24) Destroy(s_I.m_Decals.Dequeue());
        }

        public static void LightFlash(Vector3 pos, Color c, float intensity, float range, float time)
        {
            if (s_I) s_I.Flash(pos, c, intensity, range, time);
        }

        // ---------------------------------------------------------------- pooling
        GameObject Spawn(string key, Vector3 pos, Quaternion rot, float scale)
        {
            if (!m_Pools.TryGetValue(key, out var pool)) m_Pools[key] = pool = new List<GameObject>();
            GameObject go = null;
            foreach (var p in pool)
                if (p && !p.activeSelf) { go = p; break; }
            if (go == null)
            {
                go = m_Builders[key]();
                go.name = key;
                pool.Add(go);
            }
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = Vector3.one * scale;
            go.SetActive(true);
            var root = go.GetComponent<ParticleSystem>();
            root.Clear(true);
            root.Play(true);
            if (!root.main.loop) StartCoroutine(DisableWhenDone(go, root));
            return go;
        }

        IEnumerator DisableWhenDone(GameObject go, ParticleSystem ps)
        {
            yield return new WaitForSeconds(0.2f);
            while (go && ps.IsAlive(true)) yield return null;
            if (go) go.SetActive(false);
        }

        IEnumerator DisableLater(GameObject go, float t)
        {
            yield return new WaitForSeconds(t);
            if (go) go.SetActive(false);
        }

        void Flash(Vector3 pos, Color c, float intensity, float range, float time)
        {
            Light l = null;
            foreach (var x in m_Lights) if (!x.enabled) { l = x; break; }
            if (l == null)
            {
                if (m_Lights.Count >= 3) l = m_Lights[0];
                else
                {
                    l = new GameObject("FlashLight").AddComponent<Light>();
                    l.transform.SetParent(transform, false);
                    l.type = LightType.Point;
                    l.shadows = LightShadows.None;
                    m_Lights.Add(l);
                }
            }
            StartCoroutine(FlashRoutine(l, pos, c, intensity, range, time));
        }

        IEnumerator FlashRoutine(Light l, Vector3 pos, Color c, float intensity, float range, float time)
        {
            l.transform.position = pos;
            l.color = c;
            l.range = range;
            l.enabled = true;
            for (float t = 0; t < time; t += Time.deltaTime)
            {
                float k = 1f - t / time;
                l.intensity = intensity * k * k * (0.85f + 0.15f * Mathf.Sin(t * 60f));
                yield return null;
            }
            l.enabled = false;
        }

        // ---------------------------------------------------------------- builders
        ParticleSystem Make(Transform parent, string name, Material mat, bool loop = false)
        {
            var go = new GameObject(name);
            if (parent) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.loop = loop;
            m.playOnAwake = false;
            m.duration = loop ? 1f : 0.5f;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.maxParticles = 200;
            var e = ps.emission;
            e.rateOverTime = 0;
            var sh = ps.shape;
            sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortMode = ParticleSystemSortMode.Distance;
            return ps;
        }

        static void Burst(ParticleSystem ps, int min, int max, float time = 0f)
        {
            var e = ps.emission;
            e.SetBursts(new[] { new ParticleSystem.Burst(time, (short)min, (short)max) });
        }

        static void Sheet(ParticleSystem ps)
        {
            var ts = ps.textureSheetAnimation;
            ts.enabled = true;
            ts.numTilesX = 2;
            ts.numTilesY = 2;
            ts.animation = ParticleSystemAnimationType.WholeSheet;
            ts.startFrame = new ParticleSystem.MinMaxCurve(0, 3.99f);
            ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        }

        static Gradient Grad(params (Color c, float t)[] keys)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[keys.Length];
            var ak = new GradientAlphaKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                ck[i] = new GradientColorKey(keys[i].c, keys[i].t);
                ak[i] = new GradientAlphaKey(keys[i].c.a, keys[i].t);
            }
            g.SetKeys(ck, ak);
            g.mode = GradientMode.Blend;
            return g;
        }

        static void ColorLife(ParticleSystem ps, Gradient g)
        {
            var c = ps.colorOverLifetime;
            c.enabled = true;
            c.color = new ParticleSystem.MinMaxGradient(g);
        }

        static void SizeLife(ParticleSystem ps, float start, float end, float bulge = 0f)
        {
            var s = ps.sizeOverLifetime;
            s.enabled = true;
            var curve = new AnimationCurve(new Keyframe(0, start), new Keyframe(0.3f, Mathf.Lerp(start, end, 0.6f) + bulge), new Keyframe(1, end));
            s.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        static void Sphere(ParticleSystem ps, float radius)
        {
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = radius;
        }

        static void Hemisphere(ParticleSystem ps, float radius)
        {
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Hemisphere;
            sh.radius = radius;
            sh.rotation = new Vector3(-90, 0, 0);
        }

        static void Drag(ParticleSystem ps, float drag)
        {
            var l = ps.limitVelocityOverLifetime;
            l.enabled = true;
            l.drag = drag;
            l.multiplyDragByParticleSize = false;
            l.multiplyDragByParticleVelocity = true;
        }

        static void Rot(ParticleSystem ps, float speedDeg)
        {
            var m = ps.main;
            m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            if (speedDeg > 0)
            {
                var r = ps.rotationOverLifetime;
                r.enabled = true;
                r.z = new ParticleSystem.MinMaxCurve(-speedDeg * Mathf.Deg2Rad, speedDeg * Mathf.Deg2Rad);
            }
        }

        GameObject BuildExplosion()
        {
            var root = Make(null, "explosion", glow);
            // Flash
            var m = root.main;
            m.startLifetime = 0.16f; m.startSize = 16f; m.startSpeed = 0; m.startColor = Hot;
            Burst(root, 1, 1);
            ColorLife(root, Grad((Color.white, 0), (new Color(1, 1, 1, 0), 1)));
            SizeLife(root, 0.6f, 1.2f);
            var t = root.transform;

            var fireball = Make(t, "fire", flame);
            m = fireball.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(5f, 15f);
            m.startSize = new ParticleSystem.MinMaxCurve(3.5f, 6.5f);
            m.gravityModifier = -0.15f;
            Burst(fireball, 22, 28);
            Hemisphere(fireball, 1.2f);
            Sheet(fireball); Rot(fireball, 90); Drag(fireball, 0.12f);
            ColorLife(fireball, Grad((new Color(1f, 0.95f, 0.7f, 1), 0), (new Color(1f, 0.55f, 0.15f, 1), 0.25f), (new Color(0.8f, 0.2f, 0.05f, 0.8f), 0.6f), (new Color(0.2f, 0.05f, 0.02f, 0), 1)));
            SizeLife(fireball, 0.5f, 1.6f);
            fireball.GetComponent<ParticleSystemRenderer>().sharedMaterial = flame;

            var smk = Make(t, "smoke", smoke);
            m = smk.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.8f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 9f);
            m.startSize = new ParticleSystem.MinMaxCurve(4f, 7.5f);
            m.gravityModifier = -0.06f;
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.18f, 0.16f, 0.15f, 0.85f), new Color(0.32f, 0.29f, 0.27f, 0.85f));
            Burst(smk, 12, 16, 0.05f);
            Hemisphere(smk, 1.5f);
            Sheet(smk); Rot(smk, 25); Drag(smk, 0.25f);
            ColorLife(smk, Grad((new Color(1, 0.75f, 0.55f, 0), 0), (new Color(1, 1, 1, 0.9f), 0.12f), (new Color(1, 1, 1, 0.6f), 0.5f), (new Color(1, 1, 1, 0), 1)));
            SizeLife(smk, 0.5f, 1.8f);

            var embers = Make(t, "embers", spark);
            m = embers.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(14f, 34f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            m.gravityModifier = 0.9f;
            m.startColor = new ParticleSystem.MinMaxGradient(Ember, Hot);
            Burst(embers, 40, 60);
            Hemisphere(embers, 0.8f);
            Drag(embers, 0.08f);
            ColorLife(embers, Grad((Color.white, 0), (new Color(1, 0.6f, 0.3f, 1), 0.6f), (new Color(1, 0.3f, 0.1f, 0), 1)));
            var er = embers.GetComponent<ParticleSystemRenderer>();
            er.renderMode = ParticleSystemRenderMode.Stretch;
            er.velocityScale = 0.06f;
            er.lengthScale = 1.2f;

            var chips = Make(t, "chunks", chip);
            m = chips.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(10f, 24f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.9f);
            m.gravityModifier = 1f;
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.13f, 0.12f), new Color(0.45f, 0.42f, 0.4f));
            Burst(chips, 14, 22);
            Hemisphere(chips, 0.8f);
            Rot(chips, 600);

            var wave = Make(t, "shockwave", ring);
            m = wave.main;
            m.startLifetime = 0.5f; m.startSpeed = 0; m.startSize = 2f;
            m.startColor = new Color(2.2f, 1.4f, 0.8f, 1f);
            Burst(wave, 1, 1);
            ColorLife(wave, Grad((Color.white, 0), (new Color(1, 1, 1, 0), 1)));
            SizeLife(wave, 1f, 13f);
            var wr = wave.GetComponent<ParticleSystemRenderer>();
            wr.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            wave.transform.localPosition = Vector3.up * 0.3f;
            return root.gameObject;
        }

        GameObject BuildDust()
        {
            var root = Make(null, "dust", smoke);
            var m = root.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(5f, 10f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            m.gravityModifier = -0.03f;
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.72f, 0.66f, 0.58f, 0.8f), new Color(0.85f, 0.8f, 0.72f, 0.8f));
            Burst(root, 16, 22);
            var sh = root.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = 1.2f;
            sh.radiusThickness = 0.1f;
            sh.rotation = new Vector3(90, 0, 0);
            Sheet(root); Rot(root, 40); Drag(root, 0.35f);
            ColorLife(root, Grad((new Color(1, 1, 1, 0), 0), (new Color(1, 1, 1, 0.8f), 0.1f), (new Color(1, 1, 1, 0), 1)));
            SizeLife(root, 0.6f, 1.7f);
            return root.gameObject;
        }

        GameObject BuildChips(string name, Color a, Color b, bool sparks)
        {
            var root = Make(null, name, chip);
            var m = root.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(5f, 13f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.42f);
            m.gravityModifier = 1f;
            m.startColor = new ParticleSystem.MinMaxGradient(a, b);
            Burst(root, 10, 16);
            var sh = root.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 45f;
            sh.radius = 0.15f;
            Rot(root, 900);
            var puff = Make(root.transform, "puff", smoke);
            var pm = puff.main;
            pm.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1f);
            pm.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            pm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            pm.startColor = new Color(b.r, b.g, b.b, 0.6f);
            Burst(puff, 3, 5);
            Sheet(puff); Rot(puff, 30);
            ColorLife(puff, Grad((new Color(1, 1, 1, 0.7f), 0), (new Color(1, 1, 1, 0), 1)));
            SizeLife(puff, 0.6f, 1.6f);
            if (sparks)
            {
                var sp = Make(root.transform, "sparks", spark);
                var sm = sp.main;
                sm.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
                sm.startSpeed = new ParticleSystem.MinMaxCurve(8f, 18f);
                sm.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.2f);
                sm.startColor = new Color(4f, 3.3f, 2.2f);
                Burst(sp, 5, 9);
                var ssh = sp.shape; ssh.enabled = true; ssh.shapeType = ParticleSystemShapeType.Cone; ssh.angle = 60;
                var r = sp.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f;
            }
            return root.gameObject;
        }

        GameObject BuildFire()
        {
            var root = Make(null, "fire", flame, true);
            var m = root.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.9f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.3f, 2.4f);
            m.gravityModifier = -0.25f;
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(3f, 1.4f, 0.35f, 1), new Color(4f, 2.2f, 0.6f, 1));
            var e = root.emission; e.rateOverTime = 16;
            var sh = root.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.6f; sh.rotation = new Vector3(90, 0, 0);
            Sheet(root); Rot(root, 20);
            ColorLife(root, Grad((new Color(1, 1, 1, 0), 0), (new Color(1, 1, 1, 1), 0.15f), (new Color(1, 0.5f, 0.3f, 0.6f), 0.6f), (new Color(1, 0.3f, 0.2f, 0), 1)));
            SizeLife(root, 0.8f, 0.3f, 0.2f);
            var t = root.transform;

            var emb = Make(t, "embers", spark, true);
            var em = emb.main;
            em.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
            em.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            em.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            em.gravityModifier = -0.15f;
            em.startColor = Ember;
            var ee = emb.emission; ee.rateOverTime = 6;
            var esh = emb.shape; esh.enabled = true; esh.shapeType = ParticleSystemShapeType.Circle; esh.radius = 0.5f; esh.rotation = new Vector3(90, 0, 0);
            var noise = emb.noise; noise.enabled = true; noise.strength = 1.5f; noise.frequency = 0.6f;
            ColorLife(emb, Grad((Color.white, 0), (new Color(1, 0.4f, 0.2f, 0), 1)));

            var smk = Make(t, "smoke", smoke, true);
            var sm = smk.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(2f, 3.5f);
            sm.startSize = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            sm.startColor = new Color(0.15f, 0.14f, 0.13f, 0.6f);
            var se = smk.emission; se.rateOverTime = 4;
            smk.transform.localPosition = Vector3.up * 1.2f;
            Sheet(smk); Rot(smk, 20);
            ColorLife(smk, Grad((new Color(1, 1, 1, 0), 0), (new Color(1, 1, 1, 0.7f), 0.2f), (new Color(1, 1, 1, 0), 1)));
            SizeLife(smk, 0.6f, 2f);
            return root.gameObject;
        }

        GameObject BuildSmokeColumn()
        {
            var root = Make(null, "smokecolumn", smoke, true);
            var m = root.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(3f, 5f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.12f, 0.11f, 0.1f, 0.55f), new Color(0.25f, 0.23f, 0.22f, 0.55f));
            m.maxParticles = 60;
            var e = root.emission; e.rateOverTime = 4.5f;
            var sh = root.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 1f; sh.rotation = new Vector3(90, 0, 0);
            var vel = root.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.8f, 1.6f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            Sheet(root); Rot(root, 15);
            ColorLife(root, Grad((new Color(1, 1, 1, 0), 0), (new Color(1, 1, 1, 0.8f), 0.15f), (new Color(1, 1, 1, 0.4f), 0.6f), (new Color(1, 1, 1, 0), 1)));
            SizeLife(root, 0.5f, 2.6f);
            return root.gameObject;
        }

        GameObject BuildFireballTrail()
        {
            var root = Make(null, "trail", flame, true);
            var m = root.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            m.startSpeed = 0.5f;
            m.startSize = new ParticleSystem.MinMaxCurve(2.4f, 3.6f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(4f, 1.8f, 0.4f, 1), new Color(5f, 3f, 0.9f, 1));
            var e = root.emission; e.rateOverTime = 30; e.rateOverDistance = 5f;
            Sphere(root, 0.5f);
            Sheet(root); Rot(root, 120);
            ColorLife(root, Grad((Color.white, 0), (new Color(1, 0.5f, 0.2f, 0.7f), 0.5f), (new Color(0.5f, 0.1f, 0.05f, 0), 1)));
            SizeLife(root, 1f, 0.2f);
            var t = root.transform;

            var smk = Make(t, "smoke", smoke, true);
            var sm = smk.main;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
            sm.startSpeed = 0.3f;
            sm.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.15f, 0.13f, 0.12f, 0.75f), new Color(0.3f, 0.27f, 0.25f, 0.75f));
            var se = smk.emission; se.rateOverDistance = 2.2f;
            Sheet(smk); Rot(smk, 30); Sphere(smk, 0.4f);
            ColorLife(smk, Grad((new Color(1, 0.6f, 0.4f, 0), 0), (new Color(1, 1, 1, 0.8f), 0.1f), (new Color(1, 1, 1, 0), 1)));
            SizeLife(smk, 0.6f, 2.4f);

            var emb = Make(t, "embers", spark, true);
            var em = emb.main;
            em.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            em.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
            em.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
            em.gravityModifier = 0.3f;
            em.startColor = Ember;
            var ee = emb.emission; ee.rateOverDistance = 2.5f;
            Sphere(emb, 0.6f);
            ColorLife(emb, Grad((Color.white, 0), (new Color(1, 0.3f, 0.1f, 0), 1)));
            return root.gameObject;
        }

        GameObject BuildFirework()
        {
            var root = Make(null, "firework", glow);
            var m = root.main;
            m.startLifetime = 0.25f; m.startSize = 7f; m.startSpeed = 0; m.startColor = new Color(3, 3, 3, 1);
            Burst(root, 1, 1);
            ColorLife(root, Grad((Color.white, 0), (new Color(1, 1, 1, 0), 1)));
            var b = Make(root.transform, "burst", spark);
            var bm = b.main;
            bm.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
            bm.startSpeed = new ParticleSystem.MinMaxCurve(10f, 15f);
            bm.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            bm.gravityModifier = 0.25f;
            Burst(b, 70, 90);
            Sphere(b, 0.2f);
            Drag(b, 0.14f);
            ColorLife(b, Grad((Color.white, 0), (new Color(1, 1, 1, 1), 0.7f), (new Color(1, 1, 1, 0), 1)));
            var r = b.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1.5f;
            return root.gameObject;
        }
    }
}
