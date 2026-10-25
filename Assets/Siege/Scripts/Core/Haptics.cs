using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Siege
{
    // Core Haptics patterns for every game event. Authored here, played by Plugins/iOS/SiegeHaptics.mm.
    // In the editor everything is a no-op (with an optional log to verify triggering).
    public static class Haptics
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern bool SiegeHaptics_Init();
        [DllImport("__Internal")] static extern void SiegeAudio_ConfigureSession();
        [DllImport("__Internal")] static extern void SiegeHaptics_Play(int count, float[] times, float[] durations, float[] intensities, float[] sharpnesses, int[] kinds, float decay);
        [DllImport("__Internal")] static extern void SiegeHaptics_StartContinuous(float intensity, float sharpness);
        [DllImport("__Internal")] static extern void SiegeHaptics_UpdateContinuous(float intensity, float sharpness);
        [DllImport("__Internal")] static extern void SiegeHaptics_StopContinuous();
#endif
        public static bool LogInEditor;
        static bool s_Init;

        struct Ev { public float t, d, i, s; public int k; }
        static readonly List<Ev> s_Events = new List<Ev>(64);

        public static void Init()
        {
            if (s_Init) return;
            s_Init = true;
            ConfigureAudio();
#if UNITY_IOS && !UNITY_EDITOR
            SiegeHaptics_Init();
#endif
        }

        // Re-applied on resume: Unity may reset the audio session category when the app regains focus.
        public static void ConfigureAudio()
        {
#if UNITY_IOS && !UNITY_EDITOR
            SiegeAudio_ConfigureSession();
#endif
        }

        static void Tap(float t, float intensity, float sharpness) =>
            s_Events.Add(new Ev { t = t, i = Mathf.Clamp01(intensity), s = Mathf.Clamp01(sharpness), k = 0 });

        static void Buzz(float t, float duration, float intensity, float sharpness) =>
            s_Events.Add(new Ev { t = t, d = duration, i = Mathf.Clamp01(intensity), s = Mathf.Clamp01(sharpness), k = 1 });

        static void Flush(string name, float decay = 0f)
        {
            if (LogInEditor && Application.isEditor) Debug.Log($"[Haptics] {name} ({s_Events.Count} events)");
#if UNITY_IOS && !UNITY_EDITOR
            int n = s_Events.Count;
            var t = new float[n]; var d = new float[n]; var i = new float[n]; var s = new float[n]; var k = new int[n];
            for (int j = 0; j < n; j++) { t[j] = s_Events[j].t; d[j] = s_Events[j].d; i[j] = s_Events[j].i; s[j] = s_Events[j].s; k[j] = s_Events[j].k; }
            SiegeHaptics_Play(n, t, d, i, s, k, decay);
#endif
            s_Events.Clear();
        }

        // ---------------------------------------------------------------- bow
        public static void BowDrawStart()
        {
#if UNITY_IOS && !UNITY_EDITOR
            SiegeHaptics_StartContinuous(0.1f, 0.3f);
#endif
        }

        public static void BowDrawUpdate(float draw)
        {
#if UNITY_IOS && !UNITY_EDITOR
            SiegeHaptics_UpdateContinuous(0.08f + draw * draw * 0.55f, 0.2f + draw * 0.4f);
#endif
        }

        public static void BowDrawStop()
        {
#if UNITY_IOS && !UNITY_EDITOR
            SiegeHaptics_StopContinuous();
#endif
        }

        public static void BowCreak(float draw)
        {
            Tap(0, 0.25f + draw * 0.35f, 0.75f);
            Flush("creak");
        }

        public static void BowRelease(float power)
        {
            Tap(0, 0.6f + power * 0.4f, 1f);
            Buzz(0.01f, 0.16f, 0.5f * power + 0.2f, 0.85f);
            Tap(0.07f, 0.35f * power, 0.5f);
            Flush("release", 0.15f);
        }

        // ---------------------------------------------------------------- arrow impacts
        public static void ArrowHitWood() { Tap(0, 0.65f, 0.55f); Tap(0.05f, 0.25f, 0.3f); Flush("arrowWood"); }
        public static void ArrowHitStone() { Tap(0, 0.4f, 1f); Flush("arrowStone"); }
        public static void HitConfirm() { Tap(0, 0.8f, 0.9f); Tap(0.09f, 0.55f, 0.9f); Flush("hitConfirm"); }

        // ---------------------------------------------------------------- catapult + fireball
        public static void CatapultLaunch(float proximity)
        {
            float p = Mathf.Lerp(0.25f, 0.7f, proximity);
            Buzz(0, 0.35f, p, 0.1f);
            Tap(0, p, 0.3f);
            Flush("launch", 0.3f);
        }

        public static void Crank() { Tap(0, 0.12f, 0.9f); Flush("crank"); }

        public static void Heartbeat(float urgency)
        {
            float i = Mathf.Lerp(0.3f, 0.9f, urgency);
            Tap(0, i, 0.2f);
            Tap(0.13f, i * 0.7f, 0.15f);
            Flush("heartbeat");
        }

        public static void CastleImpact(float strength)
        {
            strength = Mathf.Clamp01(strength);
            Tap(0, 0.7f + 0.3f * strength, 0.6f);
            Buzz(0, 0.5f + strength * 0.5f, 0.9f * strength + 0.1f, 0.15f);
            for (int n = 0; n < 7; n++)
                Tap(0.12f + n * Random.Range(0.06f, 0.13f), Random.Range(0.2f, 0.55f) * strength, Random.Range(0.4f, 1f));
            Flush("castleImpact", 0.6f);
        }

        public static void Collapse()
        {
            Buzz(0, 1.6f, 1f, 0.05f);
            for (int n = 0; n < 14; n++)
                Tap(0.05f + n * 0.11f + Random.Range(0f, 0.05f), Random.Range(0.3f, 0.8f), Random.Range(0.2f, 0.9f));
            Flush("collapse", 1.4f);
        }

        public static void NearMiss()
        {
            Buzz(0, 0.45f, 1f, 0.35f);
            Tap(0.02f, 1f, 0.8f);
            Tap(0.2f, 0.6f, 0.4f);
            Flush("nearMiss", 0.4f);
        }

        public static void TNT()
        {
            Tap(0, 1f, 1f);
            Buzz(0, 0.9f, 1f, 0.2f);
            for (int n = 0; n < 10; n++) Tap(0.15f + n * 0.09f, Random.Range(0.3f, 0.7f), Random.Range(0.6f, 1f));
            Flush("tnt", 0.8f);
        }

        public static void CatapultDestroyed()
        {
            Tap(0, 1f, 1f);
            Buzz(0, 1.8f, 1f, 0.3f);
            for (int n = 0; n < 18; n++) Tap(0.1f + n * 0.1f, Random.Range(0.3f, 0.9f), Random.Range(0.3f, 1f));
            Flush("catapultDestroyed", 1.5f);
        }

        public static void PieceLand(float strength) { Tap(0, 0.3f + 0.5f * strength, 0.5f); Flush("land"); }
        public static void UI() { Tap(0, 0.4f, 0.8f); Flush("ui"); }

        public static void Victory()
        {
            float[] beats = { 0, 0.18f, 0.36f, 0.54f, 0.9f };
            foreach (var b in beats) Tap(b, 0.8f, 0.7f);
            Buzz(0.9f, 0.6f, 0.6f, 0.5f);
            Flush("victory", 0.5f);
        }
    }
}
