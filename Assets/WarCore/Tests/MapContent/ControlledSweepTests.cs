using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // M4: a controlled step longer than the unit's radius is checked along its whole path, not only at its end, so a fast unit
    // cannot jump a thin wall. Shorter steps keep the end-only rule they always had (their end circle overlaps the start circle).
    // The thin wall is 1 m thick: cells x = 10 (wall faces at 10.0 and 11.0 metres); the unit has radius 0.25 m.
    public sealed class ControlledSweepTests
    {
        private const long Radius = 16384;

        // speedMm is the unit's speed in mm/s; the base 6000 mm/s is 13107 raw per tick.
        private static M6World World(long speedMm, long xMm, long zMm, bool reverse = false) => new M6World(
            Recipe(new[] { U(1, xMm, zMm, speed: speedMm), U(2, 70000, 35000, team: 2, squad: 2) }),
            WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (10, 0, 1, 30))), reverse);

        private static long X(M6World w) => Point(w).X.Raw;

        [Test]
        public void NoSpeedJumpsTheThinWall([Values(6000, 12000, 30000, 60000, 120000, 240000)] long speedMm,
            [Values(5000, 5300, 5700, 6100)] long startMm)
        {
            foreach (bool reverse in new[] { false, true })
            {
                var w = World(speedMm, startMm, 10000, reverse);
                long wallLeft = 10L * SimFix.Scale;
                for (int tick = 1; tick <= 120; tick++)
                {
                    w.Sim.Step(Input(1, 0));
                    Assert.That(X(w), Is.LessThanOrEqualTo(wallLeft - Radius + 1), $"speed {speedMm}, tick {tick}");
                }
            }
        }

        [Test]
        public void AFastDiagonalStepDoesNotJumpItEither()
        {
            var w = World(60000, 8500, 5000);
            for (int tick = 1; tick <= 40; tick++)
            {
                w.Sim.Step(Input(1, 1));
                Assert.That(X(w), Is.LessThan(10L * SimFix.Scale), "tick " + tick);
                if (Point(w).Z.Raw > 25L * SimFix.Scale) break;
            }
        }

        [Test]
        public void ThePathBeyondTheWallIsUnreachableFromTheWrongSide()
        {
            var w = World(60000, 15000, 10000);
            for (int tick = 1; tick <= 60; tick++)
            {
                w.Sim.Step(Input(-1, 0));
                Assert.That(X(w), Is.GreaterThanOrEqualTo(11L * SimFix.Scale + Radius - 1), "tick " + tick);
            }
        }

        [Test]
        public void FastMovementInOpenSpaceIsUnchanged()
        {
            var w = World(60000, 20000, 20000);
            long start = X(w), step = 131072; // 60000 mm/s at 30 ticks per second = 2 m
            for (int tick = 1; tick <= 5; tick++)
            {
                w.Sim.Step(Input(1, 0));
                Assert.That(X(w), Is.EqualTo(start + tick * step), "tick " + tick);
            }
        }

        [Test]
        public void AFastStepAlongTheWallIsNotBlocked()
        {
            // Touching distance from the wall face (9.75 m, radius 0.25): moving parallel keeps contact without overlap.
            var w = World(60000, 9750, 2000);
            long z = Point(w).Z.Raw;
            w.Sim.Step(Input(0, 1));
            Assert.That(Point(w).Z.Raw, Is.GreaterThan(z + Radius), "the unit moved a full fast step along the wall");
        }

        // Pins the rule that short steps are judged at their end only: this step (0.2 m, shorter than the 0.25 m radius) passes the
        // corner of a 1 m wall block at 0.235 m in the middle, while both ends are 0.255 m from it. A swept test would refuse it.
        [Test]
        public void AShortStepKeepsTheEndOnlyRule()
        {
            var w = new M6World(Recipe(new[] { U(1, 9763, 9905), U(2, 70000, 35000, team: 2, squad: 2) }),
                WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (10, 10, 1, 1))));
            SimVec3 before = Point(w);
            w.Sim.Step(Input(1, -1));
            Assert.That(Point(w).X.Raw, Is.GreaterThan(before.X.Raw), "the unit moved right");
            Assert.That(Point(w).Z.Raw, Is.LessThan(before.Z.Raw), "and down");
        }
    }
}
