using UnityEngine;

namespace Siege
{
    public enum HitSource { Arrow, Fireball, Explosion }

    public struct HitInfo
    {
        public float damage;
        public Vector3 point;
        public Vector3 direction;
        public float force;
        public HitSource source;
        public Transform part;      // collider transform that was hit (hit zones), if known
        public float power;         // 0-1 bow draw behind an arrow hit
    }

    public interface IDamageable
    {
        void TakeHit(HitInfo hit);
    }

    public static class Layers
    {
        public const int Castle = 8;
        public const int Enemy = 9;
        public const int Projectile = 10;
        public const int Debris = 11;
        public const int Environment = 12;

        public static int Mask(params int[] layers)
        {
            int m = 0;
            foreach (var l in layers) m |= 1 << l;
            return m;
        }
    }

    // Gameplay tuning in one place. 1 unit = 1 game metre (the XR origin is scaled by GameManager.unitsPerMeter).
    public static class Tune
    {
        public const float Gravity = 13f;               // m/s^2, a touch heavier than Earth for game feel

        public const float ArrowMinSpeed = 22f;         // game m/s: fast and flat enough to read as an arrow at room scale
        public const float ArrowMaxSpeed = 160f;
        public const float ArrowCooldown = 0.35f;
        public const float ArrowLifetime = 14f;
        public const int MaxArrows = 30;
        public const float ArrowDamageMin = 0.6f;       // arrow damage scales with the draw
        public const float ArrowDamageMax = 1.6f;

        public const float FireballFlightTime = 2.7f;
        public const float FireballRadius = 4.6f;
        public const float CatapultReload = 2.4f;
        public const float CatapultAim = 0.9f;
        public const float CatapultCooldown = 0.8f;
        public const int NearMissEvery = 4;
        public const float MissChance = 0.12f;

        public const float CatapultHealth = 22f;
        public const float ArrowDamage = 1f;            // reference arrow hit (knights take KnightHitDamage per unit of it)
        public const float KnightHitDamage = 2f;
        public const float TNTDamage = 5f;

        public const int MaxDebris = 160;
        public const float DebrisLifetime = 4.5f;
    }
}
