using NUnit.Framework;
namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Mt126GameplayStateTests
    {
        [Test] public void CaptureInputAcceptsInitialZeroTickAndPreservesSnapshot()
        {
            var sim=M0c2S1Fixture.Create();
            var before=sim.SaveRetainedSnapshot();
            Assert.That(sim.Clock.Tick,Is.EqualTo(0));
            Assert.DoesNotThrow(()=>sim.Entities.Gameplay.CaptureInput(0));
            Assert.That(sim.SaveRetainedSnapshot(),Is.EqualTo(before));
        }
    }
}
