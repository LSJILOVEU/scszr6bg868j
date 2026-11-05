using System;
using System.Diagnostics;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M3PathPerformanceTests
    {
        [Test]
        public void DemoHundredQueries_WarmedMeanUnderOneMillisecond_ZeroAllocation()
        {
            NavGrid grid = WcMapNavigation.Create(M2DemoMap.Build());
            var search = new GridPathfinder(grid);
            var random = new Random(707);
            var starts = new int[100]; var targets = new int[100];
            int OpenCell()
            {
                int cell;
                do { cell = random.Next(grid.Count); } while (!grid.IsPassable(cell % grid.Width, cell / grid.Width));
                return cell;
            }
            for (int i = 0; i < 100; i++) { starts[i] = OpenCell(); targets[i] = OpenCell(); }
            for (int pass = 0; pass < 5; pass++)
                for (int i = 0; i < 100; i++) search.Find(starts[i], targets[i]);
            var times = new double[100];
            for (int i = 0; i < 100; i++)
            {
                long begin = Stopwatch.GetTimestamp();
                search.Find(starts[i], targets[i]);
                times[i] = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int pass = 0; pass < 10; pass++)
                for (int i = 0; i < 100; i++) search.Find(starts[i], targets[i]);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            double sum = 0;
            foreach (double t in times) sum += t;
            Array.Sort(times);
            TestContext.Out.WriteLine($"M3 demo queries=100 mean={sum / 100:F6}ms p95={times[94]:F6}ms max={times[99]:F6}ms allocated(1000 queries)={allocated}B");
            Assert.That(sum / 100, Is.LessThan(1.0));
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void MaximumGrid_ClosedTargetLargeSearch_RecordsSingleTime()
        {
            const int width = 256;
            var bits = new byte[8192];
            for (int i = 0; i < bits.Length; i++) bits[i] = 255;
            // Seal a passable target in the northeast corner; exhaust the other 65532 open cells.
            foreach (int blocked in new[] { 65534, 65279, 65278 }) bits[blocked / 8] &= (byte)~(1 << (blocked % 8));
            var grid = new NavGrid(width, width, default, default, bits);
            var search = new GridPathfinder(grid);
            long begin = Stopwatch.GetTimestamp();
            GridPathStatus result = search.Find(0, 65535);
            double elapsed = (Stopwatch.GetTimestamp() - begin) * 1000.0 / Stopwatch.Frequency;
            TestContext.Out.WriteLine($"M3 256x256 closed target first solve={elapsed:F6}ms");
            Assert.That(result, Is.EqualTo(GridPathStatus.Unreachable));
            long before = GC.GetAllocatedBytesForCurrentThread();
            search.Find(0, 65535);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }
    }
}
