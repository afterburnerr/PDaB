using System;
using System.Collections.Generic;

namespace FPLab.Gameplay.Maze
{
    public readonly struct MazeRoom
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public MazeCellKind Kind { get; }
        public int SectionId { get; }

        internal MazeRoom(int x, int y, int width, int height, MazeCellKind kind, int sectionId)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Kind = kind;
            SectionId = sectionId;
        }
    }

    // Coordinates increase east along X and north along Y. Exterior walls stay
    // closed in the data; the scene builder decides how to display entrances.
    public sealed class MazeData
    {
        private readonly MazeCell[] cells;
        private readonly List<MazeRoom> rooms = new List<MazeRoom>();

        public int Width { get; }
        public int Height { get; }
        public int StartX => 0;
        public int StartY => 0;
        public int ExitX { get; private set; }
        public int ExitY { get; private set; }
        public MazeWall ExitWall { get; private set; }
        public int ExitDistance { get; private set; }
        public int WalkableCellCount { get; private set; }
        public IReadOnlyList<MazeRoom> Rooms => rooms;

        public MazeCell this[int x, int y]
        {
            get
            {
                if (x < 0 || x >= Width)
                    throw new ArgumentOutOfRangeException(nameof(x));
                if (y < 0 || y >= Height)
                    throw new ArgumentOutOfRangeException(nameof(y));
                return cells[y * Width + x];
            }
        }

        public bool IsWalkable(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height && cells[y * Width + x].Kind != MazeCellKind.Solid;
        }

        public MazeCellKind GetCellKind(int x, int y)
        {
            return IsWalkable(x, y) ? cells[y * Width + x].Kind : MazeCellKind.Solid;
        }

        public int GetSectionId(int x, int y)
        {
            return IsWalkable(x, y) ? cells[y * Width + x].SectionId : -1;
        }

        internal MazeData(int width, int height)
        {
            Width = width;
            Height = height;
            cells = new MazeCell[width * height];
            MazeWall allWalls = MazeWall.North | MazeWall.East | MazeWall.South | MazeWall.West;
            for (int i = 0; i < cells.Length; i++)
                cells[i] = new MazeCell(allWalls);
        }

        internal void SetWalkable(int index, MazeCellKind kind, int sectionId)
        {
            if (cells[index].Kind == MazeCellKind.Solid)
                SetCell(index, new MazeCell(cells[index].Walls, kind, sectionId));
        }

        internal void SetCell(int index, MazeCell cell)
        {
            if (cells[index].Kind == MazeCellKind.Solid && cell.Kind != MazeCellKind.Solid)
                WalkableCellCount++;
            else if (cells[index].Kind != MazeCellKind.Solid && cell.Kind == MazeCellKind.Solid)
                WalkableCellCount--;
            cells[index] = cell;
        }

        internal void AddRoom(MazeRoom room) => rooms.Add(room);

        internal void RemoveWall(int index, MazeWall wall)
        {
            MazeCell cell = cells[index];
            cells[index] = new MazeCell(cell.Walls & ~wall, cell.Kind, cell.SectionId);
        }

        internal void SetExit(int x, int y, MazeWall wall, int distance)
        {
            ExitX = x;
            ExitY = y;
            ExitWall = wall;
            ExitDistance = distance;
        }
    }
}
