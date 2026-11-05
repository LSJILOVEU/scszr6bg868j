using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Capture")]
    public sealed class M0c2S3CaptureTests
    {
        private sealed class AttributeChange : IRetainedGameplaySystem
        {
            public int Order=>1;public string StateTypeId=>"test.s3.attributes";public int StateVersion=>1;
            public void Step(SimContext c)
            {
                if(c.Clock.Tick!=2)return;
                var source=M0c2S3Fixture.Actor.Id;var target=c.Owner.GameplayTarget(source);
                c.ChangeResource(target,new GameplayApplyKey(source,100,0,source,100,1),3,8);
                c.ChangeResource(target,new GameplayApplyKey(source,100,0,source,100,2),2,100000);
            }
            public void SaveState(SimStateWriter w){}public void LoadState(SimStateReader r){}public void WriteState(StateHash h){}
        }
        private static Simulation Create()
        {
            var explicitTarget=new GameplayTargetSpec(GameplayTargetMode.Explicit,rangeRaw:12*M0c2S3Fixture.S,layerMask:M0c2S3Fixture.UnitLayer);
            var definition=new GameplayAbilityDef(11,"capture",1,1,explicitTarget,new[]{
                new GameplayAbilityAction(1,GameplayActionTrigger.OnRelease,GameplayPrimitive.Damage,1,amountAttributeKey:3,multiplierAttributeKey:2),
                M0c2S3Fixture.Action(2,GameplayPrimitive.ApplyStatus,1,-1),
                M0c2S3Fixture.Action(3,GameplayPrimitive.ApplyStatus,1,-1,GameplayActionTrigger.ActivePulse),
                M0c2S3Fixture.Action(4,GameplayPrimitive.ApplyStatus,1,-1,GameplayActionTrigger.OnEnd)},
                new[]{new GameplayCost(2,3)},1,10,3,3,1,1);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{definition}));
            sim.RegisterGameplay("s3.attributes",new AttributeChange(),SimSystemPhase.Queries);return sim;
        }
        [Test]
        public void AdmissionTickOneFreezesAttributesBeforeTickTwoChangeAndTickFourReleaseIncludingRestore()
        {
            var direct=Create();Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(direct,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Explicit)));
            var source=direct.GameplayCastAt(0).SourceSnapshot;
            Assert.That(source.CapturedTick,Is.EqualTo(1));Assert.That(source.AbilityKey,Is.EqualTo(11));
            Assert.That(source.TryAttribute(3,out long raw),Is.True);Assert.That(raw,Is.EqualTo(2));
            Assert.That(source.TryAttribute(2,out raw),Is.True);Assert.That(raw,Is.EqualTo(100000),"capture precedes the fee");
            Assert.That(source.AttributeCount,Is.EqualTo(3));Assert.That(source.ActionCaptureCount,Is.EqualTo(5),"four skill bindings plus periodic child binding");
            Assert.That(source.ActionCaptureAt(0).DefinitionKey,Is.EqualTo(1));Assert.That(source.ActionCaptureAt(0).DamageTypeKey,Is.EqualTo(1));
            direct.Step(default);Assert.That(direct.GameplayResource(M0c2S3Fixture.Actor.Id,3),Is.EqualTo(10));
            var restored=Create();Assert.DoesNotThrow(()=>restored.RestoreRetainedSnapshot(direct.SaveRetainedSnapshot(),true));
            for(int tick=3;tick<=12;tick++)
            {
                Assert.DoesNotThrow(()=>direct.Step(default));Assert.DoesNotThrow(()=>restored.Step(default));
                Assert.That(restored.StateHashRetained(),Is.EqualTo(direct.StateHashRetained()),"tick "+tick);
                if(tick==4)
                {
                    Assert.That(direct.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(7997),"floor(2*100000/65536)=3 from admission attributes");
                    Assert.That(direct.GameplayStatusAt(0).SourceSnapshot.CapturedTick,Is.EqualTo(1));
                    Assert.That(direct.GameplayStatusAt(0).StartTick,Is.EqualTo(4));Assert.That(direct.GameplayStatusAt(0).ExpiryTick,Is.EqualTo(11));
                }
                if(tick==7){Assert.That(direct.GameplayStatusAt(0).StartTick,Is.EqualTo(4));Assert.That(direct.GameplayStatusAt(0).ExpiryTick,Is.EqualTo(14));}
            }
        }
        [Test]
        public void SourceRetirementCannotRemoveAlreadyReleasedPeriodicStatusOrItsCapture()
        {
            var sim=Create();M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Explicit));
            while(sim.Clock.Tick<4)sim.Step(default);
            sim.Entities.Destroy((EntityId)sim.Entities.Resolve(new EntityLink(M0c2S3Fixture.Actor.Id)));
            Assert.DoesNotThrow(()=>sim.Step(default));Assert.That(sim.GameplayCastCount,Is.Zero);
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(7995));
            Assert.That(sim.GameplayStatusAt(0).SourceSnapshot.TryAttribute(3,out long raw),Is.True);Assert.That(raw,Is.EqualTo(2));
            Assert.That(sim.GameplayStatusAt(0).SourceSnapshot.Source.Id,Is.EqualTo(M0c2S3Fixture.Actor.Id));
        }
        [Test]
        public void NonPlacedCastCounterKeepsTheFullSixtyFourBitDomain()
        {
            var key=StableEntityKey.World(73,0,90);
            var def=new GameplayAbilityDef(1,"wide-counter",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,1)},new[]{new GameplayCost(2,3)});
            var schema=M0c2S3Fixture.Schema(abilities:new[]{def},authority:new GameplayInputAuthority(1,new[]{key.Id}));
            var catalog=new PlacedCatalog(new StableContentManifest(0),new PlacedCatalogEntry(73,6,1));
            var sim=Simulation.CreateRetainedWorld(73,catalog,new SimConfig(entityCapacity:8,timerCapacity:32),gameplaySchema:schema);
            sim.BindGameplay(sim.Entities.Create(key),1);
            object state=sim.Entities.Gameplay;var cursors=(ulong[])state.GetType().GetField("castNext",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(state);
            cursors[0]=ulong.MaxValue-1;
            var command=new GameplayInputCommand(new GameplayStamp(key.Id),1,GameplayInputKind.Activate,1);
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,command));Assert.That(sim.GameplayInputResultAt(0).CastToken,Is.EqualTo(ulong.MaxValue-1));
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,command));Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.SequenceExhausted));
            Assert.That(sim.GameplayResource(key.Id,2),Is.EqualTo(99997));
        }
        [Test]
        public void PlacedCastCounterUsesFortyEightBitsAndRejectsItsSentinelWithoutPaying()
        {
            var def=new GameplayAbilityDef(1,"counter",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,1)},new[]{new GameplayCost(2,3)});
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{def}));
            object state=sim.Entities.Gameplay;var cursors=(ulong[])state.GetType().GetField("castNext",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(state);
            var owners=(StableId[])state.GetType().GetField("owners",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(state);
            int index=Array.IndexOf(owners,M0c2S3Fixture.Actor.Id);cursors[index]=0x0000FFFFFFFFFFFEUL;
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self)));
            Assert.That(sim.GameplayInputResultAt(0).CastToken,Is.EqualTo(0x0000FFFFFFFFFFFEUL));
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self)));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.SequenceExhausted));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(99997));
        }
    }
}
