// Installs the original HL2 concrete footsteps on the existing player.
using System;
using FPLab.Gameplay.Player.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupFootsteps
{
    public static object Run()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Stop Play Mode before saving footsteps.");
        PlayerMotor motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
        if (motor == null) throw new InvalidOperationException("Player motor is missing.");

        var clips = new AudioClip[4];
        for (int i = 0; i < clips.Length; i++)
            clips[i] = Import("Assets/Audio/HalfLife2/Footsteps/concrete" + (i + 1) + ".wav");

        Transform audioTransform = motor.transform.Find("Footstep Audio");
        if (audioTransform == null)
        {
            audioTransform = new GameObject("Footstep Audio").transform;
            audioTransform.SetParent(motor.transform, false);
        }
        AudioSource source = audioTransform.GetComponent<AudioSource>();
        if (source == null) source = audioTransform.gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.volume = 1f;
        source.pitch = 1f;
        source.dopplerLevel = 0f;
        source.ignoreListenerPause = false;

        PlayerFootsteps footsteps = motor.GetComponent<PlayerFootsteps>();
        if (footsteps == null) footsteps = motor.gameObject.AddComponent<PlayerFootsteps>();
        var fields = new SerializedObject(footsteps);
        fields.FindProperty("motor").objectReferenceValue = motor;
        fields.FindProperty("controller").objectReferenceValue = motor.GetComponent<CharacterController>();
        fields.FindProperty("source").objectReferenceValue = source;
        AssignClips(fields.FindProperty("leftSteps"), clips[0], clips[2]);
        AssignClips(fields.FindProperty("rightSteps"), clips[1], clips[3]);
        fields.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(motor.gameObject.scene);
        EditorSceneManager.SaveScene(motor.gameObject.scene);
        AssetDatabase.SaveAssets();
        return new { saved = true, originalHl2Clips = clips.Length, player = motor.name };
    }

    private static void AssignClips(SerializedProperty field, AudioClip first, AudioClip second)
    {
        field.arraySize = 2;
        field.GetArrayElementAtIndex(0).objectReferenceValue = first;
        field.GetArrayElementAtIndex(1).objectReferenceValue = second;
    }

    private static AudioClip Import(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) throw new InvalidOperationException("Import the HL2 WAV first: " + path);
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
}
