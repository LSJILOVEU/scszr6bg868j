using System.Collections.Generic;
using System.Linq;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-12a fixtures: blank flat maps and the buildings the tests put on them.
    internal static class Ml12Fixtures
    {
        internal const long M = SimFix.Scale;

        // A flat w x d map at height 0 with one wall layer and nothing on it.
        internal static MapDesc Blank(int w, int d, string pack = "rwr-m6", long ceiling = 16 * M, string id = "ml12-blank") =>
            new MapDesc(id, "none", pack, new MapXZ(w, d), new MapXZ(0, 0), 0, ceiling,
                new[] { new MapLayer("walls", 1) }, null, null, new MapPlate[0], new MapCutout[0], new MapWall[0],
                new MapPoint[0], new MapBase[0], new MapUnit[0], new MapRun(41, "teamIdle"), new[] { MapEdit.Hand }, null, null,
                new MapStamp[0], new MapSrc[0], new MapNext(1, 1, 1, 1));

        internal static MapDocument Doc(MapDesc d) => MapDocument.Open(MapDescJson.Write(d));

        internal static MapDesc Desc(MapDocument doc) => MapDescJson.Read(doc.Save());

        // The demo house of PlateDemoMap without its walls, plates and cutouts (the same id, size, points, bases and units).
        internal static MapDesc DemoGround()
        {
            MapDesc demo = PlateDemoMap.Desc();
            return new MapDesc(demo.Id, demo.Content, demo.Pack, demo.Size, demo.Origin, demo.FloorRaw, demo.CeilingRaw, demo.Layers.ToArray(),
                null, null, new MapPlate[0], new MapCutout[0], new MapWall[0], demo.Points.ToArray(), demo.Bases.ToArray(), demo.Units.ToArray(),
                demo.Run, demo.Owners.ToArray(), null, null, new MapStamp[0], new MapSrc[0], new MapNext(1, 3, 3, 5));
        }

        // The demo house as a building: cells x 8..15, z 5..10, two floors of 3 m, 0.5 m plates, a 2-cell door in the west
        // wall at z 7..8 (the west segment starts at z 6), a stair along x from (10, 9), 4 cells.
        internal static MapBuildingInfo DemoHouse() => new MapBuildingInfo(8, 5, 8, 6, 2, 3 * M, M / 2, "w", 1, 2, true, 10, 9, 4);

        // A 12 x 10 house at (2, 2) on a 24 x 16 map: south door at x 5..6, stair along x from (4, 8), 6 cells (0.5 m a cell,
        // which leaves exactly 2 m between stacked stairs: 3 m - 0.5 m thick - 0.5 m rise).
        internal static MapBuildingInfo Tower(int floors) => new MapBuildingInfo(2, 2, 12, 10, floors, 3 * M, M / 2, "s", 3, 2, true, 4, 8, 6);

        internal static T BySrc<T>(IEnumerable<T> items, System.Func<T, MapSrc> src, string instance, ulong want) where T : class =>
            items.FirstOrDefault(x => src(x) != null && src(x).Instance == instance && src(x).Src == want);

        internal static string Text(MapWall w) => w.Id + " " + w.Kind + " " + w.Layer + " " + w.Cell.X + "," + w.Cell.Z + " " + w.Size.X + "x" + w.Size.Z
            + " h" + w.HeightRaw + " b" + w.BaseRaw + " o" + w.Owner + " " + Src(w.Src);

        internal static string Text(MapPlate p) => p.Id + " " + p.Cell.X + "," + p.Cell.Z + " " + p.Size.X + "x" + p.Size.Z + " y" + p.YRaw
            + " s" + (p.Slope == null ? "-" : p.Slope.Axis + p.Slope.FromRaw + ">" + p.Slope.ToRaw) + " t" + p.ThickRaw + " o" + p.Owner + " " + Src(p.Src);

        internal static string Text(MapCutout c) => c.Id + " p" + c.Plate + " " + c.Cell.X + "," + c.Cell.Z + " " + c.Size.X + "x" + c.Size.Z
            + " o" + c.Owner + " " + Src(c.Src);

        private static string Src(MapSrc s) => s == null ? "-" : s.Instance + "/" + s.Kind + "/" + s.Src;

        // Every wall, plate and cutout as text, sorted.
        internal static List<string> Entries(MapDesc d) =>
            d.Walls.Select(Text).Concat(d.Plates.Select(Text)).Concat(d.Cutouts.Select(Text)).OrderBy(s => s, System.StringComparer.Ordinal).ToList();

        // (kind, src) of every entry of the instance, sorted.
        internal static List<string> Srcs(MapDesc d, string instance) =>
            d.Walls.Where(w => w.Src?.Instance == instance).Select(w => "wall " + w.Src.Src)
                .Concat(d.Plates.Where(p => p.Src?.Instance == instance).Select(p => "plate " + p.Src.Src))
                .Concat(d.Cutouts.Where(c => c.Src?.Instance == instance).Select(c => "cutout " + c.Src.Src))
                .OrderBy(s => s, System.StringComparer.Ordinal).ToList();

        internal static List<string> Expected(params (string kind, ulong[] srcs)[] groups) =>
            groups.SelectMany(g => g.srcs.Select(s => g.kind + " " + s)).OrderBy(s => s, System.StringComparer.Ordinal).ToList();

        internal static IReadOnlyList<MapProblem> Gate(MapDocument doc) => MapGate.Check(doc.Save(), WarCore.Content.Rwr.RwrPack.Load(Desc(doc).Pack));
    }
}
