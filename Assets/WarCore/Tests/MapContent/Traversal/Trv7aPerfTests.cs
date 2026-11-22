using System;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // TRV-7a acceptance 7, by hand: a 512 x 512 map whose plate nodes are as many as its ground cells, built once. The ground and
    // the plate (one 512 x 512 plate at 3 m, no seams) both carry a town of 10 x 10 m blocks: walls 1 cell thick every 10
    // cells with a 2-wide doorway in each wall side. 16 points spread over the ground. The sight is a counter (always true), so
    // the time is the table's own; the PR multiplies the sight count by combat's measured cost per sight test.
    public sealed class Trv7aPerfTests
    {
        [Test, Explicit, Category("Manual")]
        public void ABigTownBuildsInTime()
        {
            const int N = 512;
            var ground = new byte[N * N];
            var plate = new byte[N * N];
            for (int z = 0; z < N; z++)
                for (int x = 0; x < N; x++)
                {
                    bool wall = (x % 10 == 0 && z % 10 != 4 && z % 10 != 5) || (z % 10 == 0 && x % 10 != 4 && x % 10 != 5);
                    ground[z * N + x] = plate[z * N + x] = wall ? (byte)0 : (byte)1;
                }
            var slab = Ml5Plates.Flat(1, 0, 0, N, N, 196608);
            var watch = Stopwatch.StartNew();
            var nav = new LayeredNav(Ground(N, N, ground), 0, new[] { slab }, new[] { plate }, new[] { Speeds(plate) });
            long navMs = watch.ElapsedMilliseconds;
            Assert.That(nav.Count, Is.EqualTo(2 * N * N));
            var reaches = Enumerable.Range(0, 16).Select(i => new LayeredReach(nav, Node(nav, 0, 32 * (i % 4) * 4 + 15, 32 * (i / 4) * 4 + 15))).ToArray();
            long reachMs = watch.ElapsedMilliseconds - navMs;
            watch.Restart();
            EntryTable entries = EntryTable.Build(nav);
            long entryMs = watch.ElapsedMilliseconds;
            long sights = 0;
            PointEntryTable table = PointEntryTable.Build(nav, entries, reaches, 15000, 3, (a, b) => { sights++; return true; });
            long totalMs = watch.ElapsedMilliseconds;
            int pointEntries = Enumerable.Range(0, table.PointCount).Sum(table.EntryCount);
            TestContext.Out.WriteLine("TRV-7a 512: nodes " + nav.Count + ", entries " + entries.Count + ", point entries " + pointEntries
                + ", sight checks " + table.SightChecks + ", entry table " + entryMs + " ms, with watch cells " + totalMs + " ms (graph " + navMs + " ms, 16 cost tables " + reachMs + " ms, not counted)");
            Assert.That(sights, Is.EqualTo(table.SightChecks));
            Assert.That(totalMs, Is.LessThan(3000));
        }

        // Combat's cost per sight test on the assault map (rwr-body), for the PR's estimate.
        [Test, Explicit, Category("Manual")]
        public void CombatSightCost()
        {
            M6World w = Trv7aFixture.Assault();
            LayeredNav nav = Trv7aFixture.Nav(w);
            var points = Enumerable.Range(0, nav.Count).Where(nav.IsPassable).Select(n => EntryTable.Centre(nav, n)).ToArray();
            int calls = 0, seen = 0;
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < points.Length; i += 3)
                for (int j = 0; j < points.Length; j += 7) { calls++; if (w.Combat.Sees(points[i], points[j])) seen++; }
            TestContext.Out.WriteLine("TRV-7a sight: " + calls + " calls, " + seen + " clear, " + (watch.Elapsed.TotalMilliseconds * 1000 / calls).ToString("0.00") + " us a call");
        }
    }
}
