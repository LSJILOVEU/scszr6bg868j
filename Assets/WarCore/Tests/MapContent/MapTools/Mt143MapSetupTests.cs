using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E1aSamples;

namespace WarCore.Tests
{
    public sealed class Mt143MapSetupTests
    {
        [Test]
        public void ChangingReturnedControlPointsDoesNotChangeSetup()
        {
            MapSetup setup = Bake(Sample).Setup;
            setup.ControlPoints[0] = default;
            Assert.That(setup.ControlPoints[0].Id, Is.EqualTo(1));
        }

        [Test]
        public void ChangingReturnedBasesDoesNotChangeSetup()
        {
            MapSetup setup = Bake(Sample).Setup;
            setup.Bases[0] = default;
            Assert.That(setup.Bases[0].Team, Is.EqualTo(2));
        }
    }
}
