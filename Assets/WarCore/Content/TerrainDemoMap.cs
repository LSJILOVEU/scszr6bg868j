using System;
using WarCore.Sim;

namespace WarCore.Content
{
    // WP-22 demo terrain, built in plain C#: a 48 x 32 m map with a ridge across the middle, a road along z in [14,18),
    // a mud patch, a cliff band (legal slope, but steeper than PackJson's walkable limit) and a plateau the cliffs cut off.
    // Heights are in half-metre units H(i,j); the wall stands on flat ground (D11: walls are relative to Min.Y).
    //   base(i) : 0 for i<=10, i-10 up to i=22 (6 m), 34-i down to i=34, 0 beyond
    //   extra   : +2 units (1 m) for j>=23 and 14<=i<=30
    // Surface table (ordinal order): grass, mud, road. Anchors are not part of a .wcmap.
    public static class TerrainDemoMap
    {
        public const int Width = 48, Depth = 32;
        public const long Unit = SimFix.Scale / 2;           // raw size of one height unit (0.5 m)
        public const string PackId = "rwr-terrain-demo";
        // A layer that extends the shipped "rwr" pack with terrain rules.
        public const string PackJson = "{ \"id\": \"rwr-terrain-demo\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"grass\" }, { \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }, { \"id\": \"road\", \"speedMilli\": 1250 } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 } }";

        // Spawns on the road at both ends; control points: the crest (hand-computed Y) and the mud patch.
        public static readonly SimVec3[] Spawns =
        {
            new SimVec3(HalfMetre(3), SimFix.FromRaw(0), HalfMetre(16)),
            new SimVec3(HalfMetre(44), SimFix.FromRaw(0), HalfMetre(16)),
        };
        // Crest cell (22,16): h00 = 12 units, h10 = 11 units; at u = v = S/2 the sample is 12*Unit - Unit/2 = 376832 raw (5.75 m).
        public static readonly SimVec3[] ControlPoints =
        {
            new SimVec3(HalfMetre(22), SimFix.FromRaw(376832), HalfMetre(16)),
            new SimVec3(HalfMetre(38), SimFix.FromRaw(0), HalfMetre(8)),
        };

        private static SimFix HalfMetre(int cell) => SimFix.FromRaw(cell * SimFix.Scale + SimFix.Scale / 2);

        public static int HeightUnits(int i, int j)
        {
            int baseUnits = i <= 10 ? 0 : i <= 22 ? i - 10 : i <= 34 ? 34 - i : 0;
            return baseUnits + (j >= 23 && i >= 14 && i <= 30 ? 2 : 0);
        }

        public static string[] SurfaceNames => new[] { "grass", "mud", "road" };

        public static byte SurfaceOf(int x, int z) => (byte)(z >= 14 && z < 18 ? 2 : x >= 36 && x < 40 && z >= 4 && z < 12 ? 1 : 0);

        public static WcMap Build()
        {
            var heights = new int[(Width + 1) * (Depth + 1)];
            for (int j = 0; j <= Depth; j++)
                for (int i = 0; i <= Width; i++) heights[j * (Width + 1) + i] = (int)(HeightUnits(i, j) * Unit);
            var surfaces = new byte[Width * Depth];
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++) surfaces[z * Width + x] = SurfaceOf(x, z);
            // One wall, x in [6,7), z in [8,13), 3 m tall, on flat ground.
            var wall = new WcMapWall(0, 1, "wall", "walls",
                new SimVec3(SimFix.Ratio(13, 2), SimFix.Ratio(3, 2), SimFix.Ratio(21, 2)),
                SimFix.Ratio(1, 2), SimFix.FromInt(3), SimFix.Ratio(5, 2));
            var bits = new byte[(Width * Depth + 7) / 8];
            for (int key = 0; key < Width * Depth; key++)
            {
                int x = key % Width, z = key / Width;
                bool blocked = x == 6 && z >= 8 && z < 13;
                if (!blocked) bits[key >> 3] |= (byte)(1 << (key & 7));
            }
            return new WcMap("terrain-demo", "rwr", default, SimVec3.Metres(Width, 10, Depth), SimFix.FromInt(Width), default,
                Width, Depth, new[] { new WcMapLayer("walls", 1) }, new[] { wall }, bits, heights, SurfaceNames, surfaces);
        }
    }
}
