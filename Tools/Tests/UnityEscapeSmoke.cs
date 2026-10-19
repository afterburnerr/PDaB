// Start in a fresh Play Mode session at the main menu:
// unity command run_script --file Tools/Tests/UnityEscapeSmoke.cs --entry UnityEscapeSmoke.Run
// Poll the asynchronous result with the same command and --entry UnityEscapeSmoke.Status.
using System;
using FPLab.Gameplay.Maze;
using FPLab.Gameplay.Session;
using UnityEditor;
using UnityEngine;

public static class UnityEscapeSmoke
{
    private const string ResultKey = "FPLab.UnityEscapeSmoke.Result";
    private static EscapeSession session;
    private static MazeBuilder maze;
    private static double phaseStarted;
    private static float elapsedAtPause;
    private static int phase;
    private static int wonTransitions;
    private static GameObject unrelatedPlayer;

    public static object Run()
    {
        Require(Application.isPlaying, "Enter Play Mode first.");
        Require(SessionState.GetString(ResultKey, "") != "running", "A smoke check is already running.");
        session = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();
        maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        Require(session != null && maze != null && maze.Data == null, "Session exists and no maze is generated at the main menu.");
        Require(session.State == RunState.Ready, "Start from the main menu in a fresh Play Mode session.");
        Require(session.ElapsedSeconds == 0f && Time.timeScale == 0f, "Ready starts at zero with simulation stopped.");
        Require(Cursor.lockState == CursorLockMode.None && Cursor.visible, "Ready frees the cursor.");

        wonTransitions = 0;
        session.StateChanged += RecordState;
        phaseStarted = EditorApplication.timeSinceStartup;
        session.BeginRun();
        RequireLoading();
        phase = -1;
        SessionState.SetString(ResultKey, "running");
        EditorApplication.update += Tick;
        return new { started = true, statusEntry = "UnityEscapeSmoke.Status" };
    }

    public static object Status()
    {
        return new { result = SessionState.GetString(ResultKey, "not started") };
    }

    private static void Tick()
    {
        try
        {
            Require(Application.isPlaying && session != null, "Play Mode remains active during the check.");
            if (phase == -1)
            {
                if (session.State == RunState.Loading)
                {
                    RequireLoading();
                    return;
                }

                Require(session.State == RunState.Playing && Time.timeScale == 1f, "Loading completes into Playing.");
                Require(EditorApplication.timeSinceStartup - phaseStarted >= 3d, "Loading stays visible for at least three realtime seconds.");
                Require(maze.Data != null, "Loading generates the maze before play.");
                Require(Cursor.lockState == CursorLockMode.Locked && !Cursor.visible, "Playing captures the cursor.");
                phase = 0;
                phaseStarted = EditorApplication.timeSinceStartup;
                return;
            }

            if (EditorApplication.timeSinceStartup - phaseStarted < 0.3d)
                return;

            if (phase == 0)
            {
                Require(session.ElapsedSeconds > 0f, "Run timer advances across real frames.");
                session.Pause();
                Require(session.State == RunState.Paused && Time.timeScale == 0f, "Pause stops the simulation.");
                Require(Cursor.lockState == CursorLockMode.None && Cursor.visible, "Pause frees the cursor.");
                elapsedAtPause = session.ElapsedSeconds;
                phase = 1;
            }
            else if (phase == 1)
            {
                Require(session.ElapsedSeconds == elapsedAtPause, "Paused frames do not count toward run time.");
                session.Resume();
                Require(session.State == RunState.Playing && Time.timeScale == 1f, "Resume restarts the simulation.");
                phase = 2;
            }
            else if (phase == 2)
            {
                Require(session.ElapsedSeconds > elapsedAtPause, "Timer resumes without resetting.");
                TestPhysicalExit();
                Require(session.State == RunState.Won && wonTransitions == 1, "The configured player wins exactly once.");
                Require(Time.timeScale == 0f && Cursor.lockState == CursorLockMode.None && Cursor.visible,
                    "Victory stops the simulation and frees the cursor.");
                elapsedAtPause = session.ElapsedSeconds;
                phase = 3;
            }
            else
            {
                Require(session.ElapsedSeconds == elapsedAtPause, "Victory freezes the final time.");
                session.BeginRun();
                session.Pause();
                session.Resume();
                Require(session.State == RunState.Won && wonTransitions == 1, "Completed runs ignore play/pause calls.");
                Finish("passed: ready without generation, minimum loading duration, loading excluded from timer, timer, pause, resume, unrelated capsule rejection, physical player exit, one victory, frozen final time");
                return;
            }
            phaseStarted = EditorApplication.timeSinceStartup;
        }
        catch (Exception exception)
        {
            Finish("failed: " + exception);
        }
    }

    private static void RequireLoading()
    {
        Require(session.State == RunState.Loading && Time.timeScale == 0f && session.ElapsedSeconds == 0f,
            "Loading freezes simulation and excludes generation from the run timer.");
        Require(!UnityEngine.Object.FindAnyObjectByType<PlayerMotor>().enabled
            && !UnityEngine.Object.FindAnyObjectByType<PlayerLook>().enabled, "Loading disables player movement and look.");
    }

    private static void TestPhysicalExit()
    {
        var settings = new SerializedObject(maze);
        Transform player = (Transform)settings.FindProperty("player").objectReferenceValue;
        CharacterController controller = player.GetComponent<CharacterController>();
        Require(controller != null && controller.enabled, "Configured player has an enabled CharacterController.");
        Vector3 outward;
        switch (maze.Data.ExitWall)
        {
            case MazeWall.North: outward = Vector3.forward; break;
            case MazeWall.East: outward = Vector3.right; break;
            case MazeWall.South: outward = Vector3.back; break;
            default: outward = Vector3.left; break;
        }
        outward = maze.transform.TransformDirection(outward);
        float footOffset = Mathf.Max(controller.height * 0.5f, controller.radius) - controller.center.y;
        Vector3 insideExit = maze.ExitPosition - outward * maze.CellSize + Vector3.up * footOffset;
        SimulationMode previousMode = Physics.simulationMode;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            unrelatedPlayer = new GameObject("Escape smoke unrelated capsule");
            CharacterController unrelatedController = unrelatedPlayer.AddComponent<CharacterController>();
            unrelatedController.height = controller.height;
            unrelatedController.radius = controller.radius;
            Teleport(unrelatedController, insideExit);
            unrelatedController.Move(outward * maze.CellSize);
            Physics.Simulate(Time.fixedDeltaTime);
            Require(session.State == RunState.Playing && wonTransitions == 0, "An unrelated CharacterController cannot finish the run.");
            UnityEngine.Object.DestroyImmediate(unrelatedPlayer);
            unrelatedPlayer = null;

            Teleport(controller, insideExit);
            controller.Move(outward * maze.CellSize);
            Physics.Simulate(Time.fixedDeltaTime);
            Require(Vector3.ProjectOnPlane(player.position - maze.ExitPosition, Vector3.up).magnitude < 0.1f,
                "The real controller physically crosses the exit opening.");
            Physics.Simulate(Time.fixedDeltaTime);
        }
        finally
        {
            Physics.simulationMode = previousMode;
        }
    }

    private static void Teleport(CharacterController controller, Vector3 position)
    {
        controller.enabled = false;
        controller.transform.position = position;
        controller.enabled = true;
        Physics.SyncTransforms();
    }

    private static void RecordState()
    {
        if (session.State == RunState.Won)
            wonTransitions++;
    }

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        if (session != null)
            session.StateChanged -= RecordState;
        if (unrelatedPlayer != null)
            UnityEngine.Object.DestroyImmediate(unrelatedPlayer);
        SessionState.SetString(ResultKey, result);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
