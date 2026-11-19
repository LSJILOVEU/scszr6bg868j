using System;

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

        private static MapXZ Offset(MapXZ start, bool alongX, int cells) => alongX ? new MapXZ(start.X + cells, start.Z) : new MapXZ(start.X, start.Z + cells);
        private static MapXZ Size(bool alongX, int cells) => alongX ? new MapXZ(cells, 1) : new MapXZ(1, cells);

        // The gate's floor-wall rule: top at least 1 m above the highest height point under the footprint (none on a flat map).
        private static bool ClearsTerrain(MapDesc map, Func<int, int, long> point, MapXZ cell, MapXZ size, long topRaw) =>
            map.Heights == null || topRaw >= WallSpan.HighestPoint(point, cell.X, cell.Z, cell.X + size.X, cell.Z + size.Z) + WcMapValidation.WallClearanceRaw;
    }
}
