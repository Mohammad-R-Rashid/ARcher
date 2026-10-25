using System.Collections.Generic;
using UnityEngine;

namespace Siege
{
    // File names in Assets/Siege/Audio match these ids (snake_case).
    public enum Sound
    {
        BowDraw, BowRelease, ArrowWhoosh, ArrowHitWood, ArrowHitStone,
        CatapultCrank, CatapultLaunch, CatapultSlam,
        FireballLoop, FireballFlyby, Explosion, ExplosionTnt, StoneCrumble, WoodBreak, FireCrackle,
        Thud, WarHorn, Victory, Defeat, Coins, UiTick, HitMarker, KnightGrunt,
    }

    // Pooled 3D/2D one-shots and loops. Spatial rolloff is in game units.
    public class Sfx : MonoBehaviour
    {
        public AudioClip[] clips = new AudioClip[System.Enum.GetValues(typeof(Sound)).Length];
        public float minDistance = 12f;
        public float maxDistance = 400f;

        static Sfx s_Instance;
        readonly List<AudioSource> m_Pool = new List<AudioSource>();
        int m_Next;

        void Awake()
        {
            s_Instance = this;
            for (int i = 0; i < 24; i++) m_Pool.Add(MakeSource("sfx" + i));
        }

        AudioSource MakeSource(string n)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = minDistance;
            src.maxDistance = maxDistance;
            src.dopplerLevel = 0f;
            return src;
        }

        public static AudioSource Play(Sound id, Vector3 pos, float volume = 1f, float pitchJitter = 0.06f, float spatial = 1f)
        {
            if (s_Instance == null) return null;
            var clip = s_Instance.clips[(int)id];
            if (clip == null) return null;
            var src = s_Instance.m_Pool[s_Instance.m_Next];
            s_Instance.m_Next = (s_Instance.m_Next + 1) % s_Instance.m_Pool.Count;
            src.transform.position = pos;
            src.clip = clip;
            src.loop = false;
            src.volume = volume;
            src.spatialBlend = spatial;
            src.pitch = (1f + Random.Range(-pitchJitter, pitchJitter)) * Time.timeScale.Remap01(0.2f, 1f, 0.6f, 1f);
            src.Play();
            return src;
        }

        public static AudioClip ClipFor(Sound id) => s_Instance ? s_Instance.clips[(int)id] : null;

        public static void Play2D(Sound id, float volume = 1f) =>
            Play(id, s_Instance ? s_Instance.transform.position : Vector3.zero, volume, 0f, 0f);

        // A dedicated looping source attached to a moving object (fireball roar, fires).
        public static AudioSource Loop(Sound id, Transform follow, float volume = 1f)
        {
            if (s_Instance == null) return null;
            var clip = s_Instance.clips[(int)id];
            if (clip == null) return null;
            var src = follow.gameObject.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = volume;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = s_Instance.minDistance * 0.6f;
            src.maxDistance = s_Instance.maxDistance;
            src.dopplerLevel = 0f;
            src.time = Random.Range(0f, clip.length);
            src.Play();
            return src;
        }
    }

    public static class MathExt
    {
        public static float Remap01(this float v, float a, float b, float outA, float outB) =>
            Mathf.Lerp(outA, outB, Mathf.InverseLerp(a, b, v));

        public static float Damp(float current, float target, float lambda, float dt) =>
            Mathf.Lerp(current, target, 1f - Mathf.Exp(-lambda * dt));

        public static Vector3 Damp(Vector3 current, Vector3 target, float lambda, float dt) =>
            Vector3.Lerp(current, target, 1f - Mathf.Exp(-lambda * dt));
    }
}
