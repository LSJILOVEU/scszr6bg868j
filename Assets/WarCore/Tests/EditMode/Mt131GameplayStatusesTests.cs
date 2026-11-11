using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("Mt131GameplayStatuses")]
    public sealed class Mt131GameplayStatusesTests
    {
        [Test] public void ExpiryPublishesRemovalNoticeAtBoundary()
        {
            var sim=M0c2S4Fixture.Create();M0c2S4Fixture.Step(sim,16);
            for(int tick=2;tick<=12;tick++)M0c2S4Fixture.Step(sim);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            Assert.That(sim.Events.Read(12).Length,Is.EqualTo(0));
            M0c2S4Fixture.Step(sim);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(0));
            Assert.That(sim.Events.Read(13).Length,Is.EqualTo(2));
            Assert.That(sim.Events.Read(13)[0].Type,Is.EqualTo(GameplayEvents.StatusExpiry));
            var notice=((SimEventBuffer)sim.Events).ReadStable(13,1);
            Assert.That(notice.Type,Is.EqualTo(GameplayEvents.StatusChanged));
            Assert.That(notice.Source.Id,Is.EqualTo(M0c2S4Fixture.Other.Id));
            Assert.That(notice.Target.Id,Is.EqualTo(M0c2S4Fixture.Actor.Id));
            Assert.That(notice.Payload.Tag,Is.EqualTo(0));
        }
        [Test] public void RetainedPulseIsRejectedAtExactExpiry()
        {
            var definition=new GameplayStatusDef(1,"periodic",GameplayStatusDuration.Timed,2,1,1,
                periodicActions:new[]{GameplayPeriodicAction.Damage(1,1,3)});
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Apply(1)})},statuses:new[]{definition});
            M0c2S2Fixture.Step(sim);
            sim.Scheduler.Advance(2);sim.Scheduler.Advance(3);sim.Clock.Restore(3);
            Assert.That(sim.Scheduler.TryDequeueStable(GameplayEvents.StatusExpiry,out _),Is.True);
            Assert.That(sim.Scheduler.DueCount,Is.EqualTo(1));
            Assert.That(Assert.Throws<SimBoundaryException>(()=>sim.Entities.Gameplay.BeginResources()).Message,
                Is.EqualTo("M0C_STATUS_EXPIRED_PULSE"));
        }
    }
}
