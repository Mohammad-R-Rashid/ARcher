using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Siege
{
    // Lights the virtual castle like the real room: ARKit's measured brightness drives the sun and
    // ambient strength, its colour temperature tints both (warm living room vs cool daylight).
    public class ARLightMatch : MonoBehaviour
    {
        public ARCameraManager cameraManager;
        public Light sun;
        public float baseIntensity = 1.3f;

        Color m_Sky, m_Equator, m_Ground;
        float m_K = 1f;
        Color m_Tint = Color.white;

        void Awake()
        {
            m_Sky = RenderSettings.ambientSkyColor;
            m_Equator = RenderSettings.ambientEquatorColor;
            m_Ground = RenderSettings.ambientGroundColor;
        }

        void OnEnable() { if (cameraManager) cameraManager.frameReceived += OnFrame; }
        void OnDisable() { if (cameraManager) cameraManager.frameReceived -= OnFrame; }

        void OnFrame(ARCameraFrameEventArgs args)
        {
            var le = args.lightEstimation;
            float k = m_K;
            if (le.averageIntensityInLumens.HasValue) k = Mathf.Clamp(le.averageIntensityInLumens.Value / 1000f, 0.3f, 1.4f);
            else if (le.averageBrightness.HasValue) k = Mathf.Clamp(le.averageBrightness.Value * 2f, 0.3f, 1.4f);
            m_K = Mathf.Lerp(m_K, k, 0.08f);
            if (le.averageColorTemperature.HasValue)
            {
                var t = Mathf.CorrelatedColorTemperatureToRGB(Mathf.Clamp(le.averageColorTemperature.Value, 2500f, 9000f));
                m_Tint = Color.Lerp(m_Tint, t / Mathf.Max(t.maxColorComponent, 0.001f), 0.08f);
            }
            sun.intensity = baseIntensity * m_K;
            sun.color = m_Tint;
            float amb = Mathf.Lerp(0.55f, 1.1f, Mathf.InverseLerp(0.3f, 1.4f, m_K));
            RenderSettings.ambientSkyColor = m_Sky * m_Tint * amb;
            RenderSettings.ambientEquatorColor = m_Equator * m_Tint * amb;
            RenderSettings.ambientGroundColor = m_Ground * m_Tint * amb;
        }
    }
}
