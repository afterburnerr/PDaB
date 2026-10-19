using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SetupMenuScreenshot
{
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before saving menu background.");
        const string path = "Assets/Art/UI/Entrance/MenuBackgroundGameplayClean.png";
        AssetDatabase.Refresh();
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.sRGBTexture = true;
        importer.mipmapEnabled = false;
        importer.maxTextureSize = 2048;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        var canvas = GameObject.Find("Escape Interface");
        var image = canvas.transform.Find("Menu/Stairwell artwork").GetComponent<Image>();
        image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        image.GetComponent<AspectRatioFitter>().aspectRatio = image.sprite.rect.width / image.sprite.rect.height;
        EditorUtility.SetDirty(image);
        EditorSceneManager.MarkSceneDirty(canvas.scene);
        EditorSceneManager.SaveScene(canvas.scene);
        AssetDatabase.SaveAssets();
        return new { saved = true, path, width = image.sprite.rect.width, height = image.sprite.rect.height, hudUnchanged = true };
    }
}
