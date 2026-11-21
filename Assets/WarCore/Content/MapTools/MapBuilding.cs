using System;
using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Content.MapTools
{
    // ML-12a: the parameters of one generated building (a plain value; MapBuildings turns it into stamp plans).
    //   Cell, Size: the south-west cell and the size in cells, outer walls included (at least 4 x 4).
    //   Floors: 1 to MaxFloors, the ground floor included. StoreyRaw: floor-to-floor height (even, at least 2.5 m, and
    //   StoreyRaw - ThickRaw at least WallSpan.ClearanceRaw so the floor below keeps its headroom). ThickRaw: plate
    //   thickness (at least 0.5 m).
    //   Door: the ground-floor doorway on side DoorSide ('n', 'e', 's' or 'w'), DoorAt cells from the start of that side's
    //   wall segment (south and north run the full width from the west; west and east run between them from the south),
    //   DoorWidth cells wide, with at least one frame cell on each side.
    //   Stair: one cell wide, StairLength cells along x (StairAlongX) or z from StairCell, the same cells on every floor; the
    //   cell before its foot and the cell past its top lie inside the walls.
    //   Generations: one number per floor index ever used (at least Floors entries, at most MaxFloors), starting at 0; a
    //   floor removed by MapBuildings.Removal gets the next generation so a later re-add is not stopped by its tombstones.
    public sealed class MapBuilding
    {
        public const int MaxFloors = 32, MinSide = 4;
        public const long MinStoreyRaw = 5 * SimFix.Scale / 2, MinThickRaw = SimFix.Scale / 2, MaxGeneration = 9999;

        public MapXZ Cell { get; }
        public MapXZ Size { get; }
        public int Floors { get; }
        public long StoreyRaw { get; }
        public long ThickRaw { get; }
        public char DoorSide { get; }
        public int DoorAt { get; }
        public int DoorWidth { get; }
        public bool StairAlongX { get; }
        public MapXZ StairCell { get; }
        public int StairLength { get; }
        public IReadOnlyList<int> Generations { get; }

        public MapBuilding(MapXZ cell, MapXZ size, int floors, long storeyRaw, long thickRaw, char doorSide, int doorAt, int doorWidth,
            bool stairAlongX, MapXZ stairCell, int stairLength, int[] generations = null)
        {
            if (size.X < MinSide || size.Z < MinSide) throw new ArgumentException("the building is smaller than 4 x 4 cells", nameof(size));
            if (floors < 1 || floors > MaxFloors) throw new ArgumentException("floors must be 1 to " + MaxFloors, nameof(floors));
            if (storeyRaw < MinStoreyRaw || (storeyRaw & 1) != 0) throw new ArgumentException("the storey must be even and at least 2.5 m", nameof(storeyRaw));
            if (thickRaw < MinThickRaw) throw new ArgumentException("the plate is thinner than 0.5 m", nameof(thickRaw));
            if (storeyRaw - thickRaw < WallSpan.ClearanceRaw)
                throw new ArgumentException("storey minus plate thickness leaves less than 2 m of headroom", nameof(storeyRaw));
            int sideLength = SideLength(doorSide, size);
            if (doorWidth < 1) throw new ArgumentException("the doorway is narrower than one cell", nameof(doorWidth));
            if (doorAt < 1) throw new ArgumentException("the door's first frame is narrower than one cell", nameof(doorAt));
            if ((long)doorAt + doorWidth > (long)sideLength - 1) throw new ArgumentException("the door's last frame is narrower than one cell", nameof(doorWidth));
            if (stairLength < 1) throw new ArgumentException("the stair is shorter than one cell", nameof(stairLength));
            long footX = stairCell.X - (stairAlongX ? 1L : 0L), footZ = stairCell.Z - (stairAlongX ? 0L : 1L);
            long endX = (long)stairCell.X + (stairAlongX ? stairLength + 1L : 1L), endZ = (long)stairCell.Z + (stairAlongX ? 1L : stairLength + 1L);
            if (footX < (long)cell.X + 1 || footZ < (long)cell.Z + 1 || endX > (long)cell.X + size.X - 1 || endZ > (long)cell.Z + size.Z - 1)
                throw new ArgumentException("the stair with the cell before its foot and past its top does not fit inside the walls", nameof(stairCell));
            int[] gens = generations == null ? new int[floors] : (int[])generations.Clone();
            if (gens.Length < floors || gens.Length > MaxFloors) throw new ArgumentException("one generation per floor is needed", nameof(generations));
            foreach (int g in gens)
                if (g < 0 || g > MaxGeneration) throw new ArgumentException("a generation must be 0 to " + MaxGeneration, nameof(generations));
            Cell = cell; Size = size; Floors = floors; StoreyRaw = storeyRaw; ThickRaw = thickRaw; DoorSide = doorSide; DoorAt = doorAt;
            DoorWidth = doorWidth; StairAlongX = stairAlongX; StairCell = stairCell; StairLength = stairLength;
            Generations = Array.AsReadOnly(gens);
        }

        // The same building with another floor count (generations kept, padded with 0).
        public MapBuilding WithFloors(int floors) => new MapBuilding(Cell, Size, floors, StoreyRaw, ThickRaw, DoorSide, DoorAt, DoorWidth,
            StairAlongX, StairCell, StairLength, Padded(floors));

        // The same building with these generations.
        public MapBuilding WithGenerations(int[] generations) => new MapBuilding(Cell, Size, Floors, StoreyRaw, ThickRaw, DoorSide, DoorAt,
            DoorWidth, StairAlongX, StairCell, StairLength, generations);

        internal int[] Padded(int floors)
        {
            var gens = new int[Math.Max(floors, Generations.Count)];
            for (int f = 0; f < Generations.Count; f++) gens[f] = Generations[f];
            return gens;
        }

        // Cells of the side's wall segment: south and north the full width, west and east between them.
        internal static int SideLength(char side, MapXZ size)
        {
            switch (side)
            {
                case 's': case 'n': return size.X;
                case 'w': case 'e': return size.Z - 2;
                default: throw new ArgumentException("the door side must be n, e, s or w", nameof(side));
            }
        }
    }
}
