// Applies imagegen surfaces and low-key procedural entrance lighting, without rebuilding menus.
using System;
using System.IO;
using FPLab.Gameplay.Maze;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SetupEnvironmentVisuals
{
    private const string Generated = "Assets/Art/Environment/ResidentialV2/";
    private const string Derived = "Assets/Art/Environment/DerivedResidentialV2/";

    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before applying environment visuals.");
        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        MazeAtmosphere atmosphere = maze.GetComponent<MazeAtmosphere>();

        Material plaster = Surface("EntrancePlaster", "Plaster", Color.white, 0.06f, 0f, 0.1f, 0.5f);
        Material paint = Surface("EntrancePaint", "Paint", Color.white, 0.24f, 0f, 0.08f, 0.5f);
        Material floor = Surface("EntranceFloor", "Floor", Color.white, 0.12f, 0f, 0.12f, 0.5f);
        Material ceiling = Surface("EntranceCeiling", "Ceiling", Color.white, 0.04f, 0f, 0.08f, 0.5f);
        Material doors = Surface("EntranceDoors", "Door", Color.white, 0.16f, 0f, 0.1f, 1f);
        Material trim = Surface("EntranceTrim", "Trim", new Color(0.42f, 0.4f, 0.35f), 0.2f, 0.12f, 0.08f, 1f);
        Material doorFrames = Surface("EntranceDoorFrames", "Trim", Color.white, 0.2f, 0f, 0.08f, 1f);
        Material metal = Surface("EntranceDomesticMetal", "Trim", new Color(0.28f, 0.27f, 0.24f), 0.25f, 0.25f, 0.06f, 1f);
        Material mailboxes = Surface("EntranceMailboxes", "Paint", new Color(0.68f, 0.73f, 0.65f), 0.2f, 0.08f, 0.08f, 1f);
        Material lens = Surface("EntranceLamp", "Glass", Color.white, 0.2f, 0f, 0.06f, 1f);
        Emission(lens, new Color(3.8f, 3.2f, 2.2f));
        Material start = Surface("EntranceStart", "Floor", Color.white, 0.12f, 0f, 0.12f, 0.5f);
        Material exit = Surface("EntranceExit", "Trim", new Color(0.5f, 0.57f, 0.43f), 0.18f, 0.05f, 0.08f, 1f);

        Shader insectShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/LampInsects.shader");
        if (insectShader == null) throw new InvalidOperationException("Import the lamp insect shader first.");
        const string insectPath = "Assets/Materials/Maze/EntranceLampInsects.mat";
        Material insects = AssetDatabase.LoadAssetAtPath<Material>(insectPath);
        if (insects == null)
        {
            insects = new Material(insectShader);
            AssetDatabase.CreateAsset(insects, insectPath);
        }
        insects.shader = insectShader;
        EditorUtility.SetDirty(insects);

        var mazeFields = new SerializedObject(maze);
        mazeFields.FindProperty("wallMaterial").objectReferenceValue = plaster;
        mazeFields.FindProperty("floorMaterial").objectReferenceValue = floor;
        mazeFields.FindProperty("startMaterial").objectReferenceValue = start;
        mazeFields.FindProperty("exitMaterial").objectReferenceValue = exit;
        mazeFields.FindProperty("generateOnAwake").boolValue = false;
        mazeFields.FindProperty("cellSize").floatValue = 2.6f;
        mazeFields.FindProperty("wallHeight").floatValue = 2.8f;
        mazeFields.ApplyModifiedPropertiesWithoutUndo();
        var details = new SerializedObject(atmosphere);
        details.FindProperty("doorMaterial").objectReferenceValue = doors;
        details.FindProperty("trimMaterial").objectReferenceValue = trim;
        details.FindProperty("ceilingMaterial").objectReferenceValue = ceiling;
        details.FindProperty("fixtureMaterial").objectReferenceValue = lens;
        details.FindProperty("wallBandMaterial").objectReferenceValue = paint;
        details.FindProperty("doorFrameMaterial").objectReferenceValue = doorFrames;
        details.FindProperty("metalDetailMaterial").objectReferenceValue = metal;
        details.FindProperty("mailboxMaterial").objectReferenceValue = mailboxes;
        details.FindProperty("insectMaterial").objectReferenceValue = insects;
        details.FindProperty("numberMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Maze/EntranceDoorNumbers.mat");
        details.FindProperty("maximumLights").intValue = 32;
        details.FindProperty("lampIntensity").floatValue = 2.9f;
        details.ApplyModifiedPropertiesWithoutUndo();

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.006f, 0.007f, 0.005f);
        RenderSettings.reflectionIntensity = 0.005f;
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.001f, 0.0012f, 0.001f);
        RenderSettings.fogDensity = 0.008f;
        GameObject sun = GameObject.Find("Directional Light");
        if (sun != null) sun.SetActive(false);
        Camera camera = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>().GetComponentInChildren<Camera>();
        camera.backgroundColor = new Color(0.001f, 0.0012f, 0.001f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
        cameraData.requiresDepthOption = CameraOverrideOption.On;
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;
        cameraData.dithering = true;
        UniversalRenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (pipeline != null)
        {
            pipeline.supportsCameraDepthTexture = true;
            EditorUtility.SetDirty(pipeline);
        }
        ConfigureGrading();

        // Rebuild the editor preview as part of authoring. Runtime generation
        // still happens under the loading screen, with the same material setup.
        maze.Generate();
        atmosphere.Decorate();
        foreach (Light light in maze.GeneratedRoot.GetComponentsInChildren<Light>())
        {
            if (light.shadows == LightShadows.None) continue;
            var shadowFields = new SerializedObject(light.GetUniversalAdditionalLightData());
            shadowFields.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            shadowFields.FindProperty("m_UsePipelineSettings").boolValue = false;
            shadowFields.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(maze.gameObject.scene);
        EditorSceneManager.SaveScene(maze.gameObject.scene);
        AssetDatabase.SaveAssets();
        return new { saved = true, imagegenSurfaceTypes = 7, maximumWarmLights = 32, windows = 0, lampFlies = true, previewRebuilt = true };
    }

    private static Material Surface(string name, string texture, Color tint, float smoothness, float metallic, float bump, float tiling)
    {
        Texture2D albedo = ImportAlbedo(texture);
        Texture2D normal = ImportNormal(texture);
        string path = "Assets/Materials/Maze/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", albedo);
        material.SetTextureScale("_BaseMap", Vector2.one * tiling);
        material.SetTexture("_BumpMap", normal);
        material.SetColor("_BaseColor", tint);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_BumpScale", bump);
        material.EnableKeyword("_NORMALMAP");
        material.DisableKeyword("_EMISSION");
        material.SetTexture("_EmissionMap", null);
        material.SetColor("_EmissionColor", Color.black);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Texture2D ImportAlbedo(string name)
    {
        string path = Generated + name + "_Albedo.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Finish imagegen first: " + path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = name == "Door" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.isReadable = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Texture2D ImportNormal(string name)
    {
        Directory.CreateDirectory(Derived);
        string path = Derived + name + "_Normal.png";
        File.Copy(Generated + name + "_Albedo.png", path, true);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.NormalMap;
        importer.convertToNormalmap = true;
        importer.normalmapFilter = TextureImporterNormalFilter.Standard;
        importer.heightmapScale = 0.015f;
        importer.mipmapEnabled = true;
        importer.wrapMode = name == "Door" ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.isReadable = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static void Emission(Material material, Color color)
    {
        material.SetTexture("_EmissionMap", material.GetTexture("_BaseMap"));
        material.SetColor("_EmissionColor", color);
        material.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
    }

    private static void ConfigureGrading()
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
        if (profile.TryGet(out Bloom bloom))
        {
            bloom.intensity.Override(0.06f);
            bloom.threshold.Override(1.4f);
            bloom.scatter.Override(0.25f);
        }
        if (profile.TryGet(out Vignette vignette))
        {
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.4f);
        }
        if (!profile.TryGet(out ColorAdjustments grading))
        {
            grading = profile.Add<ColorAdjustments>(true);
            grading.name = "Entrance Color Grading";
            AssetDatabase.AddObjectToAsset(grading, profile);
        }
        grading.postExposure.Override(0f);
        grading.contrast.Override(5f);
        grading.saturation.Override(-6f);
        foreach (VolumeComponent component in profile.components) EditorUtility.SetDirty(component);
        EditorUtility.SetDirty(profile);
    }
}
