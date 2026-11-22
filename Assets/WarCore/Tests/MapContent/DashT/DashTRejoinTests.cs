using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Dash;
using static WarCore.Tests.DashT.DashTWorlds;

namespace WarCore.Tests.DashT
{
    // DASH-T acceptance 7: after a dash a unit with an order heads for it again, on a terrain world and on a plate world (over
    // the layered graph from its layer), with its order's progress picked up there; no route is a failure; and the route length
    // the rejoin plans is not handed to another unit.
    public sealed class DashTRejoinTests
    {
        private static void RunOrder(SimSession s, ulong key = 1, int limit = 600)
        {
            for (int t = 0; t < limit && Wp23.Intent(s, key).Active; t++) s.StepOnce();
        }

        private static long Dist2(SimVec3 a, SimVec3 b)
        {
            long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw;
            return dx * dx + dz * dz;
        }

        // Ordered from (2.5, 1.5) to (20.5, 1.5); after one tick it dashes 2 m along +Z (4 ticks), then walks on to the target.
        [Test]
        public void Terrain_TheOrderGoesOnAfterTheDash()
        {
            SimSession s = World(Map(surface: (x, z) => x >= 8 && x < 12 ? Mud : Grass), new[] { Dasher(1, 2500, 1500, distanceMm: 2000, ticks: 4) },
                new[] { BridgeCommand.MoveTo(1, 1, 20500, 1500) });
            s.StepOnce();
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusZ), Is.True);
            for (int k = 0; k < 4; k++) s.StepOnce();
            SimVec3 at = M3TestWorld.Position(s), waypoint = M3TestWorld.Component<MoveWaypoint>(s).Point;
            MoveProgress p = M3TestWorld.Component<MoveProgress>(s);
            Assert.That(at.Z.Raw, Is.EqualTo(SimFix.Ratio(35, 10).Raw), "2 m along +Z");
            Assert.That(Wp23.Intent(s).Active, Is.True);
            Assert.That((p.BestDist2, p.Stall, p.Failed), Is.EqualTo((Dist2(at, waypoint), 0, (byte)0)), "measured from the new waypoint");
            Assert.That(p.Tail, Is.GreaterThan(0), "the new route's length beyond the waypoint");
            RunOrder(s);
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(Wp23.V(20500, 0, 1500)), "the order arrives");
            Assert.That(DashWorlds.Loco(s).HasFailed(M3TestWorld.Unit(s)), Is.False);
        }

        // In the demo house, ordered from the stair's foot (9.5, 9.5) onto the first floor at (12.5, 7.5) (layer 2); after one
        // tick it dashes 2 m along +X up the stair (2 ticks), then walks on over the layered graph from the stair to the target.
        [Test]
        public void Plates_TheOrderGoesOnFromTheStair()
        {
            SimSession s = House(new[] { Dasher(1, 9500, 9500, distanceMm: 2000, ticks: 2, cooldown: 10) },
                new[] { BridgeCommand.MoveTo(1, 1, 12500, 7500, 2) });
            s.StepOnce();
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True);
            s.StepOnce(); s.StepOnce();
            Assert.That(Layer(s), Is.EqualTo(1), "on the stair when the dash ends");
            Assert.That(Wp23.Intent(s).Active, Is.True);
            RunOrder(s);
            Assert.That(M3TestWorld.Position(s), Is.EqualTo(new SimVec3(SimFix.Ratio(25, 2), SimFix.FromInt(3), SimFix.Ratio(15, 2))), "the order arrives");
            Assert.That(Layer(s), Is.EqualTo(2));
            Assert.That(DashWorlds.Loco(s).HasFailed(M3TestWorld.Unit(s)), Is.False);
        }

        // No route from where the dash ends: the unit is put into a walled pocket round cell (20, 2) in the middle of its dash
        // (as a respawn could place it); the dash ends there and the order fails.
        [Test]
        public void NoRoute_TheOrderFails()
        {
            WcMap map = Map(walls: new[] { (19, 1, 3, 1), (19, 3, 3, 1), (19, 2, 1, 1), (21, 2, 1, 1) });
            SimSession s = World(map, new[] { Dasher(1, 2500, 5500, distanceMm: 1000, ticks: 4) }, new[] { BridgeCommand.MoveTo(1, 1, 10500, 5500) });
            s.StepOnce();
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True);
            s.StepOnce(); s.StepOnce();
            EntityStore entities = M3TestWorld.Sim(s).Entities;
            entities.Set(entities.Resolve<Position>(), M3TestWorld.Unit(s), new Position(Wp23.V(20500, 0, 2500)));
            s.StepOnce(); s.StepOnce();
            Assert.That(DashWorlds.Loco(s).IsDashing(M3TestWorld.Unit(s)), Is.False);
            Assert.That(Wp23.Intent(s).Active, Is.False, "stopped");
            Assert.That(DashWorlds.Loco(s).HasFailed(M3TestWorld.Unit(s)), Is.True);
            Assert.That(M3TestWorld.Component<MoveProgress>(s).Failed, Is.EqualTo(1));
        }

        // Unit 1 (A) and unit 2 (B) get orders along the map on ticks 1 and 2, 5 m apart. A dashes 4 m along +X from tick 1; its dash ends
        // (and it plans again) on tick 7, a tick on which B stays in its cell. B's order progress is, tick by tick, what it is in
        // the same world where A does not dash: A's planned route length does not reach B.
        [Test]
        public void TheRejoinRouteIsNotHandedOn()
        {
            BridgeCommand[] script = { BridgeCommand.MoveTo(1, 1, 20500, 1500), BridgeCommand.MoveTo(2, 2, 20500, 6500) };
            SimSession dash = World(Map(), new[] { Dasher(1, 2500, 1500), Dasher(2, 2500, 6500) }, script);
            SimSession still = World(Map(), new[] { Dasher(1, 2500, 1500), Dasher(2, 2500, 6500) }, script);
            dash.StepOnce(); still.StepOnce();
            Assert.That(DashWorlds.Dash(dash, DashWorlds.PlusX), Is.True);
            for (int t = 2; t <= 30; t++)
            {
                SimVec3 before = M3TestWorld.Position(dash, 2);
                dash.StepOnce(); still.StepOnce();
                if (t == 7)
                {
                    Assert.That(DashWorlds.State(dash).StepAt(t), Is.EqualTo(6), "A's last dash step");
                    Assert.That(M3TestWorld.Position(dash, 2).X.Raw >> 16, Is.EqualTo(before.X.Raw >> 16), "B stays in its cell");
                    Assert.That(M3TestWorld.Component<MoveProgress>(dash, 1).Tail,
                        Is.Not.EqualTo(M3TestWorld.Component<MoveProgress>(dash, 2).Tail), "A's route is not B's");
                }
                Assert.That(M3TestWorld.Position(dash, 2), Is.EqualTo(M3TestWorld.Position(still, 2)), "B moves alike, tick " + t);
                Assert.That(M3TestWorld.Component<MoveProgress>(dash, 2), Is.EqualTo(M3TestWorld.Component<MoveProgress>(still, 2)), "B's progress, tick " + t);
            }
        }
    }
}
