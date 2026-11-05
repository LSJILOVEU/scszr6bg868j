using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Input")]
    public sealed class M0c2S3InputTests
    {
        private static GameplayAbilityDef Self(ulong key,long amount)
            =>new GameplayAbilityDef(key,"input-"+key,key,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,amount)},new[]{new GameplayCost(2,1)});
        [Test] public void ActorAndOrdinalNormalizationPrecedesNoncommutingAdmissionAndPersistsCanonically()
        {
            var schema=M0c2S3Fixture.Schema(abilities:new[]{Self(1,-9000),Self(2,13)});
            var canonical=new[]{M0c2S3Fixture.Command(1,ordinal:1,mode:GameplayTargetMode.Self),M0c2S3Fixture.Command(2,ordinal:2,mode:GameplayTargetMode.Self),
                M0c2S3Fixture.Command(2,M0c2S3Fixture.OtherActor,1,GameplayTargetMode.Self)};
            var a=M0c2S3Fixture.CreateWith(schema);var b=M0c2S3Fixture.CreateWith(schema,true,1);
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(a,canonical));Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(b,canonical[2],canonical[1],canonical[0]));
            Assert.That(a.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(13),"ordinal one clamps health to zero before ordinal two adds thirteen");
            Assert.That(a.GameplayInputResultCount,Is.EqualTo(3));
            for(int i=0;i<3;i++)
            {Assert.That(a.GameplayInputResultAt(i).Failure,Is.EqualTo(GameplayAbilityFailure.None));Assert.That(b.GameplayInputResultAt(i).Actor.Id,Is.EqualTo(canonical[i].Actor.Id));Assert.That(b.GameplayInputResultAt(i).CommandOrdinal,Is.EqualTo(canonical[i].CommandOrdinal));}
            Assert.That(a.StateHashRetained(),Is.EqualTo(b.StateHashRetained()));Assert.That(a.SaveRetainedSnapshot(),Is.EqualTo(b.SaveRetainedSnapshot()));
        }
        [Test] public void DuplicateIdentityGroupRejectsEveryPayloadAndItsArrivalOrderNeverChangesSavedInput()
        {
            var schema=M0c2S3Fixture.Schema(abilities:new[]{Self(1,7),Self(2,19)});
            var one=M0c2S3Fixture.Command(1,ordinal:7,mode:GameplayTargetMode.Self);
            var two=M0c2S3Fixture.Command(2,ordinal:7,mode:GameplayTargetMode.Self);
            var valid=M0c2S3Fixture.Command(2,M0c2S3Fixture.OtherActor,1,GameplayTargetMode.Self);
            var a=M0c2S3Fixture.CreateWith(schema);var b=M0c2S3Fixture.CreateWith(schema,true,1);
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(a,one,two,valid));Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(b,valid,two,one));
            Assert.That(a.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.DuplicateCommand));
            Assert.That(a.GameplayInputResultAt(1).Failure,Is.EqualTo(GameplayAbilityFailure.DuplicateCommand));
            Assert.That(a.GameplayInputResultAt(2).Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(a.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8000));Assert.That(a.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(100000));
            Assert.That(a.GameplayAbilityFailureCount(GameplayAbilityFailure.DuplicateCommand),Is.EqualTo(2));
            Assert.That(a.SaveRetainedSnapshot(),Is.EqualTo(b.SaveRetainedSnapshot()),"different duplicate payloads must have a stable persisted order");
            Assert.That(a.StateHashRetained(),Is.EqualTo(b.StateHashRetained()));
        }
        [Test] public void AuthorityIsFrozenConfigurationAndRejectionLeavesCombatResourcesUnchanged()
        {
            var schema=M0c2S3Fixture.Schema(authority:new GameplayInputAuthority(9,new[]{M0c2S3Fixture.Actor.Id}));
            var sim=M0c2S3Fixture.CreateWith(schema);M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(4,M0c2S3Fixture.OtherActor));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.UnauthorizedActor));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.OtherActor.Id,1),Is.EqualTo(8000));Assert.That(sim.GameplayResource(M0c2S3Fixture.OtherActor.Id,2),Is.EqualTo(100000));
            Assert.That(sim.GameplayCastCount,Is.Zero);Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.UnauthorizedActor),Is.EqualTo(1));
        }
        [TestCase(0),TestCase(2),TestCase(long.MaxValue)] public void FrameTickMustNameTheTickBeingExecuted(long inputTick)
        {
            var sim=M0c2S3Fixture.Create();sim.Step(new InputFrame(default,default,gameplayTick:inputTick,skillCommands:new[]{M0c2S3Fixture.Command(4)}));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.InvalidInput));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8000));Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(100000));
        }
        [Test] public void LegacySkillSlotCannotInjectACommandOutsideTheInputFrameCommandBlock()
        {
            var sim=M0c2S3Fixture.Create();sim.Step(new InputFrame(default,default,primaryAttack:true,skillSlot:4));
            Assert.That(sim.GameplayInputResultCount,Is.Zero);Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,1),Is.EqualTo(8000));
        }
        [Test] public void InvalidInputPrecedesDuplicateAndDuplicatePrecedesAuthorityFailure()
        {
            var sim=M0c2S3Fixture.Create();var bad=new GameplayInputCommand(new GameplayStamp(M0c2S3Fixture.Target.Id),1,(GameplayInputKind)2,4);
            M0c2S3Fixture.Input(sim,bad,bad);Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.InvalidInput));
            var duplicate=M0c2S3Fixture.Command(4,M0c2S3Fixture.Target);M0c2S3Fixture.Input(sim,duplicate,duplicate);
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.DuplicateCommand));
        }
        [TestCase(false),TestCase(true)]
        public void CancelAndActivateUseTheirOrdinalOrderWithinTheSameActor(bool cancelFirst)
        {
            var ability=new GameplayAbilityDef(1,"cancel-order",1,1,new GameplayTargetSpec(GameplayTargetMode.Self),
                new[]{M0c2S3Fixture.Action(1,GameplayPrimitive.ChangeResource,1,7)},new[]{new GameplayCost(2,3)},windupTicks:3);
            var sim=M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities:new[]{ability}));
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1,mode:GameplayTargetMode.Self));
            var activate=M0c2S3Fixture.Command(1,ordinal:cancelFirst?2u:1u,mode:GameplayTargetMode.Self);
            var cancel=M0c2S3Fixture.Command(1,ordinal:cancelFirst?1u:2u,mode:GameplayTargetMode.Self,kind:GameplayInputKind.Cancel);
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,activate,cancel));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(cancelFirst?GameplayAbilityFailure.None:GameplayAbilityFailure.Busy));
            Assert.That(sim.GameplayInputResultAt(1).Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Actor.Id,2),Is.EqualTo(cancelFirst?99994:99997));
            Assert.That(sim.GameplayCastCount,Is.EqualTo(cancelFirst?1:0));
            if(cancelFirst)Assert.That(sim.GameplayCastAt(0).SourceSnapshot.CapturedTick,Is.EqualTo(2));
        }
        [Test] public void WrongActorGenerationAndUnknownSlotHaveSpecificFailures()
        {
            var sim=M0c2S3Fixture.Create();M0c2S3Fixture.Input(sim,new GameplayInputCommand(new GameplayStamp(M0c2S3Fixture.Actor.Id,1),1,GameplayInputKind.Activate,4));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.ActorLifeMismatch));
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(999));Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.UnknownAbility));
        }
    }
}
