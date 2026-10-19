// Re-runnable editor setup. Run through the Unity CLI run_script command.
using System;
using FPLab.Gameplay.Maze;
using FPLab.Gameplay.Session;
using FPLab.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;

public static class SetupEntrance
{
    private const string Art = "Assets/Art/UI/Entrance/";
    private static readonly Color Ink = new Color(0.87f, 0.85f, 0.77f);
    private static readonly Color MenuInk = new Color(0.84f, 0.85f, 0.83f);
    private static readonly Color Muted = new Color(0.59f, 0.59f, 0.52f);
    private static Font bodyFont;

    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before setup.");
        AssetDatabase.Refresh();
        ConfigureSprite(Art + "MenuBackgroundGameplayClean.png", false);
        bodyFont = AssetDatabase.LoadAssetAtPath<Font>(Art + "Fonts/HL2-DejaVuSans.ttf");
        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        PlayerMotor motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
        PlayerLook look = UnityEngine.Object.FindAnyObjectByType<PlayerLook>();
        if (maze == null || motor == null || look == null) throw new InvalidOperationException("Player/maze missing.");
        var generationSettings = new SerializedObject(maze);
        generationSettings.FindProperty("generateOnAwake").boolValue = false;
        generationSettings.ApplyModifiedPropertiesWithoutUndo();
        DestroyRoot("Escape Interface");
        var ambience = UnityEngine.Object.FindAnyObjectByType<FPLab.Gameplay.Audio.GameplayAmbience>();
        if (ambience != null) ambience.transform.SetParent(null, true);
        var menuAmbience = UnityEngine.Object.FindAnyObjectByType<FPLab.Gameplay.Audio.MenuAmbience>();
        if (menuAmbience != null) menuAmbience.transform.SetParent(null, true);
        DestroyRoot("Escape Session");
        var session = new GameObject("Escape Session").AddComponent<EscapeSession>();
        Assign(session, "maze", maze, "playerMotor", motor, "playerLook", look);
        if (ambience != null)
        {
            ambience.transform.SetParent(session.transform, false);
            Assign(ambience, "session", session);
        }
        if (menuAmbience != null)
        {
            menuAmbience.transform.SetParent(session.transform, false);
            Assign(menuAmbience, "session", session);
        }

        Material walls = Material("EntrancePlaster", new Color(0.35f, 0.34f, 0.28f));
        Material floor = Material("EntranceFloor", new Color(0.19f, 0.18f, 0.155f));
        Material paint = Material("EntrancePaint", new Color(0.16f, 0.205f, 0.155f));
        Material door = Material("EntranceDoors", new Color(0.145f, 0.115f, 0.085f));
        Material trim = Material("EntranceTrim", new Color(0.085f, 0.095f, 0.075f));
        Material ceiling = Material("EntranceCeiling", new Color(0.23f, 0.225f, 0.19f));
        Material fixture = Material("EntranceLamp", new Color(0.65f, 0.59f, 0.39f), 1.3f);
        Material start = Material("EntranceStart", new Color(0.17f, 0.185f, 0.15f));
        Material exit = Material("EntranceExit", new Color(0.13f, 0.48f, 0.28f), 1.5f);
        Assign(maze, "wallMaterial", walls, "floorMaterial", floor, "startMaterial", start, "exitMaterial", exit);
        foreach (MeshRenderer renderer in maze.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.name.StartsWith("Wall ")) renderer.sharedMaterial = walls;
            else if (renderer.name == "Floor" || renderer.name == "Exit landing") renderer.sharedMaterial = floor;
            else if (renderer.name == "Start marker") renderer.sharedMaterial = start;
            else if (renderer.name == "Exit lintel" || renderer.name == "Exit threshold") renderer.sharedMaterial = exit;
        }
        MazeAtmosphere atmosphere = maze.GetComponent<MazeAtmosphere>();
        if (atmosphere == null) atmosphere = maze.gameObject.AddComponent<MazeAtmosphere>();
        Assign(atmosphere, "doorMaterial", door, "trimMaterial", trim, "ceilingMaterial", ceiling,
            "fixtureMaterial", fixture, "wallBandMaterial", paint,
            "numberMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Maze/EntranceDoorNumbers.mat"));
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.006f, 0.007f, 0.005f);
        RenderSettings.reflectionIntensity = 0.005f;
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.001f, 0.0012f, 0.001f);
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.008f;
        GameObject sun = GameObject.Find("Directional Light");
        if (sun != null) sun.SetActive(false);
        Camera camera = motor.GetComponentInChildren<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.001f, 0.0012f, 0.001f);
        Transform existingFlashlight = camera.transform.Find("Flashlight");
        GameObject flashlightObject = existingFlashlight != null ? existingFlashlight.gameObject : new GameObject("Flashlight");
        flashlightObject.transform.SetParent(camera.transform, false);
        flashlightObject.transform.localPosition = Vector3.zero;
        flashlightObject.transform.localRotation = Quaternion.identity;
        Light flashlight = flashlightObject.GetComponent<Light>();
        if (flashlight == null) flashlight = flashlightObject.AddComponent<Light>();
        flashlight.type = LightType.Spot;
        flashlight.color = Color.white;
        flashlight.useColorTemperature = false;
        flashlight.intensity = 12f;
        flashlight.range = 19.05f;
        flashlight.spotAngle = 45f;
        flashlight.innerSpotAngle = 44f;
        flashlight.cookie = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/UI/HalfLife2/Flashlight/flashlight_border.png");
        flashlight.enabled = false;
        flashlight.shadows = LightShadows.Soft;
        flashlight.shadowBias = 0.01f;
        flashlight.shadowNormalBias = 0.05f;
        flashlight.shadowNearPlane = 0.1f;
        var playerFlashlight = motor.GetComponent<FPLab.Gameplay.Player.Equipment.PlayerFlashlight>();
        if (playerFlashlight != null) Assign(playerFlashlight, "session", session, "beam", flashlight);

        EventSystem events = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
        if (events == null) events = new GameObject("EventSystem").AddComponent<EventSystem>();
        StandaloneInputModule legacy = events.GetComponent<StandaloneInputModule>();
        if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy);
        InputSystemUIInputModule module = events.GetComponent<InputSystemUIInputModule>();
        if (module == null) module = events.gameObject.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();

        GameObject canvasObject = new GameObject("Escape Interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 1f;
        RectTransform menu = Full("Menu", canvasObject.transform);
        RectTransform background = Full("Stairwell artwork", menu);
        Image art = background.gameObject.AddComponent<Image>();
        art.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + "MenuBackgroundGameplayClean.png");
        art.raycastTarget = false;
        var ratio = background.gameObject.AddComponent<AspectRatioFitter>();
        ratio.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        ratio.aspectRatio = art.sprite.rect.width / art.sprite.rect.height;
        Image shade = Full("Shade", menu).gameObject.AddComponent<Image>();
        shade.color = new Color(0f, 0f, 0f, 0.19f);

        Font introductionFont = bodyFont;
        Text menuTitle = Label("Menu title", menu, "PDaB", 128, MenuInk, 170, 245, 850, 170);
        Text description = Label("Description", menu, "", 24, MenuInk, 0, 0, 0, 0);
        description.gameObject.SetActive(false);
        Text result = Label("Result", menu, "", 22, MenuInk, 174, 482, 700, 34);
        RectTransform buttons = Rect("Buttons", menu, 170, 530, 450, 92);
        VerticalLayoutGroup layout = buttons.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlHeight = false;
        layout.childControlWidth = false;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = false;
        layout.spacing = 0f;
        Text primaryLabel;
        Button primary = Button("Primary", buttons, "НОВАЯ ИГРА", out primaryLabel);
        Text unused;
        Button restart = Button("Restart", buttons, "НАЧАТЬ ЗАНОВО", out unused);
        Button returnMenu = Button("Main menu", buttons, "ГЛАВНОЕ МЕНЮ", out unused);
        Button quit = Button("Quit", buttons, "ВЫХОД", out unused);
        restart.gameObject.SetActive(false);
        returnMenu.gameObject.SetActive(false);

        GameObject oldHud = GameObject.Find("Canvas");
        if (oldHud != null && oldHud.GetComponentInChildren<FPLab.UI.StaminaBar>(true) != null)
            UnityEngine.Object.DestroyImmediate(oldHud);
        GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/SourceRunHud.prefab");
        if (hudPrefab == null) throw new InvalidOperationException("Create SourceRunHud.prefab with SetupSourceHud first.");
        GameObject hud = (GameObject)PrefabUtility.InstantiatePrefab(hudPrefab, canvasObject.transform);
        hud.name = "Run HUD";
        Assign(hud.GetComponent<FPLab.UI.HUD.SourceHudView>(), "session", session,
            "auxPower", motor.GetComponent<PlayerAuxPower>(), "motor", motor,
            "flashlight", motor.GetComponent<FPLab.Gameplay.Player.Equipment.PlayerFlashlight>());

        GameObject loadingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/SourceLoadingScreen.prefab");
        if (loadingPrefab == null) throw new InvalidOperationException("Create SourceLoadingScreen.prefab with SetupLoadingScreen first.");
        GameObject loadingRoot = (GameObject)PrefabUtility.InstantiatePrefab(loadingPrefab, canvasObject.transform);
        loadingRoot.SetActive(false);
        EscapeMenuView view = canvasObject.AddComponent<EscapeMenuView>();
        Assign(view, "session", session, "menuRoot", menu.gameObject, "loadingRoot", loadingRoot, "runHud", hud,
            "menuTitle", menuTitle, "description", description, "introductionFont", introductionFont, "result", result,
            "primaryLabel", primaryLabel, "primaryButton", primary, "restartButton", restart,
            "menuButton", returnMenu, "quitButton", quit);
        hud.SetActive(false);
        result.gameObject.SetActive(false);
        EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        EditorSceneManager.SaveScene(session.gameObject.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = canvasObject;
        return new { ready = true, scene = session.gameObject.scene.path, generatedGraphics = 2 };
    }

    private static void ConfigureSprite(string path, bool alpha)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.alphaIsTransparency = alpha;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    private static Material Material(string name, Color color, float emission = 0f)
    {
        string path = "Assets/Materials/Maze/" + name + ".mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, path);
        }
        bool textured = material.GetTexture("_BaseMap") != null;
        if (!textured)
        {
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.08f);
        }
        material.enableInstancing = true;
        if (emission > 0f && !textured)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void DestroyRoot(string name)
    {
        GameObject existing = GameObject.Find(name);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
    }

    private static RectTransform Full(string name, Transform parent)
    {
        RectTransform rect = Rect(name, parent, 0, 0, 0, 0);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        var child = new GameObject(name, typeof(RectTransform));
        var rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    private static Text Label(string name, Transform parent, string value, int size, Color color,
        float x, float y, float width, float height)
    {
        Text label = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>();
        label.text = value;
        label.font = bodyFont;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAnchor.MiddleLeft;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        return label;
    }

    private static Button Button(string name, Transform parent, string caption, out Text label)
    {
        RectTransform rect = Rect(name, parent, 0, 0, 450f, 46f);
        Image hit = rect.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        Button button = rect.gameObject.AddComponent<Button>();
        label = Label("Label", rect, caption, 24, Color.white, 0, 0, 450f, 46f);
        label.font = bodyFont;
        label.fontStyle = FontStyle.Normal;
        label.alignment = TextAnchor.MiddleLeft;
        button.targetGraphic = label;
        button.transition = Selectable.Transition.ColorTint;
        ColorBlock colors = button.colors;
        colors.normalColor = MenuInk;
        colors.highlightedColor = new Color(0.95f, 0.79f, 0.38f);
        colors.selectedColor = colors.normalColor;
        colors.pressedColor = new Color(0.71f, 0.57f, 0.27f);
        colors.disabledColor = new Color(0.38f, 0.39f, 0.37f);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.12f;
        button.colors = colors;
        MenuButtonAudio audio = rect.gameObject.AddComponent<MenuButtonAudio>();
        Assign(audio,
            "hoverClip", AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/HalfLife2/UI/buttonrollover.wav"),
            "pressClip", AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/HalfLife2/UI/buttonclick.wav"),
            "clickClip", AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/HalfLife2/UI/buttonclickrelease.wav"));
        return button;
    }

    private static void Assign(UnityEngine.Object target, params object[] values)
    {
        var serialized = new SerializedObject(target);
        for (int i = 0; i < values.Length; i += 2)
        {
            SerializedProperty property = serialized.FindProperty((string)values[i]);
            if (property == null) throw new InvalidOperationException("Unknown field " + values[i]);
            property.objectReferenceValue = (UnityEngine.Object)values[i + 1];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
