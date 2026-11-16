using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt219StableActivationsTests
    {
        [Test]
        public void DuplicateActivationPlanIsRejected()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            sim.Activations.Plan(1,1);
            Assert.Throws<SimBoundaryException>(() => sim.Activations.Plan(1,1));
        }

        [TestCase(0L),TestCase(-1L)]
        public void NonpositiveActivationBoundaryIsRejected(long tick)
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            Assert.Throws<SimBoundaryException>(() => sim.Activations.ReadyFor(tick));
        }

        [Test]
        public void RestoreRejectsDuplicateTickAndChunkPairs()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            var writer = new SimStateWriter(sim.Entities,materialize:true);
            writer.Int32(2);
            writer.Int32(0);
            writer.Int64(1);
            writer.UInt64(1);
            writer.Int64(1);
            writer.UInt64(1);
            var reader = new SimStateReader(writer.ToArray(),sim.Entities);
            Assert.Throws<SimSnapshotException>(() => sim.Activations.Load(reader,0));
        }
    }
}
