using System;
using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Content.MapTools
{
    // ML-12a: the building stamp. A building is one stamp instance run as two plans in one MapEdit.Batch: Structure (walls,
    // the ground-floor door, floor plates and stairs), then Cutouts (the hole in each upper floor over the stair below, which
    // has to name the plate's id, so it is planned from the description after Structure ran).
    //   Floor f (0 = ground): ground height G = the highest height point of the footprint (the footprint may differ by at
    //   most LayeredNav.StepRaw). Plate f >= 1: flat, inside the wall ring, top G + f * storey. Walls of floor f: the four
    //   one-cell sides, f = 0 standing on floorRaw up to G + storey, f >= 1 raised from G + f * storey, storey high. The door
    //   (MapPrefabs.Door) replaces its side's ground-floor wall. Stair f (0 to Floors - 2): a slope plate from G + f * storey
    //   to G + (f + 1) * storey. Cutout f >= 1: the stair's cells in plate f. The top floor has walls and no roof.
    //   Source numbers (never renumber them: a re-run matches entries by src): generation * 100000 + floor * 1000 + part,
    //   parts below. A stair belongs to the floor it starts on and uses the generation of the floor it leads to. Parts 50
    //   to 99 are kept free for a later foundation plate and ramps; every part stays below FloorStep.
    public static class MapBuildings
    {
        public const ulong PartPlate = 1, PartWallSouth = 10, PartWallNorth = 11, PartWallWest = 12, PartWallEast = 13,
            PartDoorFirst = 20, PartDoorLintel = 21, PartDoorLast = 22, PartStair = 30, PartCutout = 40;
        public const ulong FloorStep = 1000, GenerationStep = 100000;

        public static ulong Src(int floor, int generation, ulong part) => (ulong)generation * GenerationStep + (ulong)floor * FloorStep + part;

        // The floor and part of a building source number.
        public static void Decode(ulong src, out int floor, out ulong part)
        {
            ulong rest = src % GenerationStep;
            floor = (int)(rest / FloorStep);
            part = rest % FloorStep;
        }

        // Ground height of the building: the highest height point of its footprint. ArgumentException when the building
        // leaves the map, its footprint is not flat within LayeredNav.StepRaw, or its top floor's walls pass the ceiling.
        public static long Ground(MapDesc map, MapBuilding b)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (b.Cell.X < 0 || b.Cell.Z < 0 || (long)b.Cell.X + b.Size.X > map.Size.X || (long)b.Cell.Z + b.Size.Z > map.Size.Z)
                throw new ArgumentException("the building leaves the map", nameof(b));
            Func<int, int, long> point = MapBake.Point(map);
            long high = long.MinValue, low = long.MaxValue;
            for (int j = b.Cell.Z; j <= b.Cell.Z + b.Size.Z; j++)
                for (int i = b.Cell.X; i <= b.Cell.X + b.Size.X; i++)
                {
                    long h = point(i, j);
                    if (h > high) high = h;
                    if (h < low) low = h;
                }
            if (high - low > LayeredNav.StepRaw) throw new ArgumentException("the ground under the building is not flat within 0.3 m", nameof(b));
            if (high + b.Floors * b.StoreyRaw > map.CeilingRaw) throw new ArgumentException("the top floor's walls pass the ceiling", nameof(b));
            return high;
        }

        // The first plan with the walkable slope of the description's layer (its terrain rules, else the height field's bound).
        public static MapStampPlan Structure(MapDesc map, string instance, MapBuilding b)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            TerrainRules rules = ContentCatalog.Find().Load(map.Pack).Terrain;
            return Structure(map, instance, b, rules?.MaxWalkableNum ?? HeightField.MaxSlopeNumerator,
                rules?.MaxWalkableDen ?? HeightField.MaxSlopeDenominator);
        }

        // The first plan. slopeNum / slopeDen: the layer's walkable slope. With two floors or more the stair's rise per
        // cell (storey / length) must not pass it nor 5/3 (the gate's plate slope rule), and with three or more not
        // storey - thick - 2 m. Walls take the description's first layer and kind "wall".
        public static MapStampPlan Structure(MapDesc map, string instance, MapBuilding b, long slopeNum, long slopeDen)
        {
            long ground = Ground(map, b);
            if (slopeDen <= 0 || slopeNum < 0) throw new ArgumentException("the walkable slope must be num >= 0 over den > 0", nameof(slopeDen));
            // The steepest cell of the stair rises ceil(storey / length) (the gate checks each cell, and the plate's points
            // are floored), so that rise per cell is what must not pass either slope bound.
            long rise = (b.StoreyRaw + b.StairLength - 1) / b.StairLength;
            if (b.Floors >= 2 && (rise * slopeDen > slopeNum * SimFix.Scale || rise * MapGate.PlateSlopeDen > MapGate.PlateSlopeNum * SimFix.Scale))
                throw new ArgumentException("the stair is too steep: make it longer or the storey lower", nameof(b));
            // Stair f + 1 stands over stair f on the same cells: under its lowest edge in a cell there must be 2 m above the
            // highest edge of the stair below, so the rise per cell must not pass storey - thick - WallSpan.ClearanceRaw.
            if (b.Floors >= 3 && rise > b.StoreyRaw - b.ThickRaw - WallSpan.ClearanceRaw)
                throw new ArgumentException("the stairs stand over each other with less than 2 m between them: make the stair longer", nameof(b));
            if (map.Layers.Count == 0) throw new ArgumentException("the description has no layer", nameof(map));
            string layer = map.Layers[0].Name;

            var walls = new List<MapStampEntry<MapWall>>();
            var plates = new List<MapStampEntry<MapPlate>>();
            for (int f = 0; f < b.Floors; f++)
            {
                int g = b.Generations[f];
                foreach (char side in new[] { 's', 'n', 'w', 'e' })
                {
                    Side(b, side, out MapXZ start, out bool alongX, out int length);
                    if (f == 0 && side == b.DoorSide)
                    {
                        MapStampPlan door = MapPrefabs.Door(map, instance, start, alongX, length, b.DoorAt, b.DoorWidth, ground + b.StoreyRaw, layer, "wall");
                        for (int k = 0; k < door.Walls.Count; k++)
                            walls.Add(new MapStampEntry<MapWall>(Src(0, g, PartDoorFirst + (ulong)k), door.Walls[k].Template));
                        continue;
                    }
                    MapXZ size = alongX ? new MapXZ(length, 1) : new MapXZ(1, length);
                    MapWall wall = f == 0
                        ? new MapWall(0, "wall", layer, start, size, ground + b.StoreyRaw - map.FloorRaw)
                        : new MapWall(0, "wall", layer, start, size, b.StoreyRaw, baseRaw: ground + f * b.StoreyRaw);
                    walls.Add(new MapStampEntry<MapWall>(Src(f, g, SidePart(side)), wall));
                }
                if (f >= 1)
                    plates.Add(new MapStampEntry<MapPlate>(Src(f, g, PartPlate), new MapPlate(0, new MapXZ(b.Cell.X + 1, b.Cell.Z + 1),
                        new MapXZ(b.Size.X - 2, b.Size.Z - 2), ground + f * b.StoreyRaw, null, b.ThickRaw)));
            }
            for (int f = 0; f + 1 < b.Floors; f++)
                plates.Add(new MapStampEntry<MapPlate>(Src(f, b.Generations[f + 1], PartStair), new MapPlate(0, b.StairCell, StairSize(b), null,
                    new MapSlope(b.StairAlongX ? "x" : "z", ground + f * b.StoreyRaw, ground + (f + 1) * b.StoreyRaw), b.ThickRaw)));
            return new MapStampPlan(instance, Array.Empty<MapHeightWrite>(), Array.Empty<MapSurfaceWrite>(), walls.ToArray(),
                Array.Empty<MapStampEntry<MapPoint>>(), Array.Empty<MapStampEntry<MapBase>>(), Array.Empty<MapStampEntry<MapUnit>>(),
                plates.ToArray(), Array.Empty<MapStampEntry<MapCutout>>());
        }

        // The second plan, from the description after Structure ran: in each upper floor's plate (found by its src) the
        // cells of the stair below. A floor whose plate is gone (removed by hand) gets no cutout.
        public static MapStampPlan Cutouts(MapDesc stamped, string instance, MapBuilding b)
        {
            if (stamped == null) throw new ArgumentNullException(nameof(stamped));
            if (b == null) throw new ArgumentNullException(nameof(b));
            var cutouts = new List<MapStampEntry<MapCutout>>();
            for (int f = 1; f < b.Floors; f++)
            {
                ulong want = Src(f, b.Generations[f], PartPlate);
                MapPlate plate = null;
                foreach (MapPlate p in stamped.Plates)
                    if (p.Src != null && p.Src.Instance == instance && p.Src.Src == want) { plate = p; break; }
                if (plate == null) continue;
                cutouts.Add(new MapStampEntry<MapCutout>(Src(f, b.Generations[f], PartCutout), new MapCutout(0, plate.Id, b.StairCell, StairSize(b))));
            }
            return new MapStampPlan(instance, Array.Empty<MapHeightWrite>(), Array.Empty<MapSurfaceWrite>(), Array.Empty<MapStampEntry<MapWall>>(),
                Array.Empty<MapStampEntry<MapPoint>>(), Array.Empty<MapStampEntry<MapBase>>(), Array.Empty<MapStampEntry<MapUnit>>(),
                Array.Empty<MapStampEntry<MapPlate>>(), cutouts.ToArray());
        }

        // The entries to remove when the building goes down to keep floors: every entry of the instance on floor keep or
        // above, and the stairs leading to them (stairs starting on floor keep - 1 or above). Hand-edited entries included.
        public static List<KeyValuePair<string, ulong>> Removal(MapDesc map, string instance, int keep)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (keep < 1) throw new ArgumentException("a building keeps at least one floor", nameof(keep));
            var found = new List<KeyValuePair<string, ulong>>();
            foreach (MapWall w in map.Walls) if (Above(w.Src, instance, keep)) found.Add(new KeyValuePair<string, ulong>(MapEditKinds.Wall.Name, w.Id));
            foreach (MapCutout c in map.Cutouts) if (Above(c.Src, instance, keep)) found.Add(new KeyValuePair<string, ulong>(MapEditKinds.Cutout.Name, c.Id));
            foreach (MapPlate p in map.Plates) if (Above(p.Src, instance, keep)) found.Add(new KeyValuePair<string, ulong>(MapEditKinds.Plate.Name, p.Id));
            return found;
        }

        // The building after going down from its floor count to keep floors: every removed floor gets the next generation.
        public static MapBuilding Lowered(MapBuilding b, int keep)
        {
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (keep < 1 || keep >= b.Floors) throw new ArgumentException("keep must be 1 to floors - 1", nameof(keep));
            int[] gens = b.Padded(b.Floors);
            for (int f = keep; f < b.Floors; f++)
            {
                if (gens[f] >= MapBuilding.MaxGeneration) throw new InvalidOperationException("floor " + f + " has been removed too often");
                gens[f]++;
            }
            return new MapBuilding(b.Cell, b.Size, keep, b.StoreyRaw, b.ThickRaw, b.DoorSide, b.DoorAt, b.DoorWidth, b.StairAlongX,
                b.StairCell, b.StairLength, gens);
        }

        private static bool Above(MapSrc src, string instance, int keep)
        {
            if (src == null || src.Instance != instance) return false;
            Decode(src.Src, out int floor, out ulong part);
            return part == PartStair ? floor >= keep - 1 : floor >= keep;
        }

        internal static MapXZ StairSize(MapBuilding b) => b.StairAlongX ? new MapXZ(b.StairLength, 1) : new MapXZ(1, b.StairLength);

        // The side's wall segment: south and north the full width at the first and last row, west and east between them.
        internal static void Side(MapBuilding b, char side, out MapXZ start, out bool alongX, out int length)
        {
            switch (side)
            {
                case 's': start = b.Cell; alongX = true; length = b.Size.X; return;
                case 'n': start = new MapXZ(b.Cell.X, b.Cell.Z + b.Size.Z - 1); alongX = true; length = b.Size.X; return;
                case 'w': start = new MapXZ(b.Cell.X, b.Cell.Z + 1); alongX = false; length = b.Size.Z - 2; return;
                case 'e': start = new MapXZ(b.Cell.X + b.Size.X - 1, b.Cell.Z + 1); alongX = false; length = b.Size.Z - 2; return;
                default: throw new ArgumentException("the side must be n, e, s or w", nameof(side));
            }
        }

        private static ulong SidePart(char side) =>
            side == 's' ? PartWallSouth : side == 'n' ? PartWallNorth : side == 'w' ? PartWallWest : PartWallEast;
    }
}
