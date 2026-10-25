using UnityEditor;
using UnityEditor.SceneManagement;

namespace Siege.EditorTools
{
    // Enters play mode on the main scene from the command line; AutoPilot drives the round and quits.
    public static class SimRunner
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Siege/Scenes/Main.unity");
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            EditorApplication.isPlaying = true;
        }
    }
}
