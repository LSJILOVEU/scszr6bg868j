using System;
using WarCore.Sim;

namespace WarCore.Content
{
    // M7 demo: 160 x 100 m flat map. Walls are whole-cell boxes, so the passability bitmap is their footprint union.
    public static class M7DemoMap
    {
        // PLACEHOLDER layout: x, z (min corner), width, depth in metres.
        // Six short covers; no enclosing wall or narrow doorway. Central lane is 30 m wide.
        private static readonly int[][] Boxes =
        {
            new[] { 48, 20, 1, 8 }, new[] { 48, 72, 1, 8 },
            new[] { 79, 25, 1, 10 }, new[] { 79, 65, 1, 10 },
            new[] { 111, 20, 1, 8 }, new[] { 111, 72, 1, 8 },
        };

        public static WcMap Build()
        {
            const int width = 160, depth = 100;
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
            return new WcMap("m7-demo", "none", default, SimVec3.Metres(width, 4, depth), SimFix.FromInt(width),
                default, width, depth, new[] { new WcMapLayer("walls", 1) }, walls, bits);
        }

        public static byte[] Bytes() => WcMapWriter.Write(Build());
    }
}
