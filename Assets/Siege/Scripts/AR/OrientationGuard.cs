using System.Collections;
using UnityEngine;

namespace Siege
{
    // The AR camera image came out rotated 90 degrees against the HUD on some builds (iOS 27 SDK, Unity 6000.6.7).
    // The AR camera background and projection follow Screen.orientation, so this logs what the player sees and,
    // a moment after launch, re-applies the orientation: forcing the change makes iOS/ARKit recompute the camera
    // transform for the real interface orientation (the usual fix when the launch-time orientation is stale).
    public class OrientationGuard : MonoBehaviour
    {
        public static bool reapply = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindAnyObjectByType<OrientationGuard>()) return;
            var go = new GameObject("OrientationGuard");
            DontDestroyOnLoad(go);
            go.AddComponent<OrientationGuard>();
        }

        IEnumerator Start()
        {
            Log("launch");
            yield return new WaitForSeconds(1f);
            if (reapply)
            {
                // Nudge: go to the one fixed landscape and back to auto-rotation (landscape only).
                Screen.autorotateToPortrait = false;
                Screen.autorotateToPortraitUpsideDown = false;
                Screen.autorotateToLandscapeLeft = true;
                Screen.autorotateToLandscapeRight = true;
                var was = Screen.orientation;
                Screen.orientation = was == ScreenOrientation.LandscapeRight ? ScreenOrientation.LandscapeRight : ScreenOrientation.LandscapeLeft;
                yield return null;
                yield return null;
                Screen.orientation = ScreenOrientation.AutoRotation;
                Log("re-applied");
            }
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForSeconds(1.5f);
                Log("t+" + (i + 1));
            }
        }

        static void Log(string when)
        {
            Debug.Log($"[Orient] {when}: Screen.orientation={Screen.orientation} {Screen.width}x{Screen.height} device={Input.deviceOrientation}");
        }
    }
}
