using System;
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
    // ML-7b fixtures. House: the ML-8 demo house with plate ids 9 (the first floor, listed first) and 5 (the stair), so the
    // baked layers are 1 = id 5 (stair) and 2 = id 9 (first floor); the floor's cutout (id 1) over the stair is cells x 10..13,
    // z 9; a raised wall (id 6, bottom 3 m) stands on floor cell (14, 6), so that cell is solid but not walkable.
    internal static class Ml7bMaps
    {
        internal const long M = SimFix.Scale;

        internal static MapPoint Point(int id, long xMm, long zMm, MapMm? spawn = null, ulong? spawnPlate = null) =>
            new MapPoint(id, new MapMm(xMm, zMm), 3000, 0, spawn, spawnPlate: spawnPlate);

        internal static MapBase Base(int id, int team, long xMm, long zMm, ulong? plate = null) =>
            new MapBase(id, team, new MapMm(xMm, zMm), plate: plate);

        internal static MapUnit Unit(ulong key, long xMm, long zMm, ulong? plate = null, bool reserve = false) =>
            new MapUnit(key, "rwr.soldier-1", 1, 1, new MapMm(xMm, zMm), reserve, plate: plate);

        internal static MapPoint[] DemoPoints() => new[] { Point(1, 5000, 13000, new MapMm(5000, 13000)), Point(2, 19000, 13000, new MapMm(19000, 13000)) };
        internal static MapBase[] DemoBases() => new[] { Base(1, 1, 2000, 8000), Base(2, 2, 22000, 8000) };

        internal static MapDesc House(MapPoint[] points = null, MapBase[] bases = null, MapUnit[] units = null, bool stair = true)
        {
            MapDesc demo = PlateDemoMap.Desc();
            var walls = demo.Walls.ToList();
            walls.Add(new MapWall(6, "wall", "walls", new MapXZ(14, 6), new MapXZ(1, 1), 2 * M, baseRaw: 3 * M));
            var plates = new List<MapPlate> { new MapPlate(9, new MapXZ(9, 6), new MapXZ(6, 4), 3 * M, null, M / 2) };
            if (stair) plates.Add(new MapPlate(5, new MapXZ(10, 9), new MapXZ(4, 1), null, new MapSlope("x", 0, 3 * M), M / 2));
            units = units ?? new MapUnit[0];
            return new MapDesc("ml7b-house", "none", "rwr-m6", demo.Size, demo.Origin, demo.FloorRaw, demo.CeilingRaw, demo.Layers.ToArray(),
                null, null, plates.ToArray(), new[] { new MapCutout(1, 9, new MapXZ(10, 9), new MapXZ(4, 1)) }, walls.ToArray(),
                points ?? DemoPoints(), bases ?? DemoBases(), units, new MapRun(41, "teamIdle"), new[] { MapEdit.Hand }, null, null,
                new MapStamp[0], new MapSrc[0], new MapNext(7, 3, 3, units.Length == 0 ? 1 : units.Max(u => u.Key) + 1, 10, 2));
        }

        internal static byte[] Bytes(MapDesc d) => MapDescJson.Write(d);
        internal static string Text(MapDesc d) => Encoding.UTF8.GetString(Bytes(d));
        // A throw where a test expects a value is an assertion failure, not an error.
        internal static T NoThrow<T>(Func<T> f)
        {
            T value = default;
            Assert.DoesNotThrow(() => value = f());
            return value;
        }
        private static MapDesc Raw(string text) => MapDescJson.Read(Encoding.UTF8.GetBytes(text));
        internal static MapDesc Read(string text) => NoThrow(() => Raw(text));
        internal static string Error(string text) => Assert.Throws<InvalidDataException>(() => Raw(text)).Message;
        internal static string BakeError(MapDesc d) => Assert.Throws<InvalidDataException>(() => MapBake.Run(Bytes(d))).Message;
        internal static MapSetup Setup(MapDesc d) => NoThrow(() => MapBake.Run(Bytes(d)).Setup);
        internal static IReadOnlyList<MapProblem> Gate(MapDesc d) => NoThrow(() => MapGate.Check(Bytes(d), RwrPack.Load(d.Pack)));
        internal static int Line(string text, string fragment, int skip = 0) => Ml4DescTests.Line(text, fragment, skip);
    }
}
