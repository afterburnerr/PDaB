// Installs the loading UI without rebuilding the user's menu.
using System;
using System.IO;
using FPLab.Gameplay.Maze;
using FPLab.Gameplay.Session;
using FPLab.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SetupLoadingScreen
{
    private const string PrefabPath = "Assets/Prefabs/UI/SourceLoadingScreen.prefab";

    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before setup.");
        GameObject canvas = GameObject.Find("Escape Interface");
        EscapeMenuView view = canvas.GetComponent<EscapeMenuView>();
        Sprite artwork = canvas.transform.Find("Menu/Stairwell artwork").GetComponent<Image>().sprite;
        GameObject prefab = CreatePrefab(artwork);

        var viewSettings = new SerializedObject(view);
        GameObject screen = viewSettings.FindProperty("loadingRoot").objectReferenceValue as GameObject;
        if (screen == null)
            screen = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
        screen.transform.SetAsLastSibling();
        screen.SetActive(false);
        viewSettings.FindProperty("loadingRoot").objectReferenceValue = screen;
        viewSettings.ApplyModifiedPropertiesWithoutUndo();

        MazeBuilder maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        var generation = new SerializedObject(maze);
        generation.FindProperty("generateOnAwake").boolValue = false;
        generation.ApplyModifiedPropertiesWithoutUndo();
        EscapeSession session = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();
        var sessionSettings = new SerializedObject(session);
        sessionSettings.FindProperty("minimumLoadingSeconds").floatValue = 3f;
        sessionSettings.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(canvas.scene);
        EditorSceneManager.SaveScene(canvas.scene);
        AssetDatabase.SaveAssets();
        return new { saved = true, minimumSeconds = 3, generationDuringLoading = true };
    }

    private static GameObject CreatePrefab(Sprite artwork)
    {
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Prefabs/UI"));
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Materials/UI"));
        AssetDatabase.Refresh();
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/LoadingBackgroundBlur.shader");
        if (shader == null) throw new InvalidOperationException("Loading background shader is missing.");
        const string materialPath = "Assets/Materials/UI/LoadingBackgroundBlur.mat";
        Material blur = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (blur == null)
        {
            blur = new Material(shader);
            AssetDatabase.CreateAsset(blur, materialPath);
        }
        blur.shader = shader;
        blur.SetFloat("_BlurRadius", 36f);
        EditorUtility.SetDirty(blur);

        bool existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        RectTransform root = existing
            ? PrefabUtility.LoadPrefabContents(PrefabPath).GetComponent<RectTransform>()
            : Element("Source Loading Screen", null);
        Image dim = Component<Image>(root);
        dim.color = Color.black;
        // This full-screen graphic also blocks pointer input while loading.
        dim.raycastTarget = true;

        RectTransform background = Child("Blurred background", root);
        background.SetAsFirstSibling();
        Image backgroundImage = Component<Image>(background);
        backgroundImage.sprite = artwork;
        backgroundImage.material = blur;
        backgroundImage.color = new Color(0.81f, 0.81f, 0.81f, 1f);
        backgroundImage.raycastTarget = false;
        AspectRatioFitter aspect = Component<AspectRatioFitter>(background);
        aspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        aspect.aspectRatio = artwork.rect.width / artwork.rect.height;

        RectTransform panel = Child("Loading panel", root);
        panel.anchorMin = panel.anchorMax = new Vector2(1f, 0f);
        panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-14f, 16f);
        panel.sizeDelta = new Vector2(136f, 44f);
        Component<Image>(panel).color = Color.black;

        RectTransform inset = Child("Panel fill", panel);
        inset.offsetMin = Vector2.zero;
        inset.offsetMax = Vector2.zero;
        Component<Image>(inset).color = Color.black;

        Text caption = Component<Text>(Child("Loading label", inset));
        caption.text = "Загрузка...";
        caption.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/UI/Entrance/Fonts/NotoSans-Regular.ttf");
        caption.fontStyle = FontStyle.Normal;
        caption.fontSize = 18;
        caption.alignment = TextAnchor.MiddleCenter;
        caption.color = Color.white;
        caption.raycastTarget = false;

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
        if (existing) PrefabUtility.UnloadPrefabContents(root.gameObject);
        else UnityEngine.Object.DestroyImmediate(root.gameObject);
        return prefab;
    }

    private static T Component<T>(RectTransform element) where T : Component
    {
        T component = element.GetComponent<T>();
        return component != null ? component : element.gameObject.AddComponent<T>();
    }

    private static RectTransform Child(string name, RectTransform parent)
    {
        Transform child = parent.Find(name);
        return child != null ? (RectTransform)child : Element(name, parent);
    }

    private static RectTransform Element(string name, Transform parent)
    {
        var element = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        element.SetParent(parent, false);
        element.anchorMin = Vector2.zero;
        element.anchorMax = Vector2.one;
        element.offsetMin = element.offsetMax = Vector2.zero;
        element.localScale = Vector3.one;
        return element;
    }
}
