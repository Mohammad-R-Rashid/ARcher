using UnityEngine;

namespace Siege
{
    // Abstraction over how the bow is driven. Today: touch on the phone. Later: the IMU glove
    // (draw = finger/hand pull, release = open hand, aim = glove orientation or phone camera).
    public interface IBowInput
    {
        // 0..1 how far the string is pulled back.
        float Draw { get; }
        // True on the frame the string is let go.
        bool ReleasedThisFrame { get; }
        // True while the player is drawing.
        bool Drawing { get; }
        // World-space aim direction, or null to aim down the camera's centre.
        Vector3? AimDirection { get; }
    }
}
