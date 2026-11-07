using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Motion
{
    // M6 acceptance 5 and 6: braking looks at the final target, not the next waypoint, and every mover starts from rest and arrives.
    public sealed class InertiaMoverTests
    {
        private const long Cap = 13107;

        private static long Horizontal(SimVec3 a, SimVec3 b) =>
            new SimVec3(a.X - b.X, default, a.Z - b.Z).LengthSquared.Sqrt().Raw;

        [Test]
        public void Braking_LooksAtTheFinalTargetNotTheNextWaypoint()
        {
            SimSession s = InertiaWorlds.Zigzag(0, 18000);
            SimVec3 target = M3TestWorld.Point(30500, 4500);
            long twoMetres = 2 * SimFix.Scale, oneMetre = SimFix.Scale;
            int far = 0, nearWaypoint = 0, waypoints = 0, ticks = 0;
            SimVec3 last = default;
            while (Wp23.Intent(s).Active || s.Tick == 0)
            {
                SimVec3 point = M3TestWorld.Position(s);
                SimVec3 waypoint = M3TestWorld.Component<MoveWaypoint>(s).Point;
                if (!waypoint.Equals(last)) { waypoints++; last = waypoint; }
                s.StepOnce();
                ticks++;
                Assert.That(ticks, Is.LessThanOrEqualTo(400));
                if (s.Tick == 1) Assert.That(Wp23.Intent(s).Target, Is.EqualTo(target));
                if (Horizontal(point, target) <= twoMetres) continue;
                Assert.That(InertiaWorlds.State(s).Speed.Raw, Is.EqualTo(Cap), "tick " + s.Tick);
                far++;
                if (Horizontal(point, waypoint) < oneMetre) nearWaypoint++;
            }
            TestContext.Out.WriteLine($"ticks={ticks} far={far} nearWaypoint={nearWaypoint} waypoints={waypoints}");
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(target));
            Assert.That(waypoints, Is.GreaterThanOrEqualTo(4), "the start waypoint, at least two turns and the target");
            Assert.That(far, Is.GreaterThanOrEqualTo(20));
            Assert.That(nearWaypoint, Is.GreaterThanOrEqualTo(4));
        }

        private static readonly long[] ArrivalTicks = { 63, 63, 63, 63, 63 };

        [Test]
        public void EveryMover_StartsFromRestAndArrives([Range(0, 4)] int world)
        {
            var (mover, hill) = InertiaWorlds.Movers[world];
            SimSession s = InertiaWorlds.Mover(mover, hill, 9000, 18000);
            SimVec3 target = M3TestWorld.Point(30500, 12500);
            s.StepOnce();
            Assert.That(InertiaWorlds.State(s), Is.EqualTo(new MotionState(SimFix.FromRaw(655), 1)), mover + " hill " + hill);
            Assert.That(InertiaWorlds.State(s, 2), Is.EqualTo(new MotionState(default, -1)), "the idle unit is never asked");
            long arrived = -1;
            while (s.Tick < 600 && arrived < 0)
            {
                s.StepOnce();
                SimVec3 at = M3TestWorld.Position(s);
                if (at.X == target.X && at.Z == target.Z && !Wp23.Intent(s).Active) arrived = s.Tick;
            }
            TestContext.Out.WriteLine($"{mover} hill={hill} arrived={arrived}");
            Assert.That(arrived, Is.EqualTo(ArrivalTicks[world]));
        }
    }
}
