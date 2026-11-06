using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WarCore.Sim;

namespace WarCore.Content
{
    internal static class WcMapValidation
    {
        internal const int MaxFileBytes = 2 * 1024 * 1024;
        internal const int MaxWalls = 4096;
        internal const int MaxSide = HeightField.MaxSide; // alias: one source for the map side limit
        internal static void Require(bool condition, string code)
        {
            if (!condition) throw new InvalidDataException("WCMAP_" + code);
        }

        internal static byte[] TextBytes(string value)
        {
            Require(!string.IsNullOrEmpty(value), "STRING");
            byte[] bytes;
            try { bytes = new UTF8Encoding(false, true).GetBytes(value); }
            catch (EncoderFallbackException) { throw new InvalidDataException("WCMAP_UTF8"); }
            Require(bytes.Length <= 255 && value.IndexOf('\0') < 0, "STRING");
            return bytes;
        }

        internal static void Validate(WcMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            TextBytes(map.MapId); TextBytes(map.ContentId);
            const long limit = 16384 * SimFix.Scale;
            foreach (long coordinate in new[] { map.Min.X.Raw, map.Min.Y.Raw, map.Min.Z.Raw,
                map.Max.X.Raw, map.Max.Y.Raw, map.Max.Z.Raw })
                Require(coordinate >= -limit && coordinate <= limit, "BOUNDS");
            Require(map.Width > 0 && map.Width <= MaxSide && map.Depth > 0 && map.Depth <= MaxSide, "CAPACITY");
            Require(map.Max.X.Raw - map.Min.X.Raw == map.Width * SimFix.Scale
                && map.Max.Z.Raw - map.Min.Z.Raw == map.Depth * SimFix.Scale
                && map.Min.Y < map.Max.Y && map.FlatHeight == map.Min.Y
                && map.ChunkSize.Raw == Math.Max(map.Width, map.Depth) * SimFix.Scale, "BOUNDS");
            Require(map.Min.X.Raw % SimFix.Scale == 0 && map.Min.Z.Raw % SimFix.Scale == 0, "GRID");
            Require(map.Layers.Count > 0 && map.Layers.Count <= 32, "LAYERS");
            var names = new HashSet<string>(StringComparer.Ordinal);
            uint bits = 0;
            foreach (WcMapLayer layer in map.Layers)
            {
                TextBytes(layer.Name);
                Require(layer.Bit < 32 && names.Add(layer.Name) && (bits & layer.Mask) == 0, "LAYERS");
                bits |= layer.Mask;
            }
            Require(map.Walls.Count <= MaxWalls, "CAPACITY");
            var keys = new HashSet<ulong>();
            int cells = checked((int)(map.Width * map.Depth));
            var blocked = new bool[cells];
            foreach (WcMapWall wall in map.Walls)
            {
                Require(wall.Chunk == 0, "REFERENCE");
                Require(keys.Add(wall.Placement), "DUPLICATE_KEY");
                TextBytes(wall.KindId); TextBytes(wall.LayerName);
                Require(names.Contains(wall.LayerName), "REFERENCE");
                long hx = wall.HalfX.Raw, hz = wall.HalfZ.Raw, h = wall.Height.Raw;
                long x = wall.LocalCenter.X.Raw, y = wall.LocalCenter.Y.Raw, z = wall.LocalCenter.Z.Raw;
                Require(hx > 0 && hx <= MaxSide * SimFix.Scale / 2 && hz > 0 && hz <= MaxSide * SimFix.Scale / 2
                    && h > 0 && h <= map.Max.Y.Raw - map.Min.Y.Raw && h % 2 == 0, "WALL_GEOMETRY");
                Require(x >= hx && x <= map.Width * SimFix.Scale - hx
                    && z >= hz && z <= map.Depth * SimFix.Scale - hz && y == h / 2, "BOUNDS");
                Require((x - hx) % SimFix.Scale == 0 && (x + hx) % SimFix.Scale == 0
                    && (z - hz) % SimFix.Scale == 0 && (z + hz) % SimFix.Scale == 0, "GRID");
                int x0 = (int)((x - hx) / SimFix.Scale), x1 = (int)((x + hx) / SimFix.Scale);
                int z0 = (int)((z - hz) / SimFix.Scale), z1 = (int)((z + hz) / SimFix.Scale);
                for (int iz = z0; iz < z1; iz++)
                    for (int ix = x0; ix < x1; ix++) blocked[iz * (int)map.Width + ix] = true;
            }
            Require(map.Passability.Count == (cells + 7) / 8, "BITMAP_LENGTH");
            for (int i = 0; i < map.Passability.Count * 8; i++)
            {
                bool passable = (map.Passability[i >> 3] & (1 << (i & 7))) != 0;
                Require(i < cells ? passable == !blocked[i] : !passable, "NAV_WALL_MISMATCH");
            }
            ValidateHeightField(map);
            ValidateWallsOnTerrain(map);
            ValidateSurfaceTable(map, cells);
        }

        // A wall's bottom is always Min.Y (BOUNDS: centre Y = Height/2) and every height point is >= Min.Y, so the wall is
        // never buried below the ground. It must also stand at least 1 m above the highest point under its footprint:
        // the extremes of the piecewise-linear surface are at grid points, inclusive of both far edges of the footprint.
        internal const long WallClearanceRaw = SimFix.Scale;
        private static void ValidateWallsOnTerrain(WcMap map)
        {
            if (!map.HasHeightField) return;
            foreach (WcMapWall wall in map.Walls)
            {
                long x = wall.LocalCenter.X.Raw, z = wall.LocalCenter.Z.Raw, hx = wall.HalfX.Raw, hz = wall.HalfZ.Raw;
                int x0 = (int)((x - hx) / SimFix.Scale), x1 = (int)((x + hx) / SimFix.Scale);
                int z0 = (int)((z - hz) / SimFix.Scale), z1 = (int)((z + hz) / SimFix.Scale);
                long highest = long.MinValue;
                for (int iz = z0; iz <= z1; iz++)
                    for (int ix = x0; ix <= x1; ix++)
                        highest = Math.Max(highest, map.VertexRaw(ix, iz));
                Require(map.FlatHeight.Raw + wall.Height.Raw >= highest + WallClearanceRaw, "WALL_TERRAIN");
            }
        }

        internal const int MaxSurfaceNames = 64;

        private static void ValidateHeightField(WcMap map)
        {
            if (!map.HasHeightField) return;
            Require(map.HeightPoints.Count == ((int)map.Width + 1) * ((int)map.Depth + 1), "HEIGHT_SIZE");
            var points = new int[map.HeightPoints.Count];
            for (int i = 0; i < points.Length; i++)
            {
                int h = map.HeightPoints[i];
                Require(h > -HeightField.HeightLimitRaw && h < HeightField.HeightLimitRaw
                    && h >= map.Min.Y.Raw && h <= map.Max.Y.Raw, "HEIGHT_RANGE");
                points[i] = h;
            }
            var field = new HeightField((int)map.Width, (int)map.Depth, map.Min.X, map.Min.Z, points);
            for (int z = 0; z < map.Depth; z++)
                for (int x = 0; x < map.Width; x++)
                    Require(field.CellIsLegal(x, z), "HEIGHT_SLOPE");
        }

        private static void ValidateSurfaceTable(WcMap map, int cells)
        {
            if (!map.HasSurfaceTable) return;
            Require(map.SurfaceNames.Count >= 1 && map.SurfaceNames.Count <= MaxSurfaceNames, "SURFACE_COUNT");
            for (int i = 0; i < map.SurfaceNames.Count; i++)
            {
                TextBytes(map.SurfaceNames[i]);
                Require(i == 0 || StringComparer.Ordinal.Compare(map.SurfaceNames[i - 1], map.SurfaceNames[i]) < 0, "SURFACE_ORDER");
            }
            Require(map.SurfaceCells.Count == cells, "SURFACE_LENGTH");
            foreach (byte index in map.SurfaceCells) Require(index < map.SurfaceNames.Count, "SURFACE_INDEX");
        }
    }
}
