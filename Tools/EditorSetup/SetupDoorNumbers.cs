// Physical plaques and exact font outlines; no dynamic font atlas.
using System;
using System.Collections.Generic;
using System.IO;
using FPLab.Gameplay.Maze;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class SetupDoorNumbers
{
    [Serializable] private sealed class Document { public Shape[] meshes = Array.Empty<Shape>(); }
    [Serializable] private sealed class Shape
    {
        public string name = "";
        public Vector3[] vertices = Array.Empty<Vector3>();
        public Vector3[] normals = Array.Empty<Vector3>();
        public Vector2[] uv = Array.Empty<Vector2>();
        public int[] triangles = Array.Empty<int>();
    }

    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before saving apartment number plaques.");
        const string art = "Assets/Art/Environment/DoorPlaques/";
        Document document = Newtonsoft.Json.JsonConvert.DeserializeObject<Document>(File.ReadAllText(art + "Meshes/DoorPlaqueMeshes.json"));
        var meshes = new Dictionary<string, Mesh>();
        foreach (Shape shape in document.meshes)
        {
            string path = art + "Meshes/" + shape.name + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = shape.name };
                AssetDatabase.CreateAsset(mesh, path);
            }
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            foreach (Vector3 vertex in shape.vertices)
            {
                minimum = Mathf.Min(minimum, vertex.z);
                maximum = Mathf.Max(maximum, vertex.z);
            }
            // Generator records back Z=0; placement uses centered pivots.
            float centerZ = (minimum + maximum) * 0.5f;
            for (int i = 0; i < shape.vertices.Length; i++) shape.vertices[i].z -= centerZ;
            mesh.Clear();
            mesh.vertices = shape.vertices;
            mesh.normals = shape.normals;
            mesh.uv = shape.uv;
            mesh.triangles = shape.triangles;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            meshes.Add(shape.name, mesh);
        }

        string texturePath = art + "TarnishedBrass.png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
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
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        Material plate = Surface("EntranceDoorNumberPlate", texture, Color.white, 0.6f, 0.28f);
        Material screw = Surface("EntranceDoorNumberScrews", texture, new Color(0.84f, 0.83f, 0.78f), 0.7f, 0.32f);
        Material digits = Surface("EntranceDoorNumbers", null, new Color(0.065f, 0.06f, 0.05f), 0.18f, 0.2f);

        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        MazeAtmosphere atmosphere = maze.GetComponent<MazeAtmosphere>();
        var fields = new SerializedObject(atmosphere);
        fields.FindProperty("numberMaterial").objectReferenceValue = digits;
        fields.FindProperty("numberPlateMaterial").objectReferenceValue = plate;
        fields.FindProperty("numberScrewMaterial").objectReferenceValue = screw;
        fields.FindProperty("numberPlateMesh").objectReferenceValue = meshes["Plaque"];
        fields.FindProperty("numberScrewMesh").objectReferenceValue = meshes["Screw"];
        SerializedProperty digitMeshes = fields.FindProperty("numberDigitMeshes");
        digitMeshes.arraySize = 10;
        for (int i = 0; i < 10; i++) digitMeshes.GetArrayElementAtIndex(i).objectReferenceValue = meshes["Digit_" + i];
        fields.ApplyModifiedPropertiesWithoutUndo();
        if (maze.Data == null) maze.Generate();
        atmosphere.Decorate();
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
        return new { saved = true, physicalMeshes = meshes.Count, embossedDigits = true, emission = false };
    }

    private static Material Surface(string name, Texture2D texture, Color color, float metallic, float smoothness)
    {
        string path = "Assets/Materials/Maze/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", Vector2.one);
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Surface", 0f);
        material.SetFloat("_AlphaClip", 0f);
        material.SetFloat("_Cull", 2f);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        material.DisableKeyword("_EMISSION");
        material.DisableKeyword("_ALPHATEST_ON");
        material.SetColor("_EmissionColor", Color.black);
        BaseShaderGUI.SetupMaterialBlendMode(material);
        EditorUtility.SetDirty(material);
        return material;
    }
}
