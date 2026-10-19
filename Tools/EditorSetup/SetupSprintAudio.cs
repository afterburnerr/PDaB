// Installs the actual HL2 SprintStart and SprintNoPower signals.
using System;
using FPLab.Gameplay.Player.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupSprintAudio
{
    public static object Run()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Stop Play Mode before saving sprint audio.");
        PlayerMotor motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
        if (motor == null) throw new InvalidOperationException("Player motor is missing.");

        AudioClip start = Import("suit_sprint.wav");
        AudioClip exhausted = Import("suit_denydevice.wav");

        Transform audioTransform = motor.transform.Find("Sprint Audio");
        if (audioTransform == null) audioTransform = motor.transform.Find("Breathing Audio");
        if (audioTransform == null)
        {
            audioTransform = new GameObject("Sprint Audio").transform;
            audioTransform.SetParent(motor.transform, false);
        }
        audioTransform.name = "Sprint Audio";
        AudioSource source = audioTransform.GetComponent<AudioSource>();
        if (source == null) source = audioTransform.gameObject.AddComponent<AudioSource>();
        source.Stop();
        source.clip = null;
        source.playOnAwake = false;
        source.loop = false;
        source.volume = 1f;
        source.pitch = 1f;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.ignoreListenerPause = false;

        PlayerSprintAudio sprintAudio = motor.GetComponent<PlayerSprintAudio>();
        if (sprintAudio == null) sprintAudio = motor.gameObject.AddComponent<PlayerSprintAudio>();
        var fields = new SerializedObject(sprintAudio);
        fields.FindProperty("motor").objectReferenceValue = motor;
        fields.FindProperty("auxPower").objectReferenceValue = motor.GetComponent<PlayerAuxPower>();
        fields.FindProperty("source").objectReferenceValue = source;
        fields.FindProperty("sprintStartClip").objectReferenceValue = start;
        fields.FindProperty("exhaustedClip").objectReferenceValue = exhausted;
        fields.FindProperty("sprintStartVolume").floatValue = 0.9f;
        fields.FindProperty("exhaustedVolume").floatValue = 0.8f;
        fields.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(motor.gameObject.scene);
        EditorSceneManager.SaveScene(motor.gameObject.scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.DeleteAsset("Assets/Audio/HalfLife2/Breathing");
        return new { saved = true, startClip = start.name, exhaustedClip = exhausted.name, loop = false };
    }

    private static AudioClip Import(string filename)
    {
        string path = "Assets/Audio/HalfLife2/Sprint/" + filename;
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) throw new InvalidOperationException("Import the HL2 sprint WAV first: " + path);
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null) throw new InvalidOperationException("Could not load sprint WAV: " + path);
        return clip;
    }
}
