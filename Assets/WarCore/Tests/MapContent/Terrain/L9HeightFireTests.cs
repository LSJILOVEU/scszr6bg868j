using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L9 acceptance 4 and 5: automatic fire aims in 3D, so an unobstructed target more than 0.5 m higher or lower is hit, and
    // spread turns the horizontal part of the heading while keeping its vertical component. Ridge map (L9Fixture): x = 3.5 is
    // flat (Y = 0), x = 9.5 is on the slope (Y = 1.75 m = 114688 raw).
    public sealed class L9HeightFireTests
    {
        private const int Shots = 6, WithinTicks = 200;

        // Steps until Shots records are in (at most WithinTicks ticks) and returns them.
        private static List<FireRecord> FirstShots(long shooterXMm, long targetXMm, int spread, out StableId target)
        {
            var units = new[] { L9.Unit(1, 1, shooterXMm, 4500, 0), L9.Unit(2, 2, targetXMm, 4500, -1) };
            var w = L9.World(L9.RidgeMap(true), units, spread);
            var shots = new List<FireRecord>();
            for (int t = 1; t <= WithinTicks && shots.Count < Shots; t++)
            {
                w.Step();
                foreach (FireRecord f in w.Combat.Fires) shots.Add(f);
            }
            Assert.That(shots.Count, Is.GreaterThanOrEqualTo(Shots), "shots within " + WithinTicks + " ticks");
            Assert.That(w.Read<Position>(2).Value.Y.Raw - w.Read<Position>(1).Value.Y.Raw,
                Is.EqualTo(shooterXMm < targetXMm ? 114688 : -114688), "height difference");
            target = w.Sim.Entities.StableOf(w.Unit(2));
            return shots.GetRange(0, Shots);
        }

        [Test]
        public void L9HeightFire_AHigherTargetIsHit()
        {
            foreach (FireRecord f in FirstShots(3500, 9500, 0, out StableId target))
            {
                Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit));
                Assert.That(f.Target, Is.EqualTo(target));
                Assert.That(f.End, Is.EqualTo(L9.Raw(606204, 109908, 294912)));
                Assert.That(f.End.Y.Raw, Is.GreaterThan(f.Origin.Y.Raw));
            }
        }

        [Test]
        public void L9HeightFire_ALowerTargetIsHit()
        {
            foreach (FireRecord f in FirstShots(9500, 3500, 0, out StableId target))
            {
                Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit));
                Assert.That(f.Target, Is.EqualTo(target));
                Assert.That(f.End, Is.EqualTo(L9.Raw(245764, 4780, 294912)));
                Assert.That(f.End.Y.Raw, Is.LessThan(f.Origin.Y.Raw));
            }
        }

        [Test]
        public void L9HeightFire_SpreadKeepsTheVerticalComponent()
        {
            int turned = 0;
            foreach (FireRecord f in FirstShots(3500, 9500, 2000, out _))
            {
                Assert.That(f.End.Y.Raw, Is.GreaterThan(f.Origin.Y.Raw));
                if (f.End.Z.Raw != f.Origin.Z.Raw) turned++;
            }
            Assert.That(turned, Is.GreaterThanOrEqualTo(1), "spread really turned a shot");
        }
    }
}
