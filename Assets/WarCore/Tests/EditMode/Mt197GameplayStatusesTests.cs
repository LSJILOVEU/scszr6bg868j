using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Mt197GameplayStatusesTests
    {
        [TestCase(false),TestCase(true)] public void RetiringHigherOwnerRemovesItsFirstStatusAndTasks(bool reverse)
        {
            var sim=M0c2S4Fixture.Create(reverse:reverse);
            M0c2S4Fixture.Step(sim,512);M0c2S4Fixture.Step(sim,8);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(2));
            var low=M0c2S4Fixture.Actor.Id;var high=M0c2S4Fixture.Other.Id;
            if(low.CompareTo(high)>0){var swap=low;low=high;high=swap;}
            sim.Entities.Destroy(sim.Entities.Resolve(new EntityLink(high)));
            sim.CompleteSpatialBoundary();
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            Assert.That(sim.GameplayStatusAt(0).Target.Id,Is.EqualTo(low));
            Assert.That(sim.Scheduler.PendingCount,Is.EqualTo(2));
        }
        [TestCase(GameplayStackPolicy.Refresh,12),TestCase(GameplayStackPolicy.Strongest,7)]
        public void AbsorbMergeRespectsStackPolicy(GameplayStackPolicy policy,long expected)
        {
            var definition=new GameplayStatusDef(1,"shield",GameplayStatusDuration.Timed,4,
                stackPolicy:policy,absorbResourceKey:1);
            // The lower source key wins an equal-strength replacement.
            var sim=M0c2S2Fixture.CreateWith(out S2Script script,new[]{new S2Tick(new[]{S2Op.Absorb(1,true,5)}),
                new S2Tick(new[]{S2Op.Absorb(1,false,7)})},statuses:new[]{definition});
            M0c2S2Fixture.Step(sim);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            Assert.That(sim.GameplayStatusAt(0).AbsorbRemainingRaw,Is.EqualTo(5));
            M0c2S2Fixture.Step(sim);
            Assert.That(script.Rejections,Is.Zero);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            Assert.That(sim.GameplayStatusAt(0).AbsorbRemainingRaw,Is.EqualTo(expected));
        }
    }
}
