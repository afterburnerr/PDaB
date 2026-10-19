using System;
using FPLab.Gameplay.Audio;
using FPLab.Gameplay.Session;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SetupMenuAmbience
{
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before saving menu ambience.");
        const string path = "Assets/Audio/HalfLife2/Ambience/CitadelPodRideAmbience.wav";
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) throw new InvalidOperationException("Import the original Citadel ambience first.");
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.forceToMono = false;
        importer.loadInBackground = true;
        importer.SaveAndReimport();
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null || clip.length <= 0f) throw new InvalidOperationException("The Citadel ambience clip did not import.");
        EscapeSession session = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();
        Transform audio = session.transform.Find("Menu Ambience");
        if (audio == null)
        {
            audio = new GameObject("Menu Ambience").transform;
            audio.SetParent(session.transform, false);
        }
        AudioSource source = audio.GetComponent<AudioSource>();
        if (source == null) source = audio.gameObject.AddComponent<AudioSource>();
        source.Stop();
        source.clip = clip;
        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0.3f;
        source.pitch = 1f;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.ignoreListenerPause = true;
        source.bypassReverbZones = true;
        MenuAmbience ambience = audio.GetComponent<MenuAmbience>();
        if (ambience == null) ambience = audio.gameObject.AddComponent<MenuAmbience>();
        var fields = new SerializedObject(ambience);
        fields.FindProperty("session").objectReferenceValue = session;
        fields.FindProperty("source").objectReferenceValue = source;
        fields.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        EditorSceneManager.SaveScene(session.gameObject.scene);
        AssetDatabase.SaveAssets();
        return new {saved=true, clip=clip.name, duration=clip.length, source.loop, source.volume, source.ignoreListenerPause, isPlaying=Application.isPlaying};
    }
}
