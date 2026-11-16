using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt225StableSpawnsTests
    {
        [Test]
        public void SnapshotStoresWorldCursorsInSystemOrderWithNextOrdinals()
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:4),identityMode:IdentityMode.StableWorld);
            for (ulong system = 2; system > 0; system--)
            {
                for (ulong ordinal = 0; ordinal < system; ordinal++)
                    sim.Spawns.EnqueueWorld(5,new StableRequestKey(system,1,new StableId(system),1,ordinal,1));
                sim.Spawns.Commit();
            }
            var writer = new SimStateWriter(sim.Entities,materialize:true);
            sim.Spawns.Save(writer);
            var reader = new SimStateReader(writer.ToArray(),sim.Entities);
            Assert.That(reader.Int32(),Is.Zero);
            Assert.That(reader.Int32(),Is.EqualTo(2));
            for (ulong system = 1; system <= 2; system++)
            {
                Assert.That(reader.UInt64(),Is.EqualTo(system));
                Assert.That(reader.Int64(),Is.EqualTo(5L));
                Assert.That(reader.UInt64(),Is.EqualTo(system));
            }
            Assert.That(reader.Remaining,Is.Zero);
        }

        [Test]
        public void TickZeroWorldCursorRestoresAndContinuesAtNextOrdinal()
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:4),identityMode:IdentityMode.StableWorld);
            sim.Spawns.EnqueueWorld(0,new StableRequestKey(1,1,new StableId(1),1,0,1));
            sim.Spawns.Commit();
            byte[] snapshot = sim.SaveStableSnapshot();
            Assert.DoesNotThrow(() => sim.RestoreStableSnapshot(snapshot,true));
            sim.Spawns.EnqueueWorld(0,new StableRequestKey(1,1,new StableId(1),1,1,1));
            Assert.DoesNotThrow(() => sim.Spawns.Commit());
            Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(2));
            Assert.That(sim.Entities.TryResolve(StableEntityKey.World(1,0,0).Id,out _),Is.True);
            Assert.That(sim.Entities.TryResolve(StableEntityKey.World(1,0,1).Id,out _),Is.True);
        }
    }
}
