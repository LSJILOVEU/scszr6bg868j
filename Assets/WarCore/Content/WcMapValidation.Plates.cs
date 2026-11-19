using System;
using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Content
{
    // Section 12 (plates) of a version 3 file. Order: fields (this file's table), then the height table, then PLATE_SLOPE and
    // the ground headroom inside the passability check, then the wall rules, then PLATE_BURIED, PLATE_STACK (with the
    // per-cell CAPACITY), WALL_PLATE and the walkable masks (PLATE_NAV_MISMATCH). Plate heights are absolute like height
    // points; the rules work in local raw Y (minus Min.Y) like the walls.
    internal static partial class WcMapValidation
    {
        private static List<PlateGeom> PlateFields(WcMap map, int cells)
        {
            Require(map.Plates.Count <= PlateSpan.MaxPlates, "CAPACITY");
            var plates = new List<PlateGeom>(map.Plates.Count);
            ulong last = 0;
            long solidCells = 0;
            foreach (WcMapPlate p in map.Plates)
            {
                Require(p.Id > last, "PLATE_ORDER");
                last = p.Id;
                bool flat = p.Axis == PlateAxis.Flat;
                Require(p.Width >= 1 && p.Depth >= 1 && p.X >= 0 && p.Z >= 0 && (long)p.X + p.Width <= map.Width
                    && (long)p.Z + p.Depth <= map.Depth && (byte)p.Axis <= 2 && flat == (p.FromRaw == p.ToRaw)
                    && p.ThickRaw >= WcMapPlate.MinThickRaw && p.ThickRaw < HeightField.HeightLimitRaw, "PLATE_GEOMETRY");
                foreach (long h in new long[] { p.FromRaw, p.ToRaw })
                    Require(h > -HeightField.HeightLimitRaw && h < HeightField.HeightLimitRaw && h >= map.Min.Y.Raw && h <= map.Max.Y.Raw, "BOUNDS");
                Require(p.Surface == WcMapPlate.NoSurface || (map.HasSurfaceTable && p.Surface < map.SurfaceNames.Count), "PLATE_SURFACE");
                int count = p.CellCount;
                Require(p.Solid.Count == p.MaskBytes && p.Walkable.Count == p.MaskBytes, "BITMAP_LENGTH");
                for (int i = count; i < p.MaskBytes * 8; i++)
                    Require((p.Solid[i >> 3] & (1 << (i & 7))) == 0 && (p.Walkable[i >> 3] & (1 << (i & 7))) == 0, "BITMAP_LENGTH");
                bool[] solid = PlateSpan.Bits(p.Solid, count), walk = PlateSpan.Bits(p.Walkable, count);
                for (int i = 0; i < count; i++)
                {
                    Require(!walk[i] || solid[i], "PLATE_NAV_MISMATCH");
                    if (solid[i]) solidCells++;
                }
                plates.Add(new PlateGeom(p.Id, p.X, p.Z, p.Width, p.Depth, p.Axis, p.FromRaw - map.Min.Y.Raw, p.ToRaw - map.Min.Y.Raw,
                    p.ThickRaw, solid));
            }
            Require(solidCells <= cells, "CAPACITY");
            return plates;
        }

        // PLATE_SLOPE (2/1, as HEIGHT_SLOPE), then rule 1 into the ground's blocked cells.
        private static void PlateHeadroom(WcMap map, List<PlateGeom> plates, bool[] blocked)
        {
            for (int n = 0; n < plates.Count; n++)
            {
                HeightField top = map.Plates[n].TopField(map.Min.X, map.Min.Z);
                Require(!PlateSpan.Steep(top, plates[n], HeightField.MaxSlopeNumerator, HeightField.MaxSlopeDenominator, out _, out _), "PLATE_SLOPE");
            }
            bool[] headroom = PlateSpan.GroundBlocks((int)map.Width, (int)map.Depth, LocalPoint(map), plates);
            for (int i = 0; i < headroom.Length; i++)
                if (headroom[i]) blocked[i] = true;
        }

        private static void PlateRules(WcMap map, List<PlateGeom> plates)
        {
            Func<int, int, long> point = LocalPoint(map);
            foreach (PlateGeom p in plates) Require(PlateSpan.AboveGround(p, point, out _, out _), "PLATE_BURIED");
            List<PlateSpan.Conflict> stacks = PlateSpan.Stacks((int)map.Width, (int)map.Depth, plates, out int crowdedX, out _);
            Require(crowdedX < 0, "CAPACITY");
            Require(stacks.Count == 0, "PLATE_STACK");
            List<WallGeom> walls = Walls(map);
            Require(PlateSpan.WallsThrough(walls, plates).Count == 0, "WALL_PLATE");
            for (int n = 0; n < plates.Count; n++)
            {
                bool[] walk = PlateSpan.Walkable(plates[n], walls);
                bool[] stored = PlateSpan.Bits(map.Plates[n].Walkable, walk.Length);
                for (int i = 0; i < walk.Length; i++) Require(walk[i] == stored[i], "PLATE_NAV_MISMATCH");
            }
        }

        // Every wall of a validated map as the plate rules see it (local raw Y).
        internal static List<WallGeom> Walls(WcMap map)
        {
            var walls = new List<WallGeom>(map.Walls.Count);
            foreach (WcMapWall wall in map.Walls)
            {
                Footprint(wall, out int x0, out int z0, out int x1, out int z1);
                long bottom = wall.LocalCenter.Y.Raw - wall.Height.Raw / 2;
                walls.Add(new WallGeom(wall.Placement, x0, z0, x1, z1, bottom, bottom + wall.Height.Raw));
            }
            return walls;
        }
    }
}
