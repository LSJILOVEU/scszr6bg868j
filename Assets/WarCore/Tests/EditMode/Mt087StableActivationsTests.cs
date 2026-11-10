using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt087StableActivationsTests
    {
        [Test]
        public void PlanRejectsAdvancedBoundary()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            sim.Step(default);
            Assert.That(sim.Clock.Tick,Is.EqualTo(1));
            Assert.Throws<SimBoundaryException>(() => sim.Activations.Plan(1,8));
        }

        [Test]
        public void PlanRejectsDuplicateTickAndChunk()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            sim.Activations.Plan(1,8);
            Assert.Throws<SimBoundaryException>(() => sim.Activations.Plan(1,8));
        }

        [Test]
        public void SameTickChunksAreEncodedInAscendingOrder()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            sim.Activations.Plan(1,2);
            sim.Activations.Plan(1,1);
            var writer = new SimStateWriter(sim.Entities,materialize:true);
            sim.Activations.Save(writer);
            var reader = new SimStateReader(writer.ToArray(),sim.Entities);
            Assert.That(reader.Int32(),Is.EqualTo(2));
            Assert.That(reader.Int32(),Is.Zero);
            Assert.That(reader.Int64(),Is.EqualTo(1));
            Assert.That(reader.UInt64(),Is.EqualTo(1UL));
            Assert.That(reader.Int64(),Is.EqualTo(1));
            Assert.That(reader.UInt64(),Is.EqualTo(2UL));
            Assert.That(reader.Remaining,Is.Zero);
        }

        [Test]
        public void ReadyForRejectsZero()
        {
            var sim = new Simulation(1,identityMode:IdentityMode.StableWorld);
            Assert.Throws<SimBoundaryException>(() => sim.Activations.ReadyFor(0));
        }
    }
}
