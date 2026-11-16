using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Snap1SnapshotTests
    {
        [Test]
        public void PendingSpawnAfterChunkChangeRestoresToTheSameRun()
        {
            var uninterrupted = new Simulation(1,identityMode:IdentityMode.StableWorld);
            EntityId parent = uninterrupted.Entities.Create(StableEntityKey.Placed(7,0));
            EntityLink source = uninterrupted.Entities.Link(parent);
            uninterrupted.Step(default);
            uninterrupted.Spawns.Enqueue(source,new StableRequestKey(1,0,source.Id,0,0,9));
            uninterrupted.Entities.MoveToChunk(parent,99);
            Assert.That(uninterrupted.Clock.Tick,Is.EqualTo(1));
            Assert.That(uninterrupted.Spawns.PendingCount,Is.EqualTo(1));
            var restored = new Simulation(1,identityMode:IdentityMode.StableWorld);
            restored.RestoreStableSnapshot(uninterrupted.SaveStableSnapshot(),true);
            uninterrupted.Step(default);
            restored.Step(default);
            Assert.That(restored.StateHashStable(),Is.EqualTo(uninterrupted.StateHashStable()),"continued tick=2");
            StableId child = StableEntityKey.Spawned(source.Id,0,9).Id;
            Assert.That(uninterrupted.Entities.ChunkOf(child),Is.EqualTo(7));
            Assert.That(restored.Entities.ChunkOf(child),Is.EqualTo(7));
        }

        private static Simulation Pending(ulong counter, SpawnValues values)
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            EntityId parent = sim.Entities.Create(StableEntityKey.Placed(7,0));
            EntityLink source = sim.Entities.Link(parent);
            sim.Entities.SetSpawnSequence(parent,counter);
            sim.Spawns.Enqueue(source,new StableRequestKey(1,0,source.Id,0,0,9),values);
            sim.Entities.SetSpawnSequence(parent,12);
            return sim;
        }

        [TestCase(true), TestCase(false)]
        public void CapturedPayloadAffectsPendingHashAndSurvivesRestore(bool changeCounter)
        {
            var values = new SpawnValues(SimVec3.Metres(4,5,6),SimVec3.Metres(0,0,1),17);
            var original = Pending(3,values);
            var different = Pending(changeCounter ? 4UL : 3UL,changeCounter ? values : default);
            ulong hash = original.StateHashStable();
            Assert.That(different.StateHashStable(),Is.Not.EqualTo(hash));
            byte[] snapshot = original.SaveStableSnapshot();
            original.RestoreStableSnapshot(snapshot,true);
            Assert.That(original.StateHashStable(),Is.EqualTo(hash));
            Assert.That(original.SaveStableSnapshot(),Is.EqualTo(snapshot));
        }

        [Test]
        public void SnapshotVersionSixRejectsVersionFive()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            byte[] snapshot = sim.SaveStableSnapshot();
            var reader = new SimStateReader(snapshot,sim.Entities);
            reader.Int32();
            int versionOffset = snapshot.Length-reader.Remaining;
            Assert.That(reader.Int32(),Is.EqualTo(6));
            snapshot[versionOffset] = 5;
            Assert.That(Assert.Throws<SimSnapshotException>(() => sim.RestoreStableSnapshot(snapshot)).Message,
                Is.EqualTo("Stable snapshot version mismatch."));
        }
    }
}
