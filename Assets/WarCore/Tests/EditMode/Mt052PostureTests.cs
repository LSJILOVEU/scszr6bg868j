using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt052PostureTests
    {
        [Test]
        public void ZeroHeightDefinitionIsAccepted()
        {
            SimFix one = SimFix.FromInt(1);
            PostureDef value = default;
            Assert.DoesNotThrow(() => value = new PostureDef(1, SimFix.FromRaw(0), one, default, one));
            Assert.That(value.Id, Is.EqualTo(1));
            Assert.That(value.Height.Raw, Is.Zero);
        }
    }
}
