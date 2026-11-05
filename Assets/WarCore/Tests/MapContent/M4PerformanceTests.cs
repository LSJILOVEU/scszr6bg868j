using System;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M4PerformanceTests
    {
        [TestCase(20)]
        [TestCase(200)]
        public void ActiveSimStep_600Ticks_ZeroAllocation_ReportsTiming(int count)
        {
            var units = new UnitSpawn[count];
            for (int i = 0; i < count; i++)
                units[i] = new UnitSpawn((ulong)i + 1, 0, i + 1, 2500 + i % 20 * 1000, 2500 + i / 20 * 2000, 6000);
            // All move east for > 150 m after warmup. Grid remains within the 65536-cell limit.
            var world = new M4TestWorld(M3TestWorld.Boxes(256, 32), units);
            for (int i = 0; i < count; i++) world.Order(i, units[i].XMm + 220000, units[i].ZMm);
            for (int tick = 0; tick < 100; tick++) world.Sim.Step(default);
            var times = new double[600];
            long beginAlloc = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 600; tick++)
            {
                long start = Stopwatch.GetTimestamp();
                world.Sim.Step(default);
                times[tick] = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - beginAlloc;
            // Separate untimed replay verifies that every unit actually moves in every measured tick.
            var verify = new M4TestWorld(M3TestWorld.Boxes(256, 32), units);
            for (int i = 0; i < count; i++) verify.Order(i, units[i].XMm + 220000, units[i].ZMm);
            for (int tick = 0; tick < 100; tick++) verify.Sim.Step(default);
            for (int tick = 0; tick < 600; tick++)
            {
                var before = verify.Points(); verify.Sim.Step(default);
                for (int i = 0; i < count; i++)
                {
                    Assert.That(verify.Intent(i).Active, Is.True);
                    Assert.That(verify.Point(i), Is.Not.EqualTo(before[i]), $"tick={tick + 101} key={i + 1}");
                }
            }
            Assert.That(verify.Sim.StateHashRetained(), Is.EqualTo(world.Sim.StateHashRetained()));
            double sum = 0; foreach (double t in times) sum += t;
            Array.Sort(times);
            TestContext.Out.WriteLine($"M4 performance n={count} ticks=600 warmup=100 mean={sum / 600:F6}ms p95={times[569]:F6}ms max={times[599]:F6}ms allocated={allocated}B includes cell-change A*; no bridge/hash/assertions in measurement");
            Assert.That(allocated, Is.Zero);
        }
    }
}
