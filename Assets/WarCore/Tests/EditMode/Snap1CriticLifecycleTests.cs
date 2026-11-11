using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Manual")]
    public sealed class Snap1CriticLifecycleTests
    {
        [Test, Explicit]
        public void PendingSpawnFromDestroyedSourceRestoresToTheSameRun()
        {
            var uninterrupted = new Simulation(1,identityMode:IdentityMode.StableWorld);
            EntityId parent = uninterrupted.Entities.Create(StableEntityKey.Placed(7,0));
            EntityLink source = uninterrupted.Entities.Link(parent);
            uninterrupted.Spawns.Enqueue(source,new StableRequestKey(1,0,source.Id,0,0,9));
            uninterrupted.Step(default);
            uninterrupted.Spawns.Enqueue(source,new StableRequestKey(1,0,source.Id,0,0,10));
            uninterrupted.Entities.Destroy(parent);
            var restored = new Simulation(1,identityMode:IdentityMode.StableWorld);
            restored.RestoreStableSnapshot(uninterrupted.SaveStableSnapshot(),true);
            Assert.That(restored.StateHashStable(),Is.EqualTo(uninterrupted.StateHashStable()),"restored tick=1");
            for (int tick = 2; tick <= 4; tick++)
            {
                uninterrupted.Step(default);
                restored.Step(default);
                Assert.That(restored.StateHashStable(),Is.EqualTo(uninterrupted.StateHashStable()),"continued tick="+tick);
            }
        }
    }
}
