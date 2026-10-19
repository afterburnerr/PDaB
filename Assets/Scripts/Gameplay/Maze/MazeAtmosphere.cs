using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FPLab.Gameplay.Maze
{
    // Decoration follows the generated layout; it never changes its passages.
    [DisallowMultipleComponent, RequireComponent(typeof(MazeBuilder))]
    public sealed class MazeAtmosphere : MonoBehaviour
    {
        [Serializable]
        public sealed class WallSurfaceStyle
        {
            public Material upperMaterial;
            public Material lowerMaterial;
        }

        [Header("Surfaces")]
        [SerializeField] private Material doorMaterial;
        [SerializeField] private Material trimMaterial;
        [SerializeField] private Material ceilingMaterial;
        [SerializeField] private Material fixtureMaterial;
        [SerializeField] private Material wallBandMaterial;
        [SerializeField] private Material numberMaterial;
        [SerializeField] private Material numberPlateMaterial;
        [SerializeField] private Material numberScrewMaterial;
        [SerializeField] private Mesh numberPlateMesh;
        [SerializeField] private Mesh numberScrewMesh;
        [SerializeField] private Mesh[] numberDigitMeshes;
        [SerializeField] private Material doorFrameMaterial;
        [SerializeField] private Material metalDetailMaterial;
        [SerializeField] private Material mailboxMaterial;
        [SerializeField] private Material insectMaterial;
        [SerializeField] private WallSurfaceStyle[] wallSurfaceStyles;
        [SerializeField] private Material blendedUpperWallMaterial;
        [SerializeField] private Material blendedLowerWallMaterial;

        [Header("Apartment details")]
        [SerializeField, Range(0f, 1f)] private float doorChance = 0.18f;
        [SerializeField, Min(0)] private int maximumDoors = 64;
        [SerializeField, Range(0, 64)] private int maximumLights = 32;
        [SerializeField, Min(0f)] private float lampIntensity = 2.9f;
        [SerializeField] private GameObject poweredLampPrefab;
        [SerializeField] private GameObject unpoweredLampPrefab;

        private static readonly MazeWall[] Sides =
            { MazeWall.North, MazeWall.East, MazeWall.South, MazeWall.West };
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private MazeBuilder builder;
        private Transform decorationRoot;
        private GeometryBatch doors;
        private GeometryBatch trim;
        private GeometryBatch ceiling;
        private GeometryBatch fixtures;
        private GeometryBatch paint;
        private GeometryBatch doorFrames;
        private GeometryBatch metalDetails;
        private GeometryBatch mailboxes;
        private GeometryBatch entrancePortals;
        private DoorNumberPlaques numberPlaques;
        private MazeWallSurfaceLayout wallSurfaceLayout;
        private Texture2D wallStyleMap;
        private Dictionary<Vector3Int, WallFinishBatch> wallFinishBatches;

        private void OnEnable()
        {
            builder = GetComponent<MazeBuilder>();
            builder.Generated += Decorate;
        }

        private void Start()
        {
            // MazeBuilder.Awake may run before this component subscribes.
            if (builder.Data != null && decorationRoot == null)
                Decorate();
        }

        private void OnDisable()
        {
            if (builder != null)
                builder.Generated -= Decorate;
        }

        private void OnDestroy()
        {
            ReleaseMeshes();
            ReleaseWallStyleMap();
        }

        [ContextMenu("Decorate Maze")]
        public void Decorate()
        {
            if (builder == null)
                builder = GetComponent<MazeBuilder>();
            MazeData data = builder.Data;
            if (data == null || builder.GeneratedRoot == null)
                return;

            if (decorationRoot != null)
            {
                decorationRoot.gameObject.SetActive(false);
                DestroyOwned(decorationRoot.gameObject);
            }
            ReleaseMeshes();
            ReleaseWallStyleMap();
            decorationRoot = new GameObject("Entrance Atmosphere").transform;
            decorationRoot.SetParent(builder.GeneratedRoot, false);
            numberPlaques = new DoorNumberPlaques(numberPlateMesh, numberScrewMesh, numberDigitMeshes,
                numberPlateMaterial, numberScrewMaterial, numberMaterial, decorationRoot, ownedMeshes, builder.CellSize);
            doors = new GeometryBatch(true);
            trim = new GeometryBatch();
            ceiling = new GeometryBatch();
            fixtures = new GeometryBatch();
            paint = new GeometryBatch();
            doorFrames = new GeometryBatch();
            metalDetails = new GeometryBatch();
            mailboxes = new GeometryBatch();
            entrancePortals = new GeometryBatch(worldAlignedUV: true);

            float cellSize = builder.CellSize;
            float wallHeight = builder.WallHeight;
            float thickness = builder.WallThickness;
            wallSurfaceLayout = HasWallSurfaceStyles()
                ? new MazeWallSurfaceLayout(data.Width, data.Height, builder.GeneratedSeed, wallSurfaceStyles.Length)
                : null;
            if (wallSurfaceLayout != null && blendedUpperWallMaterial != null && blendedLowerWallMaterial != null)
                wallStyleMap = wallSurfaceLayout.CreateStyleMap();
            wallFinishBatches = new Dictionary<Vector3Int, WallFinishBatch>();
            ceiling.AddBox(new Vector3(0f, wallHeight + 0.08f, 0f),
                new Vector3(data.Width * cellSize + thickness, 0.16f, data.Height * cellSize + thickness), Quaternion.identity);
            BuildWallFinishes(data, cellSize, thickness, wallHeight);

            var random = new System.Random(unchecked(builder.GeneratedSeed ^ 0x4D415A45));
            var blockedWallFaces = new List<Vector3>();
            int doorCount = 0;
            var sectionDoors = new Dictionary<int, int>();
            var lampCells = new List<Vector2Int>();
            var residentialWalls = new List<WallFace>();
            for (int y = 0; y < data.Height; y++)
            {
                for (int x = 0; x < data.Width; x++)
                {
                    if (!data.IsWalkable(x, y)) continue;
                    Vector3 center = CellCenter(x, y, data, cellSize);
                    MazeCellKind kind = data.GetCellKind(x, y);
                    MazeWall serviceWall = AddServiceBay(x, y, center, data, cellSize, thickness, wallHeight);
                    foreach (MazeWall side in Sides)
                    {
                        bool isExit = x == data.ExitX && y == data.ExitY && side == data.ExitWall;
                        if (!data[x, y].HasWall(side) || isExit)
                            continue;

                        Vector3 outward = Direction(side);
                        Quaternion rotation = Quaternion.LookRotation(-outward, Vector3.up);
                        Vector3 face = center + outward * ((cellSize - thickness) * 0.5f - 0.012f);
                        if (side == serviceWall)
                        {
                            blockedWallFaces.Add(face);
                            continue;
                        }
                        if (random.NextDouble() < 0.035)
                        {
                            AddConduit(face, rotation, wallHeight);
                            blockedWallFaces.Add(face);
                        }
                        int section = data.GetSectionId(x, y);
                        sectionDoors.TryGetValue(section, out int inSection);
                        float chance = kind == MazeCellKind.Landing ? 0.72f
                            : kind == MazeCellKind.Vestibule ? 0.45f : doorChance * 0.5f;
                        bool hasDoor = doorCount < maximumDoors && inSection < 4
                            && random.NextDouble() < chance && cellSize >= 1.8f;
                        if (hasDoor)
                        {
                            doorCount++;
                            sectionDoors[section] = inSection + 1;
                            AddDoor(face, rotation, Mathf.Max(0, section) * 4 + inSection + 1, wallHeight);
                            blockedWallFaces.Add(face);
                        }
                        else if (residentialWalls.Count == 0 || residentialWalls[residentialWalls.Count - 1].Cell != new Vector2Int(x, y))
                            residentialWalls.Add(new WallFace(face, rotation, new Vector2Int(x, y)));
                    }

                    int openings = 0;
                    foreach (MazeWall side in Sides)
                        if (!data[x, y].HasWall(side)) openings++;
                    if (kind == MazeCellKind.Landing || kind == MazeCellKind.Vestibule
                        || x % 3 == 0 && y % 3 == 0 || openings != 2)
                        lampCells.Add(new Vector2Int(x, y));
                }
            }

            // Farthest-first placement covers the whole entrance instead of
            // concentrating a fixed light budget in the first generated rows.
            Shuffle(lampCells, 0, random);
            List<Vector2Int> poweredCells = ChoosePoweredCells(lampCells, data);
            int lampNumber = 0;
            foreach (Vector2Int cell in poweredCells)
                AddLamp(CellCenter(cell.x, cell.y, data, cellSize), wallHeight, true, ++lampNumber);
            int unpowered = 0;
            foreach (Vector2Int cell in lampCells)
            {
                if (unpowered >= 6) break;
                if (poweredCells.Contains(cell)) continue;
                bool nearLamp = false;
                foreach (Vector2Int powered in poweredCells)
                    if ((cell - powered).sqrMagnitude < 4) nearLamp = true;
                if (nearLamp) continue;
                AddLamp(CellCenter(cell.x, cell.y, data, cellSize), wallHeight, false, 0);
                unpowered++;
            }

            // Guarantee the familiar mailbox bank close to the entry, then
            // distribute a few more banks through distant parts of the maze.
            var startCell = new Vector2Int(data.StartX, data.StartY);
            int nearStart = 0;
            for (int i = 1; i < residentialWalls.Count; i++)
                if ((residentialWalls[i].Cell - startCell).sqrMagnitude <
                    (residentialWalls[nearStart].Cell - startCell).sqrMagnitude) nearStart = i;
            if (residentialWalls.Count > 0)
            {
                WallFace firstWall = residentialWalls[nearStart];
                residentialWalls.RemoveAt(nearStart);
                Shuffle(residentialWalls, 0, random);
                residentialWalls.Insert(0, firstWall);
            }
            var chosenWalls = new List<Vector2Int>();
            foreach (WallFace wall in residentialWalls)
            {
                if (chosenWalls.Count >= 8) break;
                bool spaced = true;
                foreach (Vector2Int other in chosenWalls)
                    if ((wall.Cell - other).sqrMagnitude < 16) spaced = false;
                if (!spaced) continue;
                chosenWalls.Add(wall.Cell);
                AddMailboxes(wall.Position, wall.Rotation);
                AddConduit(wall.Position, wall.Rotation, wallHeight);
                blockedWallFaces.Add(wall.Position);
            }

            AddLandingPortals(data, cellSize, thickness, wallHeight);
            BuildSurface("Apartment doors", doors, doorMaterial);
            GameObject portals = BuildSurface("Landing passage frames", entrancePortals, ceilingMaterial);
            if (portals != null)
            {
                portals.AddComponent<MeshCollider>().sharedMesh = portals.GetComponent<MeshFilter>().sharedMesh;
                // The collider is continuous; visible finishes join the wall batches below.
                portals.GetComponent<MeshRenderer>().enabled = false;
            }
            BuildSurface("Frames and skirting", trim, trimMaterial);
            BuildSurface("Ceiling", ceiling, ceilingMaterial);
            BuildSurface("Frosted entrance lamp glass", fixtures, fixtureMaterial, false);
            BuildSurface("Lower wall paint", paint, wallBandMaterial);
            foreach (KeyValuePair<Vector3Int, WallFinishBatch> entry in wallFinishBatches)
            {
                WallSurfaceStyle style = wallSurfaceStyles[entry.Key.y];
                string label = $"Wall finish {entry.Key.y} chunk {entry.Key.x},{entry.Key.z}";
                BuildSurface(label + " upper", entry.Value.Upper,
                    wallStyleMap != null ? blendedUpperWallMaterial : style.upperMaterial, blendWallSurfaces: wallStyleMap != null);
                BuildSurface(label + " lower", entry.Value.Lower,
                    wallStyleMap != null ? blendedLowerWallMaterial : style.lowerMaterial, blendWallSurfaces: wallStyleMap != null);
            }
            BuildSurface("Painted wooden architraves", doorFrames, doorFrameMaterial);
            BuildSurface("Domestic metal fittings", metalDetails, metalDetailMaterial);
            BuildSurface("Apartment mailboxes", mailboxes, mailboxMaterial);
            numberPlaques.Build();
            GetComponent<MazeWallDetails>()?.Decorate(builder, blockedWallFaces);
        }

        private void BuildWallFinishes(MazeData data, float cellSize, float thickness, float wallHeight)
        {
            float bandHeight = Mathf.Min(1.34f, wallHeight * 0.48f);
            var nodes = new bool[data.Width + 1, data.Height + 1];
            for (int y = 0; y < data.Height; y++)
            {
                for (int x = 0; x < data.Width; x++)
                {
                    // Match MazeBuilder's shared-edge ownership exactly.
                    if (HasBuiltWall(data, x, y, MazeWall.South))
                        AddFinishEdge(x, y, true, data, cellSize, thickness, bandHeight, nodes);
                    if (HasBuiltWall(data, x, y, MazeWall.West))
                        AddFinishEdge(x, y, false, data, cellSize, thickness, bandHeight, nodes);
                    if (y == data.Height - 1 && HasBuiltWall(data, x, y, MazeWall.North))
                        AddFinishEdge(x, y + 1, true, data, cellSize, thickness, bandHeight, nodes);
                    if (x == data.Width - 1 && HasBuiltWall(data, x, y, MazeWall.East))
                        AddFinishEdge(x + 1, y, false, data, cellSize, thickness, bandHeight, nodes);
                }
            }

            // One solid corner per node closes exposed ends and all junctions.
            // Central spans stop at these corners, so no visible faces overlap.
            for (int y = 0; y <= data.Height; y++)
                for (int x = 0; x <= data.Width; x++)
                    if (nodes[x, y])
                        AddFinishLayers(FinishNode(x, y, data, cellSize), true, true,
                            cellSize, thickness, bandHeight, builder.WallHeight);
        }

        private static bool HasBuiltWall(MazeData data, int x, int y, MazeWall side)
        {
            Vector3 direction = Direction(side);
            return data[x, y].HasWall(side)
                && (data.IsWalkable(x, y) || data.IsWalkable(x + (int)direction.x, y + (int)direction.z))
                && !(x == data.ExitX && y == data.ExitY && side == data.ExitWall);
        }

        private void AddFinishEdge(int x, int y, bool horizontal, MazeData data,
            float cellSize, float thickness, float bandHeight, bool[,] nodes)
        {
            nodes[x, y] = true;
            nodes[x + (horizontal ? 1 : 0), y + (horizontal ? 0 : 1)] = true;
            Vector3 center = FinishNode(x, y, data, cellSize)
                + (horizontal ? Vector3.right : Vector3.forward) * (cellSize * 0.5f);
            AddFinishLayers(center, horizontal, false, cellSize, thickness, bandHeight, builder.WallHeight);
        }

        private static Vector3 FinishNode(int x, int y, MazeData data, float cellSize) =>
            new Vector3((x - data.Width * 0.5f) * cellSize, 0f, (y - data.Height * 0.5f) * cellSize);

        private void AddFinishLayers(Vector3 center, bool horizontal, bool node,
            float cellSize, float thickness, float bandHeight, float wallHeight)
        {
            // Preserve the previous side-face depths, wrapping them around ends.
            GeometryBatch lower = paint;
            if (wallSurfaceLayout != null)
            {
                Vector2Int cell = wallSurfaceLayout.GetCell(center, cellSize);
                int style = wallStyleMap != null ? 0 : wallSurfaceLayout.StyleGrid[cell.y * wallSurfaceLayout.Width + cell.x];
                var key = new Vector3Int(cell.x / 4, style, cell.y / 4);
                if (!wallFinishBatches.TryGetValue(key, out WallFinishBatch batch))
                {
                    batch = new WallFinishBatch();
                    wallFinishBatches.Add(key, batch);
                }
                // A complete skin hides the builder's structural plaster on
                // both faces, all free ends and the corner blocks.
                AddFinishLayer(batch.Upper, center, horizontal, node, cellSize, thickness + 0.008f,
                    wallHeight * 0.5f, wallHeight);
                lower = batch.Lower;
            }
            AddFinishLayer(lower, center, horizontal, node, cellSize, thickness + 0.050f,
                bandHeight * 0.5f, bandHeight);
            AddFinishLayer(doorFrames, center, horizontal, node, cellSize, thickness + 0.129f,
                0.065f, 0.13f);
            AddFinishLayer(trim, center, horizontal, node, cellSize, thickness + 0.109f,
                bandHeight, 0.035f);
        }

        private bool HasWallSurfaceStyles()
        {
            if (wallSurfaceStyles == null || wallSurfaceStyles.Length == 0) return false;
            foreach (WallSurfaceStyle style in wallSurfaceStyles)
                if (style == null || style.upperMaterial == null || style.lowerMaterial == null) return false;
            return true;
        }

        private sealed class WallFinishBatch
        {
            public readonly GeometryBatch Upper = new GeometryBatch(worldAlignedUV: true);
            public readonly GeometryBatch Lower = new GeometryBatch(worldAlignedUV: true);
        }

        private static void AddFinishLayer(GeometryBatch batch, Vector3 center, bool horizontal,
            bool node, float cellSize, float width, float elevation, float height)
        {
            // In very narrow passages the corner blocks can meet directly.
            width = Mathf.Min(width, cellSize);
            if (!node && width >= cellSize) return;
            float length = node ? width : cellSize - width;
            Vector3 size = horizontal ? new Vector3(length, height, width) : new Vector3(width, height, length);
            center.y = elevation;
            batch.AddBox(center, size, Quaternion.identity);
        }

        private void AddLandingPortals(MazeData data, float cellSize, float thickness, float wallHeight)
        {
            const float clearHeight = 2.28f;
            if (wallHeight <= clearHeight) return;
            for (int y = 0; y < data.Height; y++)
                for (int x = 0; x < data.Width; x++)
                {
                    if (!data.IsWalkable(x, y)) continue;
                    for (int d = 0; d < 2; d++)
                    {
                        MazeWall side = d == 0 ? MazeWall.North : MazeWall.East;
                        Vector3 direction = Direction(side);
                        int nx = x + (int)direction.x;
                        int ny = y + (int)direction.z;
                        if (!data.IsWalkable(nx, ny) || data[x, y].HasWall(side)) continue;
                        MazeCellKind a = data.GetCellKind(x, y);
                        MazeCellKind b = data.GetCellKind(nx, ny);
                        bool aRoom = a == MazeCellKind.Landing || a == MazeCellKind.Vestibule;
                        bool bRoom = b == MazeCellKind.Landing || b == MazeCellKind.Vestibule;
                        if (aRoom == bRoom) continue;
                        Vector3 position = CellCenter(x, y, data, cellSize) + direction * (cellSize * 0.5f);
                        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
                        float span = cellSize - thickness;
                        // Open plastered doorway separates a landing from the corridor.
                        AddPortalBox(position, rotation,
                            new Vector3(0f, (wallHeight + clearHeight) * 0.5f, 0f),
                            new Vector3(span, wallHeight - clearHeight, 0.18f));
                        foreach (float sign in new[] { -1f, 1f })
                            AddPortalBox(position, rotation,
                                new Vector3(sign * (span - 0.08f) * 0.5f, clearHeight * 0.5f, 0f),
                                new Vector3(0.08f, clearHeight, 0.18f));
                    }
                }
        }

        private void AddPortalBox(Vector3 origin, Quaternion rotation, Vector3 offset, Vector3 size)
        {
            Vector3 center = origin + rotation * offset;
            entrancePortals.AddBox(center, size, rotation);
            GeometryBatch upper = ceiling;
            GeometryBatch lower = paint;
            if (wallSurfaceLayout != null)
            {
                Vector2Int cell = wallSurfaceLayout.GetCell(center, builder.CellSize);
                int style = wallStyleMap != null ? 0
                    : wallSurfaceLayout.StyleGrid[cell.y * wallSurfaceLayout.Width + cell.x];
                var key = new Vector3Int(cell.x / 4, style, cell.y / 4);
                if (!wallFinishBatches.TryGetValue(key, out WallFinishBatch batch))
                {
                    batch = new WallFinishBatch();
                    wallFinishBatches.Add(key, batch);
                }
                upper = batch.Upper;
                lower = batch.Lower;
            }
            float band = Mathf.Min(1.34f, builder.WallHeight * 0.48f);
            float bottom = center.y - size.y * 0.5f;
            float top = center.y + size.y * 0.5f;
            if (top > band)
            {
                float from = Mathf.Max(bottom, band);
                upper.AddBox(new Vector3(center.x, (from + top) * 0.5f, center.z),
                    new Vector3(size.x, top - from, size.z), rotation);
            }
            if (bottom < band)
            {
                float to = Mathf.Min(top, band);
                lower.AddBox(new Vector3(center.x, (bottom + to) * 0.5f, center.z),
                    new Vector3(size.x, to - bottom, size.z), rotation);
                trim.AddBox(new Vector3(center.x, band, center.z),
                    new Vector3(size.x + 0.014f, 0.035f, size.z + 0.014f), rotation);
                doorFrames.AddBox(new Vector3(center.x, 0.065f, center.z),
                    new Vector3(size.x + 0.022f, 0.13f, size.z + 0.022f), rotation);
            }
        }

        private MazeWall AddServiceBay(int x, int y, Vector3 center, MazeData data,
            float cellSize, float thickness, float wallHeight)
        {
            if (data.GetCellKind(x, y) != MazeCellKind.ServiceAlcove) return 0;
            MazeWall opening = 0;
            int openings = 0;
            foreach (MazeWall side in Sides)
                if (!data[x, y].HasWall(side)) { opening = side; openings++; }
            if (openings != 1) return 0;
            MazeWall end = opening == MazeWall.North ? MazeWall.South
                : opening == MazeWall.South ? MazeWall.North
                : opening == MazeWall.East ? MazeWall.West : MazeWall.East;
            Vector3 outward = Direction(end);
            Vector3 face = center + outward * ((cellSize - thickness) * 0.5f - 0.012f);
            Quaternion rotation = Quaternion.LookRotation(-outward, Vector3.up);
            bool lift = data.GetSectionId(x, y) % 3 == 0;
            float width = Mathf.Min(lift ? 1.35f : 0.95f, cellSize - thickness - 0.15f);
            float height = Mathf.Min(lift ? 2.15f : 1.9f, wallHeight - 0.3f);
            AddLocalBox(metalDetails, face, rotation, new Vector3(0f, height * 0.5f, 0.04f),
                new Vector3(width + 0.12f, height + 0.12f, 0.08f));
            foreach (float sign in new[] { -1f, 1f })
                AddLocalBox(mailboxes, face, rotation, new Vector3(sign * width * 0.25f, height * 0.5f, 0.09f),
                    new Vector3(width * 0.5f - 0.014f, height - 0.04f, 0.045f));
            if (lift)
            {
                AddLocalBox(metalDetails, face, rotation, new Vector3(width * 0.5f + 0.15f, 1.1f, 0.1f),
                    new Vector3(0.09f, 0.19f, 0.05f));
                AddLocalBox(trim, face, rotation, new Vector3(width * 0.5f + 0.15f, 1.1f, 0.13f),
                    new Vector3(0.025f, 0.035f, 0.016f));
            }
            else
                AddLocalBox(metalDetails, face, rotation, new Vector3(-0.1f, 0.95f, 0.13f),
                    new Vector3(0.025f, 0.14f, 0.035f));
            return end;
        }

        private void AddDoor(Vector3 face, Quaternion rotation, int number, float wallHeight)
        {
            float height = Mathf.Min(2.05f, wallHeight - 0.25f);
            const float width = 0.9f;
            AddLocalBox(doors, face, rotation, new Vector3(0f, height * 0.5f, 0.05f),
                new Vector3(width, height, 0.07f));
            AddLocalBox(doorFrames, face, rotation, new Vector3(-width * 0.5f - 0.03f, height * 0.5f, 0.075f),
                new Vector3(0.06f, height + 0.06f, 0.075f));
            AddLocalBox(doorFrames, face, rotation, new Vector3(width * 0.5f + 0.03f, height * 0.5f, 0.075f),
                new Vector3(0.06f, height + 0.06f, 0.075f));
            AddLocalBox(doorFrames, face, rotation, new Vector3(0f, height + 0.03f, 0.075f),
                new Vector3(width + 0.12f, 0.06f, 0.075f));
            AddLocalBox(metalDetails, face, rotation, new Vector3(0.34f, 1f, 0.1f),
                new Vector3(0.035f, 0.16f, 0.025f));
            AddLocalBox(metalDetails, face, rotation, new Vector3(0.30f, 1.03f, 0.125f),
                new Vector3(0.115f, 0.022f, 0.04f));
            AddLocalBox(metalDetails, face, rotation, new Vector3(0.34f, 0.965f, 0.12f),
                new Vector3(0.012f, 0.023f, 0.012f));
            AddLocalBox(metalDetails, face, rotation, new Vector3(0f, 1.48f, 0.095f),
                new Vector3(0.022f, 0.022f, 0.022f));
            numberPlaques.Add(face, rotation, number, height * 0.85f);
        }

        private void AddConduit(Vector3 face, Quaternion rotation, float wallHeight)
        {
            AddLocalBox(metalDetails, face, rotation, new Vector3(-0.85f, wallHeight * 0.5f, 0.045f),
                new Vector3(0.028f, wallHeight, 0.028f));
            AddLocalBox(metalDetails, face, rotation, new Vector3(-0.85f, 1.67f, 0.09f),
                new Vector3(0.32f, 0.44f, 0.14f));
            AddLocalBox(trim, face, rotation, new Vector3(-0.85f, 1.73f, 0.166f),
                new Vector3(0.12f, 0.095f, 0.012f));
            AddLocalBox(trim, face, rotation, new Vector3(-0.735f, 1.59f, 0.17f),
                new Vector3(0.018f, 0.028f, 0.015f));
        }

        private void AddMailboxes(Vector3 face, Quaternion rotation)
        {
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    float x = (column - 1) * 0.315f;
                    float y = 1.48f - row * 0.255f;
                    AddLocalBox(mailboxes, face, rotation, new Vector3(x, y, 0.08f),
                        new Vector3(0.30f, 0.24f, 0.12f));
                    AddLocalBox(trim, face, rotation, new Vector3(x, y + 0.06f, 0.145f),
                        new Vector3(0.21f, 0.012f, 0.012f));
                    AddLocalBox(metalDetails, face, rotation, new Vector3(x + 0.095f, y - 0.055f, 0.15f),
                        new Vector3(0.018f, 0.025f, 0.02f));
                    AddLocalBox(metalDetails, face, rotation, new Vector3(x - 0.035f, y - 0.035f, 0.145f),
                        new Vector3(0.07f, 0.027f, 0.012f));
                }
            }
        }

        private List<Vector2Int> ChoosePoweredCells(List<Vector2Int> candidates, MazeData data)
        {
            var chosen = new List<Vector2Int>();
            int budget = Mathf.Min(maximumLights, 64);
            if (budget <= 0) return chosen;
            int count = data.Width * data.Height;
            int[] nearest = new int[count];
            for (int i = 0; i < count; i++) nearest[i] = int.MaxValue;
            var queue = new Queue<int>();
            int Neighbor(int cell, MazeWall side)
            {
                int x = cell % data.Width, y = cell / data.Width;
                Vector3 offset = Direction(side);
                int nx = x + (int)offset.x, ny = y + (int)offset.z;
                return data.IsWalkable(nx, ny) && !data[x, y].HasWall(side)
                    ? ny * data.Width + nx : -1;
            }
            void Add(Vector2Int cell)
            {
                if (chosen.Contains(cell)) return;
                chosen.Add(cell);
                int origin = cell.y * data.Width + cell.x;
                nearest[origin] = 0;
                queue.Enqueue(origin);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    foreach (MazeWall side in Sides)
                    {
                        int next = Neighbor(current, side);
                        if (next < 0 || nearest[next] <= nearest[current] + 1) continue;
                        nearest[next] = nearest[current] + 1;
                        queue.Enqueue(next);
                    }
                }
            }
            Add(new Vector2Int(data.StartX, data.StartY));
            int[] fromStart = (int[])nearest.Clone();
            var route = new List<Vector2Int>();
            int cursor = data.ExitY * data.Width + data.ExitX;
            while (fromStart[cursor] > 0 && fromStart[cursor] != int.MaxValue)
            {
                route.Add(new Vector2Int(cursor % data.Width, cursor / data.Width));
                foreach (MazeWall side in Sides)
                {
                    int next = Neighbor(cursor, side);
                    if (next >= 0 && fromStart[next] == fromStart[cursor] - 1)
                    { cursor = next; break; }
                }
            }
            route.Add(new Vector2Int(data.StartX, data.StartY));
            route.Reverse();
            var eligible = new HashSet<Vector2Int>(candidates);
            // Reserve reliable refuges on the actual escape route first. Eight
            // graph edges stay eight edges even when adjacent halls share a wall.
            int interval = Mathf.Max(8, Mathf.CeilToInt((route.Count - 1) / Mathf.Max(1f, budget - 1f)));
            for (int i = interval; i < route.Count && chosen.Count < budget; i += interval)
            {
                int selected = i;
                for (int offset = 0; offset <= 1; offset++)
                {
                    if (i - offset > 0 && eligible.Contains(route[i - offset]))
                    { selected = i - offset; break; }
                    if (i + offset < route.Count && eligible.Contains(route[i + offset]))
                    { selected = i + offset; break; }
                }
                Add(route[selected]);
            }
            Vector2Int last = route[route.Count - 1];
            if (chosen.Count < budget && nearest[last.y * data.Width + last.x] > interval / 2)
                Add(last);
            // Then cover side branches by graph distance from all existing lamps.
            while (chosen.Count < budget)
            {
                int bestIndex = -1;
                float bestScore = -1f;
                for (int i = 0; i < candidates.Count; i++)
                {
                    Vector2Int cell = candidates[i];
                    int distance = nearest[cell.y * data.Width + cell.x];
                    if (distance < 4 || distance == int.MaxValue) continue;
                    MazeCellKind kind = data.GetCellKind(cell.x, cell.y);
                    float score = distance + (kind == MazeCellKind.Landing || kind == MazeCellKind.Vestibule ? 0.35f : 0f);
                    if (score <= bestScore) continue;
                    bestIndex = i;
                    bestScore = score;
                }
                if (bestIndex < 0) break;
                Add(candidates[bestIndex]);
            }
            return chosen;
        }

        private void AddLamp(Vector3 center, float wallHeight, bool powered, int number)
        {
            GameObject prefab = powered ? poweredLampPrefab : unpoweredLampPrefab;
            if (prefab != null)
            {
                GameObject fixture = Instantiate(prefab, decorationRoot, false);
                fixture.name = powered ? "Powered entrance plafonnier " + number : "Unpowered entrance plafonnier";
                fixture.transform.localPosition = center + Vector3.up * wallHeight;
                fixture.transform.localRotation = Quaternion.Euler(0f, Mathf.Repeat(center.x * 31f + center.z * 17f, 360f), 0f);
            }
            else
            {
                trim.AddCylinder(center + Vector3.up * (wallHeight - 0.035f), 0.2f, 0.065f);
                (powered ? fixtures : metalDetails).AddDome(center + Vector3.up * (wallHeight - 0.07f), 0.175f, 0.1f);
                trim.AddBox(center + Vector3.up * (wallHeight - 0.17f), new Vector3(0.35f, 0.018f, 0.018f), Quaternion.identity);
                trim.AddBox(center + Vector3.up * (wallHeight - 0.17f), new Vector3(0.018f, 0.018f, 0.35f), Quaternion.identity);
            }
            if (!powered) return;
            Light light = CreateLight("Apartment entrance lamp " + number,
                center + Vector3.up * (wallHeight - 0.24f), Vector3.down);
            light.color = new Color(0.98f, 0.88f, 0.68f);
            light.intensity = lampIntensity;
            light.range = 4.2f;
            light.spotAngle = 90f;
            light.innerSpotAngle = 62f;
            light.gameObject.AddComponent<FPLab.Gameplay.Enemies.LampSafeZone>().Configure(light, 1.4f);
            var bounceObject = new GameObject("Small plafonnier ceiling spill " + number);
            bounceObject.transform.SetParent(decorationRoot, false);
            bounceObject.transform.localPosition = center + Vector3.up * (wallHeight - 0.23f);
            Light bounce = bounceObject.AddComponent<Light>();
            bounce.type = LightType.Point;
            bounce.color = light.color;
            bounce.intensity = 0.16f;
            bounce.range = 0.7f;
            bounce.shadows = LightShadows.None;
            bounce.renderMode = LightRenderMode.Auto;
            AddInsects(center + Vector3.up * (wallHeight - 0.28f), number);
        }

        private void AddInsects(Vector3 position, int number)
        {
            if (insectMaterial == null) return;
            var swarm = new GameObject("Small flies around lamp " + number);
            swarm.transform.SetParent(decorationRoot, false);
            swarm.transform.localPosition = position;
            ParticleSystem particles = swarm.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.useAutoRandomSeed = false;
            particles.randomSeed = unchecked((uint)(builder.GeneratedSeed ^ (number * 7919))) | 1u;
            ParticleSystem.MainModule main = particles.main;
            main.duration = 5f;
            main.loop = true;
            main.prewarm = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;
            main.useUnscaledTime = false;
            main.startLifetime = 5f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.055f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.009f, 0.015f);
            main.startColor = new Color(0.025f, 0.021f, 0.017f, 0.9f);
            main.maxParticles = 6;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 1f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.22f;
            shape.radiusThickness = 0.75f;
            shape.scale = new Vector3(1f, 0.7f, 1f);
            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalY = new ParticleSystem.MinMaxCurve(0.7f, 1.7f);
            velocity.orbitalX = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            velocity.orbitalZ = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.055f;
            noise.frequency = 1.7f;
            noise.scrollSpeed = 0.45f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            ParticleSystemRenderer renderer = swarm.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = insectMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.minParticleSize = 0f;
            renderer.maxParticleSize = 0.03f;
            if (Application.isPlaying) particles.Play();
        }

        private Light CreateLight(string objectName, Vector3 position, Vector3 direction)
        {
            var lamp = new GameObject(objectName);
            lamp.transform.SetParent(decorationRoot, false);
            lamp.transform.localPosition = position;
            lamp.transform.localRotation = Quaternion.LookRotation(direction,
                Mathf.Abs(direction.y) > 0.99f ? Vector3.forward : Vector3.up);
            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Spot;
            light.shadows = LightShadows.Soft;
            light.shadowBias = 0.025f;
            light.shadowNormalBias = 0.08f;
            light.shadowNearPlane = 0.05f;
            light.renderMode = LightRenderMode.Auto;
            // URP's resolution-tier setter is runtime-only; editor previews
            // receive the same serialized settings from environment setup.
            if (Application.isPlaying)
            {
                UniversalAdditionalLightData lightData = light.GetUniversalAdditionalLightData();
                lightData.additionalLightsShadowResolutionTier = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                lightData.usePipelineSettings = false;
            }
            return light;
        }

        private GameObject BuildSurface(string objectName, GeometryBatch geometry, Material material, bool castShadows = true,
            bool blendWallSurfaces = false)
        {
            if (geometry.VertexCount == 0 || material == null)
                return null;
            Mesh mesh = geometry.Build(objectName);
            ownedMeshes.Add(mesh);
            var surface = new GameObject(objectName);
            surface.transform.SetParent(decorationRoot, false);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = castShadows;
            if (blendWallSurfaces)
            {
                var properties = new MaterialPropertyBlock();
                properties.SetTexture("_WallStyleMap", wallStyleMap);
                properties.SetVector("_WallMapParams", new Vector4(
                    1f / (builder.Data.Width * builder.CellSize),
                    1f / (builder.Data.Height * builder.CellSize), 0.5f, 0.5f));
                properties.SetFloat("_WallCellSize", builder.CellSize);
                renderer.SetPropertyBlock(properties);
            }
            return surface;
        }

        private static void Shuffle<T>(List<T> values, int first, System.Random random)
        {
            for (int i = values.Count - 1; i > first; i--)
            {
                int next = random.Next(first, i + 1);
                T value = values[i];
                values[i] = values[next];
                values[next] = value;
            }
        }

        private readonly struct WallFace
        {
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector2Int Cell;
            public WallFace(Vector3 position, Quaternion rotation, Vector2Int cell)
            {
                Position = position;
                Rotation = rotation;
                Cell = cell;
            }
        }

        private void ReleaseMeshes()
        {
            foreach (Mesh mesh in ownedMeshes)
                if (mesh != null) DestroyOwned(mesh);
            ownedMeshes.Clear();
        }

        private void ReleaseWallStyleMap()
        {
            if (wallStyleMap != null) DestroyOwned(wallStyleMap);
            wallStyleMap = null;
        }

        private static void DestroyOwned(UnityEngine.Object target)
        {
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }

        private static void AddLocalBox(GeometryBatch batch, Vector3 origin, Quaternion rotation,
            Vector3 offset, Vector3 size)
        {
            batch.AddBox(origin + rotation * offset, size, rotation);
        }

        private static Vector3 CellCenter(int x, int y, MazeData data, float size)
        {
            return new Vector3((x - (data.Width - 1) * 0.5f) * size, 0f, (y - (data.Height - 1) * 0.5f) * size);
        }

        private static Vector3 Direction(MazeWall side)
        {
            switch (side)
            {
                case MazeWall.North: return Vector3.forward;
                case MazeWall.East: return Vector3.right;
                case MazeWall.South: return Vector3.back;
                case MazeWall.West: return Vector3.left;
                default: throw new ArgumentOutOfRangeException(nameof(side));
            }
        }

        // Material batches keep details cheap and never add collision to passages.
        private sealed class GeometryBatch
        {
            private readonly List<Vector3> vertices = new List<Vector3>();
            private readonly List<Vector3> normals = new List<Vector3>();
            private readonly List<Vector2> uv = new List<Vector2>();
            private readonly List<int> triangles = new List<int>();
            private readonly bool normalizedFront;
            private readonly bool worldAlignedUV;
            public int VertexCount => vertices.Count;

            public GeometryBatch(bool normalizedFront = false, bool worldAlignedUV = false)
            {
                this.normalizedFront = normalizedFront;
                this.worldAlignedUV = worldAlignedUV;
            }

            public void AddCylinder(Vector3 center, float radius, float height)
            {
                const int segments = 12;
                for (int i = 0; i < segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    float b = (i + 1) * Mathf.PI * 2f / segments;
                    Vector3 left = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 right = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                    int first = vertices.Count;
                    vertices.Add(center + left * radius - Vector3.up * height * 0.5f);
                    vertices.Add(center + left * radius + Vector3.up * height * 0.5f);
                    vertices.Add(center + right * radius + Vector3.up * height * 0.5f);
                    vertices.Add(center + right * radius - Vector3.up * height * 0.5f);
                    normals.Add(left); normals.Add(left); normals.Add(right); normals.Add(right);
                    uv.Add(Vector2.zero); uv.Add(new Vector2(0f, height));
                    uv.Add(new Vector2(radius * (b - a), height)); uv.Add(new Vector2(radius * (b - a), 0f));
                    AddTriangles(first);
                }
            }

            public void AddDome(Vector3 center, float radius, float depth)
            {
                const int rings = 4;
                const int segments = 12;
                int first = vertices.Count;
                for (int ring = 0; ring <= rings; ring++)
                {
                    float latitude = ring * Mathf.PI * 0.5f / rings;
                    float radial = Mathf.Cos(latitude);
                    for (int side = 0; side <= segments; side++)
                    {
                        float angle = side * Mathf.PI * 2f / segments;
                        Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                        vertices.Add(center + direction * (radial * radius) - Vector3.up * (Mathf.Sin(latitude) * depth));
                        normals.Add((direction * (radial / radius) - Vector3.up * (Mathf.Sin(latitude) / depth)).normalized);
                        uv.Add(new Vector2(0.5f + direction.x * radial * 0.5f, 0.5f + direction.z * radial * 0.5f));
                    }
                }
                for (int ring = 0; ring < rings; ring++)
                {
                    for (int side = 0; side < segments; side++)
                    {
                        int corner = first + ring * (segments + 1) + side;
                        triangles.Add(corner); triangles.Add(corner + 1); triangles.Add(corner + segments + 2);
                        triangles.Add(corner); triangles.Add(corner + segments + 2); triangles.Add(corner + segments + 1);
                    }
                }
            }

            public void AddBox(Vector3 position, Vector3 size, Quaternion rotation)
            {
                Vector3 x = rotation * Vector3.right;
                Vector3 y = rotation * Vector3.up;
                Vector3 z = rotation * Vector3.forward;
                Vector3 half = size * 0.5f;
                AddFace(position + z * half.z, x * half.x, y * half.y, z, normalizedFront ? 1f : size.x, normalizedFront ? 1f : size.y);
                AddFace(position - z * half.z, -x * half.x, y * half.y, -z, size.x, size.y);
                AddFace(position + x * half.x, -z * half.z, y * half.y, x, size.z, size.y);
                AddFace(position - x * half.x, z * half.z, y * half.y, -x, size.z, size.y);
                AddFace(position + y * half.y, x * half.x, -z * half.z, y, size.x, size.z);
                AddFace(position - y * half.y, x * half.x, z * half.z, -y, size.x, size.z);
            }

            private void AddFace(Vector3 center, Vector3 right, Vector3 up, Vector3 normal, float width, float height)
            {
                int first = vertices.Count;
                vertices.Add(center - right - up);
                vertices.Add(center + right - up);
                vertices.Add(center + right + up);
                vertices.Add(center - right + up);
                for (int i = 0; i < 4; i++) normals.Add(normal);
                if (worldAlignedUV)
                {
                    for (int i = first; i < first + 4; i++)
                    {
                        Vector3 position = vertices[i];
                        // Metric UVs stay continuous across edge/node joins;
                        // top/bottom faces use the same horizontal projection.
                        uv.Add(Mathf.Abs(normal.y) > 0.5f
                            ? new Vector2(position.x, position.z)
                            : new Vector2(Mathf.Abs(normal.x) > 0.5f ? position.z : position.x, position.y));
                    }
                }
                else
                {
                    uv.Add(Vector2.zero);
                    uv.Add(new Vector2(width, 0f));
                    uv.Add(new Vector2(width, height));
                    uv.Add(new Vector2(0f, height));
                }
                AddTriangles(first);
            }

            private void AddTriangles(int first)
            {
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
                triangles.Add(first);
                triangles.Add(first + 2);
                triangles.Add(first + 3);
            }

            public Mesh Build(string objectName)
            {
                var mesh = new Mesh { name = objectName, indexFormat = IndexFormat.UInt32 };
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
