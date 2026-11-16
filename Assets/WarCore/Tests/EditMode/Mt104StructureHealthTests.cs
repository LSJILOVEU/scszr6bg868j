using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt104StructureHealthTests
    {
        [Test]
        public void StateVersionRemainsOne()
        {
            var f = new ExtensionFixture();
            var system = new StructureSystem(f.P.Sim.Entities, f.Healths, f.P.Positions, f.P.Colliders, 71, 30);
            Assert.That(system.StateVersion, Is.EqualTo(1));
        }
    }
}
