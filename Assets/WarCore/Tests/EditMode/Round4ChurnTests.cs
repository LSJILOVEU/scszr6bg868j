using NUnit.Framework;
using WarCore.Tests.Diagnostics;

namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Round4ChurnTests
    {
        [Test, Category("Round4ChurnCount")]
        public void ChurnIsExactlyFivePercentOfAllLiveEntities()
        {
            var scene = new Round4ChurnScene(false);
            scene.Sim.Step(default);
            Assert.That(scene.Created,Is.EqualTo(SemanticBenchScene.BodyCount/20));
            Assert.That(scene.Sim.Entities.ActiveEntityCount,Is.EqualTo(SemanticBenchScene.BodyCount));
        }
        [Test, Category("MF5ReferenceCost")]
        public void OldScanOverheadIsDetectedAgainstNoise()
        {
            ReferenceChurnBench.PositiveControl(new Round4ChurnScene(false,true),
                new Round4ChurnScene(false,true,true),0,TestContext.Out);
        }
        [Test, Category("MF5ReferenceChurn")]
        public void ReferenceChurnMaintains64ValuesInEachStorageAndMatchesNaive()
        {
            var sparse = new Round4ChurnScene(false,true);
            var naive = new Round4ChurnScene(false,true,true);
            for (int i = 0; i < 40; i++)
            {
                sparse.Sim.Step(default); naive.Sim.Step(default);
                Assert.That(sparse.References.Notices,Is.EqualTo((i+1)*192));
                Assert.That(sparse.ReferenceStateHash(),Is.EqualTo(naive.ReferenceStateHash()));
            }
        }
        [Test, Category("Round4Churn"), Timeout(900000)]
        public void ChurnSmokeKeepsThePeakWorkloadAndIndependentOracle()
            => Round4ChurnBench.Run("smoke",TestContext.Out);
        [Test, Category("Perf"), Timeout(1800000)]
        public void ChurnFullMeasuresThreeHundredTicksInBothLayouts()
            => Round4ChurnBench.Run("full",TestContext.Out);
    }
}
