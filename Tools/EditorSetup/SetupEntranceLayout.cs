using System;
using FPLab.Gameplay.Maze;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class SetupEntranceLayout
{
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before saving the entrance layout.");
        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        var settings = new SerializedObject(maze);
        settings.FindProperty("width").intValue = 33;
        settings.FindProperty("height").intValue = 33;
        settings.FindProperty("cellSize").floatValue = 2.2f;
        settings.FindProperty("minimumRouteLength").floatValue = 330f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var atmosphere = new SerializedObject(maze.GetComponent<MazeAtmosphere>());
        atmosphere.FindProperty("maximumDoors").intValue = 96;
        atmosphere.ApplyModifiedPropertiesWithoutUndo();
        maze.Generate();
        // Editor authoring does not rely on runtime OnEnable event subscriptions.
        maze.GetComponent<MazeAtmosphere>().Decorate();
        maze.GetComponent<MazeFloorDetails>()?.Decorate();
        if (maze.Data.ExitDistance * maze.CellSize < 330f)
            throw new InvalidOperationException("Authored route did not meet the intended length.");
        // Save a readable editor fallback of the runtime wall-style map.
        Material upper = atmosphere.FindProperty("blendedUpperWallMaterial").objectReferenceValue as Material;
        Material lower = atmosphere.FindProperty("blendedLowerWallMaterial").objectReferenceValue as Material;
        var properties = new MaterialPropertyBlock();
        foreach (MeshRenderer renderer in maze.GeneratedRoot.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.sharedMaterial != upper) continue;
            renderer.GetPropertyBlock(properties);
            var generated = properties.GetTexture("_WallStyleMap") as Texture2D;
            const string mapPath = "Assets/Art/Environment/DerivedHL2Walls/WallStylePreview.asset";
            Texture2D preview = AssetDatabase.LoadAssetAtPath<Texture2D>(mapPath);
            if (preview.width != generated.width || preview.height != generated.height)
                preview.Reinitialize(generated.width, generated.height, TextureFormat.RGBA32, false);
            preview.SetPixels(generated.GetPixels());
            preview.Apply(false, false);
            EditorUtility.SetDirty(preview);
            foreach (Material material in new[] { upper, lower })
            {
                material.SetTexture("_WallStyleMap", preview);
                material.SetVector("_WallMapParams", properties.GetVector("_WallMapParams"));
                material.SetFloat("_WallCellSize", maze.CellSize);
                EditorUtility.SetDirty(material);
            }
            break;
        }
        foreach (Light light in maze.GeneratedRoot.GetComponentsInChildren<Light>())
        {
            if (light.shadows == UnityEngine.LightShadows.None) continue;
            var shadow = new SerializedObject(light.GetUniversalAdditionalLightData());
            shadow.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
            shadow.FindProperty("m_UsePipelineSettings").boolValue = false;
            shadow.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(maze.gameObject.scene);
        EditorSceneManager.SaveScene(maze.gameObject.scene);
        AssetDatabase.SaveAssets();
        return new { saved = true, seed = maze.GeneratedSeed, cells = maze.Data.WalkableCellCount,
            rooms = maze.Data.Rooms.Count, routeMetres = maze.Data.ExitDistance * maze.CellSize,
            grid = "33x33", cellSize = maze.CellSize, isPlaying = Application.isPlaying };
    }
}
