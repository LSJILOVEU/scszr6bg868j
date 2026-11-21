using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Tests
{
    // TRV-2 shared fixtures (later traversal packages reuse them): the test-only content layer trv-test and the five
    // fixture maps of the plan's appendix. Every fixture has floor plates (traversal only runs on plate maps).
    internal static class Trv2Maps
    {
        internal const long M = SimFix.Scale;
        internal const string Pack = "trv-test", SteepPack = "trv-test-steep";

        // The four placeholder kinds of plan 2.2, in raw: 0.3 m = 19660, 0.6 m = 39321, 1.2 m = 78643, 2.5 m = 163840.
        internal const string KindsJson =
            "[ { \"id\": \"step\", \"shape\": \"edge\", \"riseMin\": 0, \"riseMax\": 39321, \"fallMin\": 0, \"fallMax\": 39321, \"gapMin\": 131072, \"ticksBase\": 8, \"ticksPerMetreUp\": 0, \"ticksPerMetreDown\": 0, \"pathCost\": 0 },"
            + " { \"id\": \"climb\", \"shape\": \"edge\", \"riseMin\": 39321, \"riseMax\": 78643, \"fallMin\": 0, \"fallMax\": 19660, \"gapMin\": 131072, \"ticksBase\": 30, \"ticksPerMetreUp\": 0, \"ticksPerMetreDown\": 0, \"pathCost\": 2000 },"
            + " { \"id\": \"drop\", \"shape\": \"edge\", \"riseMin\": 0, \"riseMax\": 19660, \"fallMin\": 39321, \"fallMax\": 163840, \"gapMin\": 131072, \"ticksBase\": 12, \"ticksPerMetreUp\": 0, \"ticksPerMetreDown\": 0, \"pathCost\": 1000, \"fire\": false },"
            + " { \"id\": \"vault\", \"shape\": \"over\", \"riseMin\": 0, \"riseMax\": 78643, \"fallMin\": 0, \"fallMax\": 163840, \"gapMin\": 65536, \"ticksBase\": 24, \"ticksPerMetreUp\": 0, \"ticksPerMetreDown\": 0, \"pathCost\": 2000, \"noiseRaw\": 327680 } ]";

        internal const string LayerJson = "{ \"id\": \"trv-test\", \"extends\": \"rwr-m6\", \"traversal\": { \"kinds\": " + KindsJson + " } }";
        // The same with a 1/2 walkable slope, for a ground cell that is too steep between two that are not.
        internal const string SteepJson = "{ \"id\": \"trv-test-steep\", \"extends\": \"trv-test\", \"slopeRule\": { \"maxWalkableNum\": 1, \"maxWalkableDen\": 2 } }";

        // Shipped layers from the project folder plus in-memory test layers (never written to disk, never shipped).
        internal sealed class Source : IContentSource
        {
            private readonly IContentSource shipped = FileContentSource.Rwr();
            private readonly Dictionary<string, string> extra;
            internal Source(params (string Id, string Text)[] layers) { extra = layers.ToDictionary(l => l.Id, l => l.Text); }
            public bool TryRead(string layerId, out string text, out string location)
            {
                if (extra.TryGetValue(layerId, out text)) { location = "memory:" + layerId; return true; }
                return shipped.TryRead(layerId, out text, out location);
            }
        }

        internal static ContentPack Load(string id = Pack, params (string Id, string Text)[] more)
        {
            var layers = new List<(string, string)> { (Pack, LayerJson), (SteepPack, SteepJson) };
            layers.AddRange(more);
            return ContentLoader.Load(new Source(layers.ToArray()), id);
        }

        private static ContentPack test, steep;
        internal static ContentPack Test => test ??= Load();
        internal static ContentPack Steep => steep ??= Load(SteepPack);
        internal static TraversalRules Rules => Test.SectionOrNull<TraversalRules>();

        // ---- building descriptions ----

        internal static MapPlate Box(ulong id, int x, int z, long topRaw, long thickRaw = M) => Ml4Maps.Flat(id, x, z, 1, 1, topRaw, thickRaw);
        internal static MapPlate Flat(ulong id, int x, int z, int w, int d, long topRaw, long thickRaw = M) => Ml4Maps.Flat(id, x, z, w, d, topRaw, thickRaw);
        internal static MapPlate Slope(ulong id, int x, int z, int w, int d, string axis, long fromRaw, long toRaw, long thickRaw = M) =>
            Ml4Maps.Slope(id, x, z, w, d, axis, fromRaw, toRaw, thickRaw);
        internal static MapWall Wall(ulong id, int x, int z, int w, int d, long heightRaw, long? baseRaw = null) => Ml4Maps.Wall(id, x, z, w, d, heightRaw, baseRaw);

        // Floor 0 and ceiling 10 m above it unless given, pack trv-test.
        internal static MapDesc Make(int width, int depth, int[][] heights, MapPlate[] plates, MapWall[] walls = null, MapCutout[] cutouts = null,
            string pack = Pack, long? ceilingRaw = null, long floorRaw = 0)
        {
            walls = walls ?? new MapWall[0];
            cutouts = cutouts ?? new MapCutout[0];
            return new MapDesc("trv2", "none", pack, new MapXZ(width, depth), new MapXZ(0, 0), floorRaw, ceilingRaw ?? floorRaw + 10 * M,
                new[] { new MapLayer("walls", 1) }, heights, null, plates, cutouts, walls, new MapPoint[0], new MapBase[0], new MapUnit[0],
                new MapRun(41, "none"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(Next(walls.Select(w => w.Id)), 1, 1, 1, Next(plates.Select(p => p.Id)), Next(cutouts.Select(c => c.Id))));
        }

        private static ulong Next(IEnumerable<ulong> ids) => ids.Any() ? ids.Max() + 1 : 1;

        internal static int[][] Heights(int width, int depth, Func<int, int, long> point) =>
            Enumerable.Range(0, depth + 1).Select(j => Enumerable.Range(0, width + 1).Select(i => (int)point(i, j)).ToArray()).ToArray();

        // ---- the fixtures ----

        // F-box: 16 x 16 flat; one 1 x 1 plate at (8, 8), top 1 m, 1 m thick.
        internal static MapDesc Box1(long topRaw = M) => Make(16, 16, null, new[] { Box(1, 8, 8, topRaw) });

        // F-window: 16 x 16 flat; a 2.5 m wall along z = 8 with a window at (8, 8): sill [0, 58982], lintel [131072, 163840].
        // A box in the corner (1, 1) makes it a plate map.
        // The lintel is 0.5 m high whatever its base; extra walls go over the window cell (8, 8) first, in the given order.
        internal static MapDesc Window(long sillRaw = 58982, long lintelBaseRaw = 2 * M, params MapWall[] extra)
        {
            var walls = new List<MapWall>(extra);
            walls.AddRange(new[] { Wall(11, 0, 8, 8, 1, 163840), Wall(12, 9, 8, 7, 1, 163840), Wall(13, 8, 8, 1, 1, sillRaw), Wall(14, 8, 8, 1, 1, M / 2, lintelBaseRaw) });
            return Make(16, 16, null, new[] { Box(1, 1, 1, M) }, walls.ToArray());
        }

        // F-low-wall: 16 x 16 flat; a 1 m wall at z = 8, x = 4..11; a box at (1, 1).
        internal static MapDesc LowWall(int thickness = 1) => Make(16, 16, null, new[] { Box(1, 1, 1, M) }, new[] { Wall(1, 4, 8, 8, thickness, M) });

        // F-box-wall: F-low-wall with a 2 m wall and the box at (8, 7).
        internal static MapDesc BoxWall() => Make(16, 16, null, new[] { Box(1, 8, 7, M) }, new[] { Wall(1, 4, 8, 8, 1, 2 * M) });

        // F-slope-house: 32 x 32, ground rising 6554 (10 %) per cell along +x; a 9 x 8 foundation plate (x 12..20, z 12..19)
        // flat at the highest ground point under it, reaching down to the ground, with a 1 m wall standing on it along
        // z = 15, x 13..19.
        internal static MapDesc SlopeHouse()
        {
            const long rise = 6554;
            long top = rise * 21;
            return Make(32, 32, Heights(32, 32, (i, j) => rise * i),
                new[] { Flat(1, 12, 12, 9, 8, top, top - rise * 12 + M / 2) }, new[] { Wall(1, 13, 15, 7, 1, M, top) });
        }

        // ---- reading results ----

        internal static WcMap Bake(MapDesc d) => Ml4Maps.Valid(Ml4Maps.Bake(d));
        internal static LayeredNav Nav(WcMap map, ContentPack pack = null) => WcMapLayeredNav.Build(map, pack ?? Test);

        internal static TraversalLink[] Derive(MapDesc d, ContentPack pack = null, TraversalRules rules = null, params NoTraverseZone[] zones)
        {
            WcMap map = Bake(d);
            return LinkDerive.Derive(map, Nav(map, pack), rules ?? Rules, zones);
        }

        internal static int Node(LayeredNav nav, int layer, int x, int z)
        {
            Assert.That(nav.TryNode(layer, x, z, out int node), Is.True, "node L" + layer + " (" + x + ", " + z + ")");
            return node;
        }

        // The links between the two cells (any layers), from the first to the second.
        internal static TraversalLink[] Between(MapDesc d, int fx, int fz, int tx, int tz, ContentPack pack = null, TraversalRules rules = null, params NoTraverseZone[] zones)
        {
            WcMap map = Bake(d);
            LayeredNav nav = Nav(map, pack);
            return LinkDerive.Derive(map, nav, rules ?? Rules, zones)
                .Where(l => nav.XOf(l.From) == fx && nav.ZOf(l.From) == fz && nav.XOf(l.To) == tx && nav.ZOf(l.To) == tz).ToArray();
        }

        // The links from (fromLayer, fx, fz) to (toLayer, tx, tz).
        internal static TraversalLink[] Between(MapDesc d, int fromLayer, int fx, int fz, int toLayer, int tx, int tz, TraversalRules rules = null)
        {
            WcMap map = Bake(d);
            LayeredNav nav = Nav(map);
            return LinkDerive.Derive(map, nav, rules ?? Rules, null).Where(l => nav.LayerOf(l.From) == fromLayer && nav.XOf(l.From) == fx && nav.ZOf(l.From) == fz
                && nav.LayerOf(l.To) == toLayer && nav.XOf(l.To) == tx && nav.ZOf(l.To) == tz).ToArray();
        }

        internal static string KindOf(TraversalLink link, TraversalRules rules = null) => (rules ?? Rules)[link.Kind].Id;

        // Exactly one link between the cells; returns it.
        internal static TraversalLink One(MapDesc d, int fx, int fz, int tx, int tz, ContentPack pack = null, TraversalRules rules = null)
        {
            TraversalLink[] found = Between(d, fx, fz, tx, tz, pack, rules);
            Assert.That(found.Length, Is.EqualTo(1), "links (" + fx + ", " + fz + ") -> (" + tx + ", " + tz + "): " + string.Join("; ", found));
            return found[0];
        }

        internal static void None(MapDesc d, int fx, int fz, int tx, int tz, ContentPack pack = null, TraversalRules rules = null)
        {
            TraversalLink[] found = Between(d, fx, fz, tx, tz, pack, rules);
            Assert.That(found.Length, Is.Zero, "links (" + fx + ", " + fz + ") -> (" + tx + ", " + tz + "): " + string.Join("; ", found));
        }

        // A rules table of one kind, in raw.
        internal static TraversalRules OneKind(TraversalShape shape, long riseMax, long fallMax, long gapMin = 0, int ticksBase = 1, int up = 0, int down = 0) =>
            new TraversalRules(new[] { new TraversalKind("k", shape, 0, riseMax, 0, fallMax, gapMin, ticksBase, up, down, 0) });

        internal static string BakeError(MapDesc d) => Assert.Throws<InvalidDataException>(() => MapBake.Run(Ml4Maps.Bytes(d))).Message;
    }
}
