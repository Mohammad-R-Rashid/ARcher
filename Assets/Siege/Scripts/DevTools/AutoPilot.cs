using System.Collections;
using System.IO;
using UnityEngine;

namespace Siege
{
    // Headless play-through used to verify the game without a device:
    //   Unity -batchmode -projectPath . -executeMethod Siege.EditorTools.SimRunner.Run -siegeAutopilot [victory|defeat]
    // Plays a round in the simulated room, shoots raiders (nearest the gate first) and the trebuchet, captures frames to
    // /tmp/claude-501/previews/sim_*.png and logs [AP] progress lines, then quits the editor.
    public class AutoPilot : MonoBehaviour, IBowInput
    {
        public static string OutDir = "/tmp/claude-501/previews";
        string m_Mode = "victory";
        int m_Errors;
        int m_Shot;
        bool m_TntShot, m_GotNearMiss, m_GotTnt;

        public float Draw { get; private set; }
        public bool ReleasedThisFrame { get; private set; }
        public bool Drawing { get; private set; }
        public Vector3? AimDirection { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-siegeAutopilot");
            if (i < 0) return;
            var ap = new GameObject("AutoPilot").AddComponent<AutoPilot>();
            if (i + 1 < args.Length && !args[i + 1].StartsWith("-")) ap.m_Mode = args[i + 1];
        }

        void OnEnable() => Application.logMessageReceived += OnLog;
        void OnDisable() => Application.logMessageReceived -= OnLog;

        void OnLog(string msg, string stack, LogType type)
        {
            if (msg.StartsWith("[ARROW]")) System.Console.WriteLine(msg);
            if ((type == LogType.Exception || type == LogType.Error) && !stack.Contains("UnityEditor.Search"))
            {
                m_Errors++;
                System.Console.WriteLine("[AP] ERROR " + msg + "\n" + stack);
            }
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(OutDir);
            Haptics.LogInEditor = false;
            Arrow.DebugLog = true;
            var gm = GameManager.Instance;
            while (gm == null) { yield return null; gm = GameManager.Instance; }
            gm.bow.SetInput(this);
            var canvas = FindAnyObjectByType<Canvas>();
            yield return new WaitForSeconds(0.6f);
            UICapture(gm, canvas);
            Snap(gm, "sim_01_intro");
            while (gm.State != GameManager.Phase.Battle) yield return null;
            UICapture(gm, canvas);
            yield return new WaitForSeconds(0.3f);
            Snap(gm, "sim_02_battle_start");
            Log(gm);
            Overview(gm);
            var c = gm.cam;
            System.Console.WriteLine($"[AP] cam {c.name} pos={c.transform.position} fwd={c.transform.forward} up={c.transform.up} lossy={c.transform.lossyScale} clear={c.clearFlags} bg={c.backgroundColor} near={c.nearClipPlane} castle={gm.Castle.transform.position} cat={gm.Trebuchet.transform.position}");
            if (m_Mode == "probe")
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.Exit(0);
#endif
                yield break;
            }

            float start = Time.time;
            bool gotFlight = false, gotImpact = false, gotDraw = false;
            float lastIntegrity = gm.Castle.Integrity;
            float nextShot = Time.time + 1.5f;
            if (m_Mode == "defeat") Time.timeScale = 2.5f;

            bool gotRaid = false;
            while (gm.State == GameManager.Phase.Battle && Time.time - start < 420f)
            {
                if (!gotFlight)
                    foreach (var f in Fireball.Active)
                        if (f && !f.NearMiss && f.TimeToImpact < 1.6f && f.TimeToImpact > 1.0f) { Snap(gm, "sim_03_fireball_flight"); gotFlight = true; break; }
                if (!gotImpact && gm.Castle.Integrity < lastIntegrity - 0.001f)
                {
                    yield return new WaitForSeconds(0.12f);
                    Snap(gm, "sim_04_impact");
                    yield return new WaitForSeconds(0.6f);
                    Snap(gm, "sim_05_after_impact");
                    gotImpact = true;
                }
                lastIntegrity = gm.Castle.Integrity;
                if (!m_GotNearMiss)
                    foreach (var f in Fireball.Active)
                        if (f && f.NearMiss && Vector3.Distance(f.transform.position, gm.cam.transform.position) < 30f) { Snap(gm, "sim_10_near_miss"); m_GotNearMiss = true; break; }
                if (m_TntShot && !m_GotTnt && gm.Trebuchet && System.Array.Exists(gm.Trebuchet.barrels, b => b == null))
                {
                    yield return new WaitForSeconds(0.1f);
                    Snap(gm, "sim_11_tnt");
                    m_GotTnt = true;
                }

                if (!gotRaid && gm.waves && NearestRaider(gm, 30f) != null)
                {
                    gotRaid = true;
                    Snap(gm, "sim_12_raid");
                }
                if (m_Mode == "victory" && Time.time >= nextShot && (NearestRaider(gm, 1000f) != null || (gm.Trebuchet && gm.Trebuchet.Alive)))
                {
                    yield return Shoot(gm, !gotDraw);
                    gotDraw = true;
                    nextShot = Time.time + 1.1f;
                }
                if ((int)(Time.time - start) % 10 == 0 && Time.frameCount % 30 == 0) Log(gm);
                yield return null;
            }
            Log(gm);
            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(1.0f);
            Snap(gm, "sim_06_end");
            yield return new WaitForSecondsRealtime(2.5f);
            Snap(gm, "sim_07_end_late");
            System.Console.WriteLine($"[AP] DONE mode={m_Mode} state={gm.State} errors={m_Errors} shots={m_Shot}");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(m_Errors == 0 ? 0 : 3);
#endif
        }

        IEnumerator Shoot(GameManager gm, bool capture)
        {
            var cat = gm.Trebuchet;
            var cam = gm.cam.transform;
            // Raiders first (closest to the gate), otherwise the trebuchet (and once, its powder kegs).
            var raider = NearestRaider(gm, 1000f);
            Vector3 target;
            if (raider != null) target = raider.transform.position + Vector3.up * 1.2f + raider.transform.forward * 0.6f;
            else
            {
                target = cat.AimCenter + Random.insideUnitSphere * 1.5f;
                if (!m_TntShot)
                    foreach (var b in cat.barrels)
                        if (b) { target = b.transform.position + Vector3.up * 0.6f * b.transform.lossyScale.y; m_TntShot = true; break; }
            }
            // Ballistic aim at full power (low arc)
            var from = gm.bow.nockedArrow.position;
            var d = target - from;
            var flat = new Vector3(d.x, 0, d.z);
            float x = flat.magnitude, y = d.y, v = Tune.ArrowMaxSpeed, g = Tune.Gravity;
            float disc = v * v * v * v - g * (g * x * x + 2 * y * v * v);
            float ang = Mathf.Atan((v * v - Mathf.Sqrt(Mathf.Max(0, disc))) / (g * x));
            var dir = (flat.normalized * Mathf.Cos(ang) + Vector3.up * Mathf.Sin(ang)).normalized;
            if (gm.simRig) gm.simRig.LookAt(cam.position + dir * 50f);
            AimDirection = dir;
            Drawing = true;
            for (float t = 0; t < 0.6f; t += Time.deltaTime)
            {
                Draw = Mathf.Clamp01(t / 0.5f);
                yield return null;
            }
            Draw = 1f;
            if (capture)
            {
                var tr = gm.bow.trajectory;
                System.Console.WriteLine($"[AP] traj enabled={(tr ? tr.enabled : false)} count={(tr ? tr.positionCount : -1)} p0={(tr && tr.positionCount > 0 ? tr.GetPosition(0) : Vector3.zero)} pN={(tr && tr.positionCount > 1 ? tr.GetPosition(tr.positionCount - 1) : Vector3.zero)} draw={gm.bow.CurrentDraw:F2} mat={(tr ? tr.sharedMaterial : null)} w={(tr ? tr.widthMultiplier : 0)}");
                Snap(gm, "sim_08_full_draw");
            }
            Drawing = false;
            ReleasedThisFrame = true;
            m_Shot++;
            yield return null;
            yield return null;
            ReleasedThisFrame = false;
            if (capture)
            {
                yield return new WaitForSeconds(0.25f);
                Snap(gm, "sim_09_arrow_flight");
            }
            // look back at the battlefield
            if (gm.simRig && gm.Castle) gm.simRig.LookAt(Vector3.Lerp(gm.Castle.Center, cat.transform.position, 0.45f) + Vector3.up * 2f);
        }

        static Soldier NearestRaider(GameManager gm, float maxDistToGate)
        {
            if (!gm.waves || !gm.Castle) return null;
            var gate = gm.Castle.Gate ? gm.Castle.Gate.WorldBounds.center : gm.Castle.Center;
            Soldier best = null; float bd = maxDistToGate;
            foreach (var s in gm.waves.Soldiers)
            {
                if (!s || !s.Alive) continue;
                float d = Vector3.Distance(s.transform.position, gate);
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        void Log(GameManager gm)
        {
            System.Console.WriteLine($"[AP] t={Time.time:F1} state={gm.State} castle={(gm.Castle ? gm.Castle.Integrity : 0):F2} treb={(gm.Trebuchet ? gm.Trebuchet.Health : 0):F1} wave={(gm.waves ? gm.waves.Wave : 0)} raiders={(gm.waves ? gm.waves.AliveCount : 0)} gate={(gm.Castle && gm.Castle.Gate ? gm.Castle.Gate.Health : 0):F2} fireballs={Fireball.Active.Count} errors={m_Errors}");
        }

        static void UICapture(GameManager gm, Canvas canvas)
        {
            if (!canvas) return;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = gm.cam;
            canvas.planeDistance = gm.cam.nearClipPlane * 1.5f;
        }

        // Bird's-eye shot of the whole layout (castle, town, catapult) for checking placement.
        static void Overview(GameManager gm)
        {
            var go = new GameObject("OverviewCam");
            var c = go.AddComponent<Camera>();
            c.CopyFrom(gm.cam);
            c.fieldOfView = 55f;
            var mid = Vector3.Lerp(gm.Castle.transform.position, gm.Trebuchet.transform.position, 0.35f);
            go.transform.position = mid + new Vector3(55f, 70f, -45f);
            go.transform.LookAt(mid);
            var rt = RenderTexture.GetTemporary(1600, 900, 24);
            c.targetTexture = rt;
            c.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(OutDir, "sim_00_overview.png"), tex.EncodeToPNG());
            c.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(go);
        }

        static void Snap(GameManager gm, string name)
        {
            var cam = gm.cam;
            var rt = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            System.Console.WriteLine("[AP] snap " + name);
        }
    }
}
