// Updates only menu presentation; preserves gameplay, audio, loading and HUD setup.
using System;
using FPLab.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class SetupHalfLifeMenu
{
    public static object Run()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play before authoring the menu.");
        AssetDatabase.Refresh();
        EscapeMenuView view = UnityEngine.Object.FindAnyObjectByType<EscapeMenuView>(FindObjectsInactive.Include);
        if (view == null) throw new InvalidOperationException("Escape menu is missing.");
        Font font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Art/UI/Entrance/Fonts/HL2-DejaVuSans.ttf");
        if (font == null) throw new InvalidOperationException("Bundled Half-Life 2 menu font is missing.");
        var fields = new SerializedObject(view);
        var menu = (GameObject)fields.FindProperty("menuRoot").objectReferenceValue;
        if (menu == null) throw new InvalidOperationException("Menu root is missing.");
        Transform existingTitle = menu.transform.Find("Menu title");
        Text title;
        if (existingTitle == null)
        {
            var titleObject = new GameObject("Menu title", typeof(RectTransform), typeof(Text));
            titleObject.transform.SetParent(menu.transform, false);
            title = titleObject.GetComponent<Text>();
        }
        else
        {
            title = existingTitle.GetComponent<Text>();
            if (title == null) throw new InvalidOperationException("Menu title must be a Text component.");
        }
        fields.FindProperty("menuTitle").objectReferenceValue = title;
        fields.FindProperty("introductionFont").objectReferenceValue = font;
        var description = fields.FindProperty("description").objectReferenceValue as Text;
        if (description != null)
        {
            description.text = "";
            description.gameObject.SetActive(false);
            EditorUtility.SetDirty(description);
        }
        fields.ApplyModifiedPropertiesWithoutUndo();
        CanvasScaler scaler = view.GetComponent<CanvasScaler>();
        if (scaler == null) throw new InvalidOperationException("Menu CanvasScaler is missing.");
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        // Apply the same presentation path used by every actual session state.
        view.SendMessage("Refresh", SendMessageOptions.RequireReceiver);
        Canvas.ForceUpdateCanvases();
        EditorUtility.SetDirty(view);
        EditorUtility.SetDirty(scaler);
        EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        EditorSceneManager.SaveScene(view.gameObject.scene);
        AssetDatabase.SaveAssets();
        return new
        {
            ready = true,
            title = title.text,
            font = font.name,
            titleFontSize = title.fontSize,
            titlePosition = title.rectTransform.anchoredPosition.ToString(),
            buttonFontSize = 24,
            rowHeight = 46,
            backgroundPreserved = true,
            loadingAndHudPreserved = true,
            scene = view.gameObject.scene.path
        };
    }
}
