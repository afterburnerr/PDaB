using System;

namespace FPLab.Gameplay.Maze
{
    [Flags]
    public enum MazeWall
    {
        North = 1,
        East = 2,
        South = 4,
        West = 8
    }

    public enum MazeCellKind
    {
        Solid,
        Corridor,
        Landing,
        Vestibule,
        ServiceAlcove
    }

    public readonly struct MazeCell
    {
        public MazeWall Walls { get; }
        public MazeCellKind Kind { get; }
        public int SectionId { get; }

        internal MazeCell(MazeWall walls, MazeCellKind kind = MazeCellKind.Solid, int sectionId = -1)
        {
            Walls = walls;
            Kind = kind;
            SectionId = sectionId;
        }

        public bool HasWall(MazeWall wall)
        {
            return (Walls & wall) == wall;
        }
    }
}
