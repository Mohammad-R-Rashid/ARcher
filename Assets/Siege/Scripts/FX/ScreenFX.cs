using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Siege
{
    // Camera-side juice. In AR the tracked camera can't be moved, so "shake" jolts the camera offset
    // parent by a few millimetres, and impacts pulse vignette + chromatic aberration instead.
    public class ScreenFX : MonoBehaviour
    {
        public Transform shakeTarget;      // XR Origin camera offset (or editor camera rig)
        public Volume volume;
        public Image heatOverlay;          // orange edge glow for near misses / close blasts
        public Image damageOverlay;        // red edge pulse when the castle is hit

        static ScreenFX s_I;
        Vignette m_Vignette;
        ChromaticAberration m_CA;
        float m_Trauma;
        float m_Punch;
        float m_Heat;
        float m_Damage;
        Vector3 m_ShakeBase;
        Quaternion m_ShakeRotBase = Quaternion.identity;
        float m_BaseVignette = 0.18f;
        Coroutine m_Slow;

        void Awake()
        {
            s_I = this;
            if (volume && volume.profile)
            {
                volume.profile.TryGet(out m_Vignette);
                volume.profile.TryGet(out m_CA);
            }
            if (shakeTarget) { m_ShakeBase = shakeTarget.localPosition; m_ShakeRotBase = shakeTarget.localRotation; }
        }

        public static void Shake(float amount) { if (s_I) s_I.m_Trauma = Mathf.Min(1f, s_I.m_Trauma + amount); }
        public static void Punch(float amount) { if (s_I) s_I.m_Punch = Mathf.Min(1f, s_I.m_Punch + amount); }
        public static void Heat(float amount) { if (s_I) s_I.m_Heat = Mathf.Min(1f, s_I.m_Heat + amount); }
        public static void Damage(float amount) { if (s_I) s_I.m_Damage = Mathf.Min(1f, s_I.m_Damage + amount); }

        public static void SlowMo(float scale, float realSeconds)
        {
            if (!s_I) return;
            if (s_I.m_Slow != null) s_I.StopCoroutine(s_I.m_Slow);
            s_I.m_Slow = s_I.StartCoroutine(s_I.SlowRoutine(scale, realSeconds));
        }

        IEnumerator SlowRoutine(float scale, float realSeconds)
        {
            SetTimeScale(scale);
            yield return new WaitForSecondsRealtime(realSeconds);
            for (float t = 0; t < 0.4f; t += Time.unscaledDeltaTime)
            {
                SetTimeScale(Mathf.Lerp(scale, 1f, t / 0.4f));
                yield return null;
            }
            SetTimeScale(1f);
            m_Slow = null;
        }

        static void SetTimeScale(float s)
        {
            Time.timeScale = s;
            Time.fixedDeltaTime = (1f / 60f) * s;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (shakeTarget)
            {
                // Shake is in the target's local space (metres under a scaled XR origin): a few cm of jolt
                // plus a couple of degrees of roll/pitch so the whole room appears to rock on big hits.
                float k = m_Trauma * m_Trauma;
                float t = Time.unscaledTime * 32f;
                var n = new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, Mathf.PerlinNoise(t, t + 3f) - 0.5f);
                shakeTarget.localPosition = m_ShakeBase + n * k * 0.05f;
                shakeTarget.localRotation = m_ShakeRotBase * Quaternion.Euler(n.y * k * 5f, n.x * k * 3f, n.z * k * 6f);
            }
            m_Trauma = Mathf.Max(0, m_Trauma - dt * 1.6f);
            m_Punch = Mathf.Max(0, m_Punch - dt * 2.2f);
            m_Heat = Mathf.Max(0, m_Heat - dt * 1.3f);
            m_Damage = Mathf.Max(0, m_Damage - dt * 1.5f);
            if (m_Vignette)
            {
                m_Vignette.intensity.value = m_BaseVignette + m_Punch * 0.3f + m_Damage * 0.15f;
                m_Vignette.color.value = Color.Lerp(Color.black, new Color(0.45f, 0.05f, 0f), Mathf.Max(m_Damage, m_Heat));
            }
            if (m_CA) m_CA.intensity.value = Mathf.Clamp01(m_Punch * 0.9f + m_Heat * 0.4f);
            if (heatOverlay) heatOverlay.color = new Color(1f, 0.55f, 0.15f, m_Heat * 0.85f);
            if (damageOverlay) damageOverlay.color = new Color(0.9f, 0.08f, 0.02f, m_Damage * 0.6f);
        }

        void OnDisable() { SetTimeScale(1f); }
    }
}
