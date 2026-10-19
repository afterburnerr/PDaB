using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FPLab.Gameplay.Audio;
using FPLab.Gameplay.Enemies;
using FPLab.Gameplay.Maze;
using FPLab.Gameplay.Player.Equipment;
using FPLab.Gameplay.Session;
using FPLab.UI.HUD;
using FPLab.UI.Menus;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Editor-only, explicitly invoked fixture. Does not change scenes/assets or enter/exit Play.
public static class UnityStalkerSmoke
{
    public static string State { get; private set; } = "idle";
    private static readonly List<object> checks = new List<object>();
    private static readonly List<Keyboard> keyboards = new List<Keyboard>();
    private static Keyboard keyboard;
    private static EscapeSession session;
    private static MazeBuilder maze;
    private static EntranceStalker enemy;
    private static PlayerMotor motor;
    private static PlayerLook look;
    private static Transform player;
    private static double started, phaseStarted;
    private static int phase, darkCell, safeNeighbor, keyStep;
    private static Vector3 enemyBefore;
    private static float initialGraph;
    private static bool sawWhispers;
    private static EscapeSession previousSession;
    private static string output;
    private static readonly Key[] cheatKeys = { Key.E, Key.A, Key.S, Key.Y, Key.T, Key.P };

    public static object Run()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run only after entering Play and reaching Ready.");
        if (State == "running") throw new InvalidOperationException("A smoke fixture is already running.");
        output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Tools/Tests/Stalker");
        Directory.CreateDirectory(output);
        checks.Clear(); keyboards.Clear();
        started = phaseStarted = EditorApplication.timeSinceStartup;
        Bind();
        if (session.State != RunState.Ready) throw new InvalidOperationException("Start this fixture from the main menu (Ready).");
        try
        {
            foreach (Keyboard device in InputSystem.devices.OfType<Keyboard>().ToArray())
                if (device.enabled) { keyboards.Add(device); InputSystem.DisableDevice(device); }
            keyboard = InputSystem.AddDevice<Keyboard>("StalkerSmokeKeyboard");
        }
        catch { CleanupInput(); throw; }
        started = phaseStarted = EditorApplication.timeSinceStartup;
        phase = keyStep = 0; sawWhispers = false;
        State = "running";
        EditorApplication.update += Tick;
        WriteReport(false, null);
        return new { started = true, report = Path.Combine(output, "PlayReport.json"), timeoutSeconds = 120 };
    }

    public static object Abort()
    {
        if (State == "running") Finish(false, "Aborted by operator.");
        return new { state = State };
    }

    private static void Bind()
    {
        session = UnityEngine.Object.FindAnyObjectByType<EscapeSession>();
        maze = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        enemy = UnityEngine.Object.FindAnyObjectByType<EntranceStalker>(FindObjectsInactive.Include);
        motor = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>(FindObjectsInactive.Include);
        look = UnityEngine.Object.FindAnyObjectByType<PlayerLook>(FindObjectsInactive.Include);
        Require(session != null && maze != null && enemy != null && motor != null && look != null, "Runtime references present");
        player = motor.transform;
    }

    private static double Age => EditorApplication.timeSinceStartup - phaseStarted;
    private static void Next(int next) { phase = next; phaseStarted = EditorApplication.timeSinceStartup; WriteReport(false, null); }
    private static void Tick()
    {
        try
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Play Mode ended while fixture was running.");
            if (EditorApplication.timeSinceStartup - started > 120) throw new TimeoutException("Global 120 second timeout, phase " + phase);
            if (session != null && session.State == RunState.Playing && phase != 8)
            {
                motor.enabled = false;
                look.enabled = false;
            }
            switch (phase)
            {
                case 0:
                    if (Age < 0.5) break;
                    var ambience = UnityEngine.Object.FindAnyObjectByType<MenuAmbience>();
                    AudioSource menuAudio = Get<AudioSource>(ambience, "source");
                    Require(menuAudio != null && menuAudio.isPlaying && menuAudio.clip != null && menuAudio.clip.name.Contains("Citadel"), "Ready menu plays Citadel ambience");
                    Shot("Menu.png"); Next(1); break;
                case 1:
                    if (Age < 0.7) break;
                    session.BeginRun(); Next(2); break;
                case 2:
                    if (session.State != RunState.Playing) { Deadline(15, "Loading did not finish"); break; }
                    Require(LampSafeZone.Active.Count == 32, "32 registered ceiling light safe zones", LampSafeZone.Active.Count);
                    Warp(player, maze.StartPosition);
                    Require(LampSafeZone.FindAt(Feet(), player) != null, "Start is protected by a real ceiling lamp");
                    List<int> route = ExitRoute();
                    darkCell = route.Skip(14).Take(20).FirstOrDefault(c => LampSafeZone.FindAt(Center(c), player) == null);
                    Require(darkCell != 0, "Found dark route cell after sufficient fair-spawn progress", darkCell);
                    Warp(player, Center(darkCell));
                    var initialFlashlight = UnityEngine.Object.FindAnyObjectByType<PlayerFlashlight>();
                    initialFlashlight.GetType().GetMethod("SetOn", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(initialFlashlight, new object[] {true});
                    player.rotation = Quaternion.LookRotation(Center(route[route.IndexOf(darkCell)+1]) - Center(darkCell), Vector3.up);
                    Next(3); break;
                case 3:
                    if (!enemy.IsActive) { Deadline(26, "Enemy did not fairly spawn behind the progressed player"); break; }
                    Require(enemy.GraphDistanceToPlayer >= 8, "Fair spawn retains graph distance", enemy.GraphDistanceToPlayer);
                    Require(enemy.VisualRoot.gameObject.activeInHierarchy, "Enemy visual is active");
                    Animator animator = enemy.VisualRoot.GetComponentInChildren<Animator>();
                    Require(animator != null && animator.runtimeAnimatorController != null, "Enemy Animator/controller present");
                    Require(animator.GetCurrentAnimatorClipInfo(0).Length > 0, "Animator has an active clip");
                    Shot("Gameplay.png");
                    enemyBefore = enemy.WorldPosition;
                    Next(4); break;
                case 4:
                    var audioView = UnityEngine.Object.FindAnyObjectByType<StalkerThreatView>();
                    AudioSource murmurs = Get<AudioSource>(audioView, "murmurs");
                    if (enemy.Threat01 > 0.01f && murmurs != null && murmurs.isPlaying) sawWhispers = true;
                    if (Age < 2) break;
                    Require(Vector3.Distance(enemyBefore, enemy.WorldPosition) > 0.25f, "Enemy actually advances over two seconds");
                    Require(sawWhispers, "Threat starts positional murmurs during approach");
                    Warp(player, maze.StartPosition);
                    safeNeighbor = Neighbors(0).First();
                    PositionEnemy(Center(safeNeighbor), safeNeighbor);
                    initialGraph = 1;
                    Next(5); break;
                case 5:
                    if (Age < 2) break;
                    Require(session.State == RunState.Playing && enemy.PlayerIsSafe, "Player survives under lamp");
                    Require(enemy.IsRetreating || enemy.GraphDistanceToPlayer > initialGraph, "Lamp triggers retreat rather than camping", enemy.GraphDistanceToPlayer);
                    Next(6); break;
                case 6:
                    if (Age < 12) break;
                    Require(session.State == RunState.Playing && enemy.PlayerIsSafe, "Protected player survives sustained lamp hold");
                    Require(enemy.GraphDistanceToPlayer >= 8, "Enemy retreats at least eight graph cells", enemy.GraphDistanceToPlayer);
                    Next(7); break;
                case 7:
                    if (Age < 5) break;
                    Require(session.State == RunState.Playing && enemy.PlayerIsSafe && enemy.GraphDistanceToPlayer >= 8, "Enemy does not return and camp while player stays lit", enemy.GraphDistanceToPlayer);
                    LampSafeZone zone = LampSafeZone.FindAt(Feet(), player);
                    Light source = Get<Light>(zone, "source");
                    bool sourceEnabled = source.enabled;
                    try { source.enabled = false; Require(LampSafeZone.FindAt(Feet(), player) == null, "Unpowered lamp grants no safety"); }
                    finally { source.enabled = sourceEnabled; }
                    Warp(player, Center(darkCell));
                    var flashlight = UnityEngine.Object.FindAnyObjectByType<PlayerFlashlight>();
                    Require(flashlight != null, "Flashlight component exists");
                    flashlight.GetType().GetMethod("SetOn", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(flashlight, new object[] {true});
                    Require(flashlight.IsOn, "Player flashlight really is switched on during safety check");
                    Require(LampSafeZone.FindAt(Feet(), player) == null, "Flashlight cannot register a safe zone in a dark cell");
                    int adjacent = Neighbors(darkCell).First();
                    Vector3 direction = (Center(adjacent) - Center(darkCell)).normalized;
                    PositionEnemy(Center(darkCell) + direction * 0.84f, darkCell);
                    Set(enemy, "retreatRemaining", 10f);
                    Set(enemy, "contactSeconds", 0f); Set(enemy, "caught", false);
                    Require(enemy.IsRetreating, "Dark contact fixture starts during active retreat cooldown");
                    motor.enabled = look.enabled = true;
                    Next(8); break;
                case 8:
                    if (session.State != RunState.Caught) { Deadline(2, "Close contact did not trigger Caught"); break; }
                    Require(!motor.enabled && !look.enabled && Time.timeScale == 0f, "Capture freezes player controls/world");
                    Require(!UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include).Any(c => c.GetType().Name == "StalkerCaptureView"), "No camera capture animation component remains");
                    Image screamer = Get<Image>(UnityEngine.Object.FindAnyObjectByType<StalkerScreamerView>(), "image");
                    Require(screamer != null && screamer.enabled && screamer.sprite != null, "Still capture illustration is visible");
                    AudioSource stinger = Get<AudioSource>(UnityEngine.Object.FindAnyObjectByType<StalkerThreatView>(), "captureSound");
                    Require(stinger != null && stinger.isPlaying && stinger.clip != null && stinger.ignoreListenerPause, "Jumpscare sound plays despite frozen game");
                    Require(!enemy.IsRetreating, "Enemy confronts player following into darkness without waiting for cooldown");
                    Shot("StalkerCaught.png"); Next(9); break;
                case 9:
                    if (session.State != RunState.Lost) { Deadline(5, "Caught did not finish in Lost"); break; }
                    EscapeMenuView menu = UnityEngine.Object.FindAnyObjectByType<EscapeMenuView>();
                    Button restart = Get<Button>(menu, "primaryButton");
                    Require(restart != null && restart.gameObject.activeInHierarchy && restart.GetComponentInChildren<Text>().text == "НАЧАТЬ ЗАНОВО", "Defeat offers restart");
                    Require(Get<Image>(UnityEngine.Object.FindAnyObjectByType<StalkerScreamerView>(), "image").enabled, "Illustration remains behind defeat UI");
                    Shot("Defeat.png"); Next(10); break;
                case 10:
                    if (Age < 0.7) break;
                    previousSession = session;
                    session.RestartRun(); Next(11); break;
                case 11:
                    if (session == null || session == previousSession) Bind();
                    if (session == previousSession || session.State != RunState.Playing) { Deadline(18, "Restart did not reload and begin a new run"); break; }
                    Require(!enemy.IsDisabledByCheat && session.ElapsedSeconds < 3f, "Restart resets enemy cheat flag and elapsed time");
                    Warp(player, maze.StartPosition);
                    keyStep = 0; Next(12); break;
                case 12:
                    if (Age < 0.12) break;
                    InputSystem.QueueStateEvent(keyboard, keyStep % 2 == 0 ? new KeyboardState(cheatKeys[keyStep / 2]) : new KeyboardState());
                    keyStep++; phaseStarted = EditorApplication.timeSinceStartup;
                    if (keyStep == 8) Next(13);
                    break;
                case 13:
                    if (Age < 0.3) break;
                    Require(enemy.IsDisabledByCheat && !enemy.VisualRoot.gameObject.activeInHierarchy && enemy.Threat01 == 0f, "Actual easy key sequence disables enemy for this run");
                    Next(14); break;
                case 14:
                    if (Age < 0.12) break;
                    InputSystem.QueueStateEvent(keyboard, keyStep % 2 == 0 ? new KeyboardState(cheatKeys[keyStep / 2]) : new KeyboardState());
                    keyStep++; phaseStarted = EditorApplication.timeSinceStartup;
                    if (keyStep == 12) Next(15);
                    break;
                case 15:
                    if (Age < 0.4) break;
                    Require(session.State == RunState.Playing, "Actual tp keys do not finish the run");
                    Vector3 outward = ExitDirection();
                    Require(Vector3.Distance(Feet(), maze.ExitPosition - outward * maze.CellSize) < 0.1f, "tp places player inside before the exit");
                    Require(Vector3.Dot(player.forward, outward) > 0.95f, "tp faces the player toward the exit");
                    Next(16); break;
                case 16:
                    if (session.State != RunState.Won)
                    {
                        player.GetComponent<CharacterController>().Move(ExitDirection() * (4f * (float)Math.Min(0.05, Time.unscaledDeltaTime)));
                        Deadline(4, "Walking through the actual exit trigger did not win");
                        break;
                    }
                    Require(true, "Player wins only after walking out through the exit trigger");
                    Shot("Victory.png"); Next(17); break;
                case 17:
                    if (Age < 0.8) break;
                    foreach (string name in new[] {"Menu.png", "StalkerCaught.png", "Defeat.png", "Victory.png"})
                        Require(File.Exists(Path.Combine(output, name)) && new FileInfo(Path.Combine(output, name)).Length > 1000, "Native frame capture saved: " + name);
                    Finish(true, null); break;
            }
        }
        catch (Exception exception) { Finish(false, exception.ToString()); }
    }

    private static void PositionEnemy(Vector3 feet, int cell)
    {
        Warp(enemy.transform, feet);
        Set(enemy, "centerCell", cell); Set(enemy, "waypointCell", cell); Set(enemy, "repathRemaining", 0f);
    }
    private static Vector3 ExitDirection() => maze.transform.TransformDirection(maze.Data.ExitWall == MazeWall.North ? Vector3.forward
        : maze.Data.ExitWall == MazeWall.South ? Vector3.back : maze.Data.ExitWall == MazeWall.East ? Vector3.right : Vector3.left);
    private static void Warp(Transform target, Vector3 feet)
    {
        CharacterController controller = target.GetComponent<CharacterController>();
        bool enabled = controller != null && controller.enabled;
        if (enabled) controller.enabled = false;
        float offset = controller == null ? 0f : (Mathf.Max(controller.height * 0.5f, controller.radius) - controller.center.y) * target.lossyScale.y;
        target.position = feet + Vector3.up * offset;
        if (enabled) controller.enabled = true;
        Physics.SyncTransforms();
    }
    private static Vector3 Feet()
    {
        CharacterController controller = player.GetComponent<CharacterController>();
        return controller == null ? player.position : player.TransformPoint(controller.center - Vector3.up * controller.height * 0.5f);
    }
    private static Vector3 Center(int cell) => maze.transform.TransformPoint(new Vector3(
        (cell % maze.Data.Width - (maze.Data.Width - 1) * 0.5f) * maze.CellSize, 0f,
        (cell / maze.Data.Width - (maze.Data.Height - 1) * 0.5f) * maze.CellSize));
    private static IEnumerable<int> Neighbors(int cell)
    {
        int x = cell % maze.Data.Width, y = cell / maze.Data.Width;
        int[] dx = {0,1,0,-1}, dy = {1,0,-1,0};
        MazeWall[] walls = {MazeWall.North,MazeWall.East,MazeWall.South,MazeWall.West};
        for (int i=0; i<4; i++)
            if (!maze.Data[x,y].HasWall(walls[i]) && maze.Data.IsWalkable(x+dx[i],y+dy[i]))
                yield return (y+dy[i])*maze.Data.Width+x+dx[i];
    }
    private static List<int> ExitRoute()
    {
        int count=maze.Data.Width*maze.Data.Height, exit=maze.Data.ExitY*maze.Data.Width+maze.Data.ExitX;
        int[] parent=Enumerable.Repeat(-1,count).ToArray();
        var queue=new Queue<int>(); queue.Enqueue(0); parent[0]=0;
        while(queue.Count>0)
        {
            int cell=queue.Dequeue();
            foreach(int next in Neighbors(cell)) if(parent[next]<0) { parent[next]=cell; queue.Enqueue(next); }
        }
        Require(parent[exit]>=0,"Exit is reachable");
        var route=new List<int>();
        for(int cell=exit;;cell=parent[cell]) { route.Add(cell); if(cell==0) break; }
        route.Reverse(); return route;
    }
    private static T Get<T>(object obj, string name) where T : class => obj == null ? null : Field(obj,name)?.GetValue(obj) as T;
    private static FieldInfo Field(object obj,string name) => obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
    private static void Set(object obj,string name,object value,bool required=true)
    {
        FieldInfo field=obj==null?null:Field(obj,name);
        if(field==null) { if(required) throw new MissingFieldException(name); return; }
        field.SetValue(obj,value);
    }
    private static void Require(bool pass,string label,object evidence=null)
    {
        checks.Add(new { check=label,pass,evidence,phase,elapsed=EditorApplication.timeSinceStartup-started });
        if(!pass) throw new InvalidOperationException(label);
    }
    private static void Deadline(double seconds,string message) { if(Age>seconds) throw new TimeoutException(message); }
    private static void Shot(string filename) => ScreenCapture.CaptureScreenshot(Path.Combine(output,filename));
    private static void WriteReport(bool finished,string error)
    {
        File.WriteAllText(Path.Combine(output,"PlayReport.json"),JsonConvert.SerializeObject(new {
            status=State,finished,phase,elapsed=EditorApplication.timeSinceStartup-started,error,checks,
            session=session==null?"destroyed":session.State.ToString(),
            seed=maze==null?0:maze.GeneratedSeed,
            screenshots=new[]{"Menu.png","StalkerCaught.png","Defeat.png","Victory.png"}
        },Formatting.Indented));
    }
    private static void CleanupInput()
    {
        try
        {
            if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        }
        finally
        {
            keyboard=null;
            foreach(Keyboard device in keyboards) if(device!=null && device.added) InputSystem.EnableDevice(device);
            keyboards.Clear();
        }
    }
    private static void Finish(bool success,string error)
    {
        EditorApplication.update-=Tick;
        CleanupInput();
        if(session!=null)
        {
            bool playing=session.State==RunState.Playing;
            if(motor!=null) motor.enabled=playing;
            if(look!=null) look.enabled=playing;
        }
        State=success?"passed":"failed";
        WriteReport(true,error);
        Debug.Log("Stalker smoke "+State+"; report: "+Path.Combine(output,"PlayReport.json")+(error==null?"":"; "+error));
    }
}
