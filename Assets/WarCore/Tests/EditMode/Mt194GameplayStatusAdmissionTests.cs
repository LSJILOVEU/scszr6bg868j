using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S2")]
    public sealed class Mt194GameplayStatusAdmissionTests
    {
        [TestCase(-1),TestCase(GameplayMath.MaxPackets+1)]
        public void DispelRejectsOutOfRangeMaximum(int maximum)
        {
            var sim=M0c2S2Fixture.CreateWith(out _,new[]{new S2Tick(new[]{S2Op.Dispel(1,maximum:maximum)})});
            var error=Assert.Throws<SimBoundaryException>(()=>M0c2S2Fixture.Step(sim));
            Assert.That(error.Message,Is.EqualTo("M0C_DISPEL_COUNT"));
        }

        [Test] public void DuplicateApplyKeyIsRejectedWithDistinctAdmissionOrdinals()
        {
            var sim=M0c2S2Fixture.CreateWith(out _,new S2Tick[0]);sim.Step(default);
            var source=M0c2S2Fixture.Low;
            var key=new GameplayApplyKey(source,1,0,source,1,1);
            var command=GameplayAction.ApplyStatus(sim.GameplayTarget(M0c2S2Fixture.Target.Id),key,1).Command;
            Assert.That(sim.Entities.Gameplay.AdmitStatus(command,0),Is.EqualTo(GameplayFailure.None));
            var error=Assert.Throws<SimBoundaryException>(()=>sim.Entities.Gameplay.AdmitStatus(command,1));
            Assert.That(error.Message,Is.EqualTo("M0C_DUPLICATE_APPLY_KEY"));
        }

        [Test] public void StatusCapacityRejectionIncrementsOnlyCapacityDiagnostic()
        {
            var sim=M0c2TaskBStatusFixture.Create(new M0c2TaskBStatusFixture.NoticeScript(5));sim.Step(default);
            Assert.That(sim.GameplayStatusCount,Is.EqualTo(5));
            byte[] diagnostics=new M0c2S2ProtocolTests.Document(sim).Tables[8][0];
            Assert.That(M0c2S2ProtocolTests.Word(diagnostics,40),Is.EqualTo(1UL),"Status capacity rejections");
            Assert.That(M0c2S2ProtocolTests.Word(diagnostics,32),Is.EqualTo(0UL),"Immunity rejections");
        }
    }
}
