using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S4")]
    public sealed class Mt187GameplayLifeTests
    {
        [Test] public void UnknownOwnerRejectsLifeQuery()
        {
            var s=M0c2S4Fixture.Create();
            var failure=Assert.Throws<SimBoundaryException>(()=>s.GameplayLife(StableEntityKey.Placed(97,3).Id));
            Assert.That(failure.Message,Is.EqualTo("M0C_LIFE_OWNER"));
        }
        [Test] public void DeathCancelsRegenerationAndSnapshotRestores()
        {
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"health",-100,100,100,regenAmountRaw:1,regenPeriodTicks:4,isLife:true)},
                new[]{new GameplayDamageTypeDef(1,"direct",1,1)},new[]{new GameplayPipelineDef(1,new GameplayDamageSpec(0,GameplayDamageStages.None))},
                new[]{new GameplayArchetypeDef(1,new[]{1UL})},new GameplayCapacities(4,4,32,64),"life.critical",
                lifeRules:new[]{new GameplayLifeRuleDef(1,1,false,0)});
            var s=M0c2S4Fixture.Create(schema:schema);M0c2S4Fixture.Step(s);
            Assert.That(s.Scheduler.PendingCount,Is.EqualTo(2));
            M0c2S4Fixture.Step(s,1);
            Assert.That(M0c2S4Fixture.Life(s).State,Is.EqualTo(GameplayLifeState.Dead));
            Assert.That(M0c2S4Fixture.Health(s),Is.EqualTo(-10));
            Assert.That(s.Scheduler.PendingCount,Is.EqualTo(1));
            var snapshot=s.SaveRetainedSnapshot();var restored=M0c2S4Fixture.Create(reverse:true,schema:schema);
            Assert.DoesNotThrow(()=>restored.RestoreRetainedSnapshot(snapshot,true));
            Assert.That(restored.StateHashRetained(),Is.EqualTo(s.StateHashRetained()));
        }
        [Test] public void TwoDownedOwnersRestoreAndExpireIndependently()
        {
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"health",-100,100,0,isLife:true)},
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],new[]{new GameplayArchetypeDef(1,new[]{1UL})},
                new GameplayCapacities(4,4,32,64),"life.critical",lifeRules:new[]{new GameplayLifeRuleDef(1,1,true,4)});
            var s=M0c2S4Fixture.Create(schema:schema);M0c2S4Fixture.Step(s);
            Assert.That(s.GameplayLife(M0c2S4Fixture.Actor.Id).State,Is.EqualTo(GameplayLifeState.Downed));
            Assert.That(s.GameplayLife(M0c2S4Fixture.Other.Id).State,Is.EqualTo(GameplayLifeState.Downed));
            Assert.That(s.GameplayLife(M0c2S4Fixture.Actor.Id).DownedDueTick,Is.EqualTo(5));
            Assert.That(s.GameplayLife(M0c2S4Fixture.Other.Id).DownedDueTick,Is.EqualTo(5));
            Assert.That(s.Scheduler.PendingCount,Is.EqualTo(2));
            var snapshot=s.SaveRetainedSnapshot();var restored=M0c2S4Fixture.Create(reverse:true,schema:schema);
            Assert.DoesNotThrow(()=>restored.RestoreRetainedSnapshot(snapshot,true));
            Assert.That(restored.StateHashRetained(),Is.EqualTo(s.StateHashRetained()));
            for(int tick=2;tick<=5;tick++){M0c2S4Fixture.Step(s);M0c2S4Fixture.Step(restored);}
            Assert.That(restored.GameplayLife(M0c2S4Fixture.Actor.Id).State,Is.EqualTo(GameplayLifeState.Dead));
            Assert.That(restored.GameplayLife(M0c2S4Fixture.Other.Id).State,Is.EqualTo(GameplayLifeState.Dead));
            Assert.That(restored.Scheduler.PendingCount,Is.Zero);
            Assert.That(restored.StateHashRetained(),Is.EqualTo(s.StateHashRetained()));
        }
    }
}
