using System;
using System.Collections.Generic;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.TerrainView
{
    // L8a fixtures, all written out here (no demo map or pack defaults): map F and its variants, its content pack, its session.
    internal static class L8aFixtures
    {
        internal const long S = SimFix.Scale;
        internal const ulong Seed = 41;
        internal const string PackId = "l8a-terrain";
        // ground walkable, mud not; walkable slope 3/4.
        internal const string PackJson = "{ \"id\": \"l8a-terrain\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"ground\" }, { \"id\": \"mud\", \"passable\": false } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 } }";
        internal static readonly string[] Names = { "ground", "mud" };

        internal static ContentPack Pack() =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { [PackId] = PackJson }, FileContentSource.Rwr()), PackId);

        internal static ContentPack RwrOnly() => ContentLoader.Load(FileContentSource.Rwr(), "rwr");

        // Map F: 8 x 6 cells, Min = (minX, 0, 0), Max = (minX + width, 4, 6), FlatHeight 0, ChunkSize = width.
        // Heights: raw(i, j) = ridge(i) + lift, ridge(4) = 98304 (1.5 m) and 0 elsewhere. One wall 3 m tall over cells
        // (wallX, 1) and (wallX, 2) (local centre (wallX + 0.5, 1.5, 2.0), half 0.5 x 1.0; wallX = 2 puts the centre at x = 2.5); file bits are its exact
        // complement. Surface table ["ground", "mud"] with mud only at (6, 5) (none when mud is false), when surfaces is true.
        internal static WcMap F(int width = 8, long minX = 0, bool heights = true, bool ridge = true, long lift = 0,
            int wallX = 1, bool surfaces = true, bool mud = true)
        {
            const int depth = 6;
            var wall = new WcMapWall(0, 1, "wall", "walls",
                new SimVec3(SimFix.FromRaw(wallX * S + S / 2), SimFix.FromRaw(3 * S / 2), SimFix.FromInt(2)),
                SimFix.Ratio(1, 2), SimFix.FromInt(3), SimFix.FromInt(1));
            var bits = new byte[(width * depth + 7) / 8];
            for (int z = 0; z < depth; z++)
                for (int x = 0; x < width; x++)
                {
                    bool blocked = x == wallX && (z == 1 || z == 2);
                    if (!blocked) { int k = z * width + x; bits[k >> 3] |= (byte)(1 << (k & 7)); }
                }
            int[] points = null;
            if (heights)
            {
                points = new int[(width + 1) * (depth + 1)];
                for (int j = 0; j <= depth; j++)
                    for (int i = 0; i <= width; i++) points[j * (width + 1) + i] = (int)((ridge && i == 4 ? 98304 : 0) + lift);
            }
            byte[] cells = null;
            if (surfaces)
            {
                cells = new byte[width * depth];
                if (mud) cells[5 * width + 6] = 1;
            }
            var min = new SimVec3(SimFix.FromRaw(minX * S), default, default);
            var max = new SimVec3(SimFix.FromRaw((minX + width) * S), SimFix.FromInt(4), SimFix.FromInt(depth));
            return new WcMap("l8a-f", "none", min, max, SimFix.FromInt(width), default, (uint)width, depth,
                new[] { new WcMapLayer("walls", 1) }, new[] { wall }, bits, points, surfaces ? Names : null, cells);
        }

        internal static byte[] Bytes(WcMap map) => WcMapWriter.Write(map);

        // One unit at (0.5, 0, 0.5) (cell (0,0), open), path mover, no tick run.
        internal static SimSession Session(WcMap map, ContentPack pack)
        {
            var recipe = new WorldRecipe(Seed, MoverKind.InterimPath, new[] { new UnitSpawn(1, 0, 1, 500, 500, 3000) },
                Array.Empty<BridgeCommand>()).WithTerrainRules(pack);
            return SimSession.Create(recipe, Bytes(map));
        }

        // The L7 hand fixture, written again here: 3 x 2 cells, Min = (-2, -1, 3), Max = (1, 1, 5), FlatHeight -1.
        internal static readonly int[] HandRaw =
        {
            0, -16384, -3, 0,
            -16384, 32768, 0, 0,
            -65536, 0, 16384, 32768,
        };

        internal static WcMap Hand()
        {
            const int w = 3, d = 2;
            var bits = new byte[1];
            for (int key = 0; key < w * d; key++) bits[key >> 3] |= (byte)(1 << (key & 7));
            SimFix floor = SimFix.FromInt(-1);
            return new WcMap("l8a-hand", "none", new SimVec3(SimFix.FromInt(-2), floor, SimFix.FromInt(3)),
                new SimVec3(SimFix.FromInt(1), SimFix.FromInt(1), SimFix.FromInt(5)), SimFix.FromInt(3), floor,
                w, d, new[] { new WcMapLayer("walls", 1) }, new WcMapWall[0], bits, HandRaw, null, null);
        }

        internal static HeightField Field(WcMap map)
        {
            var points = new int[map.HeightPoints.Count];
            for (int i = 0; i < points.Length; i++) points[i] = map.HeightPoints[i];
            return new HeightField((int)map.Width, (int)map.Depth, map.Min.X, map.Min.Z, points);
        }
    }
}
