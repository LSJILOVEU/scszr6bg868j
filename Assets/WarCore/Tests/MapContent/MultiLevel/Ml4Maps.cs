using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-4 fixtures: small descriptions with floor plates, the plate demo map and the four counter-example maps.
    internal static class Ml4Maps
    {
        internal const long M = SimFix.Scale;
        internal const long Half = M / 2;
        internal const string DemoPath = "Assets/WarCore/Maps/plate-demo.map.json";

        internal static MapPlate Flat(ulong id, int x, int z, int w, int d, long yRaw, long thickRaw = Half, string surface = null) =>
            new MapPlate(id, new MapXZ(x, z), new MapXZ(w, d), yRaw, null, thickRaw, surface);

        internal static MapPlate Slope(ulong id, int x, int z, int w, int d, string axis, long fromRaw, long toRaw, long thickRaw = Half) =>
            new MapPlate(id, new MapXZ(x, z), new MapXZ(w, d), null, new MapSlope(axis, fromRaw, toRaw), thickRaw);

        internal static MapCutout Cut(ulong id, ulong plate, int x, int z, int w, int d) =>
            new MapCutout(id, plate, new MapXZ(x, z), new MapXZ(w, d));

        internal static MapWall Wall(ulong id, int x, int z, int w, int d, long heightRaw, long? baseRaw = null) =>
            new MapWall(id, "wall", "walls", new MapXZ(x, z), new MapXZ(w, d), heightRaw, baseRaw: baseRaw);

        // Flat ground unless heights are given; ceiling 10 m above the floor unless given; no points, bases or units.
        internal static MapDesc Make(long floorRaw, int width, int depth, int[][] heights, MapPlate[] plates, MapCutout[] cutouts = null,
            MapWall[] walls = null, MapUnit[] units = null, MapSurfaces surfaces = null, long? ceilingRaw = null)
        {
            cutouts = cutouts ?? new MapCutout[0];
            walls = walls ?? new MapWall[0];
            units = units ?? new MapUnit[0];
            return new MapDesc("ml4", "none", "rwr-m6", new MapXZ(width, depth), new MapXZ(0, 0), floorRaw, ceilingRaw ?? floorRaw + 10 * M,
                new[] { new MapLayer("walls", 1) }, heights, surfaces, plates, cutouts, walls, new MapPoint[0], new MapBase[0], units,
                new MapRun(41, "none"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(Next(walls.Select(w => w.Id)), 1, 1, Next(units.Select(u => u.Key)), Next(plates.Select(p => p.Id)), Next(cutouts.Select(c => c.Id))));
        }

        internal static MapDesc Make(int width, int depth, params MapPlate[] plates) => Make(0, width, depth, null, plates);

        private static ulong Next(IEnumerable<ulong> ids) => ids.Any() ? ids.Max() + 1 : 1;

        internal static int[][] Ground(int width, int depth, long raw) =>
            Enumerable.Range(0, depth + 1).Select(_ => Enumerable.Repeat((int)raw, width + 1).ToArray()).ToArray();

        internal static byte[] Bytes(MapDesc d) => MapDescJson.Write(d);
        internal static string Text(MapDesc d) => Encoding.UTF8.GetString(Bytes(d));
        internal static WcMap Bake(MapDesc d) => MapBake.BuildMap(MapDescJson.Read(Bytes(d)));
        internal static string BakeError(MapDesc d) => Assert.Throws<InvalidDataException>(() => MapBake.Run(Bytes(d))).Message;
        internal static IReadOnlyList<MapProblem> Gate(MapDesc d) => MapGate.Check(Bytes(d), RwrPack.Load(d.Pack));
        internal static IReadOnlyList<MapProblem> Gate(MapDesc d, ContentPack pack) => MapGate.Check(Bytes(d), pack);

        internal static WcMap Valid(WcMap map)
        {
            Assert.DoesNotThrow(() => WcMapValidation.Validate(map), "the .wcmap rules accept the baked map");
            return map;
        }

        // ---- plate demo (acceptance 10, deliverable 5) ----

        // 24 x 16 m flat. A two-storey 8 x 6 house on cells x 8..15, z 5..10: 3 m floor walls round it with a 2-cell doorway
        // on the west side (z 7..8); inside (x 9..14, z 6..9) a 4-cell stair along x on row z = 9 rising 0 -> 3 m (slope 3/4),
        // the first floor at 3 m (0.5 m thick) with a cutout over the stair, on it a partition at x = 11 (bottom 3 m, 2.5 m
        // high) with a doorway at z = 7 under a lintel 2 m over the floor, and the roof at 6 m. Points, bases and the two
        // teams' four soldiers each are outside on the ground.
        internal static MapDesc Demo()
        {
            var walls = new[]
            {
                Wall(1, 8, 5, 8, 1, 3 * M), Wall(2, 8, 10, 8, 1, 3 * M), Wall(3, 8, 6, 1, 1, 3 * M), Wall(4, 8, 9, 1, 1, 3 * M),
                Wall(5, 15, 6, 1, 4, 3 * M),
                Wall(6, 11, 6, 1, 1, 2 * M + Half, 3 * M), Wall(7, 11, 8, 1, 1, 2 * M + Half, 3 * M), Wall(8, 11, 7, 1, 1, Half, 5 * M),
            };
            var plates = new[] { Slope(1, 9, 9, 4, 1, "x", 0, 3 * M), Flat(2, 9, 6, 6, 4, 3 * M), Flat(3, 9, 6, 6, 4, 6 * M) };
            var cutouts = new[] { Cut(1, 2, 9, 9, 4, 1) };
            var units = new MapUnit[8];
            for (int i = 0; i < 4; i++)
            {
                units[i] = new MapUnit((ulong)(i + 1), "rwr.soldier-1", 1, 1, new MapMm(1500 + 1500 * (i % 2), 2000 + 1500 * (i / 2)), false);
                units[i + 4] = new MapUnit((ulong)(i + 5), "rwr.soldier-1", 2, 2, new MapMm(22500 - 1500 * (i % 2), 2000 + 1500 * (i / 2)), false);
            }
            return new MapDesc("plate-demo", "none", "rwr-m6", new MapXZ(24, 16), new MapXZ(0, 0), 0, 8 * M,
                new[] { new MapLayer("walls", 1) }, null, null, plates, cutouts, walls,
                new[]
                {
                    new MapPoint(1, new MapMm(5000, 13000), 3000, 0, new MapMm(5000, 13000)),
                    new MapPoint(2, new MapMm(19000, 13000), 3000, 0, new MapMm(19000, 13000)),
                },
                new[] { new MapBase(1, 1, new MapMm(2000, 8000)), new MapBase(2, 2, new MapMm(22000, 8000)) },
                units, new MapRun(41, "teamIdle"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(9, 3, 3, 9, 4, 2));
        }

        // ---- the four counter-examples (acceptance 10, deliverable 6) ----

        // Ground 1 m everywhere, a plate at 0.5 m: buried at its first cell (1, 1).
        internal static MapDesc Buried() => Make(0, 6, 4, Ground(6, 4, M), new[] { Flat(1, 1, 1, 3, 2, Half) });

        // Two flat plates 1 m apart (box gap 0.5 m).
        internal static MapDesc Stacked() => Make(6, 4, Flat(1, 1, 1, 3, 2, 3 * M), Flat(2, 2, 1, 3, 2, 4 * M));

        // A 3 m floor wall under a plate at 2.5 m (bottom 2 m).
        internal static MapDesc WallThrough() => Make(0, 6, 4, null, new[] { Flat(1, 1, 1, 3, 2, 2 * M + Half) },
            walls: new[] { Wall(1, 2, 2, 2, 1, 3 * M) });

        // A slope rising 1.75 m per cell: legal in .wcmap (2/1), over the gate's 5/3.
        internal static MapDesc Steep() => Make(6, 4, Slope(1, 1, 1, 2, 2, "x", 0, 2 * (M + 3 * M / 4)));
    }
}
