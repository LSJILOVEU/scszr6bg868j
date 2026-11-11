using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Snap1SpawnSnapshotTests
    {
        [Test]
        public void PendingSpawnKeepsCapturedChunkAfterRestore()
        {
            var a = new Simulation(1,config:new SimConfig(entityCapacity:4),identityMode:IdentityMode.StableWorld);
            var b = new Simulation(1,config:new SimConfig(entityCapacity:4),identityMode:IdentityMode.StableWorld);
            EntityId parent = a.Entities.Create(StableEntityKey.Placed(7,1));
            EntityLink source = a.Entities.Link(parent);
            a.Step(default);
            a.Spawns.Enqueue(source,new StableRequestKey(1,0,source.Id,1,0,9));
            a.Entities.MoveToChunk(parent,99);
            Assert.That(a.Spawns.PendingCount,Is.EqualTo(1));
            b.RestoreStableSnapshot(a.SaveStableSnapshot());
            a.Step(default);
            b.Step(default);
            Assert.That(a.Entities.ChunkOf(StableEntityKey.Spawned(source.Id,0,9).Id),Is.EqualTo(7));
            Assert.That(b.StateHashStable(),Is.EqualTo(a.StateHashStable()),"continued tick=2");
        }

        [TestCase(false), TestCase(true)]
        public void PendingSnapshotPreservesCounterAndValues(bool world)
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:4),identityMode:IdentityMode.StableWorld);
            EntityId parent = sim.Entities.Create(StableEntityKey.Placed(7,1));
            EntityLink source = sim.Entities.Link(parent);
            var key = new StableRequestKey(1,0,source.Id,1,0,9);
            var values = new SpawnValues(SimVec3.Metres(2,3,4),SimVec3.Metres(0,0,1),17);
            sim.Entities.SetSpawnSequence(parent,4);
            if (world)
                sim.Spawns.EnqueueWorld(0,key,values);
            else
                sim.Spawns.Enqueue(source,key,values);
            sim.Entities.SetSpawnSequence(parent,9);
            byte[] snapshot = sim.SaveStableSnapshot();
            ulong hash = sim.StateHashStable();
            sim.RestoreStableSnapshot(snapshot);
            Assert.That(sim.StateHashStable(),Is.EqualTo(hash),"pending captures");
            Assert.That(sim.SaveStableSnapshot(),Is.EqualTo(snapshot));
        }

        [Test]
        public void SnapshotVersionSixRejectsVersionFive()
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:4),identityMode:IdentityMode.StableWorld);
            byte[] snapshot = sim.SaveStableSnapshot();
            Assert.That(snapshot[8],Is.EqualTo(6));
            snapshot[8] = 5;
            Assert.That(Assert.Throws<SimSnapshotException>(() => sim.RestoreStableSnapshot(snapshot)).Message,
                Is.EqualTo("Stable snapshot version mismatch."));
        }
    }
}
