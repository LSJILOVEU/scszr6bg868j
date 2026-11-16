using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Input")]
    public sealed class Mt185GameplayInputRuntimeTests
    {
        [TestCase(false),TestCase(true)]
        public void DuplicateResultsUseAscendingActorGeneration(bool reverse)
        {
            var sim=M0c2S3Fixture.Create();
            var first=new GameplayInputCommand(new GameplayStamp(M0c2S3Fixture.Actor.Id,0),1,GameplayInputKind.Activate,4);
            var second=new GameplayInputCommand(new GameplayStamp(M0c2S3Fixture.Actor.Id,1),1,GameplayInputKind.Activate,4);
            M0c2S3Fixture.Input(sim,reverse?second:first,reverse?first:second);
            Assert.That(sim.GameplayInputResultCount,Is.EqualTo(2));
            Assert.That(sim.GameplayInputResultAt(0).Actor.Generation,Is.EqualTo(0));
            Assert.That(sim.GameplayInputResultAt(1).Actor.Generation,Is.EqualTo(1));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.DuplicateCommand));
            Assert.That(sim.GameplayInputResultAt(1).Failure,Is.EqualTo(GameplayAbilityFailure.DuplicateCommand));
        }
        [TestCase(0,1),TestCase(1,-2),TestCase(1,2),TestCase(33,33)]
        public void MalformedOrOversizedFrameRecordsOneInvalidInput(int length,int count)
        {
            var sim=M0c2S3Fixture.Create();var commands=new GameplayInputCommand[length];
            for(int i=0;i<length;i++)commands[i]=M0c2S3Fixture.Command(4,ordinal:(uint)i+1);
            sim.Step(new InputFrame(default,default,gameplayTick:1,skillCommands:commands,skillCommandCount:count));
            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.InvalidInput),Is.EqualTo(1));
            Assert.That(sim.GameplayInputResultCount,Is.Zero);
            Assert.That(sim.GameplayCastCount,Is.Zero);
        }
        [Test]
        public void DestroyedAuthorizedActorReportsActorGone()
        {
            var sim=M0c2S3Fixture.Create();
            sim.Entities.Destroy((EntityId)sim.Entities.Resolve(new EntityLink(M0c2S3Fixture.Actor.Id)));
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(4));
            Assert.That(sim.GameplayInputResultCount,Is.EqualTo(1));
            Assert.That(sim.GameplayInputResultAt(0).Failure,Is.EqualTo(GameplayAbilityFailure.ActorGone));
            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.ActorGone),Is.EqualTo(1));
        }
    }
}
