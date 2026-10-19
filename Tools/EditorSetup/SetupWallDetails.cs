using System;
using System.IO;
using FPLab.Gameplay.Maze;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class SetupWallDetails
{
    [Serializable] private sealed class Manifest { public Entry[] assets = Array.Empty<Entry>(); }
    [Serializable] private sealed class Entry
    {
        public string name = "";
        public string asset = "";
        public string category = "";
        public float worldWidth = 0f;
        public float worldHeight = 0f;
    }

    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before saving wall details.");
        Manifest manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<Manifest>(File.ReadAllText("Tools/Art/HalfLife2-wall-decals.json"));
        if (manifest?.assets == null || manifest.assets.Length == 0)
            throw new InvalidOperationException("Import the original Half-Life 2 wall decals first.");
        if (!AssetDatabase.IsValidFolder("Assets/Materials/Maze/WallDecals"))
            AssetDatabase.CreateFolder("Assets/Materials/Maze", "WallDecals");
        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        if (maze == null) throw new InvalidOperationException("MazeBuilder is missing.");
        MazeWallDetails details = maze.GetComponent<MazeWallDetails>();
        if (details == null) details = maze.gameObject.AddComponent<MazeWallDetails>();
        var fields = new SerializedObject(details);
        fields.FindProperty("density").floatValue = 0.65f;
        fields.FindProperty("maximumMarks").intValue = 260;
        fields.FindProperty("graffitiScale").floatValue = 2f;
        SerializedProperty decals = fields.FindProperty("decals");
        decals.arraySize = manifest.assets.Length;
        for (int i = 0; i < manifest.assets.Length; i++)
        {
            Entry entry = manifest.assets[i];
            int category = entry.category == "poster" ? 1 : entry.category == "grime" ? 2 : 0;
            SerializedProperty decal = decals.GetArrayElementAtIndex(i);
            decal.FindPropertyRelative("material").objectReferenceValue = Material(entry, category);
            decal.FindPropertyRelative("size").vector2Value = new Vector2(entry.worldWidth, entry.worldHeight);
            decal.FindPropertyRelative("category").enumValueIndex = category;
            decal.FindPropertyRelative("ceilingAligned").boolValue = entry.name == "decalstain001a"
                || entry.name == "decalstain002a" || entry.name == "decalstain011a" || entry.name == "decalstain015a";
        }
        fields.ApplyModifiedPropertiesWithoutUndo();

        if (maze.Data == null) maze.Generate();
        maze.GetComponent<MazeAtmosphere>().Decorate();
        foreach (Light light in maze.GeneratedRoot.GetComponentsInChildren<Light>())
        {
            if (light.shadows == LightShadows.None) continue;
            var shadow = new SerializedObject(light.GetUniversalAdditionalLightData());
            shadow.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            shadow.FindProperty("m_UsePipelineSettings").boolValue = false;
            shadow.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(maze.gameObject.scene);
        EditorSceneManager.SaveScene(maze.gameObject.scene);
        AssetDatabase.SaveAssets();
        int count = details.PlacedMarks;
        return new { saved = true, originalHl2Variants = manifest.assets.Length, placedWallMarks = count };
    }

    private static Material Material(Entry entry, int category)
    {
        var importer = AssetImporter.GetAtPath(entry.asset) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing decal: " + entry.asset);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 4;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = false;
        importer.SaveAndReimport();
        string path = "Assets/Materials/Maze/WallDecals/" + entry.name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(entry.asset));
        material.SetColor("_BaseColor", new Color(1f, 1f, 1f, category == 1 ? 0.95f : category == 2 ? 0.6f : 0.85f));
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_BlendModePreserveSpecular", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", 0f);
        material.SetFloat("_SpecularHighlights", 0f);
        material.SetFloat("_EnvironmentReflections", 0f);
        material.SetFloat("_ReceiveShadows", 1f);
        material.SetColor("_EmissionColor", Color.black);
        material.DisableKeyword("_EMISSION");
        material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        BaseShaderGUI.SetupMaterialBlendMode(material);
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        EditorUtility.SetDirty(material);
        return material;
    }
}
