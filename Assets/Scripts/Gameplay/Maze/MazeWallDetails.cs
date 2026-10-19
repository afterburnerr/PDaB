using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPLab.Gameplay.Maze
{
    [DisallowMultipleComponent, RequireComponent(typeof(MazeBuilder))]
    public sealed class MazeWallDetails : MonoBehaviour
    {
        public enum DecalCategory { Graffiti, Poster, Grime }

        [Serializable]
        public sealed class WallDecal
        {
            public Material material;
            public Vector2 size = Vector2.one;
            public DecalCategory category;
            public bool ceilingAligned;
        }

        [SerializeField] private WallDecal[] decals;
        [SerializeField, Range(0f, 1f)] private float density = 0.65f;
        [SerializeField, Min(0)] private int maximumMarks = 260;
        [SerializeField, Min(0.1f)] private float graffitiScale = 2f;
        [SerializeField, HideInInspector] private Transform decorationRoot;
        [SerializeField, HideInInspector] private List<Mesh> ownedMeshes = new List<Mesh>();

        private static readonly MazeWall[] Sides =
            { MazeWall.North, MazeWall.East, MazeWall.South, MazeWall.West };
        private const int ChunkSize = 4;
        public int PlacedMarks { get; private set; }

        private void OnDestroy() => ReleaseMeshes();

        // Called after apartment fittings have reserved their wall faces.
        // All positions, including reservations, are in generated-maze space.
        public void Decorate(MazeBuilder builder, List<Vector3> blockedFaces)
        {
            PlacedMarks = 0;
            if (builder.Data == null || builder.GeneratedRoot == null) return;
            if (decorationRoot != null)
            {
                decorationRoot.gameObject.SetActive(false);
                DestroyOwned(decorationRoot.gameObject);
            }
            ReleaseMeshes();
            decorationRoot = new GameObject("Wall Details").transform;
            decorationRoot.SetParent(builder.GeneratedRoot, false);
            if (decals == null || decals.Length == 0 || maximumMarks == 0) return;

            var random = new System.Random(unchecked(builder.GeneratedSeed ^ 0x57414C4C));
            var variants = new List<Variant>();
            for (int i = 0; i < decals.Length; i++)
                if (decals[i] != null && decals[i].material != null
                    && decals[i].size.x > 0f && decals[i].size.y > 0f)
                    variants.Add(new Variant(decals[i]));
            if (variants.Count == 0) return;

            MazeData data = builder.Data;
            var faces = new List<WallFace>();
            for (int y = 0; y < data.Height; y++)
                for (int x = 0; x < data.Width; x++)
                {
                    if (!data.IsWalkable(x, y)) continue;
                    Vector3 center = new Vector3((x - (data.Width - 1) * 0.5f) * builder.CellSize,
                        0f, (y - (data.Height - 1) * 0.5f) * builder.CellSize);
                    foreach (MazeWall side in Sides)
                    {
                        if (!data[x, y].HasWall(side)
                            || x == data.ExitX && y == data.ExitY && side == data.ExitWall) continue;
                        Vector3 outward = Direction(side);
                        Vector3 boundary = center + outward * ((builder.CellSize - builder.WallThickness) * 0.5f);
                        if (IsReserved(boundary - outward * 0.012f, blockedFaces)) continue;
                        faces.Add(new WallFace(boundary, -outward, new Vector2Int(x, y)));
                    }
                }

            Shuffle(faces, random);
            int target = Mathf.Min(maximumMarks, Mathf.RoundToInt(faces.Count * density));
            var bag = new List<Variant>();
            int placed = 0;
            foreach (WallFace face in faces)
            {
                if (placed >= target) break;
                if (bag.Count == 0)
                {
                    bag.AddRange(variants);
                    Shuffle(bag, random);
                }
                Variant variant = TakeVariant(bag, face.Cell);
                if (!TryPlace(variant, face, builder, random)) continue;
                variant.PreviousCells.Add(face.Cell);
                placed++;
            }

            foreach (Variant variant in variants)
                foreach (KeyValuePair<Vector2Int, GeometryBatch> chunk in variant.Chunks)
                    BuildSurface(variant.Decal.material, chunk.Key, chunk.Value, builder.gameObject.layer);
            PlacedMarks = placed;
        }

        private static bool IsReserved(Vector3 face, List<Vector3> blockedFaces)
        {
            if (blockedFaces == null) return false;
            foreach (Vector3 blocked in blockedFaces)
            {
                Vector3 delta = blocked - face;
                delta.y = 0f;
                if (delta.sqrMagnitude < 0.01f) return true;
            }
            return false;
        }

        private static Variant TakeVariant(List<Variant> bag, Vector2Int cell)
        {
            int chosen = 0;
            float bestDistance = -1f;
            for (int i = 0; i < bag.Count; i++)
            {
                float nearest = float.PositiveInfinity;
                foreach (Vector2Int previous in bag[i].PreviousCells)
                    nearest = Mathf.Min(nearest, (previous - cell).sqrMagnitude);
                if (nearest > bestDistance) { chosen = i; bestDistance = nearest; }
                if (nearest >= 9f) break;
            }
            Variant result = bag[chosen];
            bag.RemoveAt(chosen);
            return result;
        }

        private bool TryPlace(Variant variant, WallFace face, MazeBuilder builder, System.Random random)
        {
            bool graffiti = variant.Decal.category == DecalCategory.Graffiti;
            float band = Mathf.Min(1.34f, builder.WallHeight * 0.48f);
            const float lowerBottom = 0.20f;
            float lowerTop = band - 0.06f;
            float upperBottom = band + 0.065f;
            float upperTop = builder.WallHeight - (variant.Decal.ceilingAligned ? 0f : 0.18f);
            bool lower = !graffiti && !variant.Decal.ceilingAligned
                && variant.Decal.category != DecalCategory.Poster && random.NextDouble() < 0.45;
            float bottom = graffiti || lower ? lowerBottom : upperBottom;
            float top = graffiti ? upperTop : lower ? lowerTop : upperTop;
            if (top <= bottom) return false;

            float maxWidth = builder.CellSize - builder.WallThickness - (graffiti ? 0.15f : 0.25f);
            if (maxWidth <= 0f) return false;
            float tilt = variant.Decal.ceilingAligned ? 0f
                : variant.Decal.category == DecalCategory.Poster ? 3f : 6f;
            float angle = Range(random, -tilt, tilt) * Mathf.Deg2Rad;
            Vector2 size = variant.Decal.size * Range(random, 0.85f, 1.15f);
            if (graffiti) size *= graffitiScale;
            else if (variant.Decal.category == DecalCategory.Poster) size *= 1.2f;
            float cosine = Mathf.Cos(angle);
            float sine = Mathf.Sin(angle);
            // Fit the rotated bounds without distorting the original artwork.
            float width = size.x * cosine + size.y * Mathf.Abs(sine);
            float height = size.y * cosine + size.x * Mathf.Abs(sine);
            float fit = Mathf.Min(1f, Mathf.Min(maxWidth / width, (top - bottom) / height));
            size *= fit;
            width *= fit;
            height *= fit;
            if (size.x < 0.08f || size.y < 0.08f) return false;

            Vector3 baseRight = Vector3.Cross(Vector3.up, face.Normal);
            Vector3 right = baseRight * cosine + Vector3.up * sine;
            Vector3 up = Vector3.up * cosine - baseRight * sine;
            float horizontal = Range(random, -(maxWidth - width) * 0.5f, (maxWidth - width) * 0.5f);
            float elevation = variant.Decal.ceilingAligned ? top - height * 0.5f
                : Range(random, bottom + height * 0.5f, top - height * 0.5f);
            Vector3 center = face.Position + baseRight * horizontal + Vector3.up * elevation;
            Vector2Int key = new Vector2Int(face.Cell.x / ChunkSize, face.Cell.y / ChunkSize);
            if (!variant.Chunks.TryGetValue(key, out GeometryBatch batch))
            {
                batch = new GeometryBatch();
                variant.Chunks.Add(key, batch);
            }
            if (graffiti)
                batch.AddProjectedGraffiti(center, right * (size.x * 0.5f), up * (size.y * 0.5f), face.Normal, band);
            else
                batch.AddQuad(center + face.Normal * (lower ? 0.029f : 0.008f),
                    right * (size.x * 0.5f), up * (size.y * 0.5f), face.Normal);
            return true;
        }

        private void BuildSurface(Material material, Vector2Int chunk, GeometryBatch batch, int layer)
        {
            var mesh = new Mesh { name = "Wall details " + material.name + " " + chunk, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(batch.Vertices);
            mesh.SetNormals(batch.Normals);
            mesh.SetTangents(batch.Tangents);
            mesh.SetUVs(0, batch.Uvs);
            mesh.SetTriangles(batch.Triangles, 0);
            mesh.RecalculateBounds();
            ownedMeshes.Add(mesh);
            var surface = new GameObject(mesh.name) { layer = layer };
            surface.transform.SetParent(decorationRoot, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
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

        private static void DestroyOwned(UnityEngine.Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private static float Range(System.Random random, float minimum, float maximum) =>
            minimum + (float)random.NextDouble() * (maximum - minimum);

        private static Vector3 Direction(MazeWall wall)
        {
            switch (wall)
            {
                case MazeWall.North: return Vector3.forward;
                case MazeWall.East: return Vector3.right;
                case MazeWall.South: return Vector3.back;
                default: return Vector3.left;
            }
        }

        private static void Shuffle<T>(List<T> values, System.Random random)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                T value = values[i]; values[i] = values[other]; values[other] = value;
            }
        }

        private readonly struct WallFace
        {
            public readonly Vector3 Position;
            public readonly Vector3 Normal;
            public readonly Vector2Int Cell;
            public WallFace(Vector3 position, Vector3 normal, Vector2Int cell)
            { Position = position; Normal = normal; Cell = cell; }
        }

        private sealed class Variant
        {
            public readonly WallDecal Decal;
            public readonly List<Vector2Int> PreviousCells = new List<Vector2Int>();
            public readonly Dictionary<Vector2Int, GeometryBatch> Chunks = new Dictionary<Vector2Int, GeometryBatch>();
            public Variant(WallDecal decal) => Decal = decal;
        }

        private sealed class GeometryBatch
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector4> Tangents = new List<Vector4>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();

            public void AddProjectedGraffiti(Vector3 center, Vector3 right, Vector3 up, Vector3 normal, float band)
            {
                // Project one continuous image onto the three existing depths:
                // green paint, projecting wooden rail, then upper plaster.
                var quad = new List<DecalVertex>
                {
                    new DecalVertex(center - right - up, new Vector2(0f, 0f)),
                    new DecalVertex(center + right - up, new Vector2(1f, 0f)),
                    new DecalVertex(center + right + up, new Vector2(1f, 1f)),
                    new DecalVertex(center - right + up, new Vector2(0f, 1f))
                };
                float railBottom = band - 0.0175f;
                float railTop = band + 0.0175f;
                Vector3 tangent = right.normalized;
                AddPolygon(ClipHeight(quad, railBottom, false), normal, tangent, 0.029f);
                AddPolygon(ClipHeight(ClipHeight(quad, railBottom, true), railTop, false),
                    normal, tangent, 0.0585f);
                AddPolygon(ClipHeight(quad, railTop, true), normal, tangent, 0.008f);
            }

            private static List<DecalVertex> ClipHeight(List<DecalVertex> polygon, float height, bool keepAbove)
            {
                var result = new List<DecalVertex>();
                if (polygon.Count == 0) return result;
                DecalVertex previous = polygon[polygon.Count - 1];
                bool previousInside = keepAbove ? previous.Position.y >= height : previous.Position.y <= height;
                foreach (DecalVertex current in polygon)
                {
                    bool currentInside = keepAbove ? current.Position.y >= height : current.Position.y <= height;
                    if (currentInside != previousInside)
                    {
                        float t = (height - previous.Position.y) / (current.Position.y - previous.Position.y);
                        result.Add(new DecalVertex(Vector3.LerpUnclamped(previous.Position, current.Position, t),
                            Vector2.LerpUnclamped(previous.Uv, current.Uv, t)));
                    }
                    if (currentInside) result.Add(current);
                    previous = current;
                    previousInside = currentInside;
                }
                return result;
            }

            private void AddPolygon(List<DecalVertex> polygon, Vector3 normal, Vector3 tangent, float offset)
            {
                if (polygon.Count < 3) return;
                int first = Vertices.Count;
                foreach (DecalVertex vertex in polygon)
                {
                    Vertices.Add(vertex.Position + normal * offset);
                    Normals.Add(normal);
                    Tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, 1f));
                    Uvs.Add(vertex.Uv);
                }
                for (int i = 1; i < polygon.Count - 1; i++)
                {
                    Triangles.Add(first); Triangles.Add(first + i); Triangles.Add(first + i + 1);
                }
            }

            private readonly struct DecalVertex
            {
                public readonly Vector3 Position;
                public readonly Vector2 Uv;
                public DecalVertex(Vector3 position, Vector2 uv) { Position = position; Uv = uv; }
            }

            public void AddQuad(Vector3 center, Vector3 right, Vector3 up, Vector3 normal)
            {
                int first = Vertices.Count;
                Vertices.Add(center - right - up);
                Vertices.Add(center + right - up);
                Vertices.Add(center - right + up);
                Vertices.Add(center + right + up);
                Vector3 tangent = right.normalized;
                for (int i = 0; i < 4; i++)
                {
                    Normals.Add(normal);
                    Tangents.Add(new Vector4(tangent.x, tangent.y, tangent.z, 1f));
                }
                Uvs.Add(new Vector2(0f, 0f)); Uvs.Add(new Vector2(1f, 0f));
                Uvs.Add(new Vector2(0f, 1f)); Uvs.Add(new Vector2(1f, 1f));
                Triangles.Add(first); Triangles.Add(first + 1); Triangles.Add(first + 2);
                Triangles.Add(first + 2); Triangles.Add(first + 1); Triangles.Add(first + 3);
            }
        }
    }
}
