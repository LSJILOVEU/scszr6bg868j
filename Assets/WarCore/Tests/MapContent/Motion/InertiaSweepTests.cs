using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // M6 acceptance 7: an AI unit that accelerates to a high speed (1 000 000 mm/s^2, 72817 raw per tick squared) still does
    // not pass a thin wall or a unit of the other team; the sweep stops it after it has reached its full speed.
    public sealed class InertiaSweepTests
    {
        private const double Touch = 0.5;
        private const int Ticks = 300;

        private static long Cap(long speedMm) => SimFix.Ratio(speedMm, 30000).Raw;

        private static double Metres(long raw) => (double)raw / SimFix.Scale;

        // Smallest distance from the other unit's centre to the straight step from a to b, in metres (as in UnitSweepTests).
        private static double Closest(SimVec3 a, SimVec3 b, SimVec3 other)
        {
            double ax = Metres(a.X.Raw), az = Metres(a.Z.Raw), dx = Metres(b.X.Raw) - ax, dz = Metres(b.Z.Raw) - az;
            double qx = Metres(other.X.Raw) - ax, qz = Metres(other.Z.Raw) - az;
            double length2 = dx * dx + dz * dz;
            double t = length2 == 0 ? 0 : Math.Max(0, Math.Min(1, (qx * dx + qz * dz) / length2));
            double ex = qx - t * dx, ez = qz - t * dz;
            return Math.Sqrt(ex * ex + ez * ez);
        }

        private static double Step(SimVec3 a, SimVec3 b) => Math.Sqrt(Math.Pow(Metres(b.X.Raw - a.X.Raw), 2) + Math.Pow(Metres(b.Z.Raw - a.Z.Raw), 2));

        [Test]
        public void AnAcceleratedUnitDoesNotPassTheThinWall([Values(6000, 60000, 240000)] long speedMm)
        {
            SimSession s = InertiaWorlds.Wall(speedMm, 1000000, 0);
            long cap = Cap(speedMm);
            int loops = 0, fullBeforeContact = 0, stopped = 0;
            bool contact = false;
            for (int tick = 1; tick <= Ticks; tick++)
            {
                SimVec3 before = M3TestWorld.Position(s);
                s.StepOnce();
                loops++;
                SimVec3 after = M3TestWorld.Position(s);
                long speed = InertiaWorlds.State(s).Speed.Raw;
                Assert.That(after.X.Raw, Is.LessThanOrEqualTo(InertiaWorlds.WallLeftRaw - InertiaWorlds.Radius + 1), $"speed {speedMm}, tick {tick}");
                Assert.That(InertiaWorlds.State(s).Tick, Is.EqualTo((long)tick), "the blocked unit is still asked");
                if (after.X.Raw - before.X.Raw < speed) { contact = true; stopped++; }
                else if (!contact && speed == cap) fullBeforeContact++;
            }
            TestContext.Out.WriteLine($"speed {speedMm}: fullBeforeContact={fullBeforeContact} stopped={stopped}");
            Assert.That(loops, Is.EqualTo(Ticks));
            Assert.That(fullBeforeContact, Is.GreaterThanOrEqualTo(1));
            Assert.That(stopped, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void AnAcceleratedUnitDoesNotJumpAUnitOfTheOtherTeam([Values(6000, 60000, 240000)] long speedMm, [Values(0, 150, 300)] long offsetZ)
        {
            SimSession s = InertiaWorlds.Crowd(speedMm, offsetZ, 1000000, 0);
            long cap = Cap(speedMm);
            SimVec3 other = M3TestWorld.Position(s, 3);
            int loops = 0, fullBeforeContact = 0, held = 0;
            bool contact = false;
            for (int tick = 1; tick <= Ticks; tick++)
            {
                SimVec3 before = M3TestWorld.Position(s);
                bool active = Wp23.Intent(s).Active;
                s.StepOnce();
                loops++;
                SimVec3 after = M3TestWorld.Position(s);
                Assert.That(Closest(before, after, other), Is.GreaterThanOrEqualTo(Touch - 0.01), $"speed {speedMm}, tick {tick}");
                Assert.That(M3TestWorld.Position(s, 3), Is.EqualTo(other), "the bystander is not pushed");
                if (!active) continue;
                MotionState state = InertiaWorlds.State(s);
                Assert.That(state.Tick, Is.EqualTo((long)tick), "an active unit is asked every tick");
                // The route runs along z = 10.5 m: a step that leaves that line, or no step at all, is the bystander in the way.
                bool deflected = after.Z != before.Z || after.Equals(before);
                if (deflected && !contact) contact = true;
                if (deflected && after.X.Raw - before.X.Raw < state.Speed.Raw) held++;
                if (!contact && state.Speed.Raw == cap) fullBeforeContact++;
            }
            Assert.That(Wp23.Intent(s).Active, Is.False, "the unit got past and arrived");
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(M3TestWorld.Point(60500, 10500)));
            TestContext.Out.WriteLine($"speed {speedMm} offset {offsetZ}: fullBeforeContact={fullBeforeContact} held={held}");
            Assert.That(loops, Is.EqualTo(Ticks));
            Assert.That(fullBeforeContact, Is.GreaterThanOrEqualTo(1));
            Assert.That(held, Is.GreaterThanOrEqualTo(1));
        }
    }
}
