using System;
using FPLab.Gameplay.Audio;
using FPLab.Gameplay.Session;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupGameplayAmbience
{
    public static object Run()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Stop Play Mode before saving gameplay ambience.");

        EscapeSession session = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();
        if (session == null)
            throw new InvalidOperationException("Escape session is missing.");

        const string path = "Assets/Audio/HalfLife2/Ambience/DistortedTrumpets.mp3";
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null)
            throw new InvalidOperationException("Import the original HL2 radio1 audio first.");

        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.Streaming;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.forceToMono = false;
        importer.loadInBackground = true;
        importer.SaveAndReimport();

        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
            throw new InvalidOperationException("Could not load Distorted Trumpets.");

        Transform audio = session.transform.Find("Gameplay Ambience");
        if (audio == null)
        {
            audio = new GameObject("Gameplay Ambience").transform;
            audio.SetParent(session.transform, false);
        }

        AudioSource source = audio.GetComponent<AudioSource>();
        if (source == null) source = audio.gameObject.AddComponent<AudioSource>();
        source.Stop();
        source.clip = clip;
        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0.2f;
        source.pitch = 1f;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.priority = 128;
        source.ignoreListenerPause = false;
        source.bypassReverbZones = true;

        GameplayAmbience ambience = audio.GetComponent<GameplayAmbience>();
        if (ambience == null) ambience = audio.gameObject.AddComponent<GameplayAmbience>();
        var fields = new SerializedObject(ambience);
        fields.FindProperty("session").objectReferenceValue = session;
        fields.FindProperty("source").objectReferenceValue = source;
        fields.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        EditorSceneManager.SaveScene(session.gameObject.scene);
        AssetDatabase.SaveAssets();
        return new {
            saved = true, clip = clip.name, duration = clip.length, channels = clip.channels,
            source.loop, source.volume, source.pitch, source.spatialBlend, source.playOnAwake,
            loadType = importer.defaultSampleSettings.loadType.ToString(),
            compression = importer.defaultSampleSettings.compressionFormat.ToString(),
            session = session.name
        };
    }
}
