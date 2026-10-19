using System;
using System.Collections.Generic;
using FPLab.Gameplay.Session;
using FPLab.UI.Menus;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Run at the main menu. Exercises UI raycasts and real Button event handlers.
public static class UnityMenuSmoke
{
    public static object Run()
    {
        EscapeSession session = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();
        Require(session != null && session.State == RunState.Ready, "Start at the main menu.");
        Transform root = GameObject.Find("Escape Interface").transform;
        Transform menu = root.Find("Menu");
        Canvas.ForceUpdateCanvases();
        CheckVisible(menu.Find("Description"));
        Button primary = menu.Find("Buttons/Primary").GetComponent<Button>();
        CheckButton(primary);
        CheckButton(menu.Find("Buttons/Quit").GetComponent<Button>());
        Require(EscapeMenuView.FormatTime(0f) == "00:00" && EscapeMenuView.FormatTime(125.9f) == "02:05"
            && EscapeMenuView.FormatTime(3600f) == "60:00", "Clock formats elapsed minutes without rounding up.");
        ExecuteEvents.Execute(primary.gameObject, new PointerEventData(EventSystem.current)
            { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        Require(session.State == RunState.Loading && session.ElapsedSeconds == 0f && Time.timeScale == 0f,
            "Clicking the generated button begins loading with a frozen run timer.");
        Require(root.Find("Source Loading Screen").gameObject.activeSelf, "Loading screen is visible.");
        Require(!root.Find("Run HUD").gameObject.activeSelf, "Run HUD stays hidden during loading.");
        return new { loading = true, nextEntry = "UnityMenuSmoke.AfterLoading", minimumLoadingSeconds = 3 };
    }

    public static object AfterLoading()
    {
        EscapeSession session = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();
        Require(session != null && session.State == RunState.Playing, "Wait for loading to complete into Playing.");
        Transform root = GameObject.Find("Escape Interface").transform;
        Require(!root.Find("Menu").gameObject.activeSelf && !root.Find("Source Loading Screen").gameObject.activeSelf,
            "Gameplay hides the menu and loading screen.");
        Require(root.Find("Run HUD").gameObject.activeSelf, "Run timer HUD is visible.");
        Canvas.ForceUpdateCanvases();
        CheckVisible(root.Find("Run HUD/AUX POWER"));
        CheckVisible(root.Find("Run HUD/Timer panel"));
        session.Pause();
        // Newly activated Graphics acquire their raycast depth on the next
        // rendered frame. Check the pause screen in a separate CLI call.
        return new { started = true, nextEntry = "UnityMenuSmoke.PauseButtons", width = Screen.width, height = Screen.height };
    }

    public static object PauseButtons()
    {
        EscapeSession session = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();
        Require(session.State == RunState.Paused, "Open the pause screen first.");
        Transform menu = GameObject.Find("Escape Interface").transform.Find("Menu");
        Button primary = menu.Find("Buttons/Primary").GetComponent<Button>();
        Canvas.ForceUpdateCanvases();
        int checkedPauseButtons = 0;
        foreach (Button button in menu.GetComponentsInChildren<Button>())
        {
            CheckButton(button);
            checkedPauseButtons++;
        }
        Require(checkedPauseButtons == 4, "Pause offers continue, restart, main menu and quit.");
        ExecuteEvents.Execute(primary.gameObject, new PointerEventData(EventSystem.current)
            { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        Require(session.State == RunState.Playing, "Continue button resumes.");
        session.Pause();
        return new { passed = true, checkedPauseButtons, width = Screen.width, height = Screen.height };
    }

    public static object Layout()
    {
        Transform menu = GameObject.Find("Escape Interface").transform.Find("Menu");
        Require(menu.gameObject.activeInHierarchy, "Open a menu before the layout check.");
        Canvas.ForceUpdateCanvases();
        CheckVisible(menu.Find("Description"));
        foreach (Button button in menu.GetComponentsInChildren<Button>()) CheckButton(button);
        return new { passed = true, width = Screen.width, height = Screen.height };
    }

    private static void CheckButton(Button button)
    {
        CheckVisible(button.transform);
        Vector3[] corners = new Vector3[4];
        button.GetComponent<RectTransform>().GetWorldCorners(corners);
        Vector2 center = (corners[0] + corners[2]) * 0.5f;
        var pointer = new PointerEventData(EventSystem.current) { position = center };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);
        Require(hits.Count > 0 && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) == button.gameObject,
            "Button receives the top UI raycast: " + button.name);
    }

    private static void CheckVisible(Transform target)
    {
        Vector3[] corners = new Vector3[4];
        ((RectTransform)target).GetWorldCorners(corners);
        foreach (Vector3 corner in corners)
            Require(corner.x >= -1f && corner.x <= Screen.width + 1f && corner.y >= -1f && corner.y <= Screen.height + 1f,
                "UI rectangle fits screen: " + target.name + " at " + corner);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
