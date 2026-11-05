using System;
using WarCore.Sim;

namespace WarCore.Content
{
    // Map + rules -> TerrainTables. The map file's passability bitmap only says where walls are; slope and surface
    // blocking come from the pack's rules, so one map file can run under different packs.
    public static class TerrainDerivation
    {
        public static TerrainTables Derive(WcMap map, ContentPack pack)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            return Derive(map, pack.Terrain, pack.Id);
        }

        public static TerrainTables Derive(WcMap map, TerrainRules rules, string packId)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            int width = (int)map.Width, depth = (int)map.Depth, count = width * depth;
            HeightField ground = null;
            if (map.HasHeightField)
            {
                var points = new int[map.HeightPoints.Count];
                for (int i = 0; i < points.Length; i++) points[i] = map.HeightPoints[i];
                ground = new HeightField(width, depth, map.Min.X, map.Min.Z, points);
            }
            int slopeNum = rules?.MaxWalkableNum ?? HeightField.MaxSlopeNumerator, slopeDen = rules?.MaxWalkableDen ?? HeightField.MaxSlopeDenominator;

            // Resolve each name in the map's table to the pack's definition once.
            SurfaceDef[] byIndex = new SurfaceDef[map.SurfaceNames.Count];
            for (int i = 0; i < byIndex.Length; i++)
            {
                if (rules == null)
                    throw new ContentException("content pack '" + packId + "' has no terrain section, but map '" + map.MapId + "' names surface '" + map.SurfaceNames[i] + "' (surface table entry " + i + ")");
                if (!rules.TryFind(map.SurfaceNames[i], out byIndex[i]))
                    throw new ContentException("content pack '" + packId + "' terrain surfaces has no entry '" + map.SurfaceNames[i] + "' named by map '" + map.MapId + "' (surface table entry " + i + ")");
            }

            var bits = new byte[(count + 7) / 8];
            var cost = new byte[count];
            var speed = new ushort[count];
            var defaultDef = new SurfaceDef("default", true, 1000, 1);
            for (int key = 0; key < count; key++)
            {
                int x = key % width, z = key / width;
                SurfaceDef def = map.HasSurfaceTable ? byIndex[map.SurfaceCells[key]] : defaultDef;
                bool walkable = map.IsPassable(x, z) && def.Passable
                    && (ground == null || ground.CellWithinSlope(x, z, slopeNum, slopeDen));
                if (!walkable) continue;
                bits[key >> 3] |= (byte)(1 << (key & 7));
                cost[key] = (byte)def.Cost;
                speed[key] = (ushort)def.SpeedMilli;
            }
            var nav = new NavGrid(width, depth, map.Min.X, map.Min.Z, bits);
            string[] names = new string[map.SurfaceNames.Count];
            for (int i = 0; i < names.Length; i++) names[i] = map.SurfaceNames[i];
            byte[] cells = new byte[map.HasSurfaceTable ? count : 0];
            for (int i = 0; i < cells.Length; i++) cells[i] = map.SurfaceCells[i];
            return new TerrainTables(ground, nav, cost, speed, names, cells, rules == null ? Array.Empty<byte>() : rules.Canonical());
        }
    }
}
