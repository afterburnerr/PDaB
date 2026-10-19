using UnityEngine;

namespace FPLab.Gameplay.Maze
{
    // Nearby walls share a finish across several cells. Jittered macroregions
    // avoid assigning an unrelated material to every individual wall segment.
    internal sealed class MazeWallSurfaceLayout
    {
        private const int RegionSize = 3;
        public int Width { get; }
        private int Height { get; }
        public int[] StyleGrid { get; }

        public MazeWallSurfaceLayout(int width, int height, int seed, int styleCount)
        {
            Width = width;
            Height = height;
            StyleGrid = new int[width * height];
            if (styleCount <= 1) return;

            var random = new System.Random(unchecked(seed ^ 0x57414C4C));
            int columns = Mathf.CeilToInt(width / (float)RegionSize);
            int rows = Mathf.CeilToInt(height / (float)RegionSize);
            var sites = new Vector2[columns * rows];
            int[] styles = RegionStyles(sites.Length, styleCount);
            for (int i = styles.Length - 1; i > 0; i--)
            {
                int other = random.Next(i + 1);
                int previous = styles[i];
                styles[i] = styles[other];
                styles[other] = previous;
            }

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    float spanX = Mathf.Min(RegionSize, width - x * RegionSize);
                    float spanY = Mathf.Min(RegionSize, height - y * RegionSize);
                    sites[y * columns + x] = new Vector2(
                        x * RegionSize + spanX * (0.25f + (float)random.NextDouble() * 0.5f),
                        y * RegionSize + spanY * (0.25f + (float)random.NextDouble() * 0.5f));
                }
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var center = new Vector2(x + 0.5f, y + 0.5f);
                    float distance = float.PositiveInfinity;
                    int closest = 0;
                    for (int i = 0; i < sites.Length; i++)
                    {
                        float candidate = (center - sites[i]).sqrMagnitude;
                        if (candidate >= distance) continue;
                        distance = candidate;
                        closest = i;
                    }
                    StyleGrid[y * width + x] = styles[closest];
                }
            }
        }

        public Vector2Int GetCell(Vector3 localPosition, float cellSize) => new Vector2Int(
            Mathf.Clamp(Mathf.FloorToInt(localPosition.x / cellSize + Width * 0.5f), 0, Width - 1),
            Mathf.Clamp(Mathf.FloorToInt(localPosition.z / cellSize + Height * 0.5f), 0, Height - 1));

        public Texture2D CreateStyleMap()
        {
            var map = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
            {
                name = "Maze wall finish weights",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };
            var weights = new Color32[StyleGrid.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                switch (StyleGrid[i])
                {
                    case 1: weights[i] = new Color32(0, 255, 0, 255); break;
                    case 2: weights[i] = new Color32(0, 0, 255, 255); break;
                    default: weights[i] = new Color32(255, 0, 0, 255); break;
                }
            }
            map.SetPixels32(weights);
            // Authoring setup can persist a tiny preview copy; gameplay needs
            // only the GPU data once the map has been uploaded.
            map.Apply(false, Application.isPlaying);
            return map;
        }

        private static int[] RegionStyles(int regionCount, int styleCount)
        {
            var result = new int[regionCount];
            int cursor = 0;
            // Three configured finishes use roughly 60% base, 23% aged paint,
            // and 17% tiles. Whole macroregions keep these variations legible.
            for (int style = 1; style < styleCount && cursor < regionCount; style++)
            {
                float share = styleCount == 3 ? (style == 1 ? 0.23f : 0.17f) : 0.4f / (styleCount - 1);
                int count = Mathf.Max(1, Mathf.RoundToInt(regionCount * share));
                int available = Mathf.Max(0, regionCount - cursor - 1);
                count = Mathf.Min(count, available);
                for (int i = 0; i < count; i++) result[cursor++] = style;
            }
            // Unassigned entries retain the building's original paint/plaster.
            return result;
        }
    }
}
