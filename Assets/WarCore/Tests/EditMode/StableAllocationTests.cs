using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableAllocationTests
    {
        [Test]
        public void WarmStableHashStepQueriesAndSchedulerAllocateStrictlyZeroWithPositiveControl()
        {
            var sim = StableSnapshotTests.Scene(false);
            sim.Rng.NextUInt64(sim.Rng.Stream("stable-shared"));
            var output = new SpatialHit[16];
            var request = SpatialQueries.Sphere(default,SimFix.FromInt(100));
            var type = new EventTypeId(200);
            for (int i = 0; i < 24; i++)
            {
                sim.Scheduler.ScheduleFor(sim.Clock.Tick+1,1,new StableId(1),1,new StableSimEvent(type));
                sim.Step(default);
                sim.Scheduler.TryDequeueStable(out _);
                sim.SpatialQuery.AllCapped(request,16,output);
                sim.StateHashStable();
            }
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Stable strict zero requires a direct meter with a positive control.");
            Assert.That(meter.DirectControlDelta.Bytes,Is.GreaterThanOrEqualTo(32768));
            long total = 0;
            long maximum = 0;
            ulong checksum = 0;
            for (int i = 0; i < 300; i++)
            {
                var before = meter.Read();
                sim.Scheduler.ScheduleFor(sim.Clock.Tick+1,1,new StableId(1),1,new StableSimEvent(type));
                sim.Step(default);
                sim.Scheduler.TryDequeueStable(out _);
                sim.SpatialQuery.AllCapped(request,16,output);
                checksum ^= sim.StateHashStable();
                var sample = meter.Difference(before);
                total += sample.Bytes;
                maximum = Math.Max(maximum,sample.Bytes);
            }
            TestContext.WriteLine("Stable allocation: direct; samples=300; control_bytes={0}; total_bytes={1}; max_bytes={2}; checksum=0x{3:X16}",
                meter.DirectControlDelta.Bytes,total,maximum,checksum);
            Assert.That(total,Is.Zero);
            Assert.That(maximum,Is.Zero);
        }
    }
}
