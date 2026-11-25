using System.Collections.Generic;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-11a fixture: a two-storey house between two teams; the point upstairs can be walked to by the stair only. The tests
    // of floors, stairs and the AI on them are built on it. The house stage of the showcase is a map file of its own
    // (Assets/WarCore/Maps/showcase-house.map.json), free to change without these tests.
    // 40 x 20 m flat ground at 0, 1 m cells.
    //   House: walls round cells x 14..25, z 5..14, 6 m tall, with a 2-cell doorway in the west wall (z 11..12, before the
    //     stair's foot) and in the east wall (z 6..7); no line from one team's start runs through both. Above 3 m the north and south walls have two 1-cell openings each
    //     (windows without glass): nobody walks through, lines pass.
    //     Yard walls run from the house to the north and south edges, so the only way across the map is through the house.
    //   Stair (layer 1): cells x 17..20, z 11..12, rising 0 -> 3 m along x. Its foot faces the ground cells (16, 11..12) inside
    //     the house; its top faces the first floor's cells (21, 11..12).
    //   First floor (layer 2): flat at 3 m (0.5 m thick) over the whole inside, cells x 15..24, z 6..13, with a cutout over
    //     the stair. Its bottom, 2.5 m, leaves the ground under it walkable.
    //   Points: 1 on the ground west of the house (team 1), 2 on the first floor (team 2), 3 on the ground east (team 2).
    //   Units: eight soldiers of team 1 in the west; of team 2 five in the east and three on the first floor.
    internal static class PlateAssaultMap
    {
        private const long M = SimFix.Scale;
        public const int UpstairsPoint = 1;   // index in the match's points

        public static MapDesc Desc()
        {
            var walls = new List<MapWall>();
            void Add(int x, int z, int w, int d, long height, long? from = null) =>
                walls.Add(new MapWall((ulong)walls.Count + 1, "wall", "walls", new MapXZ(x, z), new MapXZ(w, d), height, baseRaw: from));
            // Ground storey: north, south, and the west and east walls beside their doorways (west z 11..12, east z 6..7).
            Add(14, 5, 12, 1, 3 * M); Add(14, 14, 12, 1, 3 * M);
            Add(14, 6, 1, 5, 3 * M); Add(14, 13, 1, 1, 3 * M); Add(25, 8, 1, 6, 3 * M);
            // Upper storey, 3..6 m: closed to the west and east, openings at x = 18 and 21 in the north and south walls.
            foreach (int z in new[] { 5, 14 }) { Add(14, z, 4, 1, 3 * M, 3 * M); Add(19, z, 2, 1, 3 * M, 3 * M); Add(22, z, 4, 1, 3 * M, 3 * M); }
            Add(14, 6, 1, 8, 3 * M, 3 * M); Add(25, 6, 1, 8, 3 * M, 3 * M);
            // Yard walls from the house to the map's north and south edges: the only way across is through the house.
            Add(19, 0, 2, 5, 3 * M); Add(19, 15, 2, 5, 3 * M);
            var plates = new[]
            {
                new MapPlate(1, new MapXZ(17, 11), new MapXZ(4, 2), null, new MapSlope("x", 0, 3 * M), M / 2),
                new MapPlate(2, new MapXZ(15, 6), new MapXZ(10, 8), 3 * M, null, M / 2),
            };
            var cutouts = new[] { new MapCutout(1, 2, new MapXZ(17, 11), new MapXZ(4, 2)) };
            var units = new List<MapUnit>();
            for (int i = 0; i < 8; i++)
                units.Add(new MapUnit((ulong)units.Count + 1, "rwr.soldier-1", 1, 1, new MapMm(3500 + 1500 * (i % 2), 7000 + 1500 * (i / 2)), false));
            for (int i = 0; i < 5; i++)
                units.Add(new MapUnit((ulong)units.Count + 1, "rwr.soldier-2", 2, 2, new MapMm(35000 + 1500 * (i % 2), 7000 + 1500 * (i / 2)), false));
            foreach ((int x, int z) in new[] { (22500, 7500), (23500, 9500), (19500, 8500) })
                units.Add(new MapUnit((ulong)units.Count + 1, "rwr.soldier-2", 2, 3, new MapMm(x, z), false, plate: 2));
            return new MapDesc("plate-assault-demo", "none", "rwr-m6", new MapXZ(40, 20), new MapXZ(0, 0), 0, 8 * M,
                new[] { new MapLayer("walls", 1) }, null, null, plates, cutouts, walls.ToArray(),
                new[]
                {
                    new MapPoint(1, new MapMm(8000, 10000), 3000, 1, new MapMm(8000, 10000)),
                    new MapPoint(2, new MapMm(20000, 8500), 2500, 2, plate: 2),
                    new MapPoint(3, new MapMm(32000, 10000), 3000, 2, new MapMm(32000, 10000)),
                },
                new[] { new MapBase(1, 1, new MapMm(2000, 10000)), new MapBase(2, 2, new MapMm(38000, 10000)) },
                units.ToArray(), new MapRun(41, "teamIdle"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext((ulong)walls.Count + 1, 4, 3, (ulong)units.Count + 1, 3, 2));
        }

        public static byte[] DescBytes() => MapDescJson.Write(Desc());

        // The baked .wcmap bytes.
        public static byte[] Bytes() => MapBake.Run(DescBytes()).WcMapBytes;
    }
}
