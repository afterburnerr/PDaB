using System;
using System.IO;
using FPLab.Gameplay.Maze;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SetupEntranceLamp
{
    private const string Art = "Assets/Art/Environment/EntranceLamp/";

    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before saving lamps.");
        AssetDatabase.Refresh();
        Directory.CreateDirectory(Art + "Materials");
        Directory.CreateDirectory(Art + "Prefabs");
        AssetDatabase.Refresh();
        string modelPath = Art + "Model/EntranceLamp.fbx";
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Lamp FBX is missing.");
        importer.globalScale = 1f;
        importer.useFileScale = true;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.isReadable = false;
        importer.SaveAndReimport();

        Material metal = Surface("LampMetal", "LampMetal_Diffuse", Color.white, 0.65f, 0.22f, false);
        Material fasteners = Surface("LampFasteners", "LampFasteners_Diffuse", Color.white, 0.7f, 0.3f, false);
        Material glass = Surface("LampGlassOff", "LampGlass_Diffuse", new Color(0.64f, 0.62f, 0.57f), 0f, 0.16f, false);
        Material litGlass = Surface("LampGlassOn", "LampGlass_Diffuse", Color.white, 0f, 0.16f, true);
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        GameObject on = MakePrefab(model, metal, fasteners, litGlass, true);
        GameObject off = MakePrefab(model, metal, fasteners, glass, false);
        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        var atmosphere = maze.GetComponent<MazeAtmosphere>();
        var fields = new SerializedObject(atmosphere);
        fields.FindProperty("poweredLampPrefab").objectReferenceValue = on;
        fields.FindProperty("unpoweredLampPrefab").objectReferenceValue = off;
        fields.ApplyModifiedPropertiesWithoutUndo();
        if (maze.Data == null) maze.Generate();
        atmosphere.Decorate();
        // Match the existing shadow atlas budget when rebuilding the editor preview.
        foreach (Light light in maze.GeneratedRoot.GetComponentsInChildren<Light>())
        {
            if (light.shadows == LightShadows.None) continue;
            var shadow = new SerializedObject(light.GetUniversalAdditionalLightData());
            shadow.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            shadow.FindProperty("m_UsePipelineSettings").boolValue = false;
            shadow.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorUtility.SetDirty(atmosphere);
        EditorSceneManager.MarkSceneDirty(maze.gameObject.scene);
        EditorSceneManager.SaveScene(maze.gameObject.scene);
        AssetDatabase.SaveAssets();
        int powered = 0, unpowered = 0;
        foreach (Transform child in maze.GeneratedRoot.GetComponentsInChildren<Transform>())
        {
            if (child.name.StartsWith("Powered entrance plafonnier")) powered++;
            if (child.name == "Unpowered entrance plafonnier") unpowered++;
        }
        return new { saved = true, powered, unpowered, model = modelPath, isPlaying = Application.isPlaying };
    }

    private static GameObject MakePrefab(GameObject model, Material metal, Material fasteners, Material glass, bool powered)
    {
        var root = new GameObject(powered ? "EntranceLampOn" : "EntranceLampOff");
        try
        {
            GameObject instance = UnityEngine.Object.Instantiate(model, root.transform, false);
            instance.name = "Plafonnier";
            int parts = 0;
            foreach (MeshRenderer renderer in instance.GetComponentsInChildren<MeshRenderer>())
            {
                bool isGlass = renderer.name.StartsWith("LampGlass", StringComparison.Ordinal);
                Material material = isGlass ? glass : renderer.name.StartsWith("LampFasteners", StringComparison.Ordinal) ? fasteners : metal;
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = isGlass ? ShadowCastingMode.Off : ShadowCastingMode.On;
                renderer.receiveShadows = !powered || !isGlass;
                parts++;
            }
            if (parts != 3) throw new InvalidOperationException("Expected three lamp mesh parts, got " + parts);
            return PrefabUtility.SaveAsPrefabAsset(root, Art + "Prefabs/" + root.name + ".prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static Material Surface(string name, string textureName, Color color, float metallic, float smoothness, bool lit)
    {
        string texturePath = Art + "Textures/" + textureName + ".png";
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing lamp texture " + texturePath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        string path = Art + "Materials/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", texture);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_Cull", 2f);
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetTexture("_EmissionMap", lit ? texture : null);
        material.SetColor("_EmissionColor", lit ? new Color(0.9f, 0.79f, 0.55f) : Color.black);
        if (lit) material.EnableKeyword("_EMISSION");
        else material.DisableKeyword("_EMISSION");
        material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        material.enableInstancing = true;
        BaseShaderGUI.SetupMaterialBlendMode(material);
        EditorUtility.SetDirty(material);
        return material;
    }
}
