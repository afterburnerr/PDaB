# Maze checks

Run the pure C# generator checks from the project root:

```sh
./Tools/Tests/run-maze-tests.sh
```

The runner uses the installed .NET SDK (including Unity's bundled SDK), compiles
the actual maze model sources, and needs no external test packages. It covers
connectivity, reciprocal walls, perfect-maze passage counts, deterministic seeds,
boundary exit selection, invalid input, and long narrow grids.

For scene geometry and real CharacterController collision checks, open
`Assets/Scenes/SampleScene.unity`, enter Play Mode, start a run and wait for
the loading screen to close, then run:

```sh
unity command run_script --file Tools/Tests/UnityMazeSmoke.cs --entry UnityMazeSmoke.Run
```

The Play Mode check verifies wall geometry against the data, spawn clearance,
wall collisions at sprint-size steps, traversal of the complete route and exit,
fixed/random seeds, and repeated regeneration. It temporarily regenerates the
runtime maze and moves the player, then restores their initial layout/position.
Stop Play Mode to return to the saved editor scene.

## Escape flow and menus

The scene starts at the main menu. In a fresh Play session run:

```sh
unity command run_script --file Tools/Tests/UnityEscapeSmoke.cs --entry UnityEscapeSmoke.Run
unity command run_script --file Tools/Tests/UnityEscapeSmoke.cs --entry UnityEscapeSmoke.Status
```

Poll Status until it reports passed (at least 4.2 seconds). The check exercises
deferred generation, the minimum three-second loading screen, the real clock
across frames, pause/resume, rejection of unrelated colliders,
the actual player's physical exit, one victory and a frozen final time.
It leaves the game on the victory screen.

In another fresh Play session, test menu buttons and layout:

```sh
unity command run_script --file Tools/Tests/UnityMenuSmoke.cs --entry UnityMenuSmoke.Run
unity command run_script --file Tools/Tests/UnityMenuSmoke.cs --entry UnityMenuSmoke.AfterLoading
unity command run_script --file Tools/Tests/UnityMenuSmoke.cs --entry UnityMenuSmoke.PauseButtons
unity command run_script --file Tools/Tests/UnityMenuSmoke.cs --entry UnityMenuSmoke.Layout
```

Run AfterLoading after the loading screen closes (at least three seconds).
Run PauseButtons on the next rendered frame or later, because newly enabled
UI graphics acquire their raycast depths during canvas rendering. These checks
verify the real topmost button hit targets, start/continue handlers and complete
menu rectangles. Supported target resolutions are **1920x1080** and **1920x1200**;
select each in Unity's Game view and run Layout on an open menu.

Restart resets the scene, camera and stamina, generates a random maze during
the loading screen, then starts a new run. Main menu reloads into Ready.
Loading time is excluded from the run timer. The quit button ends Play Mode
in the editor and quits the application in a player build.

`Tools/EditorSetup/SetupEntrance.cs` reconstructs the menu and assigns the
scene references in Edit Mode. It preserves the existing player scripts.
`Tools/EditorSetup/SetupLoadingScreen.cs` creates the loading prefab and wires
it into the existing menu without rebuilding it.
`Tools/EditorSetup/SetupSourceHud.cs` installs the HL2-style segmented AUX POWER
bar and timer; fonts and original layout values are recorded in
`Tools/Art/HalfLife2-hud.json`.
`Tools/EditorSetup/SetupFlashlight.cs` wires the F action, shared AUX consumption,
HL2 toggle sound and original light cookie. Run it before SetupSourceHud when
installing both. PlayerAuxPower preserves the old stamina component GUID and
serialized resource settings. Flashlight mapping and source references are in
`Tools/Art/HalfLife2-flashlight-settings.json`.
Generated artwork and prompts are documented in `Tools/Art/EntranceAssets.md`.
