using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Snap1SnapshotTests
    {
        private sealed class LateSpawn : ISimSystem, IStableStateSerializer
        {
            public int Order => 1;
            public string StateTypeId => "test.snap1-late-spawn";
            public int StateVersion => 1;
            public void Step(SimContext context)
            {
                if (context.Clock.Tick != 1)
                    return;
                var source = new EntityLink(StableEntityKey.Placed(7,0).Id);
                context.Owner.Spawns.Enqueue(source,new StableRequestKey(1,0,source.Id,1,0,9));
                context.Entities.MoveToChunk(context.Entities.Resolve(source),99);
            }
            public void WriteState(StateHash hash) { }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
        }

        private static Simulation Scene()
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:8),identityMode:IdentityMode.StableWorld,identityCapacity:16);
            sim.Entities.Create(StableEntityKey.Placed(7,0));
            sim.Register("late-spawn",new LateSpawn(),SimSystemPhase.Queries);
            return sim;
        }

        [Test]
        public void PendingSpawnAfterChunkChangeContinuesWithTheSameHash()
        {
            Simulation continuous = Scene();
            Simulation restored = Scene();
            continuous.Step(default);
            Assert.That(continuous.Spawns.PendingCount,Is.EqualTo(1));
            restored.RestoreStableSnapshot(continuous.SaveStableSnapshot(),true);
            for (int tick = 2; tick <= 4; tick++)
            {
                continuous.Step(default);
                restored.Step(default);
                Assert.That(restored.StateHashStable(),Is.EqualTo(continuous.StateHashStable()),"continued tick="+tick);
            }
            StableId child = StableEntityKey.Spawned(StableEntityKey.Placed(7,0).Id,0,9).Id;
            Assert.That(restored.Entities.ChunkOf(child),Is.EqualTo(7));
        }

        [Test]
        public void PendingSnapshotKeepsCapturedCounterAndValues()
        {
            Simulation sim = Scene();
            var source = new EntityLink(StableEntityKey.Placed(7,0).Id);
            EntityId entity = sim.Entities.Resolve(source);
            sim.Entities.SetSpawnSequence(entity,7);
            sim.Spawns.Enqueue(source,new StableRequestKey(1,0,source.Id,1,0,9),
                new SpawnValues(SimVec3.Metres(4,5,6),SimVec3.Metres(0,0,1),17));
            sim.Entities.SetSpawnSequence(entity,11);
            byte[] snapshot = sim.SaveStableSnapshot();
            sim.RestoreStableSnapshot(snapshot,true);
            Assert.That(sim.SaveStableSnapshot(),Is.EqualTo(snapshot));

            var writer = new SimStateWriter(sim.Entities,materialize:true);
            sim.Spawns.Save(writer);
            var reader = new SimStateReader(writer.ToArray(),sim.Entities);
            reader.Int32();
            StableRequestKey.Read(reader);
            reader.Link();
            reader.Int64();
            Assert.That(reader.UInt64(),Is.EqualTo(7));
            Assert.That(reader.UInt64(),Is.EqualTo(7));
            SpawnValues values = SpawnValues.Read(reader);
            Assert.That(values.Position,Is.EqualTo(SimVec3.Metres(4,5,6)));
            Assert.That(values.Facing,Is.EqualTo(SimVec3.Metres(0,0,1)));
            Assert.That(values.Ownership,Is.EqualTo(17));
        }

        [Test]
        public void StableSnapshotsUseVersionSixAndRejectVersionFive()
        {
            Simulation sim = Scene();
            byte[] snapshot = sim.SaveStableSnapshot();
            var reader = new SimStateReader(snapshot,sim.Entities);
            reader.Int32();
            Assert.That(reader.Int32(),Is.EqualTo(6));
            snapshot[8] = 5;
            Assert.That(Assert.Throws<SimSnapshotException>(() => sim.RestoreStableSnapshot(snapshot)).Message,
                Is.EqualTo("Stable snapshot version mismatch."));
        }
    }
}
