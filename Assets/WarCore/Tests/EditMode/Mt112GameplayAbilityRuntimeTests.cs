using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Behavior")]
    public sealed class Mt112GameplayAbilityRuntimeTests
    {
        [TestCase(5,GameplayAbilityFailure.Cooldown),TestCase(6,GameplayAbilityFailure.InsufficientResource)]
        public void RetainedCooldownRowAllowsResourceCheckAtReadyTick(int tick,GameplayAbilityFailure expected)
        {
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(energy:2));
            var command=M0c2S3Fixture.Command(4);
            M0c2S3Fixture.Input(sim,command);
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.Zero);

            // Isolate admission from expiry cleanup while retaining the existing cooldown row.
            sim.Clock.Restore(tick);
            Assert.That(sim.GameplayCooldownReady(M0c2S3Fixture.Actor.Id,4),Is.EqualTo(6));
            var state=sim.Entities.Gameplay;
            state.CaptureAbilityInput(new InputFrame(default,default,gameplayTick:tick,skillCommands:new[]{command}),tick);
            state.RunAbilityQ0();
            Assert.That(sim.GameplayInputResultCount,Is.EqualTo(1));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(expected));
        }
    }
}
