using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Offline renders for checking models and layouts without a device (batchmode friendly).
public static class PreviewRenderer
{
    public static Color BackgroundColor = new Color(0.55f, 0.68f, 0.82f);
    public static string OutDir => "/tmp/claude-501/previews";

    public static GameObject NewStage()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.75f, 0.8f, 0.9f);
        RenderSettings.ambientEquatorColor = new Color(0.55f, 0.55f, 0.55f);
        RenderSettings.ambientGroundColor = new Color(0.3f, 0.28f, 0.25f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.transform.localScale = Vector3.one * 50f;
        floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.62f, 0.56f, 0.48f) };
        return light.gameObject;
    }

    public static void Shoot(string name, Vector3 camPos, Vector3 target, float fov = 40f, int w = 1280, int h = 720)
    {
        Directory.CreateDirectory(OutDir);
        var camGo = new GameObject("PreviewCam");
        var cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = fov;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BackgroundColor;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1000f;
        camGo.transform.position = camPos;
        camGo.transform.LookAt(target);
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        // Batchmode renders bind stale textures under the SRP Batcher; render without it.
        var urp = (UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        bool batcher = urp.useSRPBatcher;
        urp.useSRPBatcher = false;
        cam.Render();
        urp.useSRPBatcher = batcher;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), tex.EncodeToPNG());
        Object.DestroyImmediate(camGo);
        Object.DestroyImmediate(rt);
    }

    // Lays out a grid of prefabs for an asset overview.
    public static void KingdomSheet()
    {
        NewStage();
        var names = new List<string> { "tower_blue_001", "tower_001", "wall_001", "wall_002", "wall_003", "wall_004", "arch_001", "flag_blue_001", "ruin_001", "ruin_002", "ground_002", "ground_003", "box_tnt_001", "barrel_001", "stairs_001", "bridge_001" };
        int i = 0;
        foreach (var n in names)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/ithappy/Location_Kingdom/Prefabs/{n}.prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p);
            go.transform.position = new Vector3((i % 4) * 12f, 0, (i / 4) * -12f);
            i++;
        }
        Shoot("kingdom_sheet", new Vector3(18f, 45f, 25f), new Vector3(18f, 0f, -18f), 50f, 1600, 1200);
    }

    public static GameObject PlaceModel(string path, Vector3 pos, float yaw = 0f, float scale = 1f)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/ithappy/Location_Kingdom/Materials/Color.mat");
        var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        go.transform.localScale *= scale;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
        return go;
    }

    public static void Models()
    {
        NewStage();
        PlaceModel("Assets/Siege/Models/Catapult.fbx", Vector3.zero, 180f);
        PlaceModel("Assets/Siege/Models/Knight.fbx", new Vector3(3.2f, 0, 1.5f), 200f);
        PlaceModel("Assets/Siege/Models/Bow.fbx", new Vector3(-3.5f, 1.2f, 1f), 160f, 2f);
        PlaceModel("Assets/Siege/Models/Arrow.fbx", new Vector3(-4.5f, 1.2f, 0.5f), 90f, 2f);
        Shoot("models_front", new Vector3(5f, 4.5f, 10f), new Vector3(0, 1.6f, 0));
        Shoot("models_side", new Vector3(11f, 3.5f, 0.5f), new Vector3(0, 1.6f, 0));
        Shoot("knight_close", new Vector3(4.6f, 2.0f, 5.2f), new Vector3(3.2f, 1.25f, 1.5f), 30f);
        Shoot("knight_back", new Vector3(1.8f, 2.0f, -2.5f), new Vector3(3.2f, 1.25f, 1.5f), 30f);
        Shoot("bow_close", new Vector3(-3.0f, 1.5f, 3.5f), new Vector3(-4f, 1.2f, 0.8f), 30f);
    }

    public static void Prefabs()
    {
        NewStage();
        var castle = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Siege/Prefabs/Castle.prefab"));
        var cat = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Siege/Prefabs/Catapult.prefab"));
        cat.transform.SetPositionAndRotation(new Vector3(0, 0, 88), Quaternion.Euler(0, 180, 0));
        cat.transform.localScale = Vector3.one * 1.5f;
        Shoot("castle_player", new Vector3(6.6f, 18f, -33f), new Vector3(0, 3f, 4f), 52f);
        Shoot("castle_front", new Vector3(10f, 14f, 30f), new Vector3(0, 4f, 0), 45f);
        Shoot("castle_top", new Vector3(0.5f, 45f, -8f), new Vector3(0, 0, 0), 45f);
        Shoot("catapult_close", new Vector3(10f, 8f, 76f), new Vector3(0, 3f, 88f), 45f);
        Shoot("battle_wide", new Vector3(6.6f, 18f, -33f), new Vector3(0, 4f, 40f), 52f);
    }

    public static void Town()
    {
        NewStage();
        var t = PlaceModel("Assets/Siege/Models/Town.fbx", Vector3.zero, 180f);
        Shoot("town_front", new Vector3(-24f, 12f, -28f), new Vector3(-24f, 2.5f, 0), 60f, 1600, 900);
        Shoot("town_angle", new Vector3(-2f, 9f, -10f), new Vector3(-8f, 3f, 0), 50f, 1600, 900);
    }
}
