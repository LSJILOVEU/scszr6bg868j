using System;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Presentation;
using static WarCore.Tests.See1Field;

namespace WarCore.Tests
{
    // SEE-1 acceptance 10: the mean time of LayerCut.Run (radius 5) and Silhouettes.Run together over 100 runs on 2000 one-cell walls
    // (3 to 9 m high) and 500 units, half of them team mates. Run by hand ("Manual"); it prints the mean and sets no time limit.
    public sealed class See1PerformanceTests
    {
        [Test, Category("Manual")]
        public void TheMeanTimeOfACutAndTheSilhouettes()
        {
            var random = new Random(7);
            var cells = new System.Collections.Generic.HashSet<(int, int)>();
            var walls = new System.Collections.Generic.List<(int X, int Z, int W, int D, double H)>();
            while (walls.Count < 2000)
            {
                int x = random.Next(0, 64), z = random.Next(0, 64);
                if (cells.Add((x, z))) walls.Add((x, z, 1, 1, 3 + random.Next(0, 7)));
            }
            ViewMap map = Map(walls.ToArray());
            var units = new ViewEntity[500];
            for (int i = 0; i < units.Length; i++)
                units[i] = new ViewEntity((ulong)(i + 1), 0, (float)(random.NextDouble() * 64), 0, (float)(random.NextDouble() * 64), team: i % 2 + 1);
            ViewEntity me = units[0];
            for (int warm = 0; warm < 5; warm++) Once(map, me, units);
            var clock = Stopwatch.StartNew();
            for (int run = 0; run < 100; run++) Once(map, me, units);
            clock.Stop();
            TestContext.Progress.WriteLine($"SEE-1 mean {clock.Elapsed.TotalMilliseconds / 100:0.000} ms for a cut and the silhouettes ({walls.Count} walls, {units.Length} units)");
            Assert.That(Once(map, me, units), Is.LessThanOrEqualTo(249), "at most the other team mates");
        }

        private static int Once(ViewMap map, ViewEntity me, ViewEntity[] units)
        {
            LayerCutResult cut = LayerCut.Run(map.Plates, map, me, Forward, LayerCut.SeeRadius);
            return Silhouettes.Run(map.Plates, map, cut, me, units, Forward).Count;
        }
    }
}
