using System.IO;
using UnityEditor;
using UnityEditor.XR.ARKit;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

// One-shot project configuration: URP asset with the AR background feature, iOS/ARKit player
// settings, XR loader, and read/write meshes for runtime fracturing. Safe to re-run.
public static class ProjectSetup
{
    const string SettingsDir = "Assets/Settings";

    [MenuItem("Siege/Setup/Configure Project")]
    public static void Run()
    {
        Directory.CreateDirectory(SettingsDir);
        ConfigureURP();
        ConfigurePlayer();
        ConfigureXR();
        ConfigureMeshImport();
        AssetDatabase.SaveAssets();
        Debug.Log("[Siege] Project configured");
    }

    static void ConfigureURP()
    {
        var rendererPath = SettingsDir + "/SiegeRenderer.asset";
        var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (rendererData == null)
        {
            rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, rendererPath);
        }
        rendererData.renderingMode = RenderingMode.Forward;

        // Ambient occlusion grounds the buildings (contact darkening) - applied to virtual objects only.
        if (!rendererData.rendererFeatures.Exists(f => f is ScreenSpaceAmbientOcclusion))
        {
            var ao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
            ao.name = "SSAO";
            AssetDatabase.AddObjectToAsset(ao, rendererData);
            rendererData.rendererFeatures.Add(ao);
        }
        foreach (var f in rendererData.rendererFeatures)
            if (f is ScreenSpaceAmbientOcclusion ssao)
            {
                var aso = new SerializedObject(ssao);
                var st = aso.FindProperty("m_Settings");
                st.FindPropertyRelative("Downsample").boolValue = true;
                st.FindPropertyRelative("Source").intValue = 0;          // depth only (cheaper on mobile)
                st.FindPropertyRelative("Intensity").floatValue = 1.6f;
                st.FindPropertyRelative("Radius").floatValue = 0.6f;     // game units (~5 cm at room scale)
                st.FindPropertyRelative("Samples").intValue = 2;         // low
                st.FindPropertyRelative("DirectLightingStrength").floatValue = 0.15f;
                st.FindPropertyRelative("Falloff").floatValue = 60f;
                aso.ApplyModifiedPropertiesWithoutUndo();
            }

        if (!rendererData.rendererFeatures.Exists(f => f is ARBackgroundRendererFeature))
        {
            var feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
            feature.name = "ARBackground";
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            rendererData.rendererFeatures.Add(feature);
        }
        // Keep the serialized feature map in sync with the sub-assets.
        var so = new SerializedObject(rendererData);
        var map = so.FindProperty("m_RendererFeatureMap");
        map.arraySize = rendererData.rendererFeatures.Count;
        for (int i = 0; i < rendererData.rendererFeatures.Count; i++)
        {
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(rendererData.rendererFeatures[i], out _, out long localId);
            map.GetArrayElementAtIndex(i).longValue = localId;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rendererData);

        var assetPath = SettingsDir + "/SiegeURP.asset";
        var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
        if (urp == null)
        {
            urp = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(urp, assetPath);
        }
        urp.supportsHDR = true;
        urp.msaaSampleCount = 4;
        urp.renderScale = 1f;
        urp.shadowDistance = 120f;
        urp.shadowCascadeCount = 1;
        urp.supportsCameraDepthTexture = true;   // SSAO
        urp.supportsCameraOpaqueTexture = false;
        var uso = new SerializedObject(urp);
        uso.FindProperty("m_MainLightShadowmapResolution").intValue = 2048;
        uso.FindProperty("m_SoftShadowsSupported").boolValue = true;
        uso.FindProperty("m_AdditionalLightsRenderingMode").intValue = 1; // per pixel
        uso.FindProperty("m_AdditionalLightsPerObjectLimit").intValue = 4;
        uso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(urp);

        GraphicsSettings.defaultRenderPipeline = urp;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
    }

    static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "HackTX";
        PlayerSettings.productName = "Siege AR";
        PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, "com.hacktx.siegear");
        PlayerSettings.iOS.cameraUsageDescription = "Siege AR uses the camera to place your castle in the room.";
        PlayerSettings.iOS.targetOSVersionString = "16.0";
        PlayerSettings.iOS.appleDeveloperTeamID = "7D742L5CU8";
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
        PlayerSettings.iOS.requiresPersistentWiFi = false;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetGraphicsAPIs(BuildTarget.iOS, new[] { GraphicsDeviceType.Metal });
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);

        // Legacy Input (touch) + Input System (AR tracked pose driver) together.
        var ps = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0];
        var pso = new SerializedObject(ps);
        var handler = pso.FindProperty("activeInputHandler");
        if (handler != null) handler.intValue = 2;
        pso.ApplyModifiedPropertiesWithoutUndo();

        var arkit = ARKitSettings.GetOrCreateSettings();
        arkit.requirement = ARKitSettings.Requirement.Required;
        arkit.faceTracking = false;
        EditorUtility.SetDirty(arkit);

        Physics.defaultSolverIterations = 8;
        Physics.bounceThreshold = 1.5f;
        Time.fixedDeltaTime = 1f / 60f;
    }

    static void ConfigureXR()
    {
        Directory.CreateDirectory("Assets/XR");
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget perTarget))
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perTarget, true);
        }
        var group = BuildTargetGroup.iOS;
        if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
        if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
        var settings = perTarget.SettingsForBuildTarget(group);
        settings.InitManagerOnStart = true;
        XRPackageMetadataStore.AssignLoader(settings.Manager, "UnityEngine.XR.ARKit.ARKitLoader", group);
        // ARKit only bundles its native plugin when this define is present. The Editor UI adds it when you tick
        // ARKit, but a scripted setup / batch build never does, which leaves AR "Unsupported" on device.
        var target = UnityEditor.Build.NamedBuildTarget.iOS;
        var defines = new System.Collections.Generic.HashSet<string>(PlayerSettings.GetScriptingDefineSymbols(target).Split(';'));
        defines.Remove("");
        if (defines.Add("UNITY_XR_ARKIT_LOADER_ENABLED"))
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        EditorUtility.SetDirty(perTarget);
    }

    static void ConfigureMeshImport()
    {
        // Palette atlas: mips average neighbouring swatches into grey on small/distant objects.
        if (AssetImporter.GetAtPath("Assets/ithappy/Location_Kingdom/Textures/Textures.png") is TextureImporter ti && ti.mipmapEnabled)
        {
            ti.mipmapEnabled = false;
            ti.filterMode = FilterMode.Bilinear;
            ti.SaveAndReimport();
        }

        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/ithappy/Location_Kingdom/Meshes", "Assets/Siege/Models" }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.isReadable)
            {
                mi.isReadable = true;
                mi.SaveAndReimport();
            }
        }
    }
}
