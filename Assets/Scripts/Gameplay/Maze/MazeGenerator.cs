using System;
using System.Collections.Generic;

namespace FPLab.Gameplay.Maze
{
    public static class MazeGenerator
    {
        public const int MaxCellCount = 65536;

        private static readonly int[] OffsetX = { 0, 1, 0, -1 };
        private static readonly int[] OffsetY = { 1, 0, -1, 0 };
        private static readonly MazeWall[] Walls =
        {
            MazeWall.North, MazeWall.East, MazeWall.South, MazeWall.West
        };

        public static MazeData Generate(int width, int height, int seed, int minimumExitDistance = 0)
        {
            if (width < 1)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
            if (height < 1)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");
            if ((long)width * height > MaxCellCount)
                throw new ArgumentOutOfRangeException(nameof(width), $"Maze cannot exceed {MaxCellCount} cells.");
            if (minimumExitDistance < 0)
                throw new ArgumentOutOfRangeException(nameof(minimumExitDistance));

            if (width < 9 || height < 9)
                return GenerateCompact(width, height, new Random(seed));

            // Bounded selection keeps the route near its intended walking time.
            // Sparse corridors leave substantial solid apartment-sized volumes.
            int attempts = width * height > 32768 ? 2 : width * height > 8192 ? 4 : 16;
            int desiredDistance = minimumExitDistance > 0
                ? (int)Math.Min(int.MaxValue, Math.Ceiling(minimumExitDistance * 1.08))
                : Math.Max(1, (int)Math.Ceiling(width * height * 0.15));
            var random = new Random(seed);
            MazeData best = null;
            int bestSeed = 0;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int candidateSeed = random.Next();
                MazeData candidate = GenerateSections(width, height, candidateSeed, false, minimumExitDistance);
                if (best == null
                    || candidate.ExitDistance >= desiredDistance && (best.ExitDistance < desiredDistance
                        || candidate.ExitDistance < best.ExitDistance)
                    || candidate.ExitDistance < desiredDistance && best.ExitDistance < desiredDistance
                        && candidate.ExitDistance > best.ExitDistance)
                {
                    best = candidate;
                    bestSeed = candidateSeed;
                }
            }

            // Add a very small number of loops only after selecting the main
            // route. A loop is rejected if it materially shortens that route.
            return GenerateSections(width, height, bestSeed, true, minimumExitDistance);
        }

        private static int[] CreateAnchors(int length)
        {
            int segments = Math.Max(1, (length - 1) / 5);
            var anchors = new int[segments + 1];
            for (int i = 0; i <= segments; i++)
                anchors[i] = i * (length - 1) / segments;
            return anchors;
        }

        private static MazeData GenerateSections(int width, int height, int seed, bool addLoops, int minimumExitDistance)
        {
            var maze = new MazeData(width, height);
            var random = new Random(seed);
            int[] anchorsX = CreateAnchors(width);
            int[] anchorsY = CreateAnchors(height);
            int columns = anchorsX.Length;
            int rows = anchorsY.Length;
            int sectionCount = columns * rows;
            var links = new bool[sectionCount * 4];
            var landingOrder = new int[sectionCount - 1];
            var landings = new bool[sectionCount];
            landings[0] = true;
            for (int i = 0; i < landingOrder.Length; i++)
                landingOrder[i] = i + 1;
            for (int i = landingOrder.Length - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                int swap = landingOrder[i];
                landingOrder[i] = landingOrder[other];
                landingOrder[other] = swap;
            }
            int landingCount = Math.Max(1, (sectionCount * 26 + 50) / 100);
            for (int i = 0; i < landingCount - 1; i++)
                landings[landingOrder[i]] = true;

            for (int section = 0; section < sectionCount; section++)
            {
                int x = anchorsX[section % columns];
                int y = anchorsY[section / columns];
                bool landing = landings[section];
                int size = landing ? 2 : 1;
                int left = size == 1 || x == 0 ? x : x == width - 1 || random.Next(2) == 0 ? x - 1 : x;
                int bottom = size == 1 || y == 0 ? y : y == height - 1 || random.Next(2) == 0 ? y - 1 : y;
                MazeCellKind kind = landing ? MazeCellKind.Landing : MazeCellKind.Vestibule;
                AddRoom(maze, left, bottom, size, size, kind, section);
            }

            var visited = new bool[sectionCount];
            var stack = new Stack<int>();
            var directions = new int[4];
            visited[0] = true;
            stack.Push(0);
            while (stack.Count > 0)
            {
                int section = stack.Peek();
                int sx = section % columns;
                int sy = section / columns;
                int count = 0;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = sx + OffsetX[direction];
                    int ny = sy + OffsetY[direction];
                    if (nx >= 0 && nx < columns && ny >= 0 && ny < rows && !visited[ny * columns + nx])
                        directions[count++] = direction;
                }
                if (count == 0)
                {
                    stack.Pop();
                    continue;
                }

                int chosen = directions[random.Next(count)];
                int next = (sy + OffsetY[chosen]) * columns + sx + OffsetX[chosen];
                List<int> path = CreateCorridor(maze, anchorsX[sx], anchorsY[sy],
                    anchorsX[next % columns], anchorsY[next / columns], random);
                CarvePath(maze, path, section);
                links[section * 4 + chosen] = true;
                links[next * 4 + (chosen + 2) % 4] = true;
                visited[next] = true;
                stack.Push(next);
            }

            AddServiceAlcoves(maze, random, sectionCount / 3);
            ChooseExit(maze);
            if (addLoops)
                AddGuardedLoops(maze, random, anchorsX, anchorsY, links, minimumExitDistance);
            return maze;
        }

        private static void AddRoom(MazeData maze, int x, int y, int width, int height, MazeCellKind kind, int section)
        {
            for (int dy = 0; dy < height; dy++)
            {
                for (int dx = 0; dx < width; dx++)
                {
                    int index = (y + dy) * maze.Width + x + dx;
                    maze.SetWalkable(index, kind, section);
                    if (dx > 0)
                        Join(maze, index - 1, index);
                    if (dy > 0)
                        Join(maze, index - maze.Width, index);
                }
            }
            maze.AddRoom(new MazeRoom(x, y, width, height, kind, section));
        }

        private static List<int> CreateCorridor(MazeData maze, int x, int y, int endX, int endY, Random random)
        {
            var path = new List<int>(12) { y * maze.Width + x };
            int dx = Math.Sign(endX - x);
            int dy = Math.Sign(endY - y);
            int length = Math.Abs(endX - x) + Math.Abs(endY - y);
            int side = random.Next(100) < 32 && length >= 5 ? (random.Next(2) == 0 ? -1 : 1) : 0;
            // Keep the offset inside its section band, away from neighbouring
            // landings. Most links remain long, believable straight corridors.
            if (dx != 0 && (y + side < 0 || y + side >= maze.Height) ||
                dy != 0 && (x + side < 0 || x + side >= maze.Width))
                side = -side;
            for (int step = 1; step <= length; step++)
            {
                if (side != 0 && step == 3)
                {
                    x += dy != 0 ? side : 0;
                    y += dx != 0 ? side : 0;
                    path.Add(y * maze.Width + x);
                }
                if (side != 0 && step == length - 1)
                {
                    x -= dy != 0 ? side : 0;
                    y -= dx != 0 ? side : 0;
                    path.Add(y * maze.Width + x);
                }
                x += dx;
                y += dy;
                path.Add(y * maze.Width + x);
            }
            return path;
        }

        private static void CarvePath(MazeData maze, List<int> path, int section)
        {
            for (int i = 0; i < path.Count; i++)
            {
                maze.SetWalkable(path[i], MazeCellKind.Corridor, section);
                if (i > 0)
                    Join(maze, path[i - 1], path[i]);
            }
        }

        private static void Join(MazeData maze, int from, int to)
        {
            int differenceX = to % maze.Width - from % maze.Width;
            int differenceY = to / maze.Width - from / maze.Width;
            int direction = differenceX > 0 ? 1 : differenceX < 0 ? 3 : differenceY > 0 ? 0 : 2;
            maze.RemoveWall(from, Walls[direction]);
            maze.RemoveWall(to, Walls[(direction + 2) % 4]);
        }

        private static void AddServiceAlcoves(MazeData maze, Random random, int count)
        {
            int tries = Math.Min(maze.Width * maze.Height, count * 24);
            for (int attempt = 0; attempt < tries && count > 0; attempt++)
            {
                int x = random.Next(maze.Width);
                int y = random.Next(maze.Height);
                if (maze.GetCellKind(x, y) != MazeCellKind.Corridor)
                    continue;
                int direction = random.Next(4);
                int nx = x + OffsetX[direction];
                int ny = y + OffsetY[direction];
                if (nx <= 0 || nx >= maze.Width - 1 || ny <= 0 || ny >= maze.Height - 1 || maze.IsWalkable(nx, ny))
                    continue;
                int neighbours = 0;
                for (int side = 0; side < 4; side++)
                    if (maze.IsWalkable(nx + OffsetX[side], ny + OffsetY[side]))
                        neighbours++;
                if (neighbours != 1)
                    continue;
                int section = maze.GetSectionId(x, y);
                AddRoom(maze, nx, ny, 1, 1, MazeCellKind.ServiceAlcove, section);
                Join(maze, y * maze.Width + x, ny * maze.Width + nx);
                count--;
            }
        }

        private static void AddGuardedLoops(MazeData maze, Random random, int[] anchorsX, int[] anchorsY,
            bool[] links, int minimumExitDistance)
        {
            int columns = anchorsX.Length;
            int rows = anchorsY.Length;
            int baseline = maze.ExitDistance;
            int required = Math.Max(minimumExitDistance, (int)Math.Ceiling(baseline * 0.92));
            if (baseline < required)
                return;
            int remaining = Math.Min(3, columns * rows / 20);
            for (int attempt = 0; attempt < 24 && remaining > 0; attempt++)
            {
                int section = random.Next(columns * rows);
                int direction = random.Next(2); // Each undirected edge once: north/east.
                int sx = section % columns;
                int sy = section / columns;
                int nx = sx + OffsetX[direction];
                int ny = sy + OffsetY[direction];
                if (nx >= columns || ny >= rows || links[section * 4 + direction])
                    continue;
                List<int> path = CreateCorridor(maze, anchorsX[sx], anchorsY[sy], anchorsX[nx], anchorsY[ny], random);
                var before = new MazeCell[path.Count];
                for (int i = 0; i < path.Count; i++)
                    before[i] = maze[path[i] % maze.Width, path[i] / maze.Width];
                CarvePath(maze, path, section);
                ChooseExit(maze);
                if (maze.ExitDistance < required)
                {
                    for (int i = 0; i < path.Count; i++)
                        maze.SetCell(path[i], before[i]);
                    ChooseExit(maze);
                    continue;
                }
                links[section * 4 + direction] = true;
                links[(ny * columns + nx) * 4 + (direction + 2) % 4] = true;
                remaining--;
            }
        }

        private static MazeData GenerateCompact(int width, int height, Random random)
        {
            var maze = new MazeData(width, height);
            var visited = new bool[width * height];
            var stack = new Stack<int>();
            var candidates = new int[4];
            maze.SetWalkable(0, MazeCellKind.Corridor, 0);
            visited[0] = true;
            stack.Push(0);
            while (stack.Count > 0)
            {
                int index = stack.Peek();
                int x = index % width;
                int y = index / width;
                int count = 0;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = x + OffsetX[direction];
                    int ny = y + OffsetY[direction];
                    if (nx >= 0 && nx < width && ny >= 0 && ny < height && !visited[ny * width + nx])
                        candidates[count++] = direction;
                }
                if (count == 0)
                {
                    stack.Pop();
                    continue;
                }
                int chosen = candidates[random.Next(count)];
                int nextIndex = (y + OffsetY[chosen]) * width + x + OffsetX[chosen];
                maze.SetWalkable(nextIndex, MazeCellKind.Corridor, nextIndex);
                Join(maze, index, nextIndex);
                visited[nextIndex] = true;
                stack.Push(nextIndex);
            }
            ChooseExit(maze);
            return maze;
        }

        private static void ChooseExit(MazeData maze)
        {
            var distances = new int[maze.Width * maze.Height];
            for (int i = 0; i < distances.Length; i++)
                distances[i] = -1;
            var queue = new Queue<int>();
            distances[0] = 0;
            queue.Enqueue(0);
            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int x = index % maze.Width;
                int y = index / maze.Width;
                for (int direction = 0; direction < 4; direction++)
                {
                    int nx = x + OffsetX[direction];
                    int ny = y + OffsetY[direction];
                    if (maze[x, y].HasWall(Walls[direction]) || !maze.IsWalkable(nx, ny))
                        continue;
                    int nextIndex = ny * maze.Width + nx;
                    if (distances[nextIndex] >= 0)
                        continue;
                    distances[nextIndex] = distances[index] + 1;
                    queue.Enqueue(nextIndex);
                }
            }

            int farthest = -1;
            for (int y = 0; y < maze.Height; y++)
            {
                for (int x = 0; x < maze.Width; x++)
                {
                    if (x != 0 && y != 0 && x != maze.Width - 1 && y != maze.Height - 1)
                        continue;
                    int distance = distances[y * maze.Width + x];
                    if (distance <= farthest)
                        continue;
                    MazeWall exitWall = y == maze.Height - 1 ? MazeWall.North
                        : x == maze.Width - 1 ? MazeWall.East
                        : y == 0 ? MazeWall.South : MazeWall.West;
                    maze.SetExit(x, y, exitWall, distance);
                    farthest = distance;
                }
            }
        }
    }
}
