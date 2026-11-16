using NUnit.Framework;
using WarCore.Interim;

namespace WarCore.Tests.Motion
{
    public sealed class Mt030MotionStateTests
    {
        [Test]
        public void ComponentId_KeepsItsStableName()
        {
            Assert.That(MotionState.ComponentId, Is.EqualTo("interim.motion-state"));
        }
    }
}
