using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S1"),Category("M0c2S4")]
    public sealed class Mt189GameplayResourcesTests
    {
        [Test] public void SpendingOnRetiredTargetReportsTargetGone()
        {
            var sim=M0c2S1Fixture.CreateWith(out S1Script script);
            sim.Entities.Destroy(sim.Entities.Resolve(new EntityLink(M0c2S1Fixture.Target.Id)));
            sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:2));
            Assert.That(script.LastFailure,Is.EqualTo(GameplayFailure.TargetGone));
        }

        [Test] public void DisabledSpendingRejectsBeforeAnyDebit()
        {
            var schema=new GameplaySchema(new[]{new GameplayResourceDef(1,"health",0,100,100,allowSpend:false),
                    new GameplayResourceDef(2,"energy",0,2000*M0c2S1Fixture.S,2000*M0c2S1Fixture.S)},
                new GameplayDamageTypeDef[0],new GameplayPipelineDef[0],
                new[]{new GameplayArchetypeDef(1,new[]{1UL,2UL})},new GameplayCapacities(1,2,8,16),"test.critical");
            var sim=Simulation.CreateRetainedWorld(41,
                new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(17,1,1)),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(M0c2S1Fixture.Target),1);
            sim.RegisterGameplay("s1.data-program",new S1Script(false,0,0),SimSystemPhase.Queries);

            var error=Assert.Throws<SimBoundaryException>(()=>
                sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:2)));
            Assert.That(error.Message,Is.EqualTo("M0C_COST_DISABLED"));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,1),Is.EqualTo(100));
            Assert.That(sim.GameplayResource(M0c2S1Fixture.Target.Id,2),Is.EqualTo(2000*M0c2S1Fixture.S));
        }

        [Test] public void RetirementCancelsPendingDownedTimer()
        {
            var sim=M0c2S4Fixture.Create();
            M0c2S4Fixture.Step(sim,1);
            Assert.That(M0c2S4Fixture.Life(sim).State,Is.EqualTo(GameplayLifeState.Downed));
            Assert.That(sim.Scheduler.PendingCount,Is.EqualTo(1));

            sim.Entities.Destroy(sim.Entities.Resolve(new EntityLink(M0c2S4Fixture.Actor.Id)));
            sim.CompleteSpatialBoundary();

            Assert.That(sim.Scheduler.PendingCount,Is.Zero);
        }
    }
}
