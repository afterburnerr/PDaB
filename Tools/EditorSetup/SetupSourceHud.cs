// Replaces only the gameplay HUD, preserving the existing menu and loading UI.
using System;
using System.IO;
using FPLab.Gameplay.Session;
using FPLab.Gameplay.Player.Equipment;
using FPLab.UI;
using FPLab.UI.HUD;
using FPLab.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SetupSourceHud
{
    private const string PrefabPath = "Assets/Prefabs/UI/SourceRunHud.prefab";
    private const float Scale = 2.25f; // Source's 480-line proportional HUD at 1080p.
    private static readonly Color Ink = new Color32(255, 220, 0, 220);

    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before saving the HUD.");
        GameObject canvas = GameObject.Find("Escape Interface");
        EscapeMenuView menu = canvas.GetComponent<EscapeMenuView>();
        var menuFields = new SerializedObject(menu);
        GameObject oldHud = menuFields.FindProperty("runHud").objectReferenceValue as GameObject;
        GameObject prefab = CreatePrefab();
        if (oldHud != null) UnityEngine.Object.DestroyImmediate(oldHud);

        // Remove the previous continuous stamina bar, including its separate canvas.
        foreach (StaminaBar oldBar in UnityEngine.Object.FindObjectsByType<StaminaBar>(FindObjectsInactive.Include))
        {
            Canvas oldCanvas = oldBar.GetComponentInParent<Canvas>();
            if (oldCanvas != null && oldCanvas.gameObject != canvas)
                UnityEngine.Object.DestroyImmediate(oldCanvas.gameObject);
            else
                UnityEngine.Object.DestroyImmediate(oldBar.gameObject);
        }

        GameObject hud = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
        hud.name = "Run HUD";
        hud.SetActive(false);
        PlayerMotor motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
        var hudFields = new SerializedObject(hud.GetComponent<SourceHudView>());
        hudFields.FindProperty("session").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();
        hudFields.FindProperty("auxPower").objectReferenceValue = motor.GetComponent<PlayerAuxPower>();
        hudFields.FindProperty("motor").objectReferenceValue = motor;
        hudFields.FindProperty("flashlight").objectReferenceValue = motor.GetComponent<PlayerFlashlight>();
        hudFields.ApplyModifiedPropertiesWithoutUndo();
        menuFields.FindProperty("runHud").objectReferenceValue = hud;
        menuFields.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(canvas.scene);
        EditorSceneManager.SaveScene(canvas.scene);
        AssetDatabase.SaveAssets();
        return new { saved = true, segments = 10, lowPowerBelow = 0.25f, transitionSeconds = 0.4f };
    }

    private static GameObject CreatePrefab()
    {
        Font labels = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/UI/HalfLife2/Fonts/DejaVuSans-Bold.ttf");
        Font numbers = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/UI/HalfLife2/Fonts/HalfLife2.ttf");
        if (labels == null || numbers == null) throw new InvalidOperationException("Import the HL2 HUD fonts first.");
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Prefabs/UI"));
        AssetDatabase.Refresh();

        RectTransform root = Rect("Run HUD", null, 0, 0, 0, 0);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        root.gameObject.SetActive(false);
        SourceHudView view = root.gameObject.AddComponent<SourceHudView>();
        var fields = new SerializedObject(view);

        RectTransform aux = Panel("AUX POWER", root, false, 102f * Scale, 36f * Scale);
        Text caption = Label("Caption", aux, "AUX POWER", labels, 20, 8f * Scale, 4f * Scale, 90f * Scale, 24);
        Text sprint = Label("Sprint", aux, "SPRINT", labels, 20, 8f * Scale, 22f * Scale, 90f * Scale, 24);
        Text flashlight = Label("Flashlight", aux, "FLASHLIGHT", labels, 20, 8f * Scale, 34f * Scale, 90f * Scale, 24);
        SerializedProperty segments = fields.FindProperty("segments");
        segments.arraySize = 10;
        for (int i = 0; i < 10; i++)
        {
            RectTransform segment = Rect("Segment " + (i + 1), aux,
                (8f + i * 9f) * Scale, 15f * Scale, 6f * Scale, 4f * Scale);
            Image image = segment.gameObject.AddComponent<Image>();
            image.color = Ink;
            image.raycastTarget = false;
            segments.GetArrayElementAtIndex(i).objectReferenceValue = image;
        }

        RectTransform clock = Panel("Timer panel", root, true, 252f, 108f);
        Label("Caption", clock, "TIME", labels, 20, 18f, 9f, 200f, 24f);
        Text minutes = Label("Minutes", clock, "00", numbers, 72, 18f, 20f, 98f, 88f);
        minutes.alignment = TextAnchor.UpperRight;
        Text seconds = Label("Seconds", clock, "00", numbers, 72, 136f, 20f, 98f, 88f);
        // HalfLife2's ':' glyph is an icon. Use two small blocks for a clock separator.
        foreach (float y in new[] { 60f, 79f })
        {
            Image dot = Rect("Colon dot", clock, 123f, y, 4f, 4f).gameObject.AddComponent<Image>();
            dot.color = Ink;
            dot.raycastTarget = false;
        }
        fields.FindProperty("auxCaption").objectReferenceValue = caption;
        fields.FindProperty("sprintCaption").objectReferenceValue = sprint;
        fields.FindProperty("flashlightCaption").objectReferenceValue = flashlight;
        fields.FindProperty("auxPanel").objectReferenceValue = aux;
        fields.FindProperty("minutes").objectReferenceValue = minutes;
        fields.FindProperty("seconds").objectReferenceValue = seconds;
        fields.ApplyModifiedPropertiesWithoutUndo();
        root.gameObject.SetActive(false);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root.gameObject);
        return prefab;
    }

    private static RectTransform Panel(string name, Transform parent, bool right, float width, float height)
    {
        RectTransform rect = Rect(name, parent, 0, 0, width, height);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(right ? 1f : 0f, 0f);
        rect.anchoredPosition = new Vector2(right ? -36f : 36f, 36f);
        Image shade = rect.gameObject.AddComponent<Image>();
        shade.color = new Color32(0, 0, 0, 76);
        shade.raycastTarget = false;
        return rect;
    }

    private static Text Label(string name, Transform parent, string text, Font font, int size, float x, float y, float width, float height)
    {
        Text label = Rect(name, parent, x, y, width, height).gameObject.AddComponent<Text>();
        label.text = text;
        label.font = font;
        label.fontSize = size;
        label.fontStyle = FontStyle.Normal;
        label.color = Ink;
        label.alignment = TextAnchor.UpperLeft;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }
}
