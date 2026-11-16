using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
using static WarCore.Tests.M0c2S2Fixture;

namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S2Protocol")]
    public sealed class Mt129GameplayStatusProtocolTests
    {
        [Test] public void GroupedStatusRestoresMaximumSourceGeneration()
        {
            var definition=new GameplayStatusDef(1,"grouped",GameplayStatusDuration.Infinite,
                stackScope:GameplayStackScope.TargetAndSource);
            var catalog=new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(29,1,1),
                new PlacedCatalogEntry(29,2,1),new PlacedCatalogEntry(29,3,1));
            var initial=new InitialPlacedState(catalog.Digest,new int[0],
                new[]{new PlacedGeneration(catalog.Find(Low),ushort.MaxValue)});
            var sim=Simulation.CreateRetainedWorld(41,catalog,new SimConfig(entityCapacity:4,timerCapacity:16),
                initial:initial,gameplaySchema:Schema(statuses:new[]{definition}));
            foreach(var key in new[]{Target,SourceA,SourceB})sim.BindGameplay(sim.Entities.Create(key),1);
            sim.RegisterGameplay("s2.data-program",new S2Script(new[]{new S2Tick(new[]{S2Op.Apply(1)})},false,false,0),SimSystemPhase.Queries);
            Step(sim);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(1));
            Assert.That(sim.GameplayStatusAt(0).Key.GroupSource.Generation,Is.EqualTo(ushort.MaxValue));
            byte[] saved=sim.SaveRetainedSnapshot();
            Assert.DoesNotThrow(()=>sim.RestoreRetainedSnapshot(saved,true));
            Assert.That(sim.GameplayStatusAt(0).Key.GroupSource.Generation,Is.EqualTo(ushort.MaxValue));
            Assert.That(sim.GameplayStatusAt(0).SourceSnapshot.Source.Generation,Is.EqualTo(ushort.MaxValue));
            Assert.That(sim.SaveRetainedSnapshot(),Is.EqualTo(saved));
        }

        [Test] public void ExpiryAtCurrentTickReportsExpiryError()
        {
            var definition=new GameplayStatusDef(1,"timed",GameplayStatusDuration.Timed,3);
            var sim=CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Apply(1)})},statuses:new[]{definition});
            Step(sim);
            var document=new M0c2S2ProtocolTests.Document(sim);
            M0c2S2ProtocolTests.Put(document.Tables[6][0],163,(ulong)sim.Clock.Tick);
            var failure=Assert.Throws<SimSnapshotException>(()=>sim.RestoreRetainedSnapshot(document.Encode(),true));
            Assert.That(failure.Message,Is.EqualTo("M0C_STATUS_EXPIRY"));
        }

        [TestCase(10,0u),TestCase(1,1u)]
        public void SaturatedPulseRestoresLastLegalTick(int firstDelay,uint ordinal)
        {
            var definition=new GameplayStatusDef(1,"periodic",GameplayStatusDuration.Infinite,
                periodTicks:10,firstPulseDelayTicks:firstDelay,
                periodicActions:new[]{GameplayPeriodicAction.ChangeResource(1,2,1)});
            var sim=CreateWith(out _,new S2Tick[0],statuses:new[]{definition});
            long prior=long.MaxValue-6;
            sim.Clock.Restore(prior);
            typeof(SimScheduler).GetField("advancedThrough",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sim.Scheduler,prior);
            typeof(SimEventBuffer).GetField("currentTick",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(sim.Events,prior);
            sim.Step(default);
            var action=GameplayAction.ApplyStatus(sim.GameplayTarget(Target.Id),new GameplayApplyKey(Low,1,0,Low,1,1),1);
            Assert.That(sim.Entities.Gameplay.AdmitStatus(action.Command,0),Is.EqualTo(GameplayFailure.None));
            sim.Entities.Gameplay.Settle();
            if(ordinal!=0)sim.Step(default);
            Assert.That(sim.GameplayStatusAt(0).NextPulseTick,Is.EqualTo(long.MaxValue-1));
            Assert.That(sim.GameplayStatusAt(0).PulseOrdinal,Is.EqualTo(ordinal));
            byte[] saved=sim.SaveRetainedSnapshot();
            Assert.DoesNotThrow(()=>sim.RestoreRetainedSnapshot(saved,true));
            Assert.That(sim.GameplayStatusAt(0).NextPulseTick,Is.EqualTo(long.MaxValue-1));
            Assert.That(sim.GameplayStatusAt(0).PulseOrdinal,Is.EqualTo(ordinal));
            Assert.That(sim.SaveRetainedSnapshot(),Is.EqualTo(saved));
        }
    }
}
