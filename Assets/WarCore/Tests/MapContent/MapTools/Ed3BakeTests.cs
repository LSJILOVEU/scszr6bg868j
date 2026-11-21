using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Ed3Fixtures;

namespace WarCore.Tests
{
    // ED-3a acceptance 8 and 11: random specs bake without format errors; existing terrain maps; a 512 x 512 timing (manual).
    public sealed class Ed3BakeTests
    {
        private static readonly string[] Kinds = { "hill", "hills", "ridge", "mesa", "patch", "river", "road" };
        private static readonly string[] Names = { "grass", "mud", "road" };

        private static int Coord(Random random, int size) => random.Next(-8, size + 9);

        private static string Points(Random random, int width, int depth)
        {
            int count = random.Next(2, 5);
            var parts = new string[count];
            for (int n = 0; n < count; n++) parts[n] = "[" + Coord(random, width) + ", " + Coord(random, depth) + "]";
            return "[" + string.Join(", ", parts) + "]";
        }

        private static string RandomStamp(Random random, string kind, int width, int depth)
        {
            int mm = random.Next(-40000, 60001);
            switch (kind)
            {
                case "hill": return Hill(Coord(random, width), Coord(random, depth), random.Next(1, 40), mm);
                case "hills":
                    int x0 = random.Next(0, width), z0 = random.Next(0, depth);
                    return Hills(random.Next(1, 12), x0, z0, random.Next(x0, width + 1), random.Next(z0, depth + 1), 2, random.Next(2, 20), -5000, random.Next(-5000, 30000));
                case "ridge": return Ridge(Points(random, width, depth), random.Next(1, 12), mm);
                case "mesa": return Mesa(Coord(random, width), Coord(random, depth), random.Next(1, 20), mm);
                case "patch": return Patch(Coord(random, width), Coord(random, depth), random.Next(1, 20), Names[random.Next(3)], random.Next(0, 513));
                case "river": return River(Points(random, width, depth), random.Next(1, 6), random.Next(1, 8000), "mud");
                default: return Road(Points(random, width, depth), random.Next(1, 4));
            }
        }

        [Test]
        public void RandomSpecsBakeWithoutFormatErrors()
        {
            var random = new Random(50);
            var seen = new HashSet<string>();
            int composed = 0, refused = 0;
            var reasons = new List<string>();
            for (int round = 0; round < 50; round++)
            {
                int width = random.Next(8, 129), depth = random.Next(8, 129);
                int count = random.Next(1, 9);
                var stamps = new string[count];
                for (int k = 0; k < count; k++)
                {
                    string kind = round < 7 && k == 0 ? Kinds[round] : Kinds[random.Next(Kinds.Length)];
                    seen.Add(kind);
                    stamps[k] = RandomStamp(random, kind, width, depth);
                }
                MapDesc d = Map(width, depth);
                string gen = Gen(width, depth, Join(stamps), (ulong)round, random.Next(-2000, 2001).ToString(), Names[random.Next(3)]);
                MapStampPlan plan;
                try { plan = Compose(d, gen); }
                catch (ArgumentException e)
                {
                    Assert.That(e.Message, Does.StartWith("road stamp"), gen);
                    refused++;
                    reasons.Add(round + ": " + e.Message);
                    continue;
                }
                MapDesc after = Apply(d, plan);
                Assert.That(Problems(after).Exists(p => p.Code == "MAPDESC_WCMAP"), Is.False, "round " + round + " " + string.Join("; ", Problems(after)));
                AllLegal(after);
                composed++;
            }
            TestContext.Progress.WriteLine("ED3 random specs: composed " + composed + ", refused " + refused + (reasons.Count > 0 ? " (" + string.Join(" | ", reasons) + ")" : ""));
            Assert.That(seen.Count, Is.EqualTo(7));
            Assert.That(composed + refused, Is.EqualTo(50));
            Assert.That(refused, Is.LessThanOrEqualTo(10));
        }

        [Test]
        public void ExistingTerrainMapsBakeAfterTheStamp()
        {
            foreach (string path in new[] { "Assets/WarCore/Maps/terrain-demo.map.json", "Assets/WarCore/Maps/door-terrain-demo.map.json" })
            {
                MapDocument doc = MapDocument.Open(File.ReadAllBytes(path));
                MapDesc d = MapDescJson.Read(doc.Save());
                int w = d.Size.X, z = d.Size.Z;
                string gen = Gen(w, z, Join(Hill(w / 3, z / 2, 12, 4000), Ridge("[[0, 2], [" + w + ", " + (z - 3) + "]]", 3, 1500),
                    Patch(w / 2, z / 2, 5, "mud", 200), River("[[0, " + (z - 2) + "], [" + w + ", " + (z - 2) + "]]", 1, 800, "mud"),
                    Road("[[2, " + (z / 2) + "], [" + (w - 2) + ", " + (z / 2) + "]]", 1)));
                MapTerrainResult r = Ok(() => doc.WithTerrain(gen), path);
                MapDesc after = MapDescJson.Read(r.Document.Save());
                Assert.That(HasWcMap(after), Is.False, path + ": " + string.Join("; ", Problems(after)));
                AllLegal(after);
            }
        }

        [Test, Explicit, Category("Manual")]
        public void Timing512()
        {
            var stamps = new List<string> { Hills(64, 0, 0, 512, 512, 8, 60, -5000, 40000) };
            for (int n = 0; n < 3; n++) stamps.Add(Ridge("[[" + (20 + n * 150) + ", 0], [" + (80 + n * 150) + ", 256], [" + (40 + n * 150) + ", 512]]", 12, 15000));
            stamps.Add(River("[[0, 100], [200, 140], [512, 120]]", 6, 3000, "mud"));
            stamps.Add(River("[[0, 400], [300, 380], [512, 420]]", 5, 2500, "mud"));
            for (int n = 0; n < 8; n++) stamps.Add(Patch(30 + n * 60, 250 + (n % 3) * 40, 20, "mud", 300));
            stamps.Add(Road("[[10, 256], [200, 250], [400, 270], [500, 256]]", 3));
            stamps.Add(Road("[[300, 10], [260, 120], [290, 220]]", 3));
            MapDesc d = Map(512, 512);
            string gen = Gen(512, 512, Join(stamps.ToArray()), 2026);
            var total = Stopwatch.StartNew();
            var sw = Stopwatch.StartNew();
            MapStampPlan plan = Compose(d, gen);
            long compose = sw.ElapsedMilliseconds;
            sw.Restart();
            MapDesc stamped = Apply(d, plan);
            long stamp = sw.ElapsedMilliseconds;
            sw.Restart();
            MapTerrainResult r = MapDocument.Open(MapDescJson.Write(d)).WithTerrain(gen);
            long facade = sw.ElapsedMilliseconds;
            TestContext.Progress.WriteLine("ED3 512x512: compose " + compose + " ms, names + stamp " + stamp + " ms, WithTerrain " + facade + " ms, total "
                + total.ElapsedMilliseconds + " ms");
            Assert.That(r.Document.Save(), Is.EqualTo(MapDescJson.Write(stamped)));
        }
    }
}
