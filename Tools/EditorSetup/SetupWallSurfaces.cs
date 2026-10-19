using System;
using System.IO;
using FPLab.Gameplay.Maze;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class SetupWallSurfaces
{
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before saving wall styles.");
        Material agedPlaster = Surface("EntranceWallAgedUpper", "plasterwall003a", 0.04f, 0.14f);
        Material agedPaint = Surface("EntranceWallAgedPaint", "plasterwall042a", 0.15f, 0.08f);
        Material tiledPlaster = Surface("EntranceWallTiledUpper", "plasterwall032a", 0.04f, 0.14f);
        Material tiles = Surface("EntranceWallTiles", "tilewall006b", 0.16f, 0.22f);
        Material plaster = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Maze/EntrancePlaster.mat");
        Material paint = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Maze/EntrancePaint.mat");
        Material upperBlend = BlendSurface("EntranceWallUpperBlend", plaster, agedPlaster, tiledPlaster, 0.06f);
        Material lowerBlend = BlendSurface("EntranceWallLowerBlend", paint, agedPaint, tiles, 0.12f);
        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        if (maze == null) throw new InvalidOperationException("MazeBuilder is missing.");
        var settings = new SerializedObject(maze.GetComponent<MazeAtmosphere>());
        SerializedProperty styles = settings.FindProperty("wallSurfaceStyles");
        styles.arraySize = 3;
        Style(styles.GetArrayElementAtIndex(0), plaster, paint);
        Style(styles.GetArrayElementAtIndex(1), agedPlaster, agedPaint);
        Style(styles.GetArrayElementAtIndex(2), tiledPlaster, tiles);
        settings.FindProperty("blendedUpperWallMaterial").objectReferenceValue = upperBlend;
        settings.FindProperty("blendedLowerWallMaterial").objectReferenceValue = lowerBlend;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var decals = new SerializedObject(maze.GetComponent<MazeWallDetails>());
        decals.FindProperty("density").floatValue = 0.65f;
        decals.FindProperty("maximumMarks").intValue = 260;
        decals.FindProperty("graffitiScale").floatValue = 2f;
        decals.ApplyModifiedPropertiesWithoutUndo();

        if (maze.Data == null) maze.Generate();
        maze.GetComponent<MazeAtmosphere>().Decorate();
        SavePreviewMap(maze, upperBlend, lowerBlend);
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
        int marks = maze.GetComponent<MazeWallDetails>().PlacedMarks;
        return new { saved = true, wallStyles = 3, softIrregularTransitions = true, placedWallMarks = marks };
    }

    private static void Style(SerializedProperty entry, Material upper, Material lower)
    {
        if (upper == null || lower == null) throw new InvalidOperationException("Missing wall style material.");
        entry.FindPropertyRelative("upperMaterial").objectReferenceValue = upper;
        entry.FindPropertyRelative("lowerMaterial").objectReferenceValue = lower;
    }

    private static Material BlendSurface(string name, Material original, Material aged, Material tiled, float smoothness)
    {
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/EntranceWallBlend.shader");
        if (shader == null) throw new InvalidOperationException("Import the wall transition shader first.");
        if (original == null || aged == null || tiled == null) throw new InvalidOperationException("Missing wall source material.");
        string path = "Assets/Materials/Maze/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.SetTexture("_BaseMap", original.GetTexture("_BaseMap"));
        material.SetTexture("_VariantA", aged.GetTexture("_BaseMap"));
        material.SetTexture("_VariantB", tiled.GetTexture("_BaseMap"));
        material.SetTexture("_BumpMap", original.GetTexture("_BumpMap"));
        material.SetTexture("_NormalA", aged.GetTexture("_BumpMap"));
        material.SetTexture("_NormalB", tiled.GetTexture("_BumpMap"));
        material.SetTextureScale("_BaseMap", Vector2.one * 0.5f);
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_BumpScale", 0.14f);
        material.SetFloat("_WallCellSize", 2.6f);
        material.SetFloat("_TransitionWidth", 0.65f);
        material.SetFloat("_TransitionNoise", 0.18f);
        material.SetColor("_EmissionColor", Color.black);
        material.EnableKeyword("_NORMALMAP");
        material.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void SavePreviewMap(MazeBuilder maze, Material upper, Material lower)
    {
        var properties = new MaterialPropertyBlock();
        foreach (MeshRenderer renderer in maze.GeneratedRoot.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.sharedMaterial != upper) continue;
            renderer.GetPropertyBlock(properties);
            var generated = properties.GetTexture("_WallStyleMap") as Texture2D;
            if (generated == null) throw new InvalidOperationException("Wall style map was not generated.");
            const string path = "Assets/Art/Environment/DerivedHL2Walls/WallStylePreview.asset";
            Texture2D preview = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (preview == null)
            {
                preview = new Texture2D(generated.width, generated.height, TextureFormat.RGBA32, false, true);
                AssetDatabase.CreateAsset(preview, path);
            }
            else if (preview.width != generated.width || preview.height != generated.height)
                preview.Reinitialize(generated.width, generated.height, TextureFormat.RGBA32, false);
            preview.SetPixels(generated.GetPixels());
            preview.filterMode = FilterMode.Bilinear;
            preview.wrapMode = TextureWrapMode.Clamp;
            preview.Apply(false, false);
            EditorUtility.SetDirty(preview);
            foreach (Material material in new[] { upper, lower })
            {
                material.SetTexture("_WallStyleMap", preview);
                material.SetVector("_WallMapParams", properties.GetVector("_WallMapParams"));
                material.SetFloat("_WallCellSize", maze.CellSize);
                EditorUtility.SetDirty(material);
            }
            return;
        }
        throw new InvalidOperationException("No blended wall surfaces were built.");
    }

    private static Material Surface(string materialName, string texture, float smoothness, float bump)
    {
        string source = "Assets/Art/Environment/HalfLife2/WallSurfaces/" + texture + ".png";
        var importer = AssetImporter.GetAtPath(source) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Missing original HL2 texture: " + source);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.isReadable = false;
        importer.SaveAndReimport();
        const string derived = "Assets/Art/Environment/DerivedHL2Walls/";
        Directory.CreateDirectory(derived);
        string normalPath = derived + texture + "_Normal.png";
        File.Copy(source, normalPath, true);
        AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceSynchronousImport);
        var normal = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        normal.textureType = TextureImporterType.NormalMap;
        normal.convertToNormalmap = true;
        normal.heightmapScale = 0.02f;
        normal.wrapMode = TextureWrapMode.Repeat;
        normal.filterMode = FilterMode.Trilinear;
        normal.anisoLevel = 8;
        normal.mipmapEnabled = true;
        normal.maxTextureSize = 1024;
        normal.textureCompression = TextureImporterCompression.CompressedHQ;
        normal.isReadable = false;
        normal.SaveAndReimport();

        string path = "Assets/Materials/Maze/" + materialName + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(source));
        material.SetTextureScale("_BaseMap", Vector2.one * 0.5f);
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        material.SetColor("_BaseColor", Color.white);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", 0f);
        material.SetFloat("_BumpScale", bump);
        material.EnableKeyword("_NORMALMAP");
        material.DisableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", Color.black);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }
}
