using System;
using FPLab.Gameplay.Maze;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class SetupWallFinishes
{
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before saving wall finishes.");
        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        if (maze == null) throw new InvalidOperationException("MazeBuilder is missing.");
        if (maze.Data == null) maze.Generate();
        maze.GetComponent<MazeAtmosphere>().Decorate();
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
        return new { saved = true, wrappedWallEnds = true, continuousCornerFinishes = true };
    }
}
