using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPLab.Gameplay.Maze
{
    [DisallowMultipleComponent, RequireComponent(typeof(MazeBuilder))]
    public sealed class MazeFloorDetails : MonoBehaviour
    {
        [SerializeField] private Material litterMaterial;
        [SerializeField] private Material scuffMaterial;
        [SerializeField] private Material grimeMaterial;
        [SerializeField] private Material bloodMaterial;
        [SerializeField, HideInInspector] private Transform decorationRoot;
        [SerializeField, HideInInspector] private List<Mesh> ownedMeshes = new List<Mesh>();

        private const int AtlasGrid = 3;
        private const int VariantCount = AtlasGrid * AtlasGrid;
        private const int ChunkSize = 4;
        private const int MaximumTouchedCells = 64;
        private static readonly MazeWall[] Sides =
            { MazeWall.North, MazeWall.East, MazeWall.South, MazeWall.West };
        private MazeBuilder builder;

        private void OnEnable()
        {
            builder = GetComponent<MazeBuilder>();
            builder.Generated += Decorate;
        }

        private void Start()
        {
            if (builder.Data != null && decorationRoot == null)
                Decorate();
        }

        private void OnDisable()
        {
            if (builder != null)
                builder.Generated -= Decorate;
        }

        private void OnDestroy() => ReleaseMeshes();

        [ContextMenu("Decorate Maze Floor")]
        public void Decorate()
        {
            if (builder == null)
                builder = GetComponent<MazeBuilder>();
            MazeData data = builder.Data;
            // Data is intentionally transient. Keep the saved preview when the
            // editor reloads scripts, until the next actual maze generation.
            if (data == null || builder.GeneratedRoot == null)
                return;

            if (decorationRoot != null)
            {
                decorationRoot.gameObject.SetActive(false);
                DestroyOwned(decorationRoot.gameObject);
            }
            ReleaseMeshes();
            decorationRoot = new GameObject("Floor Details").transform;
            decorationRoot.SetParent(builder.GeneratedRoot, false);

            var random = new System.Random(unchecked(builder.GeneratedSeed ^ 0x464C4F52));
            var atlases = new List<AtlasBatch>();
            if (litterMaterial != null)
                atlases.Add(new AtlasBatch("Floor Detail Litter", litterMaterial, 6, 1.5f, 3.4f, 0));
            if (scuffMaterial != null)
                atlases.Add(new AtlasBatch("Floor Detail Scuffs", scuffMaterial, 6, 2f, 4.5f, 1));
            if (grimeMaterial != null)
                atlases.Add(new AtlasBatch("Floor Detail Grime", grimeMaterial, 8, 2f, 5f, 2));
            if (bloodMaterial != null)
                atlases.Add(new AtlasBatch("Floor Detail Blood", bloodMaterial, 1, 1.3f, 3.5f, 3));
            if (atlases.Count == 0)
                return;

            var cells = new List<Vector2Int>();
            for (int y = 0; y < data.Height; y++)
                for (int x = 0; x < data.Width; x++)
                    if (CanDecorate(x, y, data)) cells.Add(new Vector2Int(x, y));
            Shuffle(cells, random);

            // Preserve dirt per square metre when the corridor grid changes scale.
            float areaScale = builder.CellSize * builder.CellSize / (2.6f * 2.6f);
            int targetCount = Mathf.RoundToInt(cells.Count * areaScale * Range(random, 1.28f, 1.72f));
            int placed = 0;
            // Multiple shuffled passes cover the whole map before adding more
            // layers. A finite attempt budget handles unusually narrow cells.
            for (int candidate = 0; candidate < targetCount * 3 && placed < targetCount; candidate++)
            {
                if (candidate > 0 && candidate % cells.Count == 0) Shuffle(cells, random);
                Vector2Int cell = cells[candidate % cells.Count];
                AtlasBatch atlas = ChooseAtlas(atlases, random);
                Rect floor = CellRectangle(cell, data);
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    if (floor.width <= 0f || floor.height <= 0f) break;
                    Vector3 position = new Vector3(Range(random, floor.xMin, floor.xMax),
                        0.006f + atlas.Layer * 0.002f, Range(random, floor.yMin, floor.yMax));
                    if (atlas.Layer == 0 && random.NextDouble() < 0.4)
                        BiasToWall(ref position, data[cell.x, cell.y], floor, random);
                    if (atlas.HasNearbyCenter(position)) continue;

                    int variant = atlas.TakeVariant(position, builder.CellSize * 3f, random);
                    // Atlas cells contain complete footprint groups, not single
                    // stains: keep their original proportions and human scale.
                    bool footprints = atlas.Layer == 3 && (variant == 0 || variant == 4 || variant == 8)
                        || atlas.Layer == 1 && variant == 2;
                    float width = footprints ? Range(random, 1.1f, 1.4f)
                        : Range(random, atlas.MinimumSize, atlas.MaximumSize);
                    float depth = footprints ? width : Mathf.Min(5f, width * Range(random, 0.55f, 1.3f));
                    float angle = Range(random, 0f, Mathf.PI * 2f);
                    if (!footprints && atlas.Layer != 0 && random.NextDouble() < 0.18)
                    {
                        width = Range(random, 3.4f, 5f);
                        depth = Range(random, 0.6f, 1.2f);
                        angle = CorridorAngle(data[cell.x, cell.y], random) + Range(random, -0.14f, 0.14f);
                    }
                    Vector3 right = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Vector3 forward = new Vector3(-right.z, 0f, right.x);
                    List<ClipVertex> quad = atlas.CreateQuad(position, right * (width * 0.5f), forward * (depth * 0.5f), variant);
                    ScatterClippedQuad(quad, right, cell, data, atlas);
                    placed++;
                    break;
                }
            }

            foreach (AtlasBatch atlas in atlases)
                foreach (KeyValuePair<Vector2Int, GeometryBatch> chunk in atlas.Chunks)
                    BuildSurface(atlas, chunk.Key, chunk.Value);
        }

        private static bool CanDecorate(int x, int y, MazeData data) =>
            data.IsWalkable(x, y)
            && (x != data.StartX || y != data.StartY) && (x != data.ExitX || y != data.ExitY);

        private Rect CellRectangle(Vector2Int cell, MazeData data)
        {
            float size = builder.CellSize;
            float left = (cell.x - data.Width * 0.5f) * size;
            float bottom = (cell.y - data.Height * 0.5f) * size;
            // End the clipped overlay beneath the existing wall finish/skirting.
            float inset = builder.WallThickness * 0.5f + 0.01f;
            MazeCell walls = data[cell.x, cell.y];
            return Rect.MinMaxRect(
                left + (walls.HasWall(MazeWall.West) ? inset : 0f),
                bottom + (walls.HasWall(MazeWall.South) ? inset : 0f),
                left + size - (walls.HasWall(MazeWall.East) ? inset : 0f),
                bottom + size - (walls.HasWall(MazeWall.North) ? inset : 0f));
        }

        private void ScatterClippedQuad(List<ClipVertex> quad, Vector3 tangent, Vector2Int origin, MazeData data, AtlasBatch atlas)
        {
            var pending = new Queue<Vector2Int>();
            var visited = new HashSet<Vector2Int>();
            pending.Enqueue(origin);
            visited.Add(origin);
            while (pending.Count > 0)
            {
                Vector2Int cell = pending.Dequeue();
                List<ClipVertex> polygon = ClipToRectangle(quad, CellRectangle(cell, data));
                if (polygon.Count < 3) continue;
                atlas.AddPolygon(cell, polygon, tangent);
                foreach (MazeWall side in Sides)
                {
                    if (data[cell.x, cell.y].HasWall(side)) continue;
                    Vector2Int neighbor = cell + CellDirection(side);
                    if (!data.IsWalkable(neighbor.x, neighbor.y) || visited.Contains(neighbor)) continue;
                    // Only visit cells touched by this patch, through an open
                    // passage. Nearby rooms behind walls never receive a copy.
                    // Start/exit exclude patch centers only: clipping at their
                    // open cell boundaries would leave visible straight edges.
                    if (visited.Count >= MaximumTouchedCells) continue;
                    visited.Add(neighbor);
                    if (ClipToRectangle(quad, CellRectangle(neighbor, data)).Count >= 3)
                        pending.Enqueue(neighbor);
                }
            }
        }

        private static List<ClipVertex> ClipToRectangle(List<ClipVertex> polygon, Rect rectangle)
        {
            if (rectangle.width <= 0f || rectangle.height <= 0f) return new List<ClipVertex>();
            polygon = ClipEdge(polygon, true, rectangle.xMin, true);
            polygon = ClipEdge(polygon, true, rectangle.xMax, false);
            polygon = ClipEdge(polygon, false, rectangle.yMin, true);
            return ClipEdge(polygon, false, rectangle.yMax, false);
        }

        private static List<ClipVertex> ClipEdge(List<ClipVertex> input, bool xAxis, float boundary, bool keepGreater)
        {
            var output = new List<ClipVertex>(input.Count + 1);
            if (input.Count == 0) return output;
            ClipVertex previous = input[input.Count - 1];
            float previousDistance = (xAxis ? previous.Position.x : previous.Position.z) - boundary;
            bool previousInside = keepGreater ? previousDistance >= 0f : previousDistance <= 0f;
            foreach (ClipVertex current in input)
            {
                float currentDistance = (xAxis ? current.Position.x : current.Position.z) - boundary;
                bool currentInside = keepGreater ? currentDistance >= 0f : currentDistance <= 0f;
                if (currentInside != previousInside)
                {
                    float t = previousDistance / (previousDistance - currentDistance);
                    output.Add(new ClipVertex(Vector3.LerpUnclamped(previous.Position, current.Position, t),
                        Vector2.LerpUnclamped(previous.Uv, current.Uv, t)));
                }
                if (currentInside) output.Add(current);
                previous = current;
                previousDistance = currentDistance;
                previousInside = currentInside;
            }
            return output;
        }

        private static Vector2Int CellDirection(MazeWall wall)
        {
            switch (wall)
            {
                case MazeWall.North: return Vector2Int.up;
                case MazeWall.East: return Vector2Int.right;
                case MazeWall.South: return Vector2Int.down;
                default: return Vector2Int.left;
            }
        }

        private static float CorridorAngle(MazeCell cell, System.Random random)
        {
            int eastWest = (cell.HasWall(MazeWall.East) ? 0 : 1) + (cell.HasWall(MazeWall.West) ? 0 : 1);
            int northSouth = (cell.HasWall(MazeWall.North) ? 0 : 1) + (cell.HasWall(MazeWall.South) ? 0 : 1);
            if (eastWest == northSouth) return random.Next(2) == 0 ? 0f : Mathf.PI * 0.5f;
            return eastWest > northSouth ? 0f : Mathf.PI * 0.5f;
        }

        private static void BiasToWall(ref Vector3 position, MazeCell cell, Rect floor, System.Random random)
        {
            var walls = new List<MazeWall>(4);
            foreach (MazeWall wall in Sides)
                if (cell.HasWall(wall)) walls.Add(wall);
            if (walls.Count == 0) return;
            float inset = Range(random, 0.05f, 0.25f);
            switch (walls[random.Next(walls.Count)])
            {
                case MazeWall.North: position.z = Mathf.Max(floor.center.y, floor.yMax - inset); break;
                case MazeWall.South: position.z = Mathf.Min(floor.center.y, floor.yMin + inset); break;
                case MazeWall.East: position.x = Mathf.Max(floor.center.x, floor.xMax - inset); break;
                case MazeWall.West: position.x = Mathf.Min(floor.center.x, floor.xMin + inset); break;
            }
        }

        private static AtlasBatch ChooseAtlas(List<AtlasBatch> atlases, System.Random random)
        {
            int totalWeight = 0;
            foreach (AtlasBatch atlas in atlases) totalWeight += atlas.Weight;
            int pick = random.Next(totalWeight);
            foreach (AtlasBatch atlas in atlases)
            {
                pick -= atlas.Weight;
                if (pick < 0) return atlas;
            }
            return atlases[atlases.Count - 1];
        }

        private void BuildSurface(AtlasBatch atlas, Vector2Int chunk, GeometryBatch geometry)
        {
            if (geometry.Vertices.Count == 0) return;
            string name = atlas.Name + " " + chunk.x + "," + chunk.y;
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(geometry.Vertices);
            mesh.SetNormals(geometry.Normals);
            mesh.SetTangents(geometry.Tangents);
            mesh.SetUVs(0, geometry.Uvs);
            mesh.SetTriangles(geometry.Triangles, 0);
            mesh.RecalculateBounds();
            ownedMeshes.Add(mesh);
            var surface = new GameObject(name);
            surface.transform.SetParent(decorationRoot, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = atlas.Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void ReleaseMeshes()
        {
            foreach (Mesh mesh in ownedMeshes)
                if (mesh != null) DestroyOwned(mesh);
            ownedMeshes.Clear();
        }

        private static void DestroyOwned(Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private static float Range(System.Random random, float minimum, float maximum) =>
            minimum + (float)random.NextDouble() * (maximum - minimum);

        private static void Shuffle<T>(List<T> values, System.Random random)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                T value = values[i];
                values[i] = values[other];
                values[other] = value;
            }
        }

        private readonly struct ClipVertex
        {
            public readonly Vector3 Position;
            public readonly Vector2 Uv;
            public ClipVertex(Vector3 position, Vector2 uv) { Position = position; Uv = uv; }
        }

        private sealed class GeometryBatch
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector4> Tangents = new List<Vector4>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();

            public void AddPolygon(List<ClipVertex> polygon, Vector3 tangent)
            {
                int first = Vertices.Count;
                foreach (ClipVertex vertex in polygon)
                {
                    Vertices.Add(vertex.Position);
                    Normals.Add(Vector3.up);
                    Tangents.Add(new Vector4(tangent.x, 0f, tangent.z, -1f));
                    Uvs.Add(vertex.Uv);
                }
                for (int i = 1; i < polygon.Count - 1; i++)
                {
                    Triangles.Add(first);
                    Triangles.Add(first + i);
                    Triangles.Add(first + i + 1);
                }
            }
        }

        private sealed class AtlasBatch
        {
            public readonly string Name;
            public readonly Material Material;
            public readonly int Weight;
            public readonly float MinimumSize;
            public readonly float MaximumSize;
            public readonly int Layer;
            public readonly Dictionary<Vector2Int, GeometryBatch> Chunks = new Dictionary<Vector2Int, GeometryBatch>();
            private readonly List<int> bag = new List<int>();
            private readonly List<Vector3>[] previous = new List<Vector3>[VariantCount];

            public AtlasBatch(string name, Material material, int weight, float minimumSize, float maximumSize, int layer)
            {
                Name = name;
                Material = material;
                Weight = weight;
                MinimumSize = minimumSize;
                MaximumSize = maximumSize;
                Layer = layer;
                for (int i = 0; i < previous.Length; i++) previous[i] = new List<Vector3>();
            }

            public bool HasNearbyCenter(Vector3 position)
            {
                foreach (List<Vector3> variant in previous)
                    foreach (Vector3 other in variant)
                        if ((other - position).sqrMagnitude < 0.35f * 0.35f) return true;
                return false;
            }

            public int TakeVariant(Vector3 position, float repeatDistance, System.Random random)
            {
                if (bag.Count == 0)
                {
                    for (int i = 0; i < VariantCount; i++) bag.Add(i);
                    Shuffle(bag, random);
                }
                int chosen = bag.Count - 1;
                float furthest = -1f;
                for (int i = bag.Count - 1; i >= 0; i--)
                {
                    float nearest = float.PositiveInfinity;
                    foreach (Vector3 other in previous[bag[i]])
                        nearest = Mathf.Min(nearest, (other - position).sqrMagnitude);
                    if (nearest > furthest) { furthest = nearest; chosen = i; }
                    if (nearest >= repeatDistance * repeatDistance) { chosen = i; break; }
                }
                // A full bag is used before refilling. If every remaining variant
                // is nearby, the least repetitive option wins without retry loops.
                int variant = bag[chosen];
                bag.RemoveAt(chosen);
                previous[variant].Add(position);
                return variant;
            }

            public List<ClipVertex> CreateQuad(Vector3 position, Vector3 right, Vector3 forward, int variant)
            {
                int atlasWidth = Material.mainTexture != null ? Material.mainTexture.width : 1536;
                float inset = 1f / atlasWidth;
                float left = variant % AtlasGrid / (float)AtlasGrid + inset;
                float bottom = (AtlasGrid - 1 - variant / AtlasGrid) / (float)AtlasGrid + inset;
                float rightUv = left + 1f / AtlasGrid - inset * 2f;
                float top = bottom + 1f / AtlasGrid - inset * 2f;
                return new List<ClipVertex>
                {
                    new ClipVertex(position - right - forward, new Vector2(left, bottom)),
                    new ClipVertex(position - right + forward, new Vector2(left, top)),
                    new ClipVertex(position + right + forward, new Vector2(rightUv, top)),
                    new ClipVertex(position + right - forward, new Vector2(rightUv, bottom))
                };
            }

            public void AddPolygon(Vector2Int cell, List<ClipVertex> polygon, Vector3 tangent)
            {
                var chunk = new Vector2Int(cell.x / ChunkSize, cell.y / ChunkSize);
                if (!Chunks.TryGetValue(chunk, out GeometryBatch geometry))
                {
                    geometry = new GeometryBatch();
                    Chunks.Add(chunk, geometry);
                }
                geometry.AddPolygon(polygon, tangent);
            }
        }
    }
}
