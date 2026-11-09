using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L9 acceptance 10: K2 known edge (the distance rule is unchanged). Target selection uses a sphere of radius = range against
    // the unit capsule (Sim/SpatialGeometrySlow.cs:41-54) while the shot is a ray from the shooter's centre that must reach
    // into the capsule. On the ramp (L9Fixture.RampMap, slope 0.906662) a target 22.5 m away horizontally and about 20.4 m
    // higher is selected and visible, but the 30 m ray stops short: the shot is Open.
    public sealed class L9EdgeTests
    {
        [Test]
        public void L9Edge_K2KnownEdgeSelectedByTheCapsuleSphereButTheCentreRayIsOutOfRange()
        {
            var units = new[] { L9.Unit(1, 1, 1250, 1500, 0), L9.Unit(2, 2, 23750, 1500, -1) };
            var w = L9.World(L9.RampMap(), units);
            Assert.That(w.Read<Position>(2).Value.Y.Raw - w.Read<Position>(1).Value.Y.Raw, Is.EqualTo(1336928));
            Assert.That(w.Combat.Visible(w.Unit(1), w.Unit(2)), Is.True);
            w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(FireOutcome.Open));
        }
    }
}
