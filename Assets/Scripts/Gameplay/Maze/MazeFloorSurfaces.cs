using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPLab.Gameplay.Maze
{
    // Floor changes follow connected passages, so a material belongs to a
    // section of the building rather than an arbitrary collection of squares.
    internal static class MazeFloorSurfaces
    {
        private const int ChunkSize = 4;
        private const int MinimumRegionSize = 6;
        private static readonly MazeWall[] Walls =
            { MazeWall.North, MazeWall.East, MazeWall.South, MazeWall.West };
        private static readonly int[] StepX = { 0, 1, 0, -1 };
        private static readonly int[] StepY = { 1, 0, -1, 0 };

        internal static bool Build(Transform root, MazeData data, int seed, float cellSize,
            float wallThickness, Material concrete, Material boards, Material tiles,
            List<Mesh> ownedMeshes)
        {
            if (root == null || data == null || concrete == null || boards == null
                || tiles == null || ownedMeshes == null)
                return false;

            int[] surfaces = SelectSurfaces(data, seed);
            Material[] materials = { concrete, boards, tiles };
            string[] names = { "Concrete", "Boards", "Tiles" };
            for (int y = 0; y < data.Height; y += ChunkSize)
            {
                for (int x = 0; x < data.Width; x += ChunkSize)
                {
                    for (int surface = 0; surface < materials.Length; surface++)
                    {
                        BuildChunk(root, data, surfaces, x, y, surface, cellSize,
                            wallThickness, materials[surface], names[surface], ownedMeshes);
                    }
                }
            }
            return true;
        }

        private static int[] SelectSurfaces(MazeData data, int seed)
        {
            int count = data.Width * data.Height;
            var surfaces = new int[count];
            int target = Mathf.RoundToInt(data.WalkableCellCount * 0.18f);
            // Small maps keep their concrete floor instead of forcing a tiny
            // decorative tile or board patch into the entrance.
            if (target < MinimumRegionSize)
                return surfaces;

            var random = new System.Random(unchecked(seed ^ 0x46534C52));
            var candidates = new List<int>(data.WalkableCellCount);
            for (int y = 0; y < data.Height; y++)
                for (int x = 0; x < data.Width; x++)
                    if (data.IsWalkable(x, y)) candidates.Add(y * data.Width + x);
            for (int i = candidates.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                int value = candidates[i];
                candidates[i] = candidates[other];
                candidates[other] = value;
            }

            var seeds = new List<Vector2Int>();
            var totals = new int[3];
            // Prefer separated sections. The second pass relaxes only seed
            // spacing if narrow branches prevented enough full-size regions.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (int candidate in candidates)
                {
                    if (totals[1] >= target && totals[2] >= target) break;
                    if (surfaces[candidate] != 0 || IsEntrance(data, candidate)) continue;
                    int x = candidate % data.Width;
                    int y = candidate / data.Width;
                    if (pass == 0 && NearSeed(seeds, x, y)) continue;

                    int surface = totals[1] <= totals[2] ? 1 : 2;
                    if (totals[surface] >= target) surface = 3 - surface;
                    int remaining = target - totals[surface];
                    if (remaining < MinimumRegionSize) continue;
                    int desired = Mathf.Min(random.Next(9, 15), remaining);
                    List<int> region = GrowRegion(data, surfaces, candidate, desired, random);
                    if (region.Count < MinimumRegionSize) continue;
                    foreach (int cell in region) surfaces[cell] = surface;
                    totals[surface] += region.Count;
                    seeds.Add(new Vector2Int(x, y));
                }
            }
            // A landing is one architectural space, not four independent tiles.
            foreach (MazeRoom room in data.Rooms)
            {
                if (room.Kind != MazeCellKind.Landing) continue;
                var votes = new int[3];
                for (int y = room.Y; y < room.Y + room.Height; y++)
                    for (int x = room.X; x < room.X + room.Width; x++)
                        votes[surfaces[y * data.Width + x]]++;
                int selected = votes[2] > votes[1] ? 2 : 1;
                if (votes[0] >= votes[selected]) selected = 0;
                if (room.X <= data.StartX && data.StartX < room.X + room.Width
                        && room.Y <= data.StartY && data.StartY < room.Y + room.Height
                    || room.X <= data.ExitX && data.ExitX < room.X + room.Width
                        && room.Y <= data.ExitY && data.ExitY < room.Y + room.Height)
                    selected = 0;
                for (int y = room.Y; y < room.Y + room.Height; y++)
                    for (int x = room.X; x < room.X + room.Width; x++)
                        surfaces[y * data.Width + x] = selected;
            }
            return surfaces;
        }

        private static bool NearSeed(List<Vector2Int> seeds, int x, int y)
        {
            foreach (Vector2Int seed in seeds)
            {
                if (Mathf.Abs(seed.x - x) + Mathf.Abs(seed.y - y) < 4)
                    return true;
            }
            return false;
        }

        private static bool IsEntrance(MazeData data, int cell)
        {
            int x = cell % data.Width;
            int y = cell / data.Width;
            return (x == data.StartX && y == data.StartY)
                || (x == data.ExitX && y == data.ExitY);
        }

        private static List<int> GrowRegion(MazeData data, int[] surfaces, int seed,
            int desired, System.Random random)
        {
            var region = new List<int>(desired) { seed };
            for (int cursor = 0; cursor < region.Count && region.Count < desired; cursor++)
            {
                int cell = region[cursor];
                int x = cell % data.Width;
                int y = cell / data.Width;
                int firstDirection = random.Next(4);
                for (int step = 0; step < 4 && region.Count < desired; step++)
                {
                    int direction = (firstDirection + step) % 4;
                    if (data[x, y].HasWall(Walls[direction])) continue;
                    int nextX = x + StepX[direction];
                    int nextY = y + StepY[direction];
                    if (!data.IsWalkable(nextX, nextY)) continue;
                    int next = nextY * data.Width + nextX;
                    if (surfaces[next] != 0 || IsEntrance(data, next) || region.Contains(next))
                        continue;
                    region.Add(next);
                }
            }
            return region;
        }

        private static void BuildChunk(Transform root, MazeData data, int[] surfaces,
            int chunkX, int chunkY, int surface, float cellSize, float thickness,
            Material material, string label, List<Mesh> ownedMeshes)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            for (int y = chunkY; y < Mathf.Min(chunkY + ChunkSize, data.Height); y++)
            {
                for (int x = chunkX; x < Mathf.Min(chunkX + ChunkSize, data.Width); x++)
                {
                    if (!data.IsWalkable(x, y) || surfaces[y * data.Width + x] != surface) continue;
                    float left = (x - data.Width * 0.5f) * cellSize;
                    float bottom = (y - data.Height * 0.5f) * cellSize;
                    float right = left + cellSize;
                    float top = bottom + cellSize;
                    if (x == 0) left -= thickness * 0.5f;
                    if (y == 0) bottom -= thickness * 0.5f;
                    if (x == data.Width - 1) right += thickness * 0.5f;
                    if (y == data.Height - 1) top += thickness * 0.5f;
                    int start = vertices.Count;
                    vertices.Add(new Vector3(left, 0f, bottom));
                    vertices.Add(new Vector3(left, 0f, top));
                    vertices.Add(new Vector3(right, 0f, top));
                    vertices.Add(new Vector3(right, 0f, bottom));
                    for (int i = start; i < start + 4; i++)
                    {
                        Vector3 position = vertices[i];
                        normals.Add(Vector3.up);
                        tangents.Add(new Vector4(1f, 0f, 0f, -1f));
                        uv.Add(new Vector2(position.x, position.z));
                    }
                    triangles.Add(start);
                    triangles.Add(start + 1);
                    triangles.Add(start + 2);
                    triangles.Add(start);
                    triangles.Add(start + 2);
                    triangles.Add(start + 3);
                }
            }
            if (vertices.Count == 0) return;

            string name = $"Floor Surface {label} {chunkX},{chunkY}";
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            ownedMeshes.Add(mesh);
            var section = new GameObject(name);
            section.transform.SetParent(root, false);
            section.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = section.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }
    }
}
