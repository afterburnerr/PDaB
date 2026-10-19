using System;
using System.IO;
using System.Linq;
using FPLab.Gameplay.Enemies;
using FPLab.Gameplay.Session;
using FPLab.UI.HUD;
using FPLab.UI.Menus;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class SetupStalkerEffects
{
    // Deliberately leaves scene saving to the integration authoring step.
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before authoring stalker presentation.");
        EscapeSession session = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();
        EntranceStalker stalker = UnityEngine.Object.FindAnyObjectByType<EntranceStalker>(FindObjectsInactive.Include);
        EscapeMenuView menu = UnityEngine.Object.FindAnyObjectByType<EscapeMenuView>();
        if (session == null || stalker == null || menu == null)
            throw new InvalidOperationException("Create the session, enemy and menu before binding stalker effects.");
        string[] audioPaths = AssetDatabase.FindAssets("t:AudioClip", new[] {"Assets/Audio/Horror/Stalker"})
            .Select(AssetDatabase.GUIDToAssetPath).ToArray();
        foreach (string path in audioPaths)
        {
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = path.EndsWith("DistantMurmurs.wav") ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = path.EndsWith("CaptureSting.wav") ? AudioCompressionFormat.PCM : AudioCompressionFormat.Vorbis;
            settings.quality = 0.85f;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = true;
            importer.loadInBackground = !path.EndsWith("CaptureSting.wav");
            importer.SaveAndReimport();
        }
        if (!audioPaths.Any(p => p.EndsWith("CaptureSting.wav")) || audioPaths.Count(p => Path.GetFileName(p).StartsWith("Mutter")) != 10
            || audioPaths.Count(p => Path.GetFileName(p).StartsWith("Static")) != 4)
            throw new InvalidOperationException("Expected capture sting, ten mutters and four original static bursts.");
        Transform canvas = menu.transform;
        Transform oldOverlay = canvas.Find("Stalker Interference");
        if (oldOverlay != null) UnityEngine.Object.DestroyImmediate(oldOverlay.gameObject);

        const string illustrationPath = "Assets/Art/UI/Horror/StalkerScreamer.png";
        var illustrationImporter = AssetImporter.GetAtPath(illustrationPath) as TextureImporter;
        if (illustrationImporter == null)
            throw new InvalidOperationException("Create and import the capture illustration before authoring the screamer.");
        illustrationImporter.textureType = TextureImporterType.Sprite;
        illustrationImporter.spriteImportMode = SpriteImportMode.Single;
        illustrationImporter.sRGBTexture = true;
        illustrationImporter.mipmapEnabled = false;
        illustrationImporter.maxTextureSize = 2048;
        illustrationImporter.alphaIsTransparency = false;
        illustrationImporter.textureCompression = TextureImporterCompression.Uncompressed;
        illustrationImporter.wrapMode = TextureWrapMode.Clamp;
        illustrationImporter.filterMode = FilterMode.Bilinear;
        var illustrationSettings = new TextureImporterSettings();
        illustrationImporter.ReadTextureSettings(illustrationSettings);
        illustrationSettings.spriteMeshType = SpriteMeshType.FullRect;
        illustrationSettings.spriteBorder = Vector4.zero;
        illustrationImporter.SetTextureSettings(illustrationSettings);
        illustrationImporter.SaveAndReimport();
        Sprite illustration = AssetDatabase.LoadAssetAtPath<Sprite>(illustrationPath);
        if (illustration == null) throw new InvalidOperationException("The capture illustration did not import as a Sprite.");
        RectTransform captureImage = Rect("Capture illustration", canvas);
        captureImage.SetAsFirstSibling();
        Image picture = Get<Image>(captureImage.gameObject);
        picture.sprite = illustration;
        picture.type = Image.Type.Simple;
        picture.preserveAspect = false;
        picture.color = Color.white;
        picture.raycastTarget = false;
        picture.enabled = false;
        StalkerScreamerView screamer = Get<StalkerScreamerView>(canvas.gameObject);
        var screamerFields = new SerializedObject(screamer);
        screamerFields.FindProperty("session").objectReferenceValue = session;
        screamerFields.FindProperty("image").objectReferenceValue = picture;
        screamerFields.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(screamer);

        var menuFields = new SerializedObject(menu);
        GameObject menuRoot = (GameObject)menuFields.FindProperty("menuRoot").objectReferenceValue;
        Transform artwork = menuRoot.transform.Find("Stairwell artwork");
        if (artwork == null) throw new InvalidOperationException("The existing menu artwork was not found.");
        RectTransform shade = Rect("Caught darkness", menuRoot.transform);
        shade.SetAsFirstSibling();
        Image dark = Get<Image>(shade.gameObject);
        dark.color = new Color(0.018f, 0.006f, 0.008f, 0.9f);
        dark.raycastTarget = false;
        shade.gameObject.SetActive(false);
        menuFields.FindProperty("menuArtwork").objectReferenceValue = artwork.gameObject;
        menuFields.FindProperty("deathShade").objectReferenceValue = shade.gameObject;
        menuFields.ApplyModifiedPropertiesWithoutUndo();

        Transform audioRoot = session.transform.Find("Stalker Sound");
        if (audioRoot == null)
        {
            audioRoot = new GameObject("Stalker Sound").transform;
            audioRoot.SetParent(session.transform, false);
        }
        AudioSource murmurs = Source("Distant murmurs", audioRoot, true);
        murmurs.clip = Clip("DistantMurmurs");
        murmurs.loop = true;
        AudioSource voice = Source("Directional muttering", audioRoot, true);
        AudioSource crackle = Source("Directional radio crackle", audioRoot, true);
        AudioSource capture = Source("Capture scream", audioRoot, false);
        capture.clip = Clip("CaptureSting");
        capture.volume = 0.8f;
        capture.pitch = 1f;
        capture.priority = 32;
        capture.spatialBlend = 0f;
        capture.ignoreListenerPause = true;
        capture.bypassReverbZones = true;
        capture.bypassEffects = true;
        capture.bypassListenerEffects = true;
        capture.outputAudioMixerGroup = null;
        AudioLowPassFilter murmurFilter = Get<AudioLowPassFilter>(murmurs.gameObject);
        AudioLowPassFilter voiceFilter = Get<AudioLowPassFilter>(voice.gameObject);
        murmurFilter.cutoffFrequency = voiceFilter.cutoffFrequency = 3800f;
        AudioLowPassFilter captureFilter = Get<AudioLowPassFilter>(capture.gameObject);
        captureFilter.enabled = false; // The sting is already band-limited during synthesis.
        StalkerThreatView view = Get<StalkerThreatView>(canvas.gameObject);
        var fields = new SerializedObject(view);
        fields.FindProperty("session").objectReferenceValue = session;
        fields.FindProperty("stalker").objectReferenceValue = stalker;
        Camera camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("The first-person camera must be tagged MainCamera.");
        fields.FindProperty("listener").objectReferenceValue = camera.transform;
        fields.FindProperty("murmurs").objectReferenceValue = murmurs;
        fields.FindProperty("voice").objectReferenceValue = voice;
        fields.FindProperty("captureSound").objectReferenceValue = capture;
        fields.FindProperty("crackle").objectReferenceValue = crackle;
        fields.FindProperty("murmurFilter").objectReferenceValue = murmurFilter;
        fields.FindProperty("voiceFilter").objectReferenceValue = voiceFilter;
        AudioClip[] mutters = audioPaths.Where(p => Path.GetFileName(p).StartsWith("Mutter"))
            .OrderBy(p => p).Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p)).ToArray();
        SerializedProperty clips = fields.FindProperty("mutters");
        clips.arraySize = mutters.Length;
        for (int i = 0; i < mutters.Length; i++) clips.GetArrayElementAtIndex(i).objectReferenceValue = mutters[i];
        AudioClip[] staticBursts = audioPaths.Where(p => Path.GetFileName(p).StartsWith("Static"))
            .OrderBy(p => p).Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p)).ToArray();
        SerializedProperty bursts = fields.FindProperty("staticBursts");
        bursts.arraySize = staticBursts.Length;
        for (int i = 0; i < staticBursts.Length; i++) bursts.GetArrayElementAtIndex(i).objectReferenceValue = staticBursts[i];
        fields.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(view);
        EditorUtility.SetDirty(menu);
        AssetDatabase.SaveAssets();
        return new { prepared = true, sceneSaved = false, voiceVariations = mutters.Length, spatialWhispers = true,
            maxVoiceDistance = voice.maxDistance, staticVariations = staticBursts.Length, captureClip = capture.clip.name, captureSeconds = capture.clip.length, captureVolume = capture.volume, visualInterference = false, stillCaptureIllustration = true, deathHeading = "ТЕБЯ НАШЛИ", isPlaying = Application.isPlaying };
    }

    private static AudioClip Clip(string name)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Horror/Stalker/" + name + ".wav");
        if (clip == null) throw new InvalidOperationException("Missing audio: " + name);
        return clip;
    }

    private static AudioSource Source(string name, Transform parent, bool spatial)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
        }
        AudioSource source = Get<AudioSource>(child.gameObject);
        source.Stop();
        source.clip = null;
        source.playOnAwake = false;
        source.loop = false;
        source.volume = 0f;
        source.pitch = 1f;
        source.spatialBlend = spatial ? 1f : 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 3f;
        source.maxDistance = 55f;
        source.dopplerLevel = 0f;
        source.spread = 8f;
        source.priority = 80;
        source.ignoreListenerPause = true;
        source.bypassReverbZones = false;
        return source;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        Transform child = parent.Find(name);
        RectTransform rect = child != null ? (RectTransform)child : new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static T Get<T>(GameObject obj) where T : Component
    {
        T result = obj.GetComponent<T>();
        return result != null ? result : obj.AddComponent<T>();
    }
}
