using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;

namespace WarCore.Tests
{
    internal static class Ed3Fixtures
    {
        internal const long S = 65536;
        internal const long L = 92681;
        internal const string Pack = "rwr-terrain-demo";
        internal const long Km = 65536000;

        internal static TerrainRules Rules() => RwrPack.Load(Pack).Terrain;

        // A new map (rwr-terrain-demo) with heights at 0 and all grass, floor and ceiling as given.
        internal static MapDesc Map(int width, int depth, bool heights = true, bool surfaces = true, long floor = -Km, long ceiling = Km,
            string pack = Pack) =>
            MapNew.Desc(new MapNewSpec
            {
                Id = "ed3", Content = "rwr", Pack = pack, Width = width, Depth = depth, FloorRaw = floor, CeilingRaw = ceiling, Seed = 5,
                HeightRaw = heights ? (int?)Math.Max(floor, Math.Min(ceiling, 0)) : null, Surface = surfaces ? "grass" : null,
            });

        internal static string Gen(int width, int depth, string stamps, ulong seed = 1, string baseMm = "0", string ground = "grass",
            string pack = Pack)
        {
            string text = "{ \"version\": 1, \"width\": " + width + ", \"depth\": " + depth + ", \"pack\": \"" + pack + "\", \"seed\": "
                + seed.ToString(CultureInfo.InvariantCulture);
            if (baseMm != null) text += ", \"baseMm\": " + baseMm;
            if (ground != null) text += ", \"ground\": \"" + ground + "\"";
            return text + ", \"stamps\": [" + stamps + "] }";
        }

        internal static string Hill(int x, int z, int r, int mm) =>
            "{ \"kind\": \"hill\", \"x\": " + x + ", \"z\": " + z + ", \"radius\": " + r + ", \"heightMm\": " + mm + " }";

        internal static string Mesa(int x, int z, int r, int mm) =>
            "{ \"kind\": \"mesa\", \"x\": " + x + ", \"z\": " + z + ", \"radius\": " + r + ", \"heightMm\": " + mm + " }";

        internal static string Hills(int count, int x0, int z0, int x1, int z1, int rMin, int rMax, int hMin, int hMax) =>
            "{ \"kind\": \"hills\", \"count\": " + count + ", \"x0\": " + x0 + ", \"z0\": " + z0 + ", \"x1\": " + x1 + ", \"z1\": " + z1
            + ", \"rMin\": " + rMin + ", \"rMax\": " + rMax + ", \"hMinMm\": " + hMin + ", \"hMaxMm\": " + hMax + " }";

        internal static string Patch(int x, int z, int r, string surface, int rough) =>
            "{ \"kind\": \"patch\", \"x\": " + x + ", \"z\": " + z + ", \"radius\": " + r + ", \"surface\": \"" + surface + "\", \"rough\": " + rough + " }";

        internal static string Ridge(string points, int halfWidth, int mm) =>
            "{ \"kind\": \"ridge\", \"points\": " + points + ", \"halfWidth\": " + halfWidth + ", \"heightMm\": " + mm + " }";

        internal static string River(string points, int halfWidth, int depthMm, string surface) =>
            "{ \"kind\": \"river\", \"points\": " + points + ", \"halfWidth\": " + halfWidth + ", \"depthMm\": " + depthMm
            + ", \"surface\": \"" + surface + "\" }";

        internal static string Road(string points, int halfWidth, string surface = "road") =>
            "{ \"kind\": \"road\", \"points\": " + points + ", \"halfWidth\": " + halfWidth + ", \"surface\": \"" + surface + "\" }";

        internal static string Join(params string[] stamps) => string.Join(", ", stamps);

        internal static MapStampPlan Compose(MapDesc d, string gen) => MapTerrain.Compose(d, MapGenSpec.Read(gen), Rules());

        // The plan's surface names first (ordinal), then the stamp, in one batch (what MapDocument.WithTerrain does).
        internal static MapDesc Apply(MapDesc d, MapStampPlan plan)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (MapSurfaceWrite w in plan.Surfaces) names.Add(w.Name);
            var e = new MapEdit(d);
            e.Batch(b =>
            {
                foreach (string name in names) b.AddSurfaceName(name);
                b.Stamp(plan);
            });
            return e.Current;
        }

        // Compose and apply; an exception is an assertion failure (so a mutation that throws is caught as a failed check).
        internal static MapDesc Run(MapDesc d, string gen) => Ok(() => Apply(d, Compose(d, gen)), gen);

        internal static T Ok<T>(Func<T> body, string what = "")
        {
            T result = default;
            Assert.DoesNotThrow(() => result = body(), what);
            return result;
        }

        internal static long H(MapDesc d, int i, int j) => d.Heights[j][i];

        internal static string Surface(MapDesc d, int x, int z) => d.Surfaces.Names[MapEditDraft.OwnerIndex(d.Surfaces.Rows[z][x])];

        internal static long Raw(int mm) => MapGenSpec.MmToRaw(mm);

        // Every axis step <= L, both triangles of every cell within 2/1, every point in range.
        internal static void AllLegal(MapDesc d)
        {
            for (int j = 0; j <= d.Size.Z; j++)
                for (int i = 0; i <= d.Size.X; i++)
                {
                    Assert.That(H(d, i, j), Is.InRange(d.FloorRaw, d.CeilingRaw), "point " + i + "," + j);
                    if (i < d.Size.X) Assert.That(Math.Abs(H(d, i + 1, j) - H(d, i, j)), Is.LessThanOrEqualTo(L), "x step " + i + "," + j);
                    if (j < d.Size.Z) Assert.That(Math.Abs(H(d, i, j + 1) - H(d, i, j)), Is.LessThanOrEqualTo(L), "z step " + i + "," + j);
                }
            for (int cz = 0; cz < d.Size.Z; cz++)
                for (int cx = 0; cx < d.Size.X; cx++)
                    Assert.That(MapBrush.CellWithin(H(d, cx, cz), H(d, cx + 1, cz), H(d, cx, cz + 1), H(d, cx + 1, cz + 1), 2, 1), Is.True,
                        "cell " + cx + "," + cz);
        }

        internal static List<MapProblem> Problems(MapDesc d) =>
            new List<MapProblem>(MapGate.Check(MapDescJson.Write(d), RwrPack.Load(d.Pack)));

        internal static bool HasWcMap(MapDesc d) => Problems(d).Exists(p => p.Code == "MAPDESC_WCMAP");
    }
}
