using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S2")]
    public sealed class Mt193GameplayStatusAdmissionTests
    {
        [Test] public void MaximumDispelCountIsAcceptedAndRemovesMatchingStatus()
        {
            var sim=M0c2S2Fixture.CreateWith(out S2Script script,new[]{
                new S2Tick(new[]{S2Op.Apply(1)}),
                new S2Tick(new[]{S2Op.Dispel(1,maximum:GameplayMath.MaxPackets)})});
            M0c2S2Fixture.Step(sim);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            Assert.DoesNotThrow(()=>M0c2S2Fixture.Step(sim));
            Assert.That(script.LastFailure,Is.EqualTo(GameplayFailure.None));
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(0));
        }
        [Test] public void StatusStrengthAboveMaximumMagnitudeIsRejected()
        {
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{
                new S2Tick(new[]{S2Op.Apply(4,strength:GameplayMath.MaxMagnitude+1)})});
            Assert.That(Assert.Throws<SimBoundaryException>(()=>M0c2S2Fixture.Step(sim)).Message,
                Is.EqualTo("M0C_GAMEPLAY_MAGNITUDE: Outside [0,M]."));
        }
    }
}
