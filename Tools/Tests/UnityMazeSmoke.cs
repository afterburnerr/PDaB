// Run in Play Mode with:
// unity command run_script --file Tools/Tests/UnityMazeSmoke.cs --entry UnityMazeSmoke.Run
using System;
using System.Collections.Generic;
using System.Text;
using FPLab.Gameplay.Maze;
using UnityEditor;
using UnityEngine;

public static class UnityMazeSmoke
{
    private static readonly MazeWall[] Directions = { MazeWall.North, MazeWall.East, MazeWall.South, MazeWall.West };
    private static readonly int[] Dx = { 0, 1, 0, -1 };
    private static readonly int[] Dy = { 1, 0, -1, 0 };

    public static object Run()
    {
        if (!Application.isPlaying)
            throw new InvalidOperationException("Run the scene in Play Mode before this smoke check.");

        MazeBuilder builder = UnityEngine.Object.FindAnyObjectByType<MazeBuilder>();
        Require(builder != null && builder.Data != null, "Maze must be generated.");
        var settings = new SerializedObject(builder);
        Transform player = (Transform)settings.FindProperty("player").objectReferenceValue;
        Require(player != null, "Player reference is assigned.");
        CharacterController controller = player.GetComponent<CharacterController>();
        Require(controller != null && controller.enabled, "Player controller is enabled.");
        Vector3 originalPosition = player.position;
        Quaternion originalRotation = player.rotation;
        bool originalRandom = settings.FindProperty("randomizeSeed").boolValue;
        int originalSeed = settings.FindProperty("seed").intValue;
        int originalGeneratedSeed = builder.GeneratedSeed;
        float size = settings.FindProperty("cellSize").floatValue;
        int checkedEdges = 0;
        int routeSteps = 0;

        try
        {
            SetSeed(settings, false, 12345);
            builder.Generate();
            string fingerprint = Fingerprint(builder.Data);
            for (int repeat = 0; repeat < 3; repeat++)
            {
                builder.Generate();
                Require(Fingerprint(builder.Data) == fingerprint, "Fixed seed reproduces the maze.");
                int roots = 0;
                foreach (Transform child in builder.transform)
                    if (child.name == "Generated Maze" && child.gameObject.activeSelf) roots++;
                Require(roots == 1, "Exactly one generated root is active.");
            }

            MazeData maze = builder.Data;
            int wallCount = 0;
            foreach (BoxCollider box in builder.GetComponentsInChildren<BoxCollider>())
                if (box.name.StartsWith("Wall ", StringComparison.Ordinal)) wallCount++;
            Require(wallCount == CountExpectedWalls(maze), "No missing or duplicate active-boundary wall boxes.");

            Vector3 spawn = player.position;
            float halfSegment = Mathf.Max(0f, controller.height * 0.5f - controller.radius);
            foreach (Collider overlap in Physics.OverlapCapsule(spawn + Vector3.up * halfSegment,
                spawn - Vector3.up * halfSegment, controller.radius, ~0, QueryTriggerInteraction.Ignore))
                Require(!overlap.name.StartsWith("Wall ", StringComparison.Ordinal), "Spawn capsule is clear of walls.");
            Require(Physics.Raycast(spawn, Vector3.down, out RaycastHit floor, controller.height, ~0,
                QueryTriggerInteraction.Ignore) && floor.collider.name == "Floor", "Floor supports the spawn.");

            // Disable only the test capsule so raycasts see maze geometry, not the player.
            controller.enabled = false;
            for (int y = 0; y < maze.Height; y++)
            for (int x = 0; x < maze.Width; x++)
            for (int d = 0; d < 4; d++)
            {
                if (!maze.IsWalkable(x, y)) continue;
                Vector3 from = CellPosition(builder, x, y, size) + Vector3.up;
                Vector3 delta = builder.transform.TransformDirection(new Vector3(Dx[d], 0f, Dy[d])) * size;
                bool hit = Physics.Linecast(from, from + delta, ~0, QueryTriggerInteraction.Ignore);
                bool expected = maze[x, y].HasWall(Directions[d]);
                if (x == maze.ExitX && y == maze.ExitY && Directions[d] == maze.ExitWall) expected = false;
                Require(hit == expected, $"Geometry agrees with cell {x},{y} {Directions[d]}.");
                checkedEdges++;
            }
            controller.enabled = true;

            // A whole-cell sweep and repeated sprint-size steps must not cross a closed wall.
            Teleport(controller, spawn);
            Vector3 west = builder.transform.TransformDirection(Vector3.left);
            CollisionFlags collision = controller.Move(west * size);
            Require((collision & CollisionFlags.Sides) != 0, "CharacterController hits the outer wall.");
            Require(Vector3.Dot(player.position - spawn, west) < size * 0.5f, "Large sweep cannot tunnel through a wall.");
            Teleport(controller, spawn);
            for (int i = 0; i < 20; i++) controller.Move(west * (20f / 60f));
            Require(Vector3.Dot(player.position - spawn, west) < size * 0.5f, "Sprint-size steps cannot tunnel through a wall.");

            Teleport(controller, spawn);
            List<int> route = FindRoute(maze);
            foreach (int index in route)
            {
                Vector3 target = CellPosition(builder, index % maze.Width, index / maze.Width, size);
                target.y = player.position.y;
                CollisionFlags flags = controller.Move(target - player.position);
                Require(Vector3.ProjectOnPlane(player.position - target, Vector3.up).magnitude < 0.06f
                    && Mathf.Abs(player.position.y - target.y) <= controller.skinWidth + 0.03f,
                    $"Controller traverses open route edge {routeSteps}: actual {player.position}, target {target}, flags {flags}.");
                routeSteps++;
            }
            Vector3 landing = builder.ExitPosition;
            landing.y = player.position.y;
            controller.Move(landing - player.position);
            Require(Vector3.Distance(player.position, landing) < 0.06f, "Controller passes the exit onto its landing.");

            SetSeed(settings, true, originalSeed);
            builder.Generate();
            int firstRandom = builder.GeneratedSeed;
            builder.Generate();
            Require(firstRandom != builder.GeneratedSeed, "Random generation chooses a new effective seed.");

            return new { passed = true, checkedEdges, routeSteps, wallCount, regenerationChecks = 6 };
        }
        finally
        {
            controller.enabled = true;
            SetSeed(settings, false, originalGeneratedSeed);
            builder.Generate();
            SetSeed(settings, originalRandom, originalSeed);
            Teleport(controller, originalPosition);
            player.rotation = originalRotation;
        }
    }

    private static int CountExpectedWalls(MazeData maze)
    {
        int count = 0;
        for (int y = 0; y < maze.Height; y++)
        for (int x = 0; x < maze.Width; x++)
        for (int d = 0; d < 4; d++)
        {
            // Match the builder's canonical edge ownership: south/west and
            // the outermost north/east edges, never solid-to-solid boundaries.
            if (d == 0 && y != maze.Height - 1 || d == 1 && x != maze.Width - 1)
                continue;
            if (!maze[x, y].HasWall(Directions[d])) continue;
            if (x == maze.ExitX && y == maze.ExitY && Directions[d] == maze.ExitWall) continue;
            if (maze.IsWalkable(x, y) || maze.IsWalkable(x + Dx[d], y + Dy[d])) count++;
        }
        return count;
    }

    private static Vector3 CellPosition(MazeBuilder builder, int x, int y, float size)
    {
        return builder.transform.TransformPoint(new Vector3(
            (x - (builder.Data.Width - 1) * 0.5f) * size, 0f,
            (y - (builder.Data.Height - 1) * 0.5f) * size));
    }

    private static List<int> FindRoute(MazeData maze)
    {
        var parents = new int[maze.Width * maze.Height];
        for (int i = 0; i < parents.Length; i++) parents[i] = -1;
        parents[0] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(0);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            int x = current % maze.Width, y = current / maze.Width;
            for (int d = 0; d < 4; d++)
            {
                if (maze[x, y].HasWall(Directions[d])) continue;
                int nx = x + Dx[d], ny = y + Dy[d];
                Require(maze.IsWalkable(nx, ny), "Open route edge stays in walkable cells.");
                int next = ny * maze.Width + nx;
                if (parents[next] >= 0) continue;
                parents[next] = current;
                queue.Enqueue(next);
            }
        }
        var route = new List<int>();
        int end = maze.ExitY * maze.Width + maze.ExitX;
        Require(parents[end] >= 0, "Exit is reachable.");
        for (int current = end; current != 0; current = parents[current]) route.Add(current);
        route.Reverse();
        return route;
    }

    private static void SetSeed(SerializedObject settings, bool random, int seed)
    {
        settings.Update();
        settings.FindProperty("randomizeSeed").boolValue = random;
        settings.FindProperty("seed").intValue = seed;
        settings.ApplyModifiedPropertiesWithoutUndo();
    }

    private static string Fingerprint(MazeData maze)
    {
        var value = new StringBuilder();
        for (int y = 0; y < maze.Height; y++)
        for (int x = 0; x < maze.Width; x++)
            value.Append((int)maze[x, y].Walls).Append(':').Append((int)maze.GetCellKind(x, y))
                .Append(':').Append(maze.GetSectionId(x, y)).Append(';');
        foreach (MazeRoom room in maze.Rooms)
            value.Append('|').Append(room.X).Append(',').Append(room.Y).Append(',').Append(room.Width)
                .Append(',').Append(room.Height).Append(',').Append((int)room.Kind).Append(',').Append(room.SectionId);
        return value.ToString();
    }

    private static void Teleport(CharacterController controller, Vector3 position)
    {
        controller.enabled = false;
        controller.transform.position = position;
        controller.enabled = true;
        Physics.SyncTransforms();
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
