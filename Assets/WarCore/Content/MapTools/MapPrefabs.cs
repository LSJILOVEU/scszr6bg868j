using System;
using System.Collections.Generic;
using System.Linq;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content.MapTools
{
    // The side a ramp runs to from its doorway.
    public enum MapSide : byte { PlusX = 0, MinusX = 1, PlusZ = 2, MinusZ = 3 }

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

            long r = body.Radius.Raw, eye = body.Eye.Raw, muzzle = body.Muzzle.Raw;
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

        // TRV-1a: a ramp from a doorway to the ground outside it, one sloped plate (stamp source part, default 1).
        // first: the cell of the first row outside the doorway with the smallest x and z; width: cells across; outward: the side
        // the ramp runs to, away from the doorway; doorTopRaw: absolute raw Y of the floor at the doorway edge; slopeNum /
        // slopeDen: the pack's walkable slope, used as min(pack, 5/3) (MapGate's plate slope rule).
        // Length L is the first of 1..MaxRampCells with den * ceil(drop / L) <= num * 65536, drop = doorTopRaw - low(L) and
        // low(L) the highest ground point on the ramp's far edge L cells out: the plate's point table splits the drop by floor
        // division, so its steepest cell rises ceil(drop / L), and that cell passes HeightField.CellWithinSlope exactly then.
        // The plate falls from doorTopRaw at the doorway to low(L) at the far edge, which therefore meets every ground point
        // there within LayeredNav.StepRaw (the far edge's points may differ by at most that much). Thickness is
        // max(MinRampThickRaw, doorTopRaw - the lowest ground point under the ramp), so the underside is never above the
        // ground. Every top point must be at or above the ground point under it (plate rule 3), and the ramp must keep the
        // stack clearance from every plate of the description (plate rule 4) except the one this stamp source already placed
        // (stamping it again replaces that plate). The far edge must lie inside the map, so a row of ground cells is outside
        // the foot to walk on from. Walls, the solid cell budget and the other plate rules are left to MapGate.Check.
        public const int MaxRampCells = 16;
        public const long MinRampThickRaw = SimFix.Scale / 2;

        public static MapStampPlan Ramp(MapDesc map, string instance, MapXZ first, int width, MapSide outward, long doorTopRaw,
            long slopeNum, long slopeDen, ulong part = 1)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (width < 1) throw new ArgumentException("the ramp is narrower than one cell", nameof(width));
            if (slopeDen < 1 || slopeDen > HeightField.MaxWalkableDenominator || slopeNum < 0 || slopeNum > HeightField.MaxSlopeNumerator * slopeDen)
                throw new ArgumentOutOfRangeException(nameof(slopeNum), "the walkable slope is out of range");
            if (outward > MapSide.MinusZ) throw new ArgumentOutOfRangeException(nameof(outward));
            if (doorTopRaw > map.CeilingRaw) throw new ArgumentException("the doorway is above the ceiling", nameof(doorTopRaw));
            if (doorTopRaw > MapDescReader.HeightLimit) throw new ArgumentOutOfRangeException(nameof(doorTopRaw), "the doorway is above the description's height limit");
            long num = slopeNum, den = slopeDen;
            if (num * MapGate.PlateSlopeDen > MapGate.PlateSlopeNum * den) { num = MapGate.PlateSlopeNum; den = MapGate.PlateSlopeDen; }

            bool alongX = outward == MapSide.PlusX || outward == MapSide.MinusX;
            int sign = outward == MapSide.PlusX || outward == MapSide.PlusZ ? 1 : -1;
            int sizeAlong = alongX ? map.Size.X : map.Size.Z, sizeAcross = alongX ? map.Size.Z : map.Size.X;
            int across0 = alongX ? first.Z : first.X, row = alongX ? first.X : first.Z;
            if (across0 < 0 || (long)across0 + width > sizeAcross || row < 0 || row >= sizeAlong)
                throw new ArgumentException("the ramp leaves the map", nameof(first));
            int door = sign > 0 ? row : row + 1;   // the doorway's grid line along the ramp
            Func<int, int, long> ground = MapBake.Point(map);
            long At(int along, int acrossAt) => alongX ? ground(along, acrossAt) : ground(acrossAt, along);
            long Low(int cells, out long lowest)
            {
                long high = long.MinValue;
                lowest = long.MaxValue;
                for (int c = across0; c <= across0 + width; c++)
                {
                    long g = At(door + sign * cells, c);
                    high = Math.Max(high, g);
                    lowest = Math.Min(lowest, g);
                }
                return high;
            }

            int length = 0;
            long low = 0, farLowest = 0;
            for (int cells = 1; cells <= MaxRampCells && length == 0; cells++)
            {
                long far = (long)door + sign * cells;
                // far starts one line beyond the doorway line (0..sizeAlong) and moves by one: it meets an edge before leaving.
                if (far <= 0 || far >= sizeAlong) throw new ArgumentException("the ramp's foot reaches the map edge: no ground outside it", nameof(first));
                long high = Low(cells, out long lowest);
                if (cells == 1 && doorTopRaw <= high)
                    throw new ArgumentException("the ground outside is as high as the doorway: no drop to ramp down (flatten it)", nameof(doorTopRaw));
                long drop = doorTopRaw - high;
                if (den * CeilDiv(drop, cells) <= num * SimFix.Scale) { length = cells; low = high; farLowest = lowest; }
            }
            if (length == 0) throw new ArgumentException("the ramp needs more than " + MaxRampCells + " cells", nameof(doorTopRaw));
            if (low >= doorTopRaw) throw new ArgumentException("the ground rises back to the doorway before the ramp ends (flatten it)", nameof(doorTopRaw));
            if (low - farLowest > LayeredNav.StepRaw) throw new ArgumentException("the ground at the ramp's foot differs too much from end to end (flatten it)", nameof(first));

            int along0 = sign > 0 ? door : door - length;
            long from = sign > 0 ? doorTopRaw : low, to = sign > 0 ? low : doorTopRaw;
            long lowestGround = long.MaxValue;
            for (int a = 0; a <= length; a++)
                for (int c = across0; c <= across0 + width; c++)
                {
                    long g = At(along0 + a, c);
                    lowestGround = Math.Min(lowestGround, g);
                    if (PlateSpan.TopPoint(PlateAxis.X, from, to, length, 1, a, 0) < g)
                        throw new ArgumentException("the ramp would dip below the ground (plates must not be buried)", nameof(first));
                }
            long thick = Math.Max(MinRampThickRaw, doorTopRaw - lowestGround);
            if (thick > MapDescReader.HeightLimit) throw new ArgumentException("the ramp would be thicker than the description's height limit", nameof(doorTopRaw));

            MapXZ cell = alongX ? new MapXZ(along0, across0) : new MapXZ(across0, along0);
            MapXZ size = alongX ? new MapXZ(length, width) : new MapXZ(width, length);
            var plate = new MapPlate(0, cell, size, null, new MapSlope(alongX ? "x" : "z", from, to), thick);
            ulong close = TooClose(map, plate, instance, part);
            if (close != 0) throw new ArgumentException("the ramp is closer to plate " + close + " than the stack clearance", nameof(first));
            return new MapStampPlan(instance, Array.Empty<MapHeightWrite>(), Array.Empty<MapSurfaceWrite>(), Array.Empty<MapStampEntry<MapWall>>(),
                Array.Empty<MapStampEntry<MapPoint>>(), Array.Empty<MapStampEntry<MapBase>>(), Array.Empty<MapStampEntry<MapUnit>>(),
                new[] { new MapStampEntry<MapPlate>(part, plate) }, Array.Empty<MapStampEntry<MapCutout>>());
        }

        // Ceiling of a / b for b > 0 (a may be negative or zero).
        private static long CeilDiv(long a, long b) => -PlateSpan.FloorDiv(-a, b);

        // The id of the first plate of the description that breaks plate rule 4 with the new plate, or 0. The plate stamp source
        // (instance, part) placed before is left out: a stamp replaces it.
        private static ulong TooClose(MapDesc map, MapPlate plate, string instance, ulong part)
        {
            var own = new HashSet<ulong>(map.Plates.Where(p => p.Src != null && p.Src.Instance == instance
                && p.Src.Kind == MapEditKinds.Plate.Name && p.Src.Src == part).Select(p => p.Id));
            List<PlateGeom> geoms = MapBake.PlateGeoms(map);
            geoms.RemoveAll(g => own.Contains(g.Id));
            int ramp = geoms.Count;
            long floor = map.FloorRaw;
            geoms.Add(new PlateGeom(ulong.MaxValue, plate.Cell.X, plate.Cell.Z, plate.Size.X, plate.Size.Z,
                plate.Slope.Axis == "x" ? PlateAxis.X : PlateAxis.Z, plate.Slope.FromRaw - floor, plate.Slope.ToRaw - floor, plate.ThickRaw,
                Enumerable.Repeat(true, plate.Size.X * plate.Size.Z).ToArray()));
            foreach (PlateSpan.Conflict c in PlateSpan.Stacks(map.Size.X, map.Size.Z, geoms, out _, out _))
                if (c.A == ramp || c.B == ramp) return geoms[c.A == ramp ? c.B : c.A].Id;
            return 0;
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

            long r = body.Radius.Raw, eye = body.Eye.Raw, muzzle = body.Muzzle.Raw;
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
