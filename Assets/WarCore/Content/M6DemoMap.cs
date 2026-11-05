using System;
using WarCore.Sim;

namespace WarCore.Content
{
    // M6 demo: 80 x 40 m flat map. Walls are whole-cell boxes, so the passability bitmap is their footprint union.
    public static class M6DemoMap
    {
        // x, z (min corner) and width, depth in metres.
        private static readonly int[][] Boxes =
        {
            new[] { 28, 0, 1, 16 }, new[] { 28, 24, 1, 16 },
            new[] { 51, 0, 1, 16 }, new[] { 51, 24, 1, 16 },
        };

        public static WcMap Build()
        {
            const int width = 80, depth = 40;
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
            return new WcMap("m6-demo", "none", default, SimVec3.Metres(width, 4, depth), SimFix.FromInt(width),
                default, width, depth, new[] { new WcMapLayer("walls", 1) }, walls, bits);
        }

        public static byte[] Bytes() => WcMapWriter.Write(Build());
    }
}
