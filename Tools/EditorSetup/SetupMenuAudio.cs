// Applies the installed HL2 clips without reconstructing the visual interface.
using System;
using FPLab.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SetupMenuAudio
{
    private const string AudioPath = "Assets/Audio/HalfLife2/UI/";

    public static object Run()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Stop Play Mode before saving menu audio.");

        AudioClip hover = Import("buttonrollover.wav");
        AudioClip press = Import("buttonclick.wav");
        AudioClip release = Import("buttonclickrelease.wav");
        GameObject canvas = GameObject.Find("Escape Interface");
        if (canvas == null)
            throw new InvalidOperationException("The Escape Interface scene object is missing.");

        Transform buttons = canvas.transform.Find("Menu/Buttons");
        int configured = 0;
        foreach (Button button in buttons.GetComponentsInChildren<Button>(true))
        {
            MenuButtonAudio audio = button.GetComponent<MenuButtonAudio>();
            if (audio == null) audio = button.gameObject.AddComponent<MenuButtonAudio>();
            var fields = new SerializedObject(audio);
            fields.FindProperty("hoverClip").objectReferenceValue = hover;
            fields.FindProperty("pressClip").objectReferenceValue = press;
            fields.FindProperty("clickClip").objectReferenceValue = release;
            fields.ApplyModifiedPropertiesWithoutUndo();
            configured++;
        }

        EditorSceneManager.MarkSceneDirty(canvas.scene);
        EditorSceneManager.SaveScene(canvas.scene);
        AssetDatabase.SaveAssets();
        return new { saved = true, configuredButtons = configured };
    }

    private static AudioClip Import(string filename)
    {
        string path = AudioPath + filename;
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null)
            throw new InvalidOperationException("Import the HL2 WAV first: " + path);

        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
}
