using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4Guards")]
    public sealed class Round4GuardTests
    {
        [Test]
        public void RetainedStoreCannotSelectLegacyOrInfiniteRetention()
        {
            Assert.Throws<SimBoundaryException>(() => new EntityStore(4,retainedProtocol:true));
            Assert.Throws<SimBoundaryException>(() => new EntityStore(4,IdentityMode.StableWorld,retainedProtocol:true));
        }

        [Test]
        public void ExpiredEntityCursorCannotEnterTheSystemCursorTableOnRestore()
        {
            var sim = Round4ProtocolTests.World();
            TaskKey key = sim.Scheduler.ScheduleFor(1,1,new StableId(500),1,new StableSimEvent(new EventTypeId(100)));
            sim.Scheduler.CancelStable(key);
            byte[] bytes = sim.SaveRetainedSnapshot();
            byte[] pattern = new byte[24];
            Array.Copy(BitConverter.GetBytes(1UL),0,pattern,0,8);
            Array.Copy(BitConverter.GetBytes(500UL),0,pattern,8,8);
            Array.Copy(BitConverter.GetBytes(1UL),0,pattern,16,8);
            int found = -1;
            int matches = 0;
            for (int i = 0; i <= bytes.Length-pattern.Length; i++)
            {
                bool equal = true;
                for (int j = 0; j < pattern.Length; j++)
                    equal &= bytes[i+j] == pattern[j];
                if (equal)
                {
                    found = i;
                    matches++;
                }
            }
            Assert.That(matches,Is.EqualTo(1));
            bytes[found] = 0;
            Assert.Throws<SimSnapshotException>(() => sim.RestoreRetainedSnapshot(bytes));
        }

        [Test]
        public void WarmSpawnMotionCleanupAndDiagnosticSortingAllocateZeroBytes()
        {
            var sim = Round4ProtocolTests.World(0,4,16);
            var positions = sim.Entities.Register<Position>("position",new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider",new ColliderSerializer());
            sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(4));
            var ids = new EntityId[4];
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Strict zero requires a direct meter with a positive control.");
            Assert.That(meter.DirectControlDelta.Bytes,Is.GreaterThanOrEqualTo(32768));
            meter.Difference(meter.Read());
            for (int iteration = 0; iteration < 132; iteration++)
            {
                AllocationSample before = meter.Read();
                for (int i = 0; i < ids.Length; i++)
                {
                    ids[i] = sim.Entities.Create(StableEntityKey.World(8,sim.Clock.Tick,(ulong)i));
                    sim.Entities.Set(positions,ids[i],new Position(SimVec3.Metres(i,0,0)));
                    sim.Entities.Set(colliders,ids[i],new Collider(SimFix.Ratio(1,4),SimFix.FromInt(2)));
                }
                for (ulong i = 0; i < 2; i++)
                {
                    sim.Attach(sim.Entities.Link(ids[i]),sim.Entities.Link(ids[i+2]),default,Round4DeathTests.Key(i));
                    sim.Spawns.Enqueue(sim.Entities.Link(ids[i+2]),Round4DeathTests.Key(i));
                }
                sim.Step(default);
                for (ulong i = 0; i < 2; i++)
                {
                    EntityLink host = sim.Entities.Link(ids[i+2]);
                    sim.RequestPosition(host,default,Round4DeathTests.Key(i));
                    sim.Entities.Destroy(ids[i+2]);
                    sim.Entities.Destroy(sim.Entities.Resolve(new EntityLink(StableEntityKey.Spawned(host.Id,0,9).Id)));
                }
                sim.Step(default);
                sim.StateHashRetained();
                for (int i = 0; i < 2; i++)
                    sim.Entities.Destroy(ids[i]);
                sim.CompleteSpatialBoundary();
                long allocated = meter.Difference(before).Bytes;
                if (iteration >= 4)
                    Assert.That(allocated,Is.Zero,"Direct measurement includes request sorting, cleanup, hashing, and two ticks.");
            }
            TestContext.WriteLine("Round4 requests: direct; warmup=4; samples=128; control_bytes={0}; total_bytes=0; maximum_bytes=0",meter.DirectControlDelta.Bytes);
        }
    }
}
