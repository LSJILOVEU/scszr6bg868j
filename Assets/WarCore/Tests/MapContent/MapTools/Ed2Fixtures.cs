using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content.MapTools;

namespace WarCore.Tests
{
    internal static class Ed2Fixtures
    {
        internal const long S = 65536;
        internal const string Pack = "rwr-terrain-demo";

        // A width x depth map (rwr-terrain-demo, all grass) whose height point (i, j) is height(i, j), written by hand.
        internal static MapDesc Map(int width, int depth, Func<int, int, int> height, long floor = -262144, long ceiling = 262144)
        {
            var e = new MapEdit(MapNew.Desc(new MapNewSpec
            {
                Id = "ed2", Content = "rwr", Pack = Pack, Width = width, Depth = depth, FloorRaw = floor, CeilingRaw = ceiling,
                Seed = 3, HeightRaw = (int)Math.Max(floor, 0), Surface = "grass",
            }));
            e.Batch(b =>
            {
                for (int j = 0; j <= depth; j++)
                    for (int i = 0; i <= width; i++)
                    {
                        int h = height(i, j);
                        if (h != e.Current.Heights[j][i]) b.SetHeight(i, j, h);
                    }
            });
            return e.Current;
        }

        internal static MapDesc Flat(int width, int depth) => Map(width, depth, (i, j) => 0);

        // The writes as hand edits in one batch.
        internal static MapDesc Apply(MapDesc d, MapHeightWrite[] writes)
        {
            var e = new MapEdit(d);
            e.Batch(b => { foreach (MapHeightWrite w in writes) b.SetHeight(w.I, w.J, w.Raw); });
            return e.Current;
        }

        internal static string Show(MapHeightWrite[] writes)
        {
            var parts = new List<string>();
            foreach (MapHeightWrite w in writes) parts.Add("(" + w.I + "," + w.J + ")=" + w.Raw);
            return string.Join(" ", parts);
        }

        internal static MapHeightWrite[] W(params int[] triples)
        {
            var list = new MapHeightWrite[triples.Length / 3];
            for (int n = 0; n < list.Length; n++) list[n] = new MapHeightWrite(triples[3 * n], triples[3 * n + 1], triples[3 * n + 2]);
            return list;
        }

        internal static void Same(MapHeightWrite[] expected, MapHeightWrite[] actual) =>
            Assert.That(Show(actual), Is.EqualTo(Show(expected)));

        // Both triangles of cell (cx, cz) no steeper than 2/1.
        internal static bool CellLegal(MapDesc d, int cx, int cz) => MapBrush.CellWithin(d.Heights[cz][cx], d.Heights[cz][cx + 1],
            d.Heights[cz + 1][cx], d.Heights[cz + 1][cx + 1], 2, 1);

        internal static void AllLegal(MapDesc d)
        {
            for (int cz = 0; cz < d.Size.Z; cz++)
                for (int cx = 0; cx < d.Size.X; cx++) Assert.That(CellLegal(d, cx, cz), Is.True, "cell " + cx + "," + cz);
            for (int j = 0; j <= d.Size.Z; j++)
                for (int i = 0; i <= d.Size.X; i++)
                    Assert.That(d.Heights[j][i], Is.InRange(d.FloorRaw, d.CeilingRaw), "point " + i + "," + j);
        }
    }
}
