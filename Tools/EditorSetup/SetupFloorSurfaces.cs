using System;
using System.IO;
using FPLab.Gameplay.Maze;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class SetupFloorSurfaces
{
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before saving floor surfaces.");
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        Material concrete = RestoreConcrete("EntranceFloor", shader);
        RestoreConcrete("EntranceStart", shader);
        Material boards = Surface("EntranceFloorBoards", "FloorBoards", shader, 0.1f, 0.2f);
        Material tiles = Surface("EntranceFloorTiles", "FloorTiles", shader, 0.14f, 0.18f);

        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        if (maze == null) throw new InvalidOperationException("MazeBuilder is missing.");
        var settings = new SerializedObject(maze);
        settings.FindProperty("floorMaterial").objectReferenceValue = concrete;
        settings.FindProperty("boardsFloorMaterial").objectReferenceValue = boards;
        settings.FindProperty("tilesFloorMaterial").objectReferenceValue = tiles;
        settings.ApplyModifiedPropertiesWithoutUndo();
        maze.Generate();
        maze.GetComponent<MazeAtmosphere>().Decorate();
        maze.GetComponent<MazeFloorDetails>()?.Decorate();
        foreach (Light light in maze.GeneratedRoot.GetComponentsInChildren<Light>())
        {
            if (light.shadows == LightShadows.None) continue;
            var fields = new SerializedObject(light.GetUniversalAdditionalLightData());
            fields.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            fields.FindProperty("m_UsePipelineSettings").boolValue = false;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(maze.gameObject.scene);
        EditorSceneManager.SaveScene(maze.gameObject.scene);
        AssetDatabase.SaveAssets();
        return new { saved = true, floorMaterials = 3, shader = shader.name, connectedMaterialZones = true };
    }

    private static Texture2D Import(string name)
    {
        string path = "Assets/Art/Environment/FloorSurfaces/" + name + ".png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing generated floor texture: " + path);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.isReadable = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material RestoreConcrete(string name, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Maze/" + name + ".mat");
        if (material == null) throw new InvalidOperationException("Missing floor material: " + name);
        material.shader = shader;
        Finish(material, 0.12f, 0.12f);
        return material;
    }

    private static Material Surface(string materialName, string textureName, Shader shader,
        float smoothness, float bump)
    {
        Texture2D albedo = Import(textureName);
        const string derived = "Assets/Art/Environment/DerivedFloorSurfaces/";
        Directory.CreateDirectory(derived);
        string normalPath = derived + textureName + "_Normal.png";
        File.Copy("Assets/Art/Environment/FloorSurfaces/" + textureName + ".png", normalPath, true);
        AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceSynchronousImport);
        var normal = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        normal.textureType = TextureImporterType.NormalMap;
        normal.convertToNormalmap = true;
        normal.heightmapScale = 0.025f;
        normal.wrapMode = TextureWrapMode.Repeat;
        normal.filterMode = FilterMode.Trilinear;
        normal.anisoLevel = 8;
        normal.maxTextureSize = 1024;
        normal.textureCompression = TextureImporterCompression.CompressedHQ;
        normal.mipmapEnabled = true;
        normal.isReadable = false;
        normal.SaveAndReimport();

        string path = "Assets/Materials/Maze/" + materialName + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        Finish(material, smoothness, bump);
        return material;
    }

    private static void Finish(Material material, float smoothness, float bump)
    {
        material.SetTextureScale("_BaseMap", Vector2.one * 0.5f);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_BumpScale", bump);
        material.EnableKeyword("_NORMALMAP");
        material.DisableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Color.black);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
    }
}
