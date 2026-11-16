using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt099StableSpawnsTests
    {
        [Test]
        public void DuplicateFirstRequestReportsSpawnKeyError()
        {
            var sim = Simulation.CreateRetainedWorld(1,new SimConfig(entityCapacity:4,spawnRequestCapacity:2));
            var key = new StableRequestKey(1,1,new StableId(1),0,0,1);
            sim.Spawns.EnqueueWorld(0,key);

            var error = Assert.Throws<SimBoundaryException>(() => sim.Spawns.EnqueueWorld(0,key));

            Assert.That(error.Message,Is.EqualTo("Duplicate spawn request key."));
            Assert.That(sim.Spawns.PendingCount,Is.EqualTo(1));
        }

        [Test]
        public void CommitCanFillLastWorldCursorSlot()
        {
            var sim = Simulation.CreateRetainedWorld(1,new SimConfig(entityCapacity:4,worldCursorCapacity:2));
            sim.Spawns.EnqueueWorld(0,new StableRequestKey(1,1,new StableId(1),0,0,1));
            sim.Spawns.Commit();
            sim.Spawns.EnqueueWorld(0,new StableRequestKey(2,1,new StableId(2),0,0,1));

            Assert.DoesNotThrow(() => sim.Spawns.Commit());

            Assert.That(sim.Entities.RegisteredIdentityCount,Is.EqualTo(2));
            Assert.That(sim.Spawns.PendingCount,Is.Zero);
        }
    }
}
