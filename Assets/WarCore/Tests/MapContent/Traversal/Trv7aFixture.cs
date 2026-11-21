using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // TRV-7a fixtures: the two shipped plate demos on the body pack (eye 1.5 m) as worlds, and the headless report (a text table
    // of each point's entries and watch cells, and a top view as SVG).
    internal static class Trv7aFixture
    {
        internal const long S = SimFix.Scale;

        // A description's JSON with its pack swapped (the demos are written for rwr-m6).
        internal static byte[] WithPack(byte[] desc, string from, string to)
        {
            string text = Encoding.UTF8.GetString(desc), want = "\"pack\": \"" + from + "\"";
            Assert.That(text, Does.Contain(want));
            return new UTF8Encoding(false).GetBytes(text.Replace(want, "\"pack\": \"" + to + "\""));
        }

        internal static M6World World(byte[] desc, string pack = "rwr-body", bool endless = false)
        {
            MapBakeResult bake = MapBake.Run(WithPack(desc, "rwr-m6", pack));
            Assert.That(bake.PackId, Is.EqualTo(pack));
            WorldRecipe recipe = MapRecipe.From(bake, RwrPack.Load(bake.PackId)).WithPlatesPreview();
            return new M6World(endless ? M6World.Endless(recipe) : recipe, bake.WcMapBytes);
        }

        internal static M6World Demo(string pack = "rwr-body", bool endless = false) => World(PlateDemoMap.DescBytes(), pack, endless);
        internal static M6World Assault(string pack = "rwr-body") => World(PlateAssaultMap.DescBytes(), pack);

        internal static LayeredNav Nav(M6World w) => w.Session.PlateWorldForCheck.Nav;

        // A flat map drawn row by row (rows[z], x along the string): '#' a 3 m wall on the cell, 'P' the one point (radius
        // 0.5 m, centre at the cell middle), anything else open. One 1 x 1 plate (top 3 m, 0.5 m thick unless given) stands over
        // the cell (px, pz), joined to nothing, so the map has plates and the entry table is built. Bases at the two west corners;
        // units: team 1 soldiers standing at the given cells' middles (one in the south-east corner when none is given).
        internal static M6World Drawn(string[] rows, int px, int pz, string pack = "rwr-body", long plateTop = 3 * S, long plateThick = S / 2,
            params (int x, int z)[] units)
        {
            int depth = rows.Length, width = rows[0].Length;
            var walls = new List<MapWall>();
            MapPoint point = null;
            for (int z = 0; z < depth; z++)
            {
                Assert.That(rows[z].Length, Is.EqualTo(width));
                for (int x = 0; x < width; x++)
                {
                    char c = rows[z][x];
                    if (c == '#') walls.Add(new MapWall((ulong)walls.Count + 1, "wall", "walls", new MapXZ(x, z), new MapXZ(1, 1), 3 * S));
                    if (c == 'P') point = new MapPoint(1, new MapMm(x * 1000 + 500, z * 1000 + 500), 500, 0);
                }
            }
            Assert.That(point, Is.Not.Null, "the drawing has a point");
            var plates = new[] { new MapPlate(1, new MapXZ(px, pz), new MapXZ(1, 1), plateTop, null, plateThick) };
            if (units.Length == 0) units = new[] { (width - 1, 0) };
            var soldiers = units.Select((u, i) => new MapUnit((ulong)i + 1, "rwr.soldier-1", 1, 1, new MapMm(u.x * 1000 + 500, u.z * 1000 + 500), false)).ToArray();
            var desc = new MapDesc("trv7a-drawn", "none", pack, new MapXZ(width, depth), new MapXZ(0, 0), 0, 8 * S,
                new[] { new MapLayer("walls", 1) }, null, null, plates, new MapCutout[0], walls.ToArray(), new[] { point },
                new[] { new MapBase(1, 1, new MapMm(500, 500)), new MapBase(2, 2, new MapMm(500, depth * 1000 - 500)) },
                soldiers, new MapRun(41, "teamIdle"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext((ulong)walls.Count + 1, 2, 3, (ulong)soldiers.Length + 1, 2, 1));
            MapBakeResult bake = MapBake.Run(MapDescJson.Write(desc));
            return new M6World(MapRecipe.From(bake, RwrPack.Load(bake.PackId)).WithPlatesPreview(), bake.WcMapBytes);
        }

        internal static string Cell(LayeredNav nav, int node) => "L" + nav.LayerOf(node) + "(" + nav.XOf(node) + "," + nav.ZOf(node) + ")";

        private static string M(SimFix v) => (v.Raw / (double)S).ToString("0.###", CultureInfo.InvariantCulture);

        // One line per entry of the map, then per point its entries and their watch cells.
        internal static string Report(string name, LayeredNav nav, PointEntryTable table)
        {
            var b = new StringBuilder();
            EntryTable entries = table.Entries;
            b.AppendLine("# " + name + ": " + entries.Count + " entries, " + table.SightChecks + " sight checks");
            b.AppendLine("entry\tkind\twidth\tA\tB\tA point\tB point");
            for (int e = 0; e < entries.Count; e++)
            {
                EntryInfo i = entries[e];
                b.AppendLine(e + "\t" + i.Kind + "\t" + i.Width + "\t" + Cell(nav, i.NodeA) + "\t" + Cell(nav, i.NodeB)
                    + "\t(" + M(i.PointA.X) + "," + M(i.PointA.Y) + "," + M(i.PointA.Z) + ")\t(" + M(i.PointB.X) + "," + M(i.PointB.Y) + "," + M(i.PointB.Z) + ")");
            }
            for (int p = 0; p < table.PointCount; p++)
            {
                b.AppendLine("## point " + p + ": " + table.EntryCount(p) + " entries");
                b.AppendLine("entry\tinner\touter\tinner cost\touter cost\twatch cells (cell, sees outer)");
                for (int k = 0; k < table.EntryCount(p); k++)
                {
                    PointEntry pe = table.Entry(p, k);
                    var cells = new List<string>();
                    for (int j = 0; j < table.WatchCount(p, k); j++)
                    {
                        WatchCell c = table.Watch(p, k, j);
                        cells.Add(Cell(nav, c.Node) + (c.SeesOuter ? " +out" : ""));
                    }
                    b.AppendLine(pe.Entry + "\t" + Cell(nav, pe.InnerNode) + "\t" + Cell(nav, pe.OuterNode) + "\t" + pe.InnerCost + "\t" + pe.OuterCost + "\t" + string.Join("; ", cells));
                }
            }
            return b.ToString();
        }

        // Top view, 1 m = 20 px: blocked ground dark, plate cells tinted by layer, each entry a line from its A to its B point,
        // each watch cell a dot joined to its entry's inner point, each point a circle.
        internal static string Svg(LayeredNav nav, PointEntryTable table, IReadOnlyList<ControlPointDef> points)
        {
            const int P = 20;
            int w = nav.Width * P, h = nav.Depth * P;
            var b = new StringBuilder();
            b.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"" + w + "\" height=\"" + h + "\" viewBox=\"0 0 " + w + " " + h + "\">");
            b.AppendLine("<rect width=\"" + w + "\" height=\"" + h + "\" fill=\"#f4f1ea\"/>");
            string Y(double z) => (h - z * P).ToString("0.#", CultureInfo.InvariantCulture);
            string X(double x) => (x * P).ToString("0.#", CultureInfo.InvariantCulture);
            double Mx(SimFix v) => (v.Raw - nav.Ground.Nav.OriginX.Raw) / (double)S;
            double Mz(SimFix v) => (v.Raw - nav.Ground.Nav.OriginZ.Raw) / (double)S;
            string[] tint = { "#9a8f7a", "#7fb3d5", "#f0b27a", "#a9dfbf", "#d7bde2" };
            for (int n = 0; n < nav.Count; n++)
            {
                int layer = nav.LayerOf(n);
                if (layer == 0 && nav.IsPassable(n)) continue;
                if (layer > 0 && !nav.IsPassable(n)) continue;
                string fill = layer == 0 ? "#3b3b3b" : tint[Math.Min(layer, tint.Length - 1)];
                b.AppendLine("<rect x=\"" + X(nav.XOf(n)) + "\" y=\"" + Y(nav.ZOf(n) + 1) + "\" width=\"" + P + "\" height=\"" + P + "\" fill=\"" + fill + "\" fill-opacity=\"" + (layer == 0 ? "1" : "0.55") + "\"/>");
            }
            foreach (ControlPointDef pt in points)
                b.AppendLine("<circle cx=\"" + X(Mx(pt.Center.X)) + "\" cy=\"" + Y(Mz(pt.Center.Z)) + "\" r=\"" + (pt.Radius.Raw * P / (double)S).ToString("0.#", CultureInfo.InvariantCulture) + "\" fill=\"none\" stroke=\"#2e86c1\" stroke-width=\"3\"/>");
            for (int e = 0; e < table.Entries.Count; e++)
            {
                EntryInfo i = table.Entries[e];
                string colour = i.Kind == EntryKind.Seam ? "#8e44ad" : "#c0392b";
                b.AppendLine("<line x1=\"" + X(Mx(i.PointA.X)) + "\" y1=\"" + Y(Mz(i.PointA.Z)) + "\" x2=\"" + X(Mx(i.PointB.X)) + "\" y2=\"" + Y(Mz(i.PointB.Z)) + "\" stroke=\"" + colour + "\" stroke-width=\"5\" stroke-linecap=\"round\"/>");
                b.AppendLine("<text x=\"" + X(Mx(i.PointA.X)) + "\" y=\"" + Y(Mz(i.PointA.Z) + 0.6) + "\" font-size=\"12\" fill=\"" + colour + "\">" + e + "</text>");
            }
            for (int p = 0; p < table.PointCount; p++)
                for (int k = 0; k < table.EntryCount(p); k++)
                {
                    PointEntry pe = table.Entry(p, k);
                    for (int j = 0; j < table.WatchCount(p, k); j++)
                    {
                        WatchCell c = table.Watch(p, k, j);
                        b.AppendLine("<line x1=\"" + X(Mx(c.Point.X)) + "\" y1=\"" + Y(Mz(c.Point.Z)) + "\" x2=\"" + X(Mx(pe.InnerPoint.X)) + "\" y2=\"" + Y(Mz(pe.InnerPoint.Z)) + "\" stroke=\"#27ae60\" stroke-width=\"1\" stroke-dasharray=\"3,3\"/>");
                        b.AppendLine("<circle cx=\"" + X(Mx(c.Point.X)) + "\" cy=\"" + Y(Mz(c.Point.Z)) + "\" r=\"5\" fill=\"" + (c.SeesOuter ? "#27ae60" : "#f1c40f") + "\" stroke=\"#1e5631\"/>");
                    }
                }
            b.AppendLine("</svg>");
            return b.ToString();
        }
    }
}
