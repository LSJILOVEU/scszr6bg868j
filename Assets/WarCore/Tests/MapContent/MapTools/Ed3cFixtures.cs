using System;
using System.Text;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed3Fixtures;

namespace WarCore.Tests
{
    // ED-3c-1: the synthetic 3x3 tile T of the work package, tile and gen text builders, and a reference for the base layer.
    internal static class Ed3cFixtures
    {
        internal static readonly int[][] T =
        {
            new[] { 0, 30000, 45000 },
            new[] { 9000, 24000, 60000 },
            new[] { 30000, 45000, 54000 },
        };

        internal const string Fp = "059f05dde8ad90d037bf60e648ffd8db1644a1fc53d981baf96bb376100ce113";
        internal const string Fp24001 = "1a98bde96909f8acf771f23d6454c95d4efa31b635086f3b73b793864fb71a29";
        internal const string FpMinus60000 = "0b3389629eb313433cab07b8060d3ef33d31f7fb1f5e40a570b28a189e635de9";
        internal const string Credit = "[\"synthetic test tile\"]";

        internal static string Tile => Dem(T);

        internal static int[][] Shift(int[][] h, int by)
        {
            var r = new int[h.Length][];
            for (int b = 0; b < h.Length; b++)
            {
                r[b] = new int[h[b].Length];
                for (int a = 0; a < h[b].Length; a++) r[b][a] = h[b][a] + by;
            }
            return r;
        }

        internal static int[][] Grid(int cols, int rows, int value = 0)
        {
            var r = new int[rows][];
            for (int b = 0; b < rows; b++)
            {
                r[b] = new int[cols];
                for (int a = 0; a < cols; a++) r[b][a] = value;
            }
            return r;
        }

        internal static string Rows(int[][] h)
        {
            var sb = new StringBuilder("[");
            for (int b = 0; b < h.Length; b++)
            {
                if (b > 0) sb.Append(", ");
                sb.Append('[').Append(string.Join(", ", h[b])).Append(']');
            }
            return sb.Append(']').ToString();
        }

        // A .dem.json text; cols and rows default to the grid's own size.
        internal static string Dem(int[][] h, int step = 30000, string credit = Credit, string cols = null, string rows = null,
            string version = "1") =>
            "{ \"version\": " + version + ", \"cols\": " + (cols ?? h[0].Length.ToString()) + ", \"rows\": " + (rows ?? h.Length.ToString())
            + ", \"stepMm\": " + step + ", \"credit\": " + credit + ", \"heightsMm\": " + Rows(h) + " }";

        internal static string BaseKey(string sha = Fp, long x0 = 0, long z0 = 0, long scale = 10000, long exag = 1000, long datum = 0,
            string tile = "\"t.dem.json\"") =>
            "\"base\": { \"tile\": " + tile + ", \"sha256\": \"" + sha + "\", \"x0Mm\": " + x0 + ", \"z0Mm\": " + z0 + ", \"scaleMilli\": "
            + scale + ", \"exaggerationMilli\": " + exag + ", \"datumMm\": " + datum + " }";

        // A gen text with the base key (inserted before "stamps"; key order does not matter to Read).
        internal static string GenB(int width, int depth, string stamps = "", string baseMm = "0", string sha = Fp, long x0 = 0, long z0 = 0,
            long scale = 10000, long exag = 1000, long datum = 0) =>
            WithKey(Gen(width, depth, stamps, baseMm: baseMm), BaseKey(sha, x0, z0, scale, exag, datum));

        internal static string WithKey(string gen, string key) => gen.Replace("\"stamps\"", key + ", \"stamps\"");

        internal static MapStampPlan ComposeB(MapDesc d, string gen, string dem) =>
            MapTerrain.Compose(d, MapGenSpec.Read(gen), Rules(), dem == null ? null : MapDemTile.Read(dem));

        // Compose with the tile and apply; an exception is an assertion failure.
        internal static MapDesc RunB(MapDesc d, string gen, string dem) => Ok(() => Apply(d, ComposeB(d, gen, dem)), gen);

        // Refused with ArgumentException, nothing planned; the message contains part when given.
        internal static void RefusesB(MapDesc d, string gen, string dem, string part = null)
        {
            ArgumentException e = Assert.Throws<ArgumentException>(() => ComposeB(d, gen, dem), gen);
            if (part != null) Assert.That(e.Message, Does.Contain(part));
        }

        private static long FloorDiv(long n, long d) => (long)Math.Floor((decimal)n / d);

        // Reference for the base layer at point (i, j) before clamp and fill (work package C-c, written out independently).
        internal static long Reference(int[][] h, long step, int i, int j, int baseMm = 0, long x0 = 0, long z0 = 0, long scale = 10000,
            long exag = 1000, long datum = 0)
        {
            int cols = h[0].Length, rows = h.Length;
            long x = x0 + i * scale, z = z0 + j * scale;
            int a = (int)Math.Min(x / step, cols - 2), b = (int)Math.Min(z / step, rows - 2);
            long fx = x - a * step, fz = z - b * step;
            decimal n = (decimal)h[b][a] * (step - fx) * (step - fz) + (decimal)h[b][a + 1] * fx * (step - fz)
                + (decimal)h[b + 1][a] * (step - fx) * fz + (decimal)h[b + 1][a + 1] * fx * fz;
            long r = (long)Math.Floor(n / ((decimal)step * step));
            return Raw(baseMm) + FloorDiv((r - datum) * exag * 65536, scale * 1000);
        }
    }
}
