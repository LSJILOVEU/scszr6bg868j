using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S4")]
    public sealed class Mt186GameplayLifeTests
    {
        [Test] public void ActiveOwnersReserveFutureDownedTimers()
        {
            var s=M0c2S4Fixture.Create(timers:4);M0c2S4Fixture.Step(s,0,M0c2S4Fixture.Cast());
            Assert.That(s.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.TimerCapacity));
            Assert.That(s.GameplayCastCount,Is.Zero);Assert.That(s.Scheduler.PendingCount,Is.Zero);
        }
        [TestCase(true,GameplayLifeState.Downed),TestCase(false,GameplayLifeState.Dead)]
        public void ActiveOwnerTransitionsAtLifeThreshold(bool downed,GameplayLifeState expected)
        {
            var s=M0c2S4Fixture.Create(schema:M0c2S4Fixture.Schema(downed:downed));M0c2S4Fixture.Step(s,1|2048);
            Assert.That(M0c2S4Fixture.Health(s),Is.Zero);Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(expected));
            Assert.That(M0c2S4Fixture.Life(s).EnteredTick,Is.EqualTo(1));
        }
    }
}
