using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Mt198GameplayWorldTests
    {
        [Test]
        public void BindingAfterFirstStepRejectsInitializationOnlyOperation()
        {
            var sim = M0c2S1Fixture.Create();
            EntityId target = sim.Entities.Resolve(new EntityLink(M0c2S1Fixture.Target.Id));
            sim.Step(default);

            var error = Assert.Throws<SimBoundaryException>(() => sim.BindGameplay(target, 1));
            Assert.That(error.Message, Is.EqualTo("M0C_BIND_INITIALIZATION_ONLY"));
        }

        [Test]
        public void ResourceChangeOutsideQueryPhaseRejectsWrite()
        {
            var sim = M0c2S1Fixture.Create();
            sim.Step(default);
            var target = sim.GameplayTarget(M0c2S1Fixture.Target.Id);
            var key = new GameplayApplyKey(target.Id, 1, 0, target.Id, 1, 1);

            var error = Assert.Throws<SimBoundaryException>(() => sim.Context.ChangeResource(target, key, 1, 1));
            Assert.That(error.Message, Is.EqualTo("M0C_GAMEPLAY_QUERY_PHASE"));
        }

        [Test]
        public void ExplicitTargetWithoutPositionReportsInvalidTarget()
        {
            var sim = M0c2S3Fixture.Create();
            EntityId target = sim.Entities.Resolve(new EntityLink(M0c2S3Fixture.Target.Id));
            sim.Entities.Remove<Position>(target);

            M0c2S3Fixture.Input(sim, M0c2S3Fixture.Command(6));

            Assert.That(sim.GameplayInputResultAt(0).Failure, Is.EqualTo(GameplayAbilityFailure.InvalidTarget));
        }
    }
}
