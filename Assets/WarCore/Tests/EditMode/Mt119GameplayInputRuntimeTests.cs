using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3"),Category("M0c2S3Input")]
    public sealed class Mt119GameplayInputRuntimeTests
    {
        [TestCase(false),TestCase(true)]
        public void DuplicateVectorPayloadsAreSavedInLexicographicOrder(bool aimDiffers)
        {
            var actor=new GameplayStamp(M0c2S3Fixture.Actor.Id);
            var one=new GameplayInputCommand(actor,7,GameplayInputKind.Activate,1,GameplayTargetMode.Query,
                aimDirection:SimVec3.Metres(1,0,0),targetPoint:aimDiffers?default:SimVec3.Metres(1,0,0));
            var two=new GameplayInputCommand(actor,7,GameplayInputKind.Activate,1,GameplayTargetMode.Query,
                aimDirection:SimVec3.Metres(aimDiffers?2:1,0,0),targetPoint:aimDiffers?default:SimVec3.Metres(2,0,0));
            var sim=M0c2S3Fixture.Create();M0c2S3Fixture.Input(sim,one,two);
            Assert.That(sim.GameplayInputResultCount,Is.EqualTo(2));
            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.DuplicateCommand),Is.EqualTo(2));
            var input=new M0c2S2ProtocolTests.Document(sim).Tables[1][0];
            // The input header is 16 bytes; each command without an explicit target is 98 bytes.
            int vectorOffset=16+(aimDiffers?50:74);
            Assert.That(M0c2S2ProtocolTests.Word(input,vectorOffset),Is.EqualTo(65536UL));
            Assert.That(M0c2S2ProtocolTests.Word(input,vectorOffset+98),Is.EqualTo(131072UL));
        }
        [Test]
        public void FullInputCapacityAcceptsEveryCommand()
        {
            var schema=M0c2S3Fixture.Schema();var sim=M0c2S3Fixture.CreateWith(schema);
            var commands=new GameplayInputCommand[schema.Capacities.InputCommands];
            for(int i=0;i<commands.Length;i++)
                commands[i]=M0c2S3Fixture.Command(4,ordinal:(uint)i+1,kind:GameplayInputKind.Cancel);
            M0c2S3Fixture.Input(sim,commands);
            Assert.That(sim.GameplayInputResultCount,Is.EqualTo(commands.Length));
            for(int i=0;i<commands.Length;i++)
            {
                Assert.That(sim.GameplayInputResultAt(i).CommandOrdinal,Is.EqualTo((uint)i+1));
                Assert.That(sim.GameplayInputResultAt(i).Failure,Is.EqualTo(GameplayAbilityFailure.None));
            }
            Assert.That(sim.GameplayAbilityFailureCount(GameplayAbilityFailure.InvalidInput),Is.Zero);
        }
    }
}
