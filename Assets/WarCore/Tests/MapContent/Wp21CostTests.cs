using System;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // WP-21 cost checks on never-ending twins: steady-state allocation, MayPass call counts, and the largest guard lattice.
    public sealed class Wp21CostTests
    {
        private static readonly System.Reflection.FieldInfo PassField = typeof(InterimLocomotion).GetField("pass", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static (int Ticks, long Total, string Where, double MeanMs) Allocations(M6World w, int warmup, int ticks)
        {
            w.Step(warmup);
            var spots = new List<string>(); long total = 0; long previous = -2; long stopwatchTicks = 0;
            for (int i = 0; i < ticks; i++)
            {
                long clock = Stopwatch.GetTimestamp(), before = GC.GetAllocatedBytesForCurrentThread(); w.Sim.Step(default);
                long used = GC.GetAllocatedBytesForCurrentThread() - before; stopwatchTicks += Stopwatch.GetTimestamp() - clock;
                if (used <= 0) continue;
                Assert.That(w.Sim.Clock.Tick, Is.Not.EqualTo(previous + 1), "allocation on consecutive ticks is not a one-time growth");
                previous = w.Sim.Clock.Tick; total += used; spots.Add(previous + ":" + used);
            }
            return (spots.Count, total, string.Join(",", spots), stopwatchTicks * 1000.0 / Stopwatch.Frequency / ticks);
        }

        [Test]
        public void SteadyStateAllocatesNothingOnlyIsolatedOneTimeGrowthTicks()
        {
            var real = Allocations(M6PerformanceTests.CreateRealLoad(true), 100, 1000);
            var m7 = Allocations(new M6World(M6World.Endless(DemoRecipes.M7()), DemoRecipes.M7MapBytes()), 100, 1500);
            TestContext.Out.WriteLine($"ALLOC_REAL meanMs={real.MeanMs:F3} ticks={real.Ticks} bytes={real.Total} at={real.Where}");
            TestContext.Out.WriteLine($"ALLOC_M7 meanMs={m7.MeanMs:F3} ticks={m7.Ticks} bytes={m7.Total} at={m7.Where}");
            Assert.That(real.Ticks, Is.LessThanOrEqualTo(3)); Assert.That(m7.Ticks, Is.LessThanOrEqualTo(3));
            Assert.That(real.Total, Is.LessThan(64 * 1024)); Assert.That(m7.Total, Is.LessThan(64 * 1024));
        }

        private sealed class Counting : ICrowdPass
        {
            private readonly ICrowdPass inner;
            internal long Calls, Allowed;
            internal Counting(ICrowdPass inner) { this.inner = inner; }
            public ulong Digest => inner.Digest;
            public bool MayPass(EntityId mover, EntityId idle) { Calls++; bool ok = inner.MayPass(mover, idle); if (ok) Allowed++; return ok; }
        }

        [TestCase("real")]
        [TestCase("m7")]
        public void CountingTheRuleCallsChangesNothingAndIsReported(string which)
        {
            Func<M6World> make = which == "real" ? (Func<M6World>)(() => M6PerformanceTests.CreateRealLoad(true))
                : () => new M6World(M6World.Endless(DemoRecipes.M7()), DemoRecipes.M7MapBytes());
            var plain = make(); var counted = make();
            var counter = new Counting((ICrowdPass)PassField.GetValue(counted.System<InterimLocomotion>()));
            PassField.SetValue(counted.System<InterimLocomotion>(), counter);
            const int ticks = 700; long peak = 0, previous = 0; int movingPeak = 0; long movingTicks = 0;
            for (int i = 0; i < ticks; i++)
            {
                plain.Step(); counted.Step();
                peak = Math.Max(peak, counter.Calls - previous); previous = counter.Calls;
                int moving = 0;
                for (ulong key = 1; key <= (which == "real" ? 200UL : 300UL); key++)
                    if (counted.Has<MoveIntent>(key) && counted.Read<MoveIntent>(key).Active) moving++;
                movingPeak = Math.Max(movingPeak, moving); movingTicks += moving;
            }
            Assert.That(counted.Hash, Is.EqualTo(plain.Hash), "a pure rule: counting the calls changes nothing");
            TestContext.Out.WriteLine($"MAYPASS {which} ticks={ticks} calls={counter.Calls} allowed={counter.Allowed} peakPerTick={peak} movingPeak={movingPeak} movingUnitTicks={movingTicks}");
            Assert.That(counter.Calls, Is.GreaterThan(0));
        }

        [Test]
        public void TheLargestLatticeOfARadiusTenPointStillStepsAndKeepsEveryOrderOnItsOwnSlot()
        {
            var point = new ControlPointDef(1, M6World.P(40), SimFix.FromInt(10));
            var slots = new GuardSlots(new[] { point });
            Assert.That(slots.Count(0), Is.GreaterThan(331), "ring 11 still has points inside radius 10");
            var units = new UnitSpawn[400];
            for (int i = 0; i < units.Length; i++)
                units[i] = new UnitSpawn((ulong)i + 1, 0, 1, (long)(1000 + i % 20 * 2000), (long)(1000 + i / 20 * 1900), 6000, 1, 0);
            // think=1 is the stress case (every unit rescans the lattice every tick); 30 is the real cadence.
            M6World w = null;
            foreach (int think in new[] { 1, 30 })
            {
                w = new M6World(M6World.Endless(M6World.RecipeOf(units, M6World.Pack(new[] { point }, think: think))), WcMapWriterFor80x40());
                var times = new double[300];
                for (int i = 0; i < times.Length; i++)
                {
                    long start = Stopwatch.GetTimestamp(); w.Sim.Step(default);
                    times[i] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
                }
                double mean = 0; foreach (double t in times) mean += t / times.Length;
                Array.Sort(times);
                TestContext.Out.WriteLine($"SLOTSCAN slots={slots.Count(0)} units=400 think={think} ticks=300 mean={mean:F3}ms median={times[150]:F3}ms p95={times[285]:F3}ms max={times[299]:F3}ms");
            }
            var held = new HashSet<int>(); int standing = 0;
            for (ulong key = 1; key <= 400; key++)
            {
                int slot = slots.Nearest(w.Read<Position>(key).Value);
                if (slot < 0) continue;
                var offset = WarCore.Combat.CombatSystem.Horizontal(w.Read<Position>(key).Value - slots.At(slot)).LengthSquared;
                if (offset > new SimVec3(SimFix.Ratio(1, 4), default, default).LengthSquared) continue;
                standing++; Assert.That(held.Add(slot), Is.True, "two units resting on one slot");
            }
            int active = 0, inside = 0; for (ulong key = 1; key <= 400; key++) { if (w.Read<MoveIntent>(key).Active) active++; if (CaptureSystem.Inside(w.Read<Position>(key).Value, point)) inside++; }
            TestContext.Out.WriteLine($"SLOTSCAN_STANDING standing={standing} active={active} inside={inside}");
            Assert.That(standing, Is.GreaterThan(0));
        }
        private static byte[] WcMapWriterFor80x40() => WarCore.Content.WcMapWriter.Write(M3TestWorld.Boxes(80, 40));
    }
}
