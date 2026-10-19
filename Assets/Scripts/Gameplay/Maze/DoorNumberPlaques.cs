using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace FPLab.Gameplay.Maze
{
    // Local-space, opaque geometry: numbers receive the same lighting as the door.
    internal sealed class DoorNumberPlaques
    {
        private const float PlateCenterDepth = 0.0862f;
        private const float PlateFrontDepth = PlateCenterDepth + 0.00125f;
        private const float DigitHeight = 0.032f;
        private const float DigitGap = 0.003f;
        private const float MaximumRunWidth = 0.086f;

        private readonly SourceMesh plate;
        private readonly SourceMesh screw;
        private readonly SourceMesh[] digits;
        private readonly Material plateMaterial;
        private readonly Material screwMaterial;
        private readonly Material digitMaterial;
        private readonly Transform root;
        private readonly List<Mesh> ownedMeshes;
        private readonly float chunkSize;
        private readonly Dictionary<Vector2Int, Chunk> chunks = new Dictionary<Vector2Int, Chunk>();

        public bool IsConfigured { get; }

        public DoorNumberPlaques(Mesh plateMesh, Mesh screwMesh, Mesh[] digitMeshes,
            Material plateMaterial, Material screwMaterial, Material digitMaterial,
            Transform root, List<Mesh> ownedMeshes, float cellSize)
        {
            this.plateMaterial = plateMaterial;
            this.screwMaterial = screwMaterial;
            this.digitMaterial = digitMaterial;
            this.root = root;
            this.ownedMeshes = ownedMeshes;
            chunkSize = Mathf.Max(0.01f, cellSize * 4f);

            if (plateMesh == null || screwMesh == null || digitMeshes == null || digitMeshes.Length != 10 ||
                plateMaterial == null || screwMaterial == null || digitMaterial == null || root == null)
                return;
            foreach (Mesh digit in digitMeshes)
                if (digit == null)
                    return;

            plate = new SourceMesh(plateMesh);
            screw = new SourceMesh(screwMesh);
            digits = new SourceMesh[10];
            for (int i = 0; i < digits.Length; i++)
                digits[i] = new SourceMesh(digitMeshes[i]);
            IsConfigured = true;
        }

        // face and rotation use the same coordinates as MazeAtmosphere.AddDoor.
        // Source assets face +Z; digits have unit height and a centered pivot.
        public void Add(Vector3 face, Quaternion rotation, int number, float elevation)
        {
            if (!IsConfigured)
                return;

            var key = new Vector2Int(Mathf.FloorToInt(face.x / chunkSize), Mathf.FloorToInt(face.z / chunkSize));
            if (!chunks.TryGetValue(key, out Chunk chunk))
            {
                chunk = new Chunk();
                chunks.Add(key, chunk);
            }

            float tilt = ((number * 17) % 13 - 6) * (0.8f / 6f);
            Quaternion plaqueRotation = rotation * Quaternion.Euler(0f, 0f, tilt);
            Vector3 center = face + rotation * new Vector3(0f, elevation, PlateCenterDepth);
            chunk.Plates.Add(plate, center, plaqueRotation, 1f);

            // Two small screws fasten the plaque through its side margins.
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 position = center + plaqueRotation * new Vector3(side * 0.056f, 0f, 0.0018f);
                Quaternion screwRotation = plaqueRotation * Quaternion.Euler(0f, 0f, number * 31f + side * 19f);
                chunk.Screws.Add(screw, position, screwRotation, 1f);
            }

            string label = number.ToString(CultureInfo.InvariantCulture);
            float width = (label.Length - 1) * DigitGap;
            foreach (char character in label)
                width += digits[character - '0'].Width * DigitHeight;
            float fit = Mathf.Min(1f, MaximumRunWidth / width);
            float scale = DigitHeight * fit;
            float cursor = -width * fit * 0.5f;
            float digitDepth = PlateFrontDepth - PlateCenterDepth + 0.0005f;
            foreach (char character in label)
            {
                SourceMesh digit = digits[character - '0'];
                float digitWidth = digit.Width * scale;
                Vector3 position = center + plaqueRotation * new Vector3(cursor + digitWidth * 0.5f, 0f, digitDepth);
                chunk.Digits.Add(digit, position, plaqueRotation, scale);
                cursor += digitWidth + DigitGap * fit;
            }
        }

        public void Build()
        {
            if (!IsConfigured)
                return;
            foreach (KeyValuePair<Vector2Int, Chunk> entry in chunks)
            {
                string suffix = " " + entry.Key.x + "," + entry.Key.y;
                BuildSurface("Apartment number plates" + suffix, entry.Value.Plates, plateMaterial);
                BuildSurface("Apartment raised digits" + suffix, entry.Value.Digits, digitMaterial);
                BuildSurface("Apartment screws" + suffix, entry.Value.Screws, screwMaterial);
            }
            chunks.Clear();
        }

        private void BuildSurface(string name, Geometry geometry, Material material)
        {
            if (geometry.VertexCount == 0)
                return;
            Mesh mesh = geometry.Build(name);
            ownedMeshes.Add(mesh);
            var surface = new GameObject(name);
            surface.transform.SetParent(root, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private sealed class Chunk
        {
            public readonly Geometry Plates = new Geometry();
            public readonly Geometry Digits = new Geometry();
            public readonly Geometry Screws = new Geometry();
        }

        private sealed class SourceMesh
        {
            public readonly Vector3[] Vertices;
            public readonly Vector3[] Normals;
            public readonly Vector2[] UV;
            public readonly int[] Triangles;
            public readonly float Width;

            public SourceMesh(Mesh mesh)
            {
                Vertices = mesh.vertices;
                Normals = mesh.normals;
                UV = mesh.uv;
                Triangles = mesh.triangles;
                Width = mesh.bounds.size.x;
            }
        }

        private sealed class Geometry
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();
            public int VertexCount => vertices.Count;

            public void Add(SourceMesh source, Vector3 position, Quaternion rotation, float scale)
            {
                int first = vertices.Count;
                for (int i = 0; i < source.Vertices.Length; i++)
                {
                    vertices.Add(position + rotation * (source.Vertices[i] * scale));
                    normals.Add(rotation * source.Normals[i]);
                    uv.Add(source.UV[i]);
                }
                foreach (int index in source.Triangles)
                    triangles.Add(first + index);
            }

            public Mesh Build(string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
