using System.Collections.Generic;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Tests
{
    // TRV-1a demo: a one-room house on a slope, entered by a ramp. 32 x 32 m, ground rising 6554 a cell along +x
    // (h = 6554 x, 10 %). The floor (plate 1) on cells x 12..18, z 13..18 sits at the highest ground point under it, h(19) =
    // 124526, solid down to 0.5 m under the lowest, h(12) = 78648. Walls 2.5 m high stand on it round the edge; the west
    // wall has a two-cell doorway at z 15..16, 0.70 m above the ground at its edge. The ramp (plate 2) runs west from the
    // doorway: one cell (to h(11)) rises 52432 > 49152 at 3/4, two cells (to h(10) = 65540) rise 29493 each, so it covers
    // x 10..11, z 15..16. A neutral point on the floor and one point of each team on the ground near its end of the map; each team's base, six
    // soldiers and eighteen reserves at that end (z 3, z 29).
    internal static class Trv1aDemo
    {
        internal const long M = SimFix.Scale;
        internal const long FloorTop = 124526;
        internal const ulong FloorPlate = 1;

        internal static long Height(int i) => 6554L * i;

        // The house without the ramp.
        internal static MapDesc House()
        {
            long b = FloorTop, h = 163840;
            var walls = new[]
            {
                Ml4Maps.Wall(1, 12, 13, 1, 2, h, b), Ml4Maps.Wall(2, 12, 17, 1, 2, h, b), Ml4Maps.Wall(3, 18, 13, 1, 6, h, b),
                Ml4Maps.Wall(4, 13, 13, 5, 1, h, b), Ml4Maps.Wall(5, 13, 18, 5, 1, h, b),
            };
            var plates = new[] { Ml4Maps.Flat(FloorPlate, 12, 13, 7, 6, FloorTop, FloorTop - Height(12) + M / 2) };
            var units = new List<MapUnit>();
            for (int i = 0; i < 24; i++)
                units.Add(new MapUnit((ulong)i + 1, "rwr.soldier-1", 1, 1, new MapMm(14000 + 1500 * (i % 3), 3000 + 1500 * (i / 3 % 2)), i >= 6));
            for (int i = 0; i < 24; i++)
                units.Add(new MapUnit((ulong)i + 25, "rwr.soldier-2", 2, 2, new MapMm(14000 + 1500 * (i % 3), 29000 - 1500 * (i / 3 % 2)), i >= 6));
            return new MapDesc("trv1a-ramp-demo", "none", "rwr-m6", new MapXZ(32, 32), new MapXZ(0, 0), 0, 8 * M,
                new[] { new MapLayer("walls", 1) }, Trv1aMaps.Heights(32, 32, (i, j) => Height(i)), null, plates, new MapCutout[0], walls,
                new[]
                {
                    new MapPoint(1, new MapMm(16000, 6000), 3000, 1, new MapMm(16000, 6000)),
                    new MapPoint(2, new MapMm(15500, 15500), 2500, 0, plate: FloorPlate),
                    new MapPoint(3, new MapMm(16000, 26000), 3000, 2, new MapMm(16000, 26000)),
                },
                new[] { new MapBase(1, 1, new MapMm(16000, 1500)), new MapBase(2, 2, new MapMm(16000, 30500)) },
                units.ToArray(), new MapRun(41, "teamIdle"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(6, 4, 3, 49, 2, 1));
        }

        internal static MapStampPlan RampPlan(MapDesc house) => MapPrefabs.Ramp(house, "ramp", new MapXZ(11, 15), 2, MapSide.MinusX, FloorTop, 3, 4);

        internal static MapDesc Desc() => Trv1aMaps.Stamp(House(), RampPlan(House()));

        internal static byte[] DescBytes() => MapDescJson.Write(Desc());
    }
}
