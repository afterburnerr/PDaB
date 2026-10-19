// Installs the HL2 flashlight assets and wires the existing player in Edit Mode.
using System;
using FPLab.Gameplay.Player.Equipment;
using FPLab.Gameplay.Session;
using FPLab.UI.HUD;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

public static class SetupFlashlight
{
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before installing the flashlight.");
        PlayerMotor motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
        Camera camera = motor.GetComponentInChildren<Camera>();
        PlayerAuxPower power = motor.GetComponent<PlayerAuxPower>();
        PlayerInputReader input = motor.GetComponent<PlayerInputReader>();
        EscapeSession session = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();

        Texture2D cookie = ImportCookie();
        AudioClip clip = ImportSound();
        InputActionReference action = FindAction();
        var inputFields = new SerializedObject(input);
        inputFields.FindProperty("flashlightAction").objectReferenceValue = action;
        inputFields.ApplyModifiedPropertiesWithoutUndo();
        var powerFields = new SerializedObject(power);
        powerFields.FindProperty("flashlightDrainPerSecond").floatValue = 2.222222f;
        powerFields.ApplyModifiedPropertiesWithoutUndo();

        Transform lamp = camera.transform.Find("Flashlight");
        if (lamp == null)
        {
            lamp = new GameObject("Flashlight").transform;
            lamp.SetParent(camera.transform, false);
        }
        lamp.localPosition = Vector3.zero;
        lamp.localRotation = Quaternion.identity;
        Light beam = lamp.GetComponent<Light>();
        if (beam == null) beam = lamp.gameObject.AddComponent<Light>();
        beam.type = LightType.Spot;
        beam.color = Color.white;
        beam.useColorTemperature = false;
        beam.intensity = 12f;
        beam.range = 750f * 0.0254f;
        beam.spotAngle = 45f;
        beam.innerSpotAngle = 44f;
        beam.cookie = cookie;
        beam.shadows = LightShadows.Soft;
        beam.shadowBias = 0.01f;
        beam.shadowNormalBias = 0.05f;
        beam.shadowNearPlane = 0.1f;
        beam.enabled = false;

        Transform soundTransform = motor.transform.Find("Flashlight Audio");
        if (soundTransform == null)
        {
            soundTransform = new GameObject("Flashlight Audio").transform;
            soundTransform.SetParent(motor.transform, false);
        }
        AudioSource source = soundTransform.GetComponent<AudioSource>();
        if (source == null) source = soundTransform.gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.volume = 1f;
        source.pitch = 1f;
        source.dopplerLevel = 0f;
        source.ignoreListenerPause = false;

        PlayerFlashlight equipment = motor.GetComponent<PlayerFlashlight>();
        if (equipment == null) equipment = motor.gameObject.AddComponent<PlayerFlashlight>();
        var fields = new SerializedObject(equipment);
        fields.FindProperty("input").objectReferenceValue = input;
        fields.FindProperty("power").objectReferenceValue = power;
        fields.FindProperty("session").objectReferenceValue = session;
        fields.FindProperty("view").objectReferenceValue = camera.transform;
        fields.FindProperty("beam").objectReferenceValue = beam;
        fields.FindProperty("source").objectReferenceValue = source;
        fields.FindProperty("toggleClip").objectReferenceValue = clip;
        fields.ApplyModifiedPropertiesWithoutUndo();
        foreach (SourceHudView hud in UnityEngine.Object.FindObjectsByType<SourceHudView>(FindObjectsInactive.Include))
        {
            var hudFields = new SerializedObject(hud);
            hudFields.FindProperty("flashlight").objectReferenceValue = equipment;
            hudFields.ApplyModifiedPropertiesWithoutUndo();
        }
        EditorSceneManager.MarkSceneDirty(motor.gameObject.scene);
        EditorSceneManager.SaveScene(motor.gameObject.scene);
        AssetDatabase.SaveAssets();
        return new { saved = true, input = action.action.bindings[0].path, cookie = cookie.name, sharedAuxPower = true, startsOff = true };
    }

    private static InputActionReference FindAction()
    {
        const string path = "Assets/InputSystem_Actions.inputactions";
        InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        InputAction action = asset.FindAction("Player/Flashlight", true);
        foreach (UnityEngine.Object subasset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (subasset is InputActionReference reference && reference.action != null && reference.action.id == action.id)
                return reference;
        const string referencePath = "Assets/Settings/FlashlightAction.asset";
        InputActionReference standalone = AssetDatabase.LoadAssetAtPath<InputActionReference>(referencePath);
        if (standalone != null) standalone.Set(action);
        else
        {
            standalone = InputActionReference.Create(action);
            AssetDatabase.CreateAsset(standalone, referencePath);
        }
        return standalone;
    }

    private static Texture2D ImportCookie()
    {
        const string path = "Assets/Art/UI/HalfLife2/Flashlight/flashlight_border.png";
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Import the HL2 flashlight texture first.");
        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.sRGBTexture = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 128;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static AudioClip ImportSound()
    {
        const string path = "Assets/Audio/HalfLife2/Flashlight/flashlight1.wav";
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null) throw new InvalidOperationException("Import the HL2 flashlight sound first.");
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
}
