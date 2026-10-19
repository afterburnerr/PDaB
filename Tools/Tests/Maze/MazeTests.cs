using System;
using System.Collections.Generic;
using FPLab.Gameplay.Maze;

internal static class MazeTests
{
    private static readonly int[] Dx = { 0, 1, 0, -1 };
    private static readonly int[] Dy = { 1, 0, -1, 0 };
    private static readonly MazeWall[] Walls = { MazeWall.North, MazeWall.East, MazeWall.South, MazeWall.West };
    private static int checks;
    private static int mazes;

    private static void Main()
    {
        int[,] sizes = { { 1, 1 }, { 1, 19 }, { 23, 1 }, { 2, 2 }, { 3, 8 }, { 8, 3 }, { 15, 15 }, { 40, 30 }, { 100, 100 } };
        for (int size = 0; size < sizes.GetLength(0); size++)
        {
            for (int seed = -16; seed < 16; seed++)
                CheckMaze(sizes[size, 0], sizes[size, 1], seed);
        }
        for (int seed = -16; seed < 16; seed++)
            CheckMaze(33, 33, seed, 150);
        CheckMaze(1, MazeGenerator.MaxCellCount, int.MinValue);
        CheckMaze(MazeGenerator.MaxCellCount, 1, int.MaxValue);

        ExpectOutOfRange(() => MazeGenerator.Generate(33, 33, 0, -1));
        ExpectOutOfRange(() => MazeGenerator.Generate(0, 1, 0));
        ExpectOutOfRange(() => MazeGenerator.Generate(1, 0, 0));
        ExpectOutOfRange(() => MazeGenerator.Generate(-1, 1, 0));
        ExpectOutOfRange(() => MazeGenerator.Generate(1, -1, 0));
        ExpectOutOfRange(() => MazeGenerator.Generate(int.MaxValue, int.MaxValue, 0));
        ExpectOutOfRange(() => MazeGenerator.Generate(MazeGenerator.MaxCellCount + 1, 1, 0));

        MazeData sample = MazeGenerator.Generate(10, 10, 0);
        ExpectOutOfRange(() => { MazeCell unused = sample[-1, 0]; });
        ExpectOutOfRange(() => { MazeCell unused = sample[0, -1]; });
        ExpectOutOfRange(() => { MazeCell unused = sample[10, 0]; });
        ExpectOutOfRange(() => { MazeCell unused = sample[0, 10]; });

        MazeData otherSeed = MazeGenerator.Generate(10, 10, 1);
        bool differs = false;
        for (int y = 0; y < 10; y++)
            for (int x = 0; x < 10; x++)
                differs |= sample[x, y].Walls != otherSeed[x, y].Walls;
        Assert(differs, "Different seeds should produce different layouts.");

        Console.WriteLine($"PASS: {mazes} generated mazes; {checks} assertions. Walkable connectivity, compact tree passages, solid cells, reciprocal walls, closed boundaries, deterministic metadata, route minimum and farthest boundary exit verified.");
    }

    private static void CheckMaze(int width, int height, int seed, int minimumExitDistance = 0)
    {
        MazeData maze = MazeGenerator.Generate(width, height, seed, minimumExitDistance);
        MazeData repeat = MazeGenerator.Generate(width, height, seed, minimumExitDistance);
        mazes++;
        Assert(maze.Width == width && maze.Height == height, "Dimensions changed.");
        Assert(maze.StartX == 0 && maze.StartY == 0, "Unexpected start.");
        Assert(maze.IsWalkable(0, 0), "Start is solid.");
        Assert(!maze.IsWalkable(-1, 0) && !maze.IsWalkable(width, 0)
            && !maze.IsWalkable(0, -1) && !maze.IsWalkable(0, height), "Outside grid is walkable.");
        Assert(maze.GetCellKind(-1, 0) == MazeCellKind.Solid && maze.GetSectionId(-1, 0) == -1,
            "Outside queries should report solid/unassigned.");
        int passages = 0;
        int walkable = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Assert(maze[x, y].Walls == repeat[x, y].Walls
                    && maze.GetCellKind(x, y) == repeat.GetCellKind(x, y)
                    && maze.GetSectionId(x, y) == repeat.GetSectionId(x, y), "Seed metadata is not deterministic.");
                if (maze.IsWalkable(x, y))
                    walkable++;
                else
                    Assert(maze[x, y].Walls == (MazeWall.North | MazeWall.East | MazeWall.South | MazeWall.West),
                        "Solid cell has an open wall.");
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = x + Dx[direction];
                    int ny = y + Dy[direction];
                    bool wall = maze[x, y].HasWall(Walls[direction]);
                    if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                        Assert(wall, "Exterior boundary has an open wall.");
                    else
                    {
                        Assert(wall == maze[nx, ny].HasWall(Walls[(direction + 2) % 4]), "Walls are not reciprocal.");
                        if (!wall)
                        {
                            Assert(maze.IsWalkable(x, y) && maze.IsWalkable(nx, ny), "Passage enters a solid cell.");
                            if (direction == 0 || direction == 1)
                                passages++;
                        }
                    }
                }
            }
        }
        Assert(walkable == maze.WalkableCellCount && walkable == repeat.WalkableCellCount,
            "Walkable cell count is wrong or nondeterministic.");
        if (width < 9 || height < 9)
            Assert(passages == width * height - 1 && walkable == width * height,
                "Compact fallback must be a connected all-cell tree.");
        else
            Assert(walkable < width * height && passages >= walkable - 1,
                "Section layout should leave solid apartment volumes and connect its walkable cells.");
        Assert(maze.Rooms.Count == repeat.Rooms.Count, "Room count is not deterministic.");
        for (int i = 0; i < maze.Rooms.Count; i++)
        {
            MazeRoom room = maze.Rooms[i];
            MazeRoom other = repeat.Rooms[i];
            Assert(room.X == other.X && room.Y == other.Y && room.Width == other.Width
                && room.Height == other.Height && room.Kind == other.Kind && room.SectionId == other.SectionId,
                "Room metadata is not deterministic.");
            Assert(room.Width > 0 && room.Height > 0 && room.X >= 0 && room.Y >= 0
                && room.X + room.Width <= width && room.Y + room.Height <= height, "Room is out of bounds.");
            for (int y = room.Y; y < room.Y + room.Height; y++)
                for (int x = room.X; x < room.X + room.Width; x++)
                    Assert(maze.IsWalkable(x, y) && maze.GetCellKind(x, y) == room.Kind
                        && maze.GetSectionId(x, y) == room.SectionId, "Room does not match its cells.");
        }

        var distances = new int[width * height];
        Array.Fill(distances, -1);
        distances[0] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(0);
        int reached = 0;
        int maxBoundaryDistance = 0;
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            reached++;
            int x = current % width;
            int y = current / width;
            if (x == 0 || x == width - 1 || y == 0 || y == height - 1)
                maxBoundaryDistance = Math.Max(maxBoundaryDistance, distances[current]);

            for (int direction = 0; direction < 4; direction++)
            {
                if (maze[x, y].HasWall(Walls[direction]))
                    continue;
                int nx = x + Dx[direction];
                int ny = y + Dy[direction];
                Assert(nx >= 0 && nx < width && ny >= 0 && ny < height, "Passage leaves grid.");
                int next = ny * width + nx;
                if (distances[next] >= 0)
                    continue;
                distances[next] = distances[current] + 1;
                queue.Enqueue(next);
            }
        }
        Assert(reached == maze.WalkableCellCount, "Some walkable cells are unreachable.");
        Assert(maze.ExitDistance >= minimumExitDistance, "Selected route is below the requested regression minimum.");
        Assert(maze.ExitX >= 0 && maze.ExitX < width && maze.ExitY >= 0 && maze.ExitY < height, "Exit is out of bounds.");
        Assert(maze.ExitDistance == distances[maze.ExitY * width + maze.ExitX], "Exit distance is wrong.");
        Assert(maze.ExitDistance == maxBoundaryDistance, "Exit is not a farthest reachable boundary cell.");
        Assert(maze.ExitX == repeat.ExitX && maze.ExitY == repeat.ExitY && maze.ExitWall == repeat.ExitWall, "Exit is not deterministic.");
        bool outward = maze.ExitWall == MazeWall.North && maze.ExitY == height - 1
            || maze.ExitWall == MazeWall.East && maze.ExitX == width - 1
            || maze.ExitWall == MazeWall.South && maze.ExitY == 0
            || maze.ExitWall == MazeWall.West && maze.ExitX == 0;
        Assert(outward, "Exit wall does not face outside.");
        if (width == 1 && height == 1)
            Assert(maze.ExitWall != MazeWall.West, "1x1 entrance and exit overlap.");
    }

    private static void ExpectOutOfRange(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentOutOfRangeException)
        {
            checks++;
            return;
        }
        throw new Exception("Expected ArgumentOutOfRangeException.");
    }

    private static void Assert(bool condition, string message)
    {
        checks++;
        if (!condition)
            throw new Exception(message);
    }
}
