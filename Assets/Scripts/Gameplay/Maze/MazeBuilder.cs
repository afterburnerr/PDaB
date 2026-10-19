using System;
using System.Collections.Generic;
using FPLab.Gameplay.Session;
using UnityEngine;

namespace FPLab.Gameplay.Maze
{
    [DisallowMultipleComponent]
    public sealed class MazeBuilder : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField, Range(1, 100)] private int width = 33;
        [SerializeField, Range(1, 100)] private int height = 33;
        [SerializeField, Min(1f)] private float cellSize = 2.2f;
        [SerializeField, Min(2f)] private float wallHeight = 3f;
        [SerializeField, Min(0.05f)] private float wallThickness = 0.25f;
        [SerializeField, Min(0.05f)] private float floorThickness = 0.2f;

        [Header("Generation")]
        [SerializeField] private bool generateOnAwake = true;
        [SerializeField] private bool randomizeSeed = true;
        [SerializeField] private int seed = 12345;
        [SerializeField, Min(0f)] private float minimumRouteLength = 330f;
        [SerializeField, HideInInspector] private int generatedSeed;

        [Header("Scene references")]
        [SerializeField] private Transform player;
        [SerializeField] private Material wallMaterial;
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material boardsFloorMaterial;
        [SerializeField] private Material tilesFloorMaterial;
        [SerializeField] private Material startMaterial;
        [SerializeField] private Material exitMaterial;
        [SerializeField] private Material exitSignMaterial;
        [SerializeField, HideInInspector] private Transform generatedRoot;
        private readonly List<Mesh> surfaceMeshes = new List<Mesh>();

        public MazeData Data { get; private set; }
        public event Action<Transform> PlayerExited;
        public event Action Generated;
        public int GeneratedSeed => generatedSeed;
        public float CellSize => cellSize;
        public float WallHeight => wallHeight;
        public float WallThickness => wallThickness;
        public Transform GeneratedRoot => generatedRoot;
        public Vector3 StartPosition => transform.TransformPoint(CellCenter(0, 0));
        public Vector3 ExitPosition => Data == null
            ? transform.position
            : transform.TransformPoint(CellCenter(Data.ExitX, Data.ExitY) + Direction(Data.ExitWall) * cellSize);

        private void Awake()
        {
            if (generateOnAwake)
                Generate();
        }

        [ContextMenu("Generate Maze")]
        public void Generate()
        {
            ValidateConfiguration();
            int nextSeed = randomizeSeed ? Guid.NewGuid().GetHashCode() : seed;
            MazeData nextData = MazeGenerator.Generate(width, height, nextSeed,
                Mathf.CeilToInt(minimumRouteLength / cellSize));

            ClearGenerated();
            Data = nextData;
            generatedSeed = nextSeed;
            generatedRoot = new GameObject("Generated Maze").transform;
            generatedRoot.SetParent(transform, false);

            BuildFloor();
            BuildWalls();
            BuildMarkers();
            PlacePlayer();
            Physics.SyncTransforms();
            Generated?.Invoke();
        }

        [ContextMenu("Clear Maze")]
        public void ClearGenerated()
        {
            if (generatedRoot != null)
            {
                // Runtime destruction is deferred; disable collisions immediately.
                generatedRoot.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(generatedRoot.gameObject);
                else
                    DestroyImmediate(generatedRoot.gameObject);
            }

            generatedRoot = null;
            Data = null;
            ReleaseSurfaceMeshes();
        }

        private void OnDestroy() => ReleaseSurfaceMeshes();

        private void ReleaseSurfaceMeshes()
        {
            foreach (Mesh mesh in surfaceMeshes)
            {
                if (mesh == null) continue;
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
            surfaceMeshes.Clear();
        }

        private void ValidateConfiguration()
        {
            if (width < 1 || width > 100 || height < 1 || height > 100)
                throw new InvalidOperationException("Maze dimensions must be between 1 and 100 cells.");
            if (!IsPositiveFinite(cellSize) || !IsPositiveFinite(wallHeight)
                || !IsPositiveFinite(wallThickness) || !IsPositiveFinite(floorThickness)
                || wallThickness >= cellSize)
                throw new InvalidOperationException("Maze sizes must be finite and positive, with walls thinner than a cell.");
            if ((transform.lossyScale - Vector3.one).sqrMagnitude > 0.0001f
                || Vector3.Dot(transform.up, Vector3.up) < 0.9999f)
                throw new InvalidOperationException("Use unit scale and only Y rotation on the Maze object and its parents.");

            if (player != null && player.TryGetComponent(out CharacterController controller))
            {
                float diameter = controller.radius * 2f * Mathf.Max(player.lossyScale.x, player.lossyScale.z);
                if (cellSize - wallThickness <= diameter + controller.skinWidth * 2f)
                    throw new InvalidOperationException("Maze corridors are too narrow for the player's CharacterController.");
                if (wallHeight <= controller.stepOffset)
                    throw new InvalidOperationException("Maze walls must be taller than the player's step offset.");
            }
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void BuildFloor()
        {
            GameObject floor = CreateBox("Floor", new Vector3(0f, -floorThickness * 0.5f, 0f),
                new Vector3(width * cellSize + wallThickness, floorThickness, height * cellSize + wallThickness),
                floorMaterial, true);
            if (MazeFloorSurfaces.Build(generatedRoot, Data, generatedSeed, cellSize, wallThickness,
                floorMaterial, boardsFloorMaterial, tilesFloorMaterial, surfaceMeshes))
                floor.GetComponent<MeshRenderer>().enabled = false;
        }

        private void BuildWalls()
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    MazeCell cell = Data[x, y];
                    // South and West own shared edges, so walls are never duplicated.
                    if (cell.HasWall(MazeWall.South)) BuildWall(x, y, MazeWall.South);
                    if (cell.HasWall(MazeWall.West)) BuildWall(x, y, MazeWall.West);
                    if (y == height - 1 && cell.HasWall(MazeWall.North)) BuildWall(x, y, MazeWall.North);
                    if (x == width - 1 && cell.HasWall(MazeWall.East)) BuildWall(x, y, MazeWall.East);
                }
            }
        }

        private void BuildWall(int x, int y, MazeWall wall)
        {
            if (x == Data.ExitX && y == Data.ExitY && wall == Data.ExitWall)
                return;
            Vector3 outward = Direction(wall);
            if (!Data.IsWalkable(x, y) && !Data.IsWalkable(x + (int)outward.x, y + (int)outward.z))
                return;

            Vector3 center = CellCenter(x, y) + Direction(wall) * (cellSize * 0.5f);
            center.y = wallHeight * 0.5f;
            bool horizontal = wall == MazeWall.North || wall == MazeWall.South;
            Vector3 size = horizontal
                ? new Vector3(cellSize + wallThickness, wallHeight, wallThickness)
                : new Vector3(wallThickness, wallHeight, cellSize + wallThickness);
            CreateBox($"Wall {x},{y} {wall}", center, size, wallMaterial, true);
        }

        private void BuildMarkers()
        {
            // Prime the dynamic font atlas before building the EXIT sign.
            Font worldFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            worldFont.RequestCharactersInTexture("EXIT", 64, FontStyle.Normal);
            Vector3 outward = Direction(Data.ExitWall);
            Vector3 exit = CellCenter(Data.ExitX, Data.ExitY);
            Vector3 landing = exit + outward * cellSize;
            landing.y = -floorThickness * 0.5f;
            CreateBox("Exit landing", landing,
                new Vector3(cellSize + wallThickness, floorThickness, cellSize + wallThickness), floorMaterial, true);

            var exitTrigger = new GameObject("Exit trigger");
            exitTrigger.transform.SetParent(generatedRoot, false);
            exitTrigger.transform.localPosition = new Vector3(landing.x, wallHeight * 0.5f, landing.z);
            MazeExitTrigger trigger = exitTrigger.AddComponent<MazeExitTrigger>();
            trigger.GetComponent<BoxCollider>().size = new Vector3(cellSize - wallThickness, wallHeight, cellSize - wallThickness);
            trigger.Initialize(this, player == null ? null : player.GetComponent<CharacterController>());

            Vector3 marker = exit + outward * (cellSize * 0.5f);
            marker.y = 0.014f;
            bool horizontal = Data.ExitWall == MazeWall.North || Data.ExitWall == MazeWall.South;
            CreateBox("Exit threshold", marker,
                horizontal ? new Vector3(cellSize * 0.85f, 0.028f, 0.6f) : new Vector3(0.6f, 0.028f, cellSize * 0.85f),
                exitMaterial, false);

            Vector3 lintel = marker;
            lintel.y = wallHeight + 0.15f;
            CreateBox("Exit lintel", lintel,
                horizontal ? new Vector3(cellSize + wallThickness, 0.3f, wallThickness) : new Vector3(wallThickness, 0.3f, cellSize + wallThickness),
                exitMaterial, true);

            var label = new GameObject("Exit sign");
            label.transform.SetParent(generatedRoot, false);
            label.transform.localPosition = marker + Vector3.up * (wallHeight + 0.65f);
            label.transform.localRotation = Quaternion.LookRotation(outward, Vector3.up);
            TextMesh text = label.AddComponent<TextMesh>();
            text.text = "EXIT";
            text.font = worldFont;
            text.fontSize = 64;
            text.characterSize = 0.12f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.3f, 1f, 0.75f);
            var textRenderer = label.GetComponent<MeshRenderer>();
            textRenderer.sharedMaterial = exitSignMaterial != null ? exitSignMaterial : text.font.material;
            if (exitSignMaterial != null)
            {
                // The builtin font material draws through walls. Use depth-tested
                // world text, with the font atlas supplied per renderer.
                text.font.RequestCharactersInTexture(text.text, text.fontSize, FontStyle.Normal);
                var properties = new MaterialPropertyBlock();
                properties.SetTexture("_MainTex", text.font.material.mainTexture);
                textRenderer.SetPropertyBlock(properties);
            }
        }

        private GameObject CreateBox(string objectName, Vector3 position, Vector3 size, Material material, bool solid)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = objectName;
            box.transform.SetParent(generatedRoot, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            MeshFilter filter = box.GetComponent<MeshFilter>();
            Mesh mesh = Instantiate(filter.sharedMesh);
            mesh.name = objectName + " metric UV";
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            var coordinates = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 point = position + Vector3.Scale(vertices[i], size);
                Vector3 normal = normals[i];
                // Builder-space metres keep floor tiles and plaster at a
                // consistent scale, including the long floor and short walls.
                coordinates[i] = Mathf.Abs(normal.y) > 0.5f ? new Vector2(point.x, point.z)
                    : Mathf.Abs(normal.x) > 0.5f ? new Vector2(point.z, point.y)
                    : new Vector2(point.x, point.y);
            }
            mesh.uv = coordinates;
            mesh.RecalculateTangents();
            filter.sharedMesh = mesh;
            surfaceMeshes.Add(mesh);
            if (material != null)
            {
                MeshRenderer renderer = box.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
            }

            if (!solid)
            {
                Collider collider = box.GetComponent<Collider>();
                collider.enabled = false;
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }

            return box;
        }

        internal void NotifyPlayerExited(Transform exitedPlayer)
        {
            if (exitedPlayer == player)
                PlayerExited?.Invoke(exitedPlayer);
        }

        private void PlacePlayer()
        {
            if (player == null)
                return;

            CharacterController controller = player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            float footOffset = controller == null ? 1f
                : (Mathf.Max(controller.height * 0.5f, controller.radius) - controller.center.y) * player.lossyScale.y;
            if (wasEnabled) controller.enabled = false;

            player.position = StartPosition + Vector3.up * footOffset;
            Vector3 forward = Direction(Data.ExitWall);
            foreach (MazeWall wall in new[] { MazeWall.North, MazeWall.East, MazeWall.South, MazeWall.West })
            {
                if (!Data[Data.StartX, Data.StartY].HasWall(wall))
                {
                    forward = Direction(wall);
                    break;
                }
            }
            player.rotation = Quaternion.LookRotation(transform.TransformDirection(forward), Vector3.up);
            if (wasEnabled) controller.enabled = true;
        }

        private Vector3 CellCenter(int x, int y)
        {
            return new Vector3((x - (width - 1) * 0.5f) * cellSize, 0f, (y - (height - 1) * 0.5f) * cellSize);
        }

        private static Vector3 Direction(MazeWall wall)
        {
            switch (wall)
            {
                case MazeWall.North: return Vector3.forward;
                case MazeWall.East: return Vector3.right;
                case MazeWall.South: return Vector3.back;
                case MazeWall.West: return Vector3.left;
                default: throw new ArgumentOutOfRangeException(nameof(wall));
            }
        }
    }
}
