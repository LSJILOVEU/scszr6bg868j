using System;
using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2TaskB"),Category("M0c2TaskBAdmission")]
    public sealed class M0c2TaskBAdmissionTests
    {
        private sealed class Script : IRetainedGameplaySystem
        {
            private readonly int operation;private readonly bool sameActor,invalidOrder;
            private readonly GameplayAction[] batch=new GameplayAction[1];
            private readonly GameplayCost[] cost={new GameplayCost(2,3)};
            public GameplayFailure Result;
            public Script(int operation,bool sameActor,bool invalidOrder=false)
            {this.operation=operation;this.sameActor=sameActor;this.invalidOrder=invalidOrder;}
            public int Order=>1;public string StateTypeId=>"task-b.admission";public int StateVersion=>1;
            public void Step(SimContext c)
            {
                if(c.Clock.Tick!=1)return;
                var source=c.Owner.GameplayTarget((sameActor?M0c2S3Fixture.OtherActor:M0c2S3Fixture.Actor).Id);
                var target=c.Owner.GameplayTarget(M0c2S3Fixture.Target.Id);
                var key=new GameplayApplyKey(source.Id,99,0,source.Id,99,1);
                switch(operation)
                {
                    case 0:batch[0]=GameplayAction.ChangeResource(target,key,1,7);Result=c.SubmitGameplay(source,cost,batch,1);break;
                    case 1:Result=c.ApplyStatus(target,key,1,commandOrdinal:1);break;
                    case 2:Result=c.Dispel(target,key,1,commandOrdinal:1);break;
                    case 3:Result=c.GrantAbsorb(target,key,3,9,commandOrdinal:1);break;
                    default:Result=c.SpendResources(source,cost,1);break;
                }
                if(invalidOrder)c.SpendResources(source,ReadOnlySpan<GameplayCost>.Empty,1);
            }
            public void SaveState(SimStateWriter w){}public void LoadState(SimStateReader r){}public void WriteState(StateHash h){}
        }
        [TestCase(0,false),TestCase(1,false),TestCase(2,false),TestCase(3,false),TestCase(4,false)]
        [TestCase(0,true),TestCase(1,true),TestCase(2,true),TestCase(3,true),TestCase(4,true)]
        public void InputAdmissionAndQueryScriptsHaveSeparateOrderDomains(int operation,bool sameActor)
        {
            var sim=M0c2S3Fixture.Create();var script=new Script(operation,sameActor);
            sim.RegisterGameplay("task-b.admission",script,SimSystemPhase.Queries);
            Assert.DoesNotThrow(()=>M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(4,M0c2S3Fixture.OtherActor)),
                "Queries source may precede or equal the last input actor and ordinal");
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(script.Result,Is.EqualTo(GameplayFailure.None));
            var source=(sameActor?M0c2S3Fixture.OtherActor:M0c2S3Fixture.Actor).Id;
            Assert.That(sim.GameplayResource(source,2),Is.EqualTo(100000-(sameActor?2:0)-(operation==0 || operation==4?3:0)));
            Assert.That(sim.GameplayResource(M0c2S3Fixture.Target.Id,1),Is.EqualTo(operation==0?8007:8000));
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(operation==1 || operation==3?1:0));
        }
        [Test] public void QueryDomainStillRejectsRepeatedSourceOrdinalAfterInput()
        {
            var sim=M0c2S3Fixture.Create();sim.RegisterGameplay("task-b.admission",new Script(4,false,true),SimSystemPhase.Queries);
            var error=Assert.Throws<SimBoundaryException>(()=>M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(4,M0c2S3Fixture.OtherActor)));
            StringAssert.Contains("M0C_ADMISSION_ORDER",error.Message);
        }
    }
}
