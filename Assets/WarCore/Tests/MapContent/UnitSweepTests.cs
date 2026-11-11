using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // Short steps can cross another unit between two clear endpoints.
    // Every unit has radius 0.25 m, so two units touch at 0.5 m.
    public sealed class UnitSweepTests
    {
        private const double Touch = 0.5;

        private static double Metres(long raw) => (double)raw / SimFix.Scale;

        // Smallest distance from the other unit's centre to the straight step from a to b, in metres.
        private static double Closest(SimVec3 a, SimVec3 b, SimVec3 other)
        {
            double ax = Metres(a.X.Raw), az = Metres(a.Z.Raw), dx = Metres(b.X.Raw) - ax, dz = Metres(b.Z.Raw) - az;
            double qx = Metres(other.X.Raw) - ax, qz = Metres(other.Z.Raw) - az;
            double length2 = dx * dx + dz * dz;
            double t = length2 == 0 ? 0 : Math.Max(0, Math.Min(1, (qx * dx + qz * dz) / length2));
            double ex = qx - t * dx, ez = qz - t * dz;
            return Math.Sqrt(ex * ex + ez * ez);
        }

        // Player (key 1) at x = start walks along +X past a bystander (key 3, team 1, no orders) 3 m ahead on the same line.
        private static M6World PlayerWorld(long speedMm, long bystanderZ = 10000, bool reverse = false) => new M6World(
            Recipe(new[] { U(1, 8000, 10000, speed: speedMm), U(2, 70000, 35000, team: 2, squad: 2), U(3, 11000, bystanderZ, squad: 3) }), reverse: reverse);

        [Test]
        public void TheControlledUnitDoesNotJumpAUnitInItsWay([Values(6000, 30000, 60000, 120000, 240000)] long speedMm,
            [Values(0, 100, 240, 400)] long offsetZ)
        {
            foreach (bool reverse in new[] { false, true })
            {
                var w = PlayerWorld(speedMm, 10000 + offsetZ, reverse);
                SimVec3 other = Point(w, 3);
                for (int tick = 1; tick <= 60; tick++)
                {
                    SimVec3 before = Point(w);
                    w.Sim.Step(Input(1, 0));
                    Assert.That(Closest(before, Point(w), other), Is.GreaterThanOrEqualTo(Touch - 0.01), $"speed {speedMm}, tick {tick}");
                    Assert.That(Point(w, 3), Is.EqualTo(other), "the bystander is not pushed");
                }
            }
        }

        [Test]
        public void ACrowdUnitDoesNotJumpAUnitInItsWay([Values(6000, 60000, 240000)] long speedMm, [Values(0, 150, 300)] long offsetZ)
        {
            var units = new[] { U(1, 8000, 10000, speed: speedMm, team: 1, squad: 1), U(3, 11000, 10000 + offsetZ, team: 2, squad: 3),
                                U(2, 70000, 35000, team: 2, squad: 2) };
            // The bystander is on the other team: the default pass rule lets a unit walk through an idle unit of its own team.
            var w = new M6World(M6World.RecipeOf(units, Match()));
            w.Session.Enqueue(BridgeCommand.MoveTo(1, 1, 20000, 10000));
            SimVec3 other = Point(w, 3);
            for (int tick = 1; tick <= 90; tick++)
            {
                SimVec3 before = Point(w);
                w.Session.StepOnce();
                Assert.That(Closest(before, Point(w), other), Is.GreaterThanOrEqualTo(Touch - 0.01), $"speed {speedMm}, tick {tick}");
            }
        }

        [Test]
        public void FastMovementPastNobodyIsUnchanged()
        {
            var w = PlayerWorld(60000, 10000 + 8000);
            SimVec3 start = Point(w);
            for (int tick = 1; tick <= 5; tick++)
            {
                w.Sim.Step(Input(1, 0));
                Assert.That(Point(w).X.Raw, Is.EqualTo(start.X.Raw + tick * 131072L), "tick " + tick);
            }
        }

        [Test]
        public void AShortStepKeepsTheEndPointRule()
        {
            // At the base speed a step is 0.2 m, shorter than the two radii: the same walk with and without the broad phase agrees,
            // and the unit still stops at the bystander.
            var w = PlayerWorld(6000);
            for (int tick = 1; tick <= 120; tick++) w.Sim.Step(Input(1, 0));
            double gap = Metres(Point(w, 3).X.Raw - Point(w).X.Raw);
            Assert.That(gap, Is.InRange(Touch - 0.01, Touch + 0.25));
        }

        [Test]
        public void AFastStepAwayFromAUnitBehindIsNotBlocked()
        {
            // Unit 3 is 0.4 m behind and 0.35 m beside the player (0.53 m apart, just clear); the player runs away at 2 m per tick.
            var w = new M6World(Recipe(new[] { U(1, 12400, 10000, speed: 60000), U(2, 70000, 35000, team: 2, squad: 2), U(3, 12000, 10350, squad: 3) }));
            long start = Point(w).X.Raw;
            w.Sim.Step(Input(1, 0));
            Assert.That(Point(w).X.Raw, Is.EqualTo(start + 131072));
        }

        [Test]
        public void AStepThatStartsInsideAnotherUnitAndMovesAwayKeepsTheOldRule()
        {
            // Overlapping at the start (0.3 m apart): any step that increases the distance is allowed, even a long one through the centre.
            var w = new M6World(Recipe(new[] { U(1, 10700, 10000, speed: 60000), U(2, 70000, 35000, team: 2, squad: 2), U(3, 11000, 10000, squad: 3) }));
            long start = Point(w).X.Raw;
            w.Sim.Step(Input(1, 0));
            Assert.That(Point(w).X.Raw, Is.EqualTo(start + 131072));
        }

        [Test]
        public void AShortStepThatGrazesAUnitIsBlocked()
        {
            // Both endpoints clear the 0.5 m circle, but the middle passes 0.495 m from its centre.
            var w = new M6World(Recipe(new[] { U(1, 8000, 10000, speed: 6000), U(2, 70000, 35000, team: 2, squad: 2), U(3, 8100, 10495, squad: 3) }));
            long start = Point(w).X.Raw;
            w.Sim.Step(Input(1, 0));
            Assert.That(Point(w).X.Raw, Is.EqualTo(start), "the swept path crosses the bystander");
        }

        [Test]
        public void ASpeedAboveTheBoundIsRefusedAndNamesTheUnitAndField()
        {
            Assert.DoesNotThrow(() => U(7, speed: UnitSpawn.MaxSpeedMmPerSec));
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => U(7, speed: UnitSpawn.MaxSpeedMmPerSec + 1));
            Assert.That(ex.Message, Does.Contain("Unit 7").And.Contain("speedMmPerSec").And.Contain(UnitSpawn.MaxSpeedMmPerSec.ToString()));
            Assert.Throws<ArgumentOutOfRangeException>(() => U(7, speed: 0));
        }

        [Test]
        public void ARecordedPlayerRunReplaysToTheSameHashes()
        {
            var a = PlayerWorld(120000); var b = PlayerWorld(120000);
            for (int tick = 0; tick < 60; tick++)
            {
                var input = Input(tick < 30 ? 1 : -1, tick % 7 == 0 ? 1 : 0);
                a.Sim.Step(input); b.Sim.Step(input);
                Assert.That(b.Session.StateHash, Is.EqualTo(a.Session.StateHash), "tick " + tick);
            }
        }
    }
}
