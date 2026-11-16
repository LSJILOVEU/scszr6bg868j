using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3")]
    public sealed class Mt111GameplayAbilityProtocolTests
    {
        [Test]
        public void FirstPlacedOwnerRejectsCastNextFromAnotherGeneration()
        {
            var key=M0c2S3Fixture.Actor;
            var catalog=new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(73,1,1));
            var initial=new InitialPlacedState(catalog.Digest,new int[0],new[]{new PlacedGeneration(0,1)});
            var sim=Simulation.CreateRetainedWorld(73,catalog,new SimConfig(entityCapacity:8,timerCapacity:32),
                initial:initial,gameplaySchema:M0c2S3Fixture.Schema());
            sim.BindGameplay(sim.Entities.Create(key),1);sim.Step(default);
            var document=new M0c2S2ProtocolTests.Document(sim);
            Assert.That(catalog.Find(key.Id),Is.Zero);
            Assert.That(M0c2S2ProtocolTests.Word(document.Tables[2][0],9),Is.EqualTo(1UL));
            Assert.That(M0c2S2ProtocolTests.Word(document.Tables[2][0],25),Is.EqualTo(1UL<<48));
            M0c2S2ProtocolTests.Put(document.Tables[2][0],25,0);
            var failure=Assert.Throws<SimSnapshotException>(()=>sim.RestoreRetainedSnapshot(document.Encode(),true));
            StringAssert.Contains("M0C_OWNER_STATE",failure.Message);
        }
        [Test]
        public void NonPlacedOwnerRestoresAnActiveCast()
        {
            var key=StableEntityKey.World(73,0,90);
            var ability=new GameplayAbilityDef(1,"active-cast",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,1)},activeTicks:2);
            var schema=M0c2S3Fixture.Schema(abilities:new[]{ability},authority:new GameplayInputAuthority(1,new[]{key.Id}));
            var catalog=new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(73,6,1));
            var sim=Simulation.CreateRetainedWorld(73,catalog,new SimConfig(entityCapacity:8,timerCapacity:32),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(key),1);
            M0c2S3Fixture.Input(sim,new GameplayInputCommand(new GameplayStamp(key.Id),1,GameplayInputKind.Activate,1));
            Assert.That(catalog.Find(key.Id),Is.EqualTo(-1));
            Assert.That(sim.GameplayCastCount,Is.EqualTo(1));
            byte[] snapshot=sim.SaveRetainedSnapshot();
            Assert.DoesNotThrow(()=>sim.RestoreRetainedSnapshot(snapshot,true));
            Assert.That(sim.GameplayCastCount,Is.EqualTo(1));
            Assert.That(sim.GameplayCastAt(0).Token,Is.EqualTo(0UL));
            Assert.That(sim.GameplayCastAt(0).Phase,Is.EqualTo(GameplayCastPhase.Active));
            Assert.That(sim.SaveRetainedSnapshot(),Is.EqualTo(snapshot));
        }
    }
}
