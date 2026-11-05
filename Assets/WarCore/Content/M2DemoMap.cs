using System;
using WarCore.Sim;

namespace WarCore.Content
{
    // M2b demo: 64 x 32 m flat map. Walls are whole-cell boxes, so the passability bitmap is their footprint union.
    public static class M2DemoMap
    {
        // x, z (min corner) and width, depth in metres.
        private static readonly int[][] Boxes =
        {
            new[] { 24, 0, 2, 24 },   // long vertical wall, blocks the lane of unit 1
            new[] { 26, 10, 8, 2 },   // L shape: two boxes that overlap on [32,34) x [10,12)
            new[] { 32, 4, 2, 8 },
            new[] { 40, 2, 4, 4 },    // lone block
        };

        public static WcMap Build()
        {
            const int width = 64, depth = 32;
            var walls = new WcMapWall[Boxes.Length];
            var blocked = new bool[width * depth];
            for (int i = 0; i < Boxes.Length; i++)
            {
                int[] b = Boxes[i];
                SimFix halfX = SimFix.Ratio(b[2], 2), halfZ = SimFix.Ratio(b[3], 2);
                walls[i] = new WcMapWall(0, (ulong)(i + 1), "wall", "walls",
                    new SimVec3(SimFix.FromInt(b[0]) + halfX, SimFix.FromInt(1), SimFix.FromInt(b[1]) + halfZ),
                    halfX, SimFix.FromInt(2), halfZ);
                for (int z = b[1]; z < b[1] + b[3]; z++)
                    for (int x = b[0]; x < b[0] + b[2]; x++)
                        blocked[z * width + x] = true;
            }
            var bits = new byte[(width * depth + 7) / 8];
            for (int index = 0; index < blocked.Length; index++)
                if (!blocked[index])
                    bits[index >> 3] |= (byte)(1 << (index & 7));
            return new WcMap("m2b-demo", "none", default, SimVec3.Metres(width, 4, depth), SimFix.FromInt(width),
                default, width, depth, new[] { new WcMapLayer("walls", 1) }, walls, bits);
        }

        public static byte[] Bytes() => WcMapWriter.Write(Build());
    }
}
