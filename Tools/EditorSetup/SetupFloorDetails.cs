// Import the transparent imagegen atlases and bind floor decoration to maze generation.
using System;
using FPLab.Gameplay.Maze;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SetupFloorDetails
{
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before saving floor decoration.");
        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        if (maze == null) throw new InvalidOperationException("MazeBuilder is missing.");
        Material litter = AtlasMaterial("FloorLitterLarge", 0.95f);
        Material scuffs = AtlasMaterial("FloorScuffs", 0.7f);
        Material grime = AtlasMaterial("FloorGrime", 0.55f);
        Material blood = AtlasMaterial("FloorBlood", 0.85f);

        MazeFloorDetails details = maze.GetComponent<MazeFloorDetails>();
        if (details == null) details = maze.gameObject.AddComponent<MazeFloorDetails>();
        var fields = new SerializedObject(details);
        fields.FindProperty("litterMaterial").objectReferenceValue = litter;
        fields.FindProperty("scuffMaterial").objectReferenceValue = scuffs;
        fields.FindProperty("grimeMaterial").objectReferenceValue = grime;
        fields.FindProperty("bloodMaterial").objectReferenceValue = blood;
        fields.ApplyModifiedPropertiesWithoutUndo();

        // Runtime data is deliberately not serialized. Rebuild the authoring
        // preview after a domain reload; normal runs still build under loading.
        if (maze.Data == null) maze.Generate();
        details.Decorate();
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
        return new { saved = true, atlasCount = 4, variants = 36, shader = litter.shader.name };
    }

    private static Material AtlasMaterial(string name, float opacity)
    {
        string texturePath = "Assets/Art/Environment/FloorDetails/" + name + ".png";
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing atlas: " + texturePath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        // Keep equal thirds on exact pixel boundaries; transparent gutters
        // protect adjacent variants from bilinear/mipmap bleeding.
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 2048;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = false;
        importer.SaveAndReimport();

        string materialPath = "Assets/Materials/Maze/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
        material.SetColor("_BaseColor", new Color(1f, 1f, 1f, opacity));
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
        material.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        material.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
        BaseShaderGUI.SetupMaterialBlendMode(material);
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.SetShaderPassEnabled("DepthOnly", false);
        EditorUtility.SetDirty(material);
        return material;
    }
}
