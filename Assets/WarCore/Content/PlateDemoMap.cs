using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Content
{
    // ML-8 demo: a map whose soldiers walk up a stair to a first floor and back. 24 x 16 m flat ground at 0, 1 m cells.
    //   House: 3 m walls round cells x 8..15, z 5..10, with a 2-cell doorway on the west side (z 7..8).
    //   Stair (layer 1): along x on row z = 9, cells x 10..13, rising 0 -> 3 m (0.75 m a cell). Its foot faces the open ground
    //     cell (9, 9) inside the house; its top faces the first floor's cell (14, 9).
    //   First floor (layer 2): flat at 3 m (0.5 m thick) on cells x 9..14, z 6..9, with a cutout over the stair. Its bottom,
    //     2.5 m, leaves the ground under it walkable; no wall stands under it.
    //   Outside: open ground, two points and two bases; the description's units are four soldiers of team 1 on the west side.
    // The only seams are stair foot <-> ground (9, 9) and stair top <-> first floor (14, 9).
    public static class PlateDemoMap
    {
        private const long M = SimFix.Scale;

        public static MapDesc Desc()
        {
            var walls = new[]
            {
                Wall(1, 8, 5, 8, 1), Wall(2, 8, 10, 8, 1), Wall(3, 8, 6, 1, 1), Wall(4, 8, 9, 1, 1), Wall(5, 15, 6, 1, 4),
            };
            var plates = new[]
            {
                new MapPlate(1, new MapXZ(10, 9), new MapXZ(4, 1), null, new MapSlope("x", 0, 3 * M), M / 2),
                new MapPlate(2, new MapXZ(9, 6), new MapXZ(6, 4), 3 * M, null, M / 2),
            };
            var cutouts = new[] { new MapCutout(1, 2, new MapXZ(10, 9), new MapXZ(4, 1)) };
            var units = new MapUnit[4];
            for (int i = 0; i < 4; i++)
                units[i] = new MapUnit((ulong)(i + 1), "rwr.soldier-1", 1, 1, new MapMm(1500 + 1500 * (i % 2), 2000 + 1500 * (i / 2)), false);
            return new MapDesc("plate-walk-demo", "none", "rwr-m6", new MapXZ(24, 16), new MapXZ(0, 0), 0, 8 * M,
                new[] { new MapLayer("walls", 1) }, null, null, plates, cutouts, walls,
                new[]
                {
                    new MapPoint(1, new MapMm(5000, 13000), 3000, 0, new MapMm(5000, 13000)),
                    new MapPoint(2, new MapMm(19000, 13000), 3000, 0, new MapMm(19000, 13000)),
                },
                new[] { new MapBase(1, 1, new MapMm(2000, 8000)), new MapBase(2, 2, new MapMm(22000, 8000)) },
                units, new MapRun(41, "teamIdle"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(6, 3, 3, 5, 3, 2));
        }

        public static byte[] DescBytes() => MapDescJson.Write(Desc());

        // The baked .wcmap bytes.
        public static byte[] Bytes() => MapBake.Run(DescBytes()).WcMapBytes;

        private static MapWall Wall(ulong id, int x, int z, int w, int d) =>
            new MapWall(id, "wall", "walls", new MapXZ(x, z), new MapXZ(w, d), 3 * M);
    }
}
