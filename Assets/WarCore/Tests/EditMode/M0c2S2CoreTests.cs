using NUnit.Framework;
using Simulation=WarCore.Sim.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S2")]
    public sealed class M0c2S2CoreTests
    {
        [Test] public void StatusStateHasReadOnlyPublicObservation()
        {
            var property=typeof(Simulation).GetProperty("GameplayStatusCount");
            Assert.That(property,Is.Not.Null,"S2 status state requires canonical read-only observation.");
            Assert.That(property.CanWrite,Is.False);
        }
    }
}
