using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content.MapTools
{
    // Editor prefabs: ready-made groups of entries for MapEdit.Stamp.
    public static class MapPrefabs
    {
        // A one-cell-thick wall with a doorway: left frame, lintel and right frame, stamp sources 1, 2 and 3.
        // start: first cell of the wall; alongX: the wall runs along +X (else +Z); length: cells in all; doorStart, doorWidth:
        // the doorway's first cell counted from start, and its width in cells (at least 1 frame cell on each side).
        // topRaw: absolute raw Y of the wall top. The frames stand on floorRaw (height topRaw - floorRaw, which must be even
        // and pass the 1 m terrain clearance). The lintel's underside is WallSpan.ClearanceRaw above the highest corner of the
        // doorway cells (1 raw higher when that makes its height odd), so the doorway stays walkable; its top is topRaw.
        public static MapStampPlan Door(MapDesc map, string instance, MapXZ start, bool alongX, int length, int doorStart, int doorWidth,
            long topRaw, string layer, string kind)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (doorWidth < 1) throw new ArgumentException("the doorway is narrower than one cell", nameof(doorWidth));
            if (doorStart < 1) throw new ArgumentException("the left frame is narrower than one cell", nameof(doorStart));
            if ((long)doorStart + doorWidth > (long)length - 1) throw new ArgumentException("the right frame is narrower than one cell", nameof(length));
            long endX = (long)start.X + (alongX ? length : 1), endZ = (long)start.Z + (alongX ? 1 : length);
            if (start.X < 0 || start.Z < 0 || endX > map.Size.X || endZ > map.Size.Z) throw new ArgumentException("the wall leaves the map", nameof(start));
            if (topRaw > map.CeilingRaw) throw new ArgumentException("the wall top is above the ceiling", nameof(topRaw));

            Func<int, int, long> point = MapBake.Point(map);
            MapXZ doorCell = Offset(start, alongX, doorStart);
            MapXZ doorSize = Size(alongX, doorWidth);
            long ground = WallSpan.HighestPoint(point, doorCell.X, doorCell.Z, doorCell.X + doorSize.X, doorCell.Z + doorSize.Z);
            long bottom = ground + WallSpan.ClearanceRaw;
            if (((topRaw - bottom) & 1) != 0) bottom++;
            if (topRaw < bottom + 2) throw new ArgumentException("the wall top is too low for a lintel above the doorway", nameof(topRaw));

            long frame = topRaw - map.FloorRaw;
            if (frame <= 0 || (frame & 1) != 0) throw new ArgumentException("the frame height topRaw - floorRaw must be even and positive", nameof(topRaw));
            MapXZ left = start, leftSize = Size(alongX, doorStart);
            MapXZ right = Offset(start, alongX, doorStart + doorWidth), rightSize = Size(alongX, length - doorStart - doorWidth);
            if (!ClearsTerrain(map, point, left, leftSize, topRaw) || !ClearsTerrain(map, point, right, rightSize, topRaw))
                throw new ArgumentException("a frame does not stand 1 m above the ground", nameof(topRaw));

            return new MapStampPlan(instance, Array.Empty<MapHeightWrite>(), Array.Empty<MapSurfaceWrite>(), new[]
            {
                new MapStampEntry<MapWall>(1, new MapWall(0, kind, layer, left, leftSize, frame)),
                new MapStampEntry<MapWall>(2, new MapWall(0, kind, layer, doorCell, doorSize, topRaw - bottom, baseRaw: bottom)),
                new MapStampEntry<MapWall>(3, new MapWall(0, kind, layer, right, rightSize, frame)),
            }, Array.Empty<MapStampEntry<MapPoint>>(), Array.Empty<MapStampEntry<MapBase>>(), Array.Empty<MapStampEntry<MapUnit>>());
        }

        // ML-3 window, ground level only (windows in upper floors wait for floor slabs, ML-4): a one-cell-thick wall with an
        // opening, stamp sources 1 to 4: left frame, sill, lintel, right frame. The floor of the opening (its "board") is the
        // highest corner of the window cells, as for the door. Sill top = floor + sillRaw, opening top = floor + openingRaw
        // (the lintel's underside; its top is topRaw). Frames stand on floorRaw like the door's.
        // Placeholders (a feel choice, change here only): sill 0.9 m, opening top 2.0 m. With muzzle 1.5 m, radius 0.25 m and
        // a 1 m sill the steepest shot out is (1.5 - 0.9) / 1.25 = 0.48 down and (2.0 - 1.5) / 1.25 = 0.4 up.
        // Gate (r = unit radius, E = eye, M = muzzle of the body): sill >= 2r (lower is a threshold, use a door);
        // sill <= min(E, M) - r (sight and level shots clear the sill); opening top >= max(E, M) + r. Each bound inclusive.
        // On a flat map (no height table) the sill is a floor wall; on terrain it is a raised wall whose bottom is the lowest
        // corner of the window cells (at least floorRaw + 1 raw), so it stands on the ground without the floor walls' 1 m rule.
        // A sill or lintel whose height would be odd grows 1 raw (sill top up, lintel underside up), and the gate reads the
        // heights after that.
        public const long DefaultSillRaw = 58982; // 0.9 m (SimFix.Ratio(9, 10))
        public const long DefaultOpeningRaw = 2 * SimFix.Scale; // 2.0 m

        public static MapStampPlan Window(MapDesc map, string instance, MapXZ start, bool alongX, int length, int windowStart, int windowWidth,
            long topRaw, string layer, string kind, CombatBody body, long sillRaw = DefaultSillRaw, long openingRaw = DefaultOpeningRaw)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (windowWidth < 1) throw new ArgumentException("the window is narrower than one cell", nameof(windowWidth));
            if (windowStart < 1) throw new ArgumentException("the left frame is narrower than one cell", nameof(windowStart));
            if ((long)windowStart + windowWidth > (long)length - 1) throw new ArgumentException("the right frame is narrower than one cell", nameof(length));
            long endX = (long)start.X + (alongX ? length : 1), endZ = (long)start.Z + (alongX ? 1 : length);
            if (start.X < 0 || start.Z < 0 || endX > map.Size.X || endZ > map.Size.Z) throw new ArgumentException("the wall leaves the map", nameof(start));
            if (topRaw > map.CeilingRaw) throw new ArgumentException("the wall top is above the ceiling", nameof(topRaw));

            Func<int, int, long> point = MapBake.Point(map);
            MapXZ cell = Offset(start, alongX, windowStart);
            MapXZ size = Size(alongX, windowWidth);
            long board = WallSpan.HighestPoint(point, cell.X, cell.Z, cell.X + size.X, cell.Z + size.Z);
            long lowest = LowestPoint(point, cell.X, cell.Z, cell.X + size.X, cell.Z + size.Z);

            long sillBottom = map.Heights == null ? map.FloorRaw : Math.Max(lowest, map.FloorRaw + 1);
            long sillTop = board + sillRaw;
            if (((sillTop - sillBottom) & 1) != 0) sillTop++;
            long openingTop = board + openingRaw;
            if (((topRaw - openingTop) & 1) != 0) openingTop++;

            long r = CombatSection.UnitRadiusRaw, eye = body.Eye.Raw, muzzle = body.Muzzle.Raw;
            long sill = sillTop - board, opening = openingTop - board;
            if (sill < 2 * r) throw new ArgumentException("the sill is lower than 2 x the unit radius; use a door", nameof(sillRaw));
            if (sill > Math.Min(eye, muzzle) - r) throw new ArgumentException("the sill is higher than min(eye, muzzle) - unit radius", nameof(sillRaw));
            if (opening < Math.Max(eye, muzzle) + r) throw new ArgumentException("the opening top is lower than max(eye, muzzle) + unit radius", nameof(openingRaw));
            if (topRaw < openingTop + 2) throw new ArgumentException("the wall top is too low for a lintel above the window", nameof(topRaw));

            long frame = topRaw - map.FloorRaw;
            if (frame <= 0 || (frame & 1) != 0) throw new ArgumentException("the frame height topRaw - floorRaw must be even and positive", nameof(topRaw));
            MapXZ left = start, leftSize = Size(alongX, windowStart);
            MapXZ right = Offset(start, alongX, windowStart + windowWidth), rightSize = Size(alongX, length - windowStart - windowWidth);
            if (!ClearsTerrain(map, point, left, leftSize, topRaw) || !ClearsTerrain(map, point, right, rightSize, topRaw))
                throw new ArgumentException("a frame does not stand 1 m above the ground", nameof(topRaw));

            MapWall sillWall = map.Heights == null
                ? new MapWall(0, kind, layer, cell, size, sillTop - map.FloorRaw)
                : new MapWall(0, kind, layer, cell, size, sillTop - sillBottom, baseRaw: sillBottom);
            return new MapStampPlan(instance, Array.Empty<MapHeightWrite>(), Array.Empty<MapSurfaceWrite>(), new[]
            {
                new MapStampEntry<MapWall>(1, new MapWall(0, kind, layer, left, leftSize, frame)),
                new MapStampEntry<MapWall>(2, sillWall),
                new MapStampEntry<MapWall>(3, new MapWall(0, kind, layer, cell, size, topRaw - openingTop, baseRaw: openingTop)),
                new MapStampEntry<MapWall>(4, new MapWall(0, kind, layer, right, rightSize, frame)),
            }, Array.Empty<MapStampEntry<MapPoint>>(), Array.Empty<MapStampEntry<MapBase>>(), Array.Empty<MapStampEntry<MapUnit>>());
        }

        // ML-12a: Door on a floor plate. As Door, but every part is a raised wall standing on floorTopRaw (the plate's top,
        // absolute raw Y): frames floorTopRaw to topRaw, the lintel's underside WallSpan.ClearanceRaw above floorTopRaw (1 raw
        // higher when that makes its height odd). Stamp sources 1, 2 and 3. No terrain rule: the walls do not touch the ground.
        public static MapStampPlan DoorOn(MapDesc map, string instance, MapXZ start, bool alongX, int length, int doorStart, int doorWidth,
            long floorTopRaw, long topRaw, string layer, string kind)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (doorWidth < 1) throw new ArgumentException("the doorway is narrower than one cell", nameof(doorWidth));
            if (doorStart < 1) throw new ArgumentException("the left frame is narrower than one cell", nameof(doorStart));
            if ((long)doorStart + doorWidth > (long)length - 1) throw new ArgumentException("the right frame is narrower than one cell", nameof(length));
            long endX = (long)start.X + (alongX ? length : 1), endZ = (long)start.Z + (alongX ? 1 : length);
            if (start.X < 0 || start.Z < 0 || endX > map.Size.X || endZ > map.Size.Z) throw new ArgumentException("the wall leaves the map", nameof(start));
            if (topRaw > map.CeilingRaw) throw new ArgumentException("the wall top is above the ceiling", nameof(topRaw));
            long frame = topRaw - floorTopRaw;
            if (frame <= 0 || (frame & 1) != 0) throw new ArgumentException("the frame height topRaw - floorTopRaw must be even and positive", nameof(topRaw));
            long bottom = floorTopRaw + WallSpan.ClearanceRaw;
            if (((topRaw - bottom) & 1) != 0) bottom++;
            if (topRaw < bottom + 2) throw new ArgumentException("the wall top is too low for a lintel above the doorway", nameof(topRaw));
            MapXZ doorCell = Offset(start, alongX, doorStart), doorSize = Size(alongX, doorWidth);
            MapXZ left = start, leftSize = Size(alongX, doorStart);
            MapXZ right = Offset(start, alongX, doorStart + doorWidth), rightSize = Size(alongX, length - doorStart - doorWidth);
            return new MapStampPlan(instance, Array.Empty<MapHeightWrite>(), Array.Empty<MapSurfaceWrite>(), new[]
            {
                new MapStampEntry<MapWall>(1, new MapWall(0, kind, layer, left, leftSize, frame, baseRaw: floorTopRaw)),
                new MapStampEntry<MapWall>(2, new MapWall(0, kind, layer, doorCell, doorSize, topRaw - bottom, baseRaw: bottom)),
                new MapStampEntry<MapWall>(3, new MapWall(0, kind, layer, right, rightSize, frame, baseRaw: floorTopRaw)),
            }, Array.Empty<MapStampEntry<MapPoint>>(), Array.Empty<MapStampEntry<MapBase>>(), Array.Empty<MapStampEntry<MapUnit>>());
        }

        // ML-12a: Window on a floor plate. As Window with the board at floorTopRaw, and every part a raised wall: frames and
        // sill stand on floorTopRaw, the lintel on the opening top. Same gate against the body, same odd-height rule, stamp
        // sources 1 to 4. No terrain rule.
        public static MapStampPlan WindowOn(MapDesc map, string instance, MapXZ start, bool alongX, int length, int windowStart, int windowWidth,
            long floorTopRaw, long topRaw, string layer, string kind, CombatBody body, long sillRaw = DefaultSillRaw, long openingRaw = DefaultOpeningRaw)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (windowWidth < 1) throw new ArgumentException("the window is narrower than one cell", nameof(windowWidth));
            if (windowStart < 1) throw new ArgumentException("the left frame is narrower than one cell", nameof(windowStart));
            if ((long)windowStart + windowWidth > (long)length - 1) throw new ArgumentException("the right frame is narrower than one cell", nameof(length));
            long endX = (long)start.X + (alongX ? length : 1), endZ = (long)start.Z + (alongX ? 1 : length);
            if (start.X < 0 || start.Z < 0 || endX > map.Size.X || endZ > map.Size.Z) throw new ArgumentException("the wall leaves the map", nameof(start));
            if (topRaw > map.CeilingRaw) throw new ArgumentException("the wall top is above the ceiling", nameof(topRaw));

            MapXZ cell = Offset(start, alongX, windowStart), size = Size(alongX, windowWidth);
            long sillTop = floorTopRaw + sillRaw;
            if (((sillTop - floorTopRaw) & 1) != 0) sillTop++;
            long openingTop = floorTopRaw + openingRaw;
            if (((topRaw - openingTop) & 1) != 0) openingTop++;

            long r = CombatSection.UnitRadiusRaw, eye = body.Eye.Raw, muzzle = body.Muzzle.Raw;
            long sill = sillTop - floorTopRaw, opening = openingTop - floorTopRaw;
            if (sill < 2 * r) throw new ArgumentException("the sill is lower than 2 x the unit radius; use a door", nameof(sillRaw));
            if (sill > Math.Min(eye, muzzle) - r) throw new ArgumentException("the sill is higher than min(eye, muzzle) - unit radius", nameof(sillRaw));
            if (opening < Math.Max(eye, muzzle) + r) throw new ArgumentException("the opening top is lower than max(eye, muzzle) + unit radius", nameof(openingRaw));
            if (topRaw < openingTop + 2) throw new ArgumentException("the wall top is too low for a lintel above the window", nameof(topRaw));
            long frame = topRaw - floorTopRaw;
            if (frame <= 0 || (frame & 1) != 0) throw new ArgumentException("the frame height topRaw - floorTopRaw must be even and positive", nameof(topRaw));

            MapXZ left = start, leftSize = Size(alongX, windowStart);
            MapXZ right = Offset(start, alongX, windowStart + windowWidth), rightSize = Size(alongX, length - windowStart - windowWidth);
            return new MapStampPlan(instance, Array.Empty<MapHeightWrite>(), Array.Empty<MapSurfaceWrite>(), new[]
            {
                new MapStampEntry<MapWall>(1, new MapWall(0, kind, layer, left, leftSize, frame, baseRaw: floorTopRaw)),
                new MapStampEntry<MapWall>(2, new MapWall(0, kind, layer, cell, size, sill, baseRaw: floorTopRaw)),
                new MapStampEntry<MapWall>(3, new MapWall(0, kind, layer, cell, size, topRaw - openingTop, baseRaw: openingTop)),
                new MapStampEntry<MapWall>(4, new MapWall(0, kind, layer, right, rightSize, frame, baseRaw: floorTopRaw)),
            }, Array.Empty<MapStampEntry<MapPoint>>(), Array.Empty<MapStampEntry<MapBase>>(), Array.Empty<MapStampEntry<MapUnit>>());
        }

        private static long LowestPoint(Func<int, int, long> point, int x0, int z0, int x1, int z1)
        {
            long lowest = long.MaxValue;
            for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++) lowest = Math.Min(lowest, point(ix, iz));
            return lowest;
        }

        private static MapXZ Offset(MapXZ start, bool alongX, int cells) => alongX ? new MapXZ(start.X + cells, start.Z) : new MapXZ(start.X, start.Z + cells);
        private static MapXZ Size(bool alongX, int cells) => alongX ? new MapXZ(cells, 1) : new MapXZ(1, cells);

        // The gate's floor-wall rule: top at least 1 m above the highest height point under the footprint (none on a flat map).
        private static bool ClearsTerrain(MapDesc map, Func<int, int, long> point, MapXZ cell, MapXZ size, long topRaw) =>
            map.Heights == null || topRaw >= WallSpan.HighestPoint(point, cell.X, cell.Z, cell.X + size.X, cell.Z + size.Z) + WcMapValidation.WallClearanceRaw;
    }
}
