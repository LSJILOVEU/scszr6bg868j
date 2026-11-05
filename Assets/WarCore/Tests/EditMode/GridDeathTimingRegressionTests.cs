using System;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bA")]
    public sealed class GridDeathTimingRegressionTests
    {
        private const int Limit = 8;

        private static void RequireDirectTimer()
        {
            if (!Stopwatch.IsHighResolution || Stopwatch.Frequency <= 0)
                Assert.Inconclusive("timing=unavailable; no high-resolution monotonic counter.");
            long start = Stopwatch.GetTimestamp();
            long observed = start;
            for (int i = 0; i < 4096; i++)
                observed = Stopwatch.GetTimestamp();
            if (observed <= start)
                Assert.Inconclusive("timing=unavailable; counter did not detect the positive-control workload.");
            TestContext.WriteLine("2bA timing=direct; frequency={0}; positive-control ticks={1}", Stopwatch.Frequency, observed - start);
        }

        private static long Measure(ISpatialQuery query, EntityId[] buffer, out int count)
        {
            long start = Stopwatch.GetTimestamp();
            count = query.Sphere(SimVec3.Metres(60, 1, 75), SimFix.FromInt(512), buffer);
            return Stopwatch.GetTimestamp() - start;
        }

        private static void RequireRatio(long live, long firstAfterDeath)
        {
            Assert.That(live, Is.GreaterThan(0), "Counter resolution must resolve the live query.");
            Assert.That(firstAfterDeath, Is.LessThanOrEqualTo(live * Limit), "First query after death exceeded the fixed 8x limit.");
        }

        [Test]
        public void _2bA_R21_FirstQueryAfterOneThousandDeathsStaysWithinEightTimesLive()
        {
            RequireDirectTimer();
            var scene = new GridOwnershipTestFixture(2048, 8192);
            var victims = new EntityId[1000];
            var buffer = new EntityId[2048];
            for (int i = 0; i < 2000; i++)
            {
                EntityId entity = scene.CreateBody(SimVec3.Metres(1 + i % 40 * 3, 1, 1 + i / 40 * 3));
                if (i < victims.Length)
                    victims[i] = entity;
            }
            scene.InitializeIndex();
            byte[] saved = scene.Sim.SaveSnapshot();
            var live = new long[17];
            var dead = new long[17];
            var ratios = new double[17];
            for (int sample = 0; sample < live.Length; sample++)
            {
                if (sample != 0)
                {
                    // Cold reset is outside both timing windows; each pair starts with all 2000 live bodies.
                    scene.Sim.RestoreSnapshot(saved);
                    scene.Grid.Rebuild();
                }
                for (int warm = 0; warm < 8; warm++)
                    scene.Grid.Sphere(SimVec3.Metres(60, 1, 75), SimFix.FromInt(512), buffer);
                live[sample] = Measure(scene.Grid, buffer, out int liveHits);
                foreach (EntityId victim in victims)
                    scene.Store.Destroy(victim);
                int memberships = scene.Grid.MembershipCount;
                dead[sample] = Measure(scene.Grid, buffer, out int deadHits);
                Assert.That(liveHits, Is.EqualTo(2000));
                Assert.That(deadHits, Is.EqualTo(1000), "The first-after-death query must still do real work.");
                Assert.That(scene.Grid.MembershipCount, Is.EqualTo(memberships), "Timing may not hide query-time cleanup.");
                ratios[sample] = live[sample] == 0 ? double.PositiveInfinity : (double)dead[sample] / live[sample];
                TestContext.WriteLine("2bA death timing pair={0}; live_ticks={1}; first_dead_ticks={2}; ratio={3:F6}; hits=2000/1000", sample, live[sample], dead[sample], ratios[sample]);
            }
            Array.Sort(ratios);
            TestContext.WriteLine("2bA death timing: seed=0x2BA1000; grid=8m; bodies=2000; deaths=1000; pairs=17; warmup=8/pair; sphere origin=(60,1,75)m; radius=512m; exclusions=none; median_ratio={0:F6}; max_ratio={1:F6}; limit=8; timing=direct; Mono/Editor diagnostic only.", ratios[8], ratios[16]);
            for (int sample = 0; sample < live.Length; sample++)
                RequireRatio(live[sample], dead[sample]);
        }

        [Test]
        public void _2bA_TimingGuardReportsInjectedQueryDelay()
        {
            RequireDirectTimer();
            var scene = new GridOwnershipTestFixture();
            scene.CreateBody(SimVec3.Metres(60, 1, 75));
            scene.InitializeIndex();
            var buffer = new EntityId[16];
            for (int i = 0; i < 8; i++)
                Measure(scene.Grid, buffer, out _);
            long live = Measure(scene.Grid, buffer, out int liveHits);
            Assert.That(live, Is.GreaterThan(0));
            var delayed = new DelayedQuery(scene.Grid, live * 16);
            long slow = Measure(delayed, buffer, out int slowHits);
            Assert.That(slowHits, Is.EqualTo(liveHits));
            TestContext.WriteLine("2bA timing mutation: live_ticks={0}; delayed_ticks={1}; ratio={2:F6}; expected guard limit=8", live, slow, (double)slow / live);
            Assert.Throws<AssertionException>(() => RequireRatio(live, slow));
        }

        private sealed class DelayedQuery : SpatialQueryBase
        {
            private readonly GridSpatialQuery grid;
            private readonly long delay;

            internal DelayedQuery(GridSpatialQuery grid, long delay)
            {
                this.grid = grid;
                this.delay = delay;
            }

            protected override int Execute(SpatialRequest request, Span<EntityId> entities, Span<RayHit> hits)
            {
                int count = grid.Sphere(request.Origin, request.Radius, entities);
                long start = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() - start < delay)
                {
                    // An injected measurable delay proves the same timer and ratio guard can report a regression.
                }
                return count;
            }
        }
    }
}
