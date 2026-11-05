using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableClosureTests
    {
        [Test]
        public void NonemptyTriggerRegistryHashHasZeroDirectAllocation()
        {
            Simulation sim = StableSystemTests.Make(false);
            for (int i = 0; i < 32; i++)
            {
                sim.Step(default);
                sim.StateHashStable();
            }
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Strict zero needs a direct positive control.");
            var before = meter.Read();
            for (int i = 0; i < 300; i++)
            {
                sim.Step(default);
                sim.StateHashStable();
            }
            long bytes = meter.Difference(before).Bytes;
            TestContext.WriteLine("Stable nonempty trigger/control/posture: samples=300; direct_bytes="+bytes);
            Assert.That(bytes,Is.Zero);
        }
        [Test]
        public void SpawnSequenceIsStoreOwnedThroughAllGenericWriteApis()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,1));
            ComponentType<SpawnSequence> counter = sim.Entities.Resolve<SpawnSequence>();
            Assert.Throws<SimBoundaryException>(() => sim.Entities.Set(counter,entity,new SpawnSequence(9)));
            Assert.Throws<SimBoundaryException>(() => sim.Entities.Get(counter,entity));
            Assert.Throws<SimBoundaryException>(() => sim.Entities.Remove(counter,entity));
            Assert.That(sim.Spawns.Counter(sim.Entities.Link(entity)),Is.Zero);
        }
        [Test]
        public void LateActivationPlanFailsBeforeTimelineMutation()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            sim.Step(default);
            sim.Step(default);
            ulong before = sim.StateHashStable();
            Assert.Throws<SimBoundaryException>(() => sim.Activations.Plan(1,8));
            Assert.That(sim.StateHashStable(),Is.EqualTo(before));
        }
        [Test]
        public void StableSoundCapturesIdentityAcrossDeathAndSlotReuse()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld,identityCapacity:8);
            EntityId entity = sim.Entities.Create(StableEntityKey.Placed(1,1));
            EntityLink link = sim.Entities.Link(entity);
            var sound = SoundEvent.Stable(default,SimFix.FromInt(5),2,19,link);
            StableSimEvent saved = sound.ToStableEvent(new EventTypeId(100));
            sim.Entities.Destroy(entity);
            sim.Step(default);
            EntityId reused = sim.Entities.Create(StableEntityKey.Placed(1,2));
            Assert.That(reused.Id,Is.EqualTo(entity.Id));
            SoundEvent restored = SoundEvent.FromStableEvent(saved,new EventTypeId(100));
            Assert.That(restored.SourceLink,Is.EqualTo(link));
            Assert.That(restored.TypeTag,Is.EqualTo(19));
            Assert.Throws<SimBoundaryException>(() => restored.ToEvent(new EventTypeId(100)));
        }
    }
}
