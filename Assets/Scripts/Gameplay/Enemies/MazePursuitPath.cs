using FPLab.Gameplay.Maze;

namespace FPLab.Gameplay.Enemies
{
    // Breadth-first distance field points towards the player without cutting
    // corners or depending on a NavMesh rebuilt during procedural generation.
    internal sealed class MazePursuitPath
    {
        private static readonly int[] dx = { 0, 1, 0, -1 };
        private static readonly int[] dy = { 1, 0, -1, 0 };
        private static readonly MazeWall[] walls = { MazeWall.North, MazeWall.East, MazeWall.South, MazeWall.West };
        private int[] distance;
        private int[] queue;
        private MazeData data;

        public void Rebuild(MazeData layout, int target)
        {
            data = layout;
            int count = data.Width * data.Height;
            if (distance == null || distance.Length != count)
            {
                distance = new int[count];
                queue = new int[count];
            }
            for (int i = 0; i < count; i++) distance[i] = -1;
            if (target < 0 || target >= count || !data.IsWalkable(target % data.Width, target / data.Width)) return;
            int read = 0, write = 0;
            queue[write++] = target;
            distance[target] = 0;
            while (read < write)
            {
                int current = queue[read++];
                for (int direction = 0; direction < 4; direction++)
                {
                    int next = Neighbor(current, direction);
                    if (next < 0 || distance[next] >= 0) continue;
                    distance[next] = distance[current] + 1;
                    queue[write++] = next;
                }
            }
        }

        public int Distance(int cell) => distance == null || cell < 0 || cell >= distance.Length ? -1 : distance[cell];

        public int Neighbor(int cell, int direction)
        {
            if (data == null || cell < 0 || cell >= data.Width * data.Height) return -1;
            int x = cell % data.Width, y = cell / data.Width;
            int nx = x + dx[direction], ny = y + dy[direction];
            if (!data.IsWalkable(nx, ny) || data[x, y].HasWall(walls[direction])
                || data[nx, ny].HasWall(walls[(direction + 2) % 4])) return -1;
            return ny * data.Width + nx;
        }

        public int NextTowardsTarget(int cell)
        {
            int best = cell, bestDistance = Distance(cell);
            if (bestDistance <= 0) return best;
            for (int direction = 0; direction < 4; direction++)
            {
                int candidate = Neighbor(cell, direction), length = Distance(candidate);
                if (length >= 0 && length < bestDistance)
                {
                    best = candidate;
                    bestDistance = length;
                }
            }
            return best;
        }
    }
}
