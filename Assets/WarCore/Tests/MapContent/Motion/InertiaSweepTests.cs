using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Motion
{
    // M6 acceptance 7: an AI unit that accelerates to a high speed (1 000 000 mm/s^2, 72817 raw per tick squared) still does
    // not pass a thin wall or a unit of the other team; the sweep stops it after it has reached its full speed.
    public sealed class InertiaSweepTests
    {
        // Two units touch when their centres are 0.5 m (32768 raw) apart. The movers step along truncated unit vectors, so a
        // step may end a few raw short of an exact length; 2 raw is the allowance.
        private const double TouchRaw = 32768 - 2;
        private const int Ticks = 300;

        private static long Cap(long speedMm) => SimFix.Ratio(speedMm, 30000).Raw;

        // Smallest distance from the other unit's centre to the straight step from a to b, in raw units (as in UnitSweepTests).
        private static double Closest(SimVec3 a, SimVec3 b, SimVec3 other)
        {
            double ax = a.X.Raw, az = a.Z.Raw, dx = b.X.Raw - ax, dz = b.Z.Raw - az;
            double qx = other.X.Raw - ax, qz = other.Z.Raw - az;
            double length2 = dx * dx + dz * dz;
            double t = length2 == 0 ? 0 : Math.Max(0, Math.Min(1, (qx * dx + qz * dz) / length2));
            double ex = qx - t * dx, ez = qz - t * dz;
            return Math.Sqrt(ex * ex + ez * ez);
        }

        // No overlap, judged the way the movers judge it: the end of every step is at least the sum of the radii from the other
        // unit (2 raw allowance). A step at least that long is checked along its path, so its whole segment must be that far; a
        // shorter step is judged by its ends, so its middle may dip into the circle by at most the sagitta of a chord as long
        // as the step: R - sqrt(R^2 - (L/2)^2), 0.0101 m for the 0.2 m step of 6000 mm/s.
        private static void Apart(SimVec3 before, SimVec3 after, SimVec3 other, string what)
        {
            Assert.That(Centre(after, other), Is.GreaterThanOrEqualTo(TouchRaw), what + ": end of the step");
            double length = Centre(before, after), r = 32768;
            double allowance = length >= r ? 0 : r - Math.Sqrt(r * r - length * length / 4);
            Assert.That(Closest(before, after, other), Is.GreaterThanOrEqualTo(TouchRaw - allowance), what + ": along the step");
        }

        private static double Centre(SimVec3 a, SimVec3 other) => Math.Sqrt(Math.Pow(a.X.Raw - other.X.Raw, 2) + Math.Pow(a.Z.Raw - other.Z.Raw, 2));

        private static SimWide Moved(SimVec3 a, SimVec3 b) => new SimVec3(b.X - a.X, default, b.Z - a.Z).LengthSquared;
        private static SimWide Squared(long raw) => new SimVec3(SimFix.FromRaw(raw), default, default).LengthSquared;

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
                if (Moved(before, after) < Squared(speed)) { contact = true; stopped++; }
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
            int loops = 0, fullBeforeContact = 0, deflections = 0;
            bool contact = false;
            for (int tick = 1; tick <= Ticks; tick++)
            {
                SimVec3 before = M3TestWorld.Position(s);
                bool active = Wp23.Intent(s).Active;
                s.StepOnce();
                loops++;
                SimVec3 after = M3TestWorld.Position(s);
                Apart(before, after, other, $"speed {speedMm}, tick {tick}");
                Assert.That(M3TestWorld.Position(s, 3), Is.EqualTo(other), "the bystander is not pushed");
                if (!active) continue;
                MotionState state = InertiaWorlds.State(s);
                Assert.That(state.Tick, Is.EqualTo((long)tick), "an active unit is asked every tick");
                // The route runs along z = 10.5 m: a step that leaves that line, or no step at all, is the bystander in the way.
                bool deflected = after.Z != before.Z || after.Equals(before);
                if (deflected && !contact) contact = true;
                if (deflected) deflections++;
                if (!contact && state.Speed.Raw == cap) fullBeforeContact++;
            }
            Assert.That(Wp23.Intent(s).Active, Is.False, "the unit got past and arrived");
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(M3TestWorld.Point(60500, 10500)));
            TestContext.Out.WriteLine($"speed {speedMm} offset {offsetZ}: fullBeforeContact={fullBeforeContact} deflections={deflections}");
            Assert.That(loops, Is.EqualTo(Ticks));
            Assert.That(fullBeforeContact, Is.GreaterThanOrEqualTo(1));
            Assert.That(deflections, Is.GreaterThanOrEqualTo(1), "the bystander was in the way");
        }
        // Only the sweep along the step sees this bystander: on the intercepted tick the step the mover would take (one cell,
        // cut at the waypoint) has both end points clear of it and passes through it. The unit is at full speed, does not take
        // that step and does not come closer than the sum of the radii; it then gets past and arrives.
        [Test]
        public void TheSweepStopsAFullSpeedStepWhoseEndsAreClear([Values(30000, 60000, 240000)] long speedMm)
        {
            SimSession s = InertiaWorlds.CrowdMidStep(speedMm, 1000000);
            long cap = Cap(speedMm);
            SimVec3 other = M3TestWorld.Position(s, 3);
            int loops = 0, intercepted = 0, fullBefore = 0;
            for (int tick = 1; tick <= Ticks; tick++)
            {
                SimVec3 before = M3TestWorld.Position(s);
                bool active = Wp23.Intent(s).Active;
                SimVec3 waypoint = M3TestWorld.Component<MoveWaypoint>(s).Point;
                s.StepOnce();
                loops++;
                SimVec3 after = M3TestWorld.Position(s);
                Apart(before, after, other, $"speed {speedMm}, tick {tick}");
                if (!active) continue;
                long speed = InertiaWorlds.State(s).Speed.Raw;
                SimVec3 delta = waypoint - before;
                SimVec3 candidate = delta.LengthSquared <= Squared(speed) ? waypoint : before + delta.Normalized * SimFix.FromRaw(speed);
                bool sweepOnly = Centre(before, other) >= 32768 && Centre(candidate, other) >= 32768 && Closest(before, candidate, other) < 32768;
                if (!sweepOnly) { if (intercepted == 0 && speed == cap) fullBefore++; continue; }
                intercepted++;
                Assert.That(speed, Is.EqualTo(cap), $"full speed at the intercepted tick {tick}");
                Assert.That(Closest(before, candidate, other), Is.LessThan(0.2 * SimFix.Scale), "the candidate passes 0.15 m from the bystander");
                Assert.That(after, Is.Not.EqualTo(candidate), $"tick {tick}: the swept step is not taken");
            }
            TestContext.Out.WriteLine($"speed {speedMm}: intercepted={intercepted} fullBefore={fullBefore}");
            Assert.That(loops, Is.EqualTo(Ticks));
            Assert.That(intercepted, Is.EqualTo(1));
            Assert.That(fullBefore, Is.GreaterThanOrEqualTo(1));
            Assert.That(Wp23.Intent(s).Active, Is.False);
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(M3TestWorld.Point(60500, 10500)));
        }
    }
}
