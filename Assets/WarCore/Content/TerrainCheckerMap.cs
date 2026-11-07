using System;
using WarCore.Sim;

namespace WarCore.Content
{
    // A side x side checker height field for the terrain view: no walls, every cell passable, no surface table; point
    // heights alternate raw(i, j) = 32768 * ((i + j) mod 2) - 16384 (+-0.25 m; every triangle has slope sqrt(0.5) < 2).
    // Min = (0, -0.25, 0), Max = (side, 1, side), FlatHeight = Min.Y = -0.25 m (every point is >= Min.Y), ChunkSize = side.
    public static class TerrainCheckerMap
    {
        public static int RawAt(int i, int j) => 32768 * ((i + j) % 2) - 16384;

        public static WcMap Build(int side)
        {
            if (side < 1 || side > HeightField.MaxSide) throw new ArgumentOutOfRangeException(nameof(side));
            var heights = new int[(side + 1) * (side + 1)];
            for (int j = 0; j <= side; j++)
                for (int i = 0; i <= side; i++) heights[j * (side + 1) + i] = RawAt(i, j);
            int cells = side * side;
            var bits = new byte[(cells + 7) / 8];
            for (int key = 0; key < cells; key++) bits[key >> 3] |= (byte)(1 << (key & 7));
            SimFix floor = SimFix.FromRaw(-16384);
            return new WcMap("checker-" + side, "none", new SimVec3(default, floor, default), SimVec3.Metres(side, 1, side),
                SimFix.FromInt(side), floor, (uint)side, (uint)side, new[] { new WcMapLayer("walls", 1) }, new WcMapWall[0],
                bits, heights, null, null);
        }

        public static byte[] Bytes(int side) => WcMapWriter.Write(Build(side));
    }
}
