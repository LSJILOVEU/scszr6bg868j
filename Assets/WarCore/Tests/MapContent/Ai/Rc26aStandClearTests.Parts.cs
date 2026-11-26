using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.LedGame;

namespace WarCore.Tests
{
    // Rc26aStandClearTests, the rest: a member coming through the leader, refused orders, a save and load, and maps with
    // floors.
    public sealed partial class Rc26aStandClearTests
    {
        // T11. A squad of one. Unit 2's place is 0.6 m east of the standing leader, so the leader is not on it. Unit 2 comes from
        // 3 m west of the leader at 0.1 m a tick and past it (a member passes its standing leader): within a quarter metre of
        // its place it can still be 0.35 m from the leader, but it stops only once its body is clear (0.5 m).
        [Test]
        public void AMemberComingThroughTheLeaderStopsClearOfItsBody()
        {
            LedGame g = Pair(7000, 20000, 3000);
            SimVec3 leader = g.At(1), place = Plus(leader, (Raw(600), 0));
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0));
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(place)), "the leader is not on the place");
            int stopped = -1;
            for (int t = 0; t < 120 && stopped < 0; t++)
            {
                g.Wait(1);
                if (!g.Walking(2)) stopped = t;
            }
            Assert.That(stopped, Is.GreaterThan(0), "it walked and stopped");
            g.Wait(30);
            SimVec3 at = g.At(2);
            Assert.That(Xz(g.At(1)), Is.EqualTo(Xz(leader)), "the leader stood");
            Assert.That(at.X.Raw, Is.GreaterThan(leader.X.Raw), "it came through");
            Assert.That(Squared(at, leader), Is.GreaterThanOrEqualTo(Touch * Touch), "clear of the leader's body, " + Metres(at, leader) + " m");
            Assert.That(Metres(at, place), Is.LessThanOrEqualTo(0.25), "on its place");
            Assert.That(g.Walking(2), Is.False);
        }

        // T12. Unit 2 walks to its place (19, 21.73) when the anchor moves so that its place is the leader's point, and nobody
        // can stand at any of the six side points: no point to stand at, its walk is stopped, and it is not left alone for
        // good: once the leader is off the place it is sent there.
        [Test]
        public void AMemberWithNoSideToStandAtIsStoppedAndGoesBackOnceTheLeaderIsOff()
        {
            LedGame g = Pair(16000, 20000);
            SimVec3 leader = g.At(1);
            g.Order(SquadCommandKind.MoveTo, SimVec3.Metres(20, 0, 20));
            Assert.That(g.Walking(2), Is.True, "on its way");
            StandIn orders = Install(g, 2);
            for (int side = 0; side < 6; side++) orders.Blocked.Add(Plus(leader, Halves2[side]));
            g.Order(SquadCommandKind.MoveTo, AnchorFor(leader, 0));
            Assert.That(g.Led.TryPlace(g.Id(2), out SimVec3 none), Is.False, "nowhere to stand");
            Assert.That((none.X.Raw, none.Y.Raw, none.Z.Raw), Is.EqualTo((0L, 0L, 0L)), "and no point");
            Assert.That(HasOrder(g, 2), Is.False, "its walk is stopped");
            Assert.That(orders.Given.Count, Is.EqualTo(0));
            Assert.That(LeaderWalksUntil(g, Walk(0, -1000), leader, false), Is.True, "the leader walked off the place");
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(leader)), "sent to its place");
        }

        // T13. Saved in the middle of T7 (unit 2 walks to NW, SE points most away) and loaded into a world that played on
        // without any order: from there on the two play tick for tick the same, and unit 2 ends at NW in both.
        [Test]
        public void SavedWhileAMemberKeepsItsSideAndLoadedTheWorldPlaysOnTheSame()
        {
            LedGame whole = Shifted(out SimVec3 nw), early = Shifted(out _), resumed = Pair(14000, 20000);
            resumed.Wait(4);
            Assert.That(resumed.W.Hash, Is.Not.EqualTo(whole.W.Hash));
            resumed.W.Sim.RestoreRetainedSnapshot(early.W.Sim.SaveRetainedSnapshot());
            Assert.That(resumed.W.Hash, Is.EqualTo(whole.W.Hash), "the loaded world is the saved one");
            for (int t = 0; t < 60; t++)
            {
                whole.Wait(1); resumed.Wait(1);
                Assert.That(resumed.W.Hash, Is.EqualTo(whole.W.Hash), "step " + t);
            }
            Assert.That(Xz(resumed.At(2)), Is.EqualTo(Xz(whole.At(2))));
            Assert.That(Xz(StandOf(resumed, 2)), Is.EqualTo(Xz(nw)));
            Assert.That(Xz(StandOf(whole, 2)), Is.EqualTo(Xz(nw)));
            Assert.That(Metres(whole.At(2), nw), Is.LessThanOrEqualTo(0.25), "it stands at NW");
        }

        // T14. The leader stands 0.25 m east of unit 2's place, unit 2 0.25 m west of it: within a quarter metre of its place,
        // its body touching the leader's (0.5 m) without overlapping. The leader is on the place, so unit 2 is to stand at W
        // (pointing most away), 0.75 m from where it is, and is walked there.
        [Test]
        public void AMemberWithinReachOfItsPlaceIsWalkedAsideWhileTheLeaderIsOnThePlace()
        {
            LedGame g = Pair(9500, 20000);
            SimVec3 leader = g.At(1), place = Minus(leader, (BodyRadius.DefaultRaw, 0)), west = Plus(place, Halves2[5]);
            Assert.That(Squared(g.At(2), place), Is.EqualTo(BodyRadius.DefaultRaw * BodyRadius.DefaultRaw), "a quarter metre from its place");
            Assert.That(Squared(g.At(2), leader), Is.EqualTo(Touch * Touch), "touching the leader");
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0));
            Assert.That(Xz(StandOf(g, 2)), Is.EqualTo(Xz(west)));
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(west)), "walked there");
        }

        // T15. As T1: unit 2's way to NW is 4.6 m, 23 ticks; over its first 13 it is told once.
        [Test]
        public void AMemberOnItsWayAsideIsToldWhereToGoOnce()
        {
            LedGame g = Pair(14000, 20000);
            StandIn orders = Install(g, 2);
            SimVec3 place = g.At(1), nw = Plus(place, Halves2[0]);
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0));
            for (int t = 0; t < 12; t++)
            {
                g.Wait(1);
                Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(nw)), "on its way, tick " + t);
            }
            Assert.That(orders.Given.Count, Is.EqualTo(1));
        }

        // T16. As T1, but the order to NW is refused: in the same tick unit 2 is sent to NE, the next side (all point away alike).
        [Test]
        public void ARefusedOrderToStandAsideGoesToTheNextSideInTheSameTick()
        {
            LedGame g = Pair(14000, 20000);
            SimVec3 place = g.At(1), nw = Plus(place, Halves2[0]), ne = Plus(place, Halves2[1]);
            StandIn orders = Install(g, 2);
            orders.Point = nw; orders.RefusePoint = true;
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0));
            Assert.That(orders.Given.Count, Is.EqualTo(2), "two orders in the tick");
            Assert.That((Xz(orders.Given[0]), Xz(orders.Given[1])), Is.EqualTo((Xz(nw), Xz(ne))));
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(ne)), "on its way to NE");
        }

        // T17. As T1, but every order to stand aside is refused: six orders in the tick, sides 0 to 5, and no walk order. It is
        // then left alone: while the leader stands, across a save and load, after the leader walked off; a new command (the
        // places worked out again) sends it to its place.
        [Test]
        public void AMemberAllOfWhoseSidesWereRefusedIsLeftAloneUntilThePlacesChange()
        {
            LedGame g = Pair(14000, 20000);
            SimVec3 place = g.At(1), anchor = AnchorFor(place, 0);
            StandIn orders = Install(g, 2);
            orders.Point = place; orders.RefuseElsewhere = true;
            g.Order(SquadCommandKind.MoveTo, anchor);
            Assert.That(orders.Given.Count, Is.EqualTo(6), "six orders in the tick");
            for (int side = 0; side < 6; side++) Assert.That(Xz(orders.Given[side]), Is.EqualTo(Xz(Plus(place, Halves2[side]))), "order " + side);
            Assert.That(HasOrder(g, 2), Is.False);
            g.Wait(30);
            Assert.That(orders.Given.Count, Is.EqualTo(6), "not asked again while the leader stands");
            g.W.Sim.RestoreRetainedSnapshot(g.W.Sim.SaveRetainedSnapshot());
            g.Wait(30);
            Assert.That(orders.Given.Count, Is.EqualTo(6), "nor after a save and load");
            Assert.That(LeaderWalksUntil(g, Walk(0, -1000), place, false), Is.True, "the leader walked off the place");
            g.Wait(5);
            Assert.That(orders.Given.Count, Is.EqualTo(6), "nor once the leader is off");
            g.Order(SquadCommandKind.MoveTo, anchor);
            Assert.That(orders.Given.Count, Is.EqualTo(7), "a new command");
            Assert.That(Xz(Target(g, 2)), Is.EqualTo(Xz(place)), "sends it to its place");
        }

        // T18. Unit 2 waits at (16, 20); its place is (12, 20), 2 m east of the leader. Its first walk order is refused, then
        // orders are let through; the leader walks east onto the place and stands there: unit 2 gets no order at all.
        [Test]
        public void AMemberWhoseWalkToItsPlaceWasRefusedIsNotSentAside()
        {
            LedGame g = Pair(16000, 20000);
            SimVec3 place = Plus(g.At(1), (Raw(2000), 0));
            StandIn orders = Install(g, 2);
            orders.RefuseAll = true;
            g.Order(SquadCommandKind.MoveTo, AnchorFor(place, 0));
            Assert.That(orders.Given.Count, Is.EqualTo(1));
            Assert.That(Xz(orders.Given[0]), Is.EqualTo(Xz(place)), "refused on its way to its place");
            orders.RefuseAll = false;
            Assert.That(LeaderWalksUntil(g, Walk(1000, 0), place, true), Is.True, "the leader came onto the place");
            g.Wait(10);
            Assert.That(Overlap(g.At(1), place), Is.True, "and stands on it");
            Assert.That(orders.Given.Count, Is.EqualTo(1), "not sent aside");
            Assert.That(g.Walking(2), Is.False);
        }

        private static IUnitLayers LayersOf(LedGame g) =>
            (IUnitLayers)typeof(LedSquad).GetField("layers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(g.Led);

        // The house (its defenders fall on the first tick) with a squad called to follow.
        internal static LedGame House(out IUnitLayers layers)
        {
            var g = new LedGame("house", 1, 9, 10, 11, 12, 13, 14, 15, 16);
            layers = LayersOf(g);
            Assert.That(layers, Is.Not.Null, "the squad knows the floors");
            g.Order(SquadCommandKind.Follow);
            Assert.That(g.Led.MemberCount, Is.GreaterThan(0));
            return g;
        }

        // The leader walks along the points (metres), steering straight at each.
        internal static void WalkThrough(LedGame g, params (double X, double Z)[] way)
        {
            int leg = 0;
            for (int ticks = 0; leg < way.Length && ticks < 300; ticks++)
            {
                SimVec3 at = g.At(g.Player);
                double dx = way[leg].X - at.X.Raw / (double)SimFix.Scale, dz = way[leg].Z - at.Z.Raw / (double)SimFix.Scale, far = Math.Sqrt(dx * dx + dz * dz);
                if (far < 0.3) { leg++; continue; }
                g.Step(Walk((int)Math.Round(dx / far * 1000), (int)Math.Round(dz / far * 1000)));
            }
            Assert.That(leg, Is.EqualTo(way.Length), "the leader walked the way");
        }

        // A member whose spot puts the anchor on that layer where it stands and sees the place at the point; the anchor through
        // anchor, the place (grounded) through place.
        internal static ulong MemberFor(LedGame g, SimVec3 point, int layer, out SimVec3 anchor, out SimVec3 place)
        {
            SimVec3[] offsets = LedSquad.Formation(g.Led.Rules.Size, g.SpacingRaw);
            anchor = default;
            Assert.That(g.Moves.TryStandAt(point, layer, out place), Is.True, "a unit can stand at the point");
            foreach (ulong key in g.Members())
            {
                SimVec3 a = Minus(point, Xz(offsets[g.Led.SpotOf(g.Id(key))]));
                if (!g.Moves.TryStandAt(a, layer, out SimVec3 up) || !g.W.Combat.Sees(up, place)) continue;
                anchor = a;
                return key;
            }
            Assert.Fail("no member's anchor sees the point");
            return 0;
        }

        // The six steps aside in this world for two bodies of 0.25 m: half a lattice step when it is at least the two radii
        // long, else the full step * reach / spacing for the least reach from the two radii up whose step is (all truncated
        // toward zero).
        private static (long X, long Z)[] StepsAside(LedGame g)
        {
            SimVec3[] offsets = LedSquad.Formation(g.Led.Rules.Size, g.SpacingRaw);
            var steps = new (long X, long Z)[6];
            for (int side = 0; side < 6; side++)
            {
                long x = offsets[side].X.Raw, z = offsets[side].Z.Raw;
                steps[side] = (x / 2, z / 2);
                for (long reach = Touch; steps[side].X * steps[side].X + steps[side].Z * steps[side].Z < Touch * Touch; reach++)
                    steps[side] = (x * reach / g.SpacingRaw, z * reach / g.SpacingRaw);
            }
            return steps;
        }

        // The member stands a step aside of its place (to one of the six sides), clear of the leader.
        internal static void StandsAside(LedGame g, ulong member, SimVec3 place, string why)
        {
            SimVec3 stand = StandOf(g, member);
            (long X, long Z) off = (stand.X.Raw - place.X.Raw, stand.Z.Raw - place.Z.Raw);
            Assert.That(StepsAside(g), Does.Contain(off), why + ": a step aside of its place");
            Assert.That(Squared(stand, g.At(g.Player)), Is.GreaterThanOrEqualTo(Touch * Touch), why + ": clear of the leader");
        }

        // T19. The leader walks in by the west doorway and stands on the ground floor under the upper floor (layer 2, from x 15).
        // The squad is sent upstairs to the anchor that puts one member's place straight above the leader: 3 m above its feet,
        // more than the leader's height (1.75 m), so the leader's body is not on it and that member stands on its place.
        [Test]
        public void ALeaderOnAnotherFloorUnderAPlaceIsNotStoodAsideOf()
        {
            LedGame g = House(out IUnitLayers layers);
            EntityId me = g.W.Unit(g.Player);
            WalkThrough(g, (12.5, 12.0), (16.0, 12.0), (16.0, 9.0));
            g.Wait(1);
            SimVec3 leader = g.At(g.Player);
            Assert.That(layers.LayerOf(me), Is.EqualTo(0), "on the ground floor");
            ulong member = MemberFor(g, leader, 2, out SimVec3 anchor, out SimVec3 above);
            g.Order(SquadCommandKind.MoveTo, anchor, 2);
            Assert.That((g.Led.Mode, g.Led.AnchorLayer, layers.LayerOf(me)), Is.EqualTo((LedMode.Hold, 2, 0)));
            Assert.That(Xz(g.At(g.Player)), Is.EqualTo(Xz(leader)), "the leader stood");
            Assert.That(above.Y.Raw - leader.Y.Raw, Is.GreaterThanOrEqualTo(g.W.Read<Collider>(g.Player).Height.Raw), "above the leader's head");
            SimVec3 stand = StandOf(g, member);
            Assert.That((stand.X.Raw, stand.Y.Raw, stand.Z.Raw), Is.EqualTo((leader.X.Raw, above.Y.Raw, leader.Z.Raw)), "its own place, straight above the leader");
            Assert.That(Xz(Target(g, member, 2)), Is.EqualTo(Xz(leader)), "walked there");
        }

        // T20. On a map with floors, on the same floor: the squad, called where the leader starts (outside the house, on the
        // ground), is sent to the anchor that puts one member's place at the leader's point, level with its feet: that member
        // stands a step aside.
        [Test]
        public void ALeaderOnAPlaceOfItsOwnFloorIsStoodAsideOf()
        {
            LedGame g = House(out IUnitLayers layers);
            Assert.That(g.W.Read<Collider>(g.Player).Radius.Raw, Is.EqualTo(BodyRadius.DefaultRaw));
            SimVec3 leader = g.At(g.Player);
            Assert.That(layers.LayerOf(g.W.Unit(g.Player)), Is.EqualTo(0));
            ulong member = MemberFor(g, leader, 0, out SimVec3 anchor, out SimVec3 place);
            Assert.That(g.W.Read<Collider>(member).Radius.Raw, Is.EqualTo(BodyRadius.DefaultRaw));
            g.Order(SquadCommandKind.MoveTo, anchor, 0);
            Assert.That((g.Led.Mode, g.Led.AnchorLayer), Is.EqualTo((LedMode.Hold, 0)));
            Assert.That((place.X.Raw, place.Y.Raw, place.Z.Raw), Is.EqualTo((g.At(g.Player).X.Raw, g.At(g.Player).Y.Raw, g.At(g.Player).Z.Raw)), "the place is the leader's point");
            StandsAside(g, member, place, "member " + member);
        }

        // T21. The leader walks to the foot of the stair (plate 1, x 17 to 21, z 11 to 13, rising 0.75 m a metre from the ground
        // at x 17) and on until it is on the stair's layer: its first such step ends at x 17.17, 0.13 m up. A unit stands on
        // the ground up to about x 16.75 there. The squad holds at the stair's foot on the ground: one member's place is on
        // the ground 0.45 m west of the leader, lower than its feet by less than a body's height (1.75 m), so the leader's body
        // is on it.
        [Test]
        public void ALeaderOnTheFirstCellOfAStairIsOnAPlaceAtItsFoot()
        {
            LedGame g = House(out IUnitLayers layers);
            EntityId me = g.W.Unit(g.Player);
            WalkThrough(g, (12.5, 12.5), (16.5, 12.5));
            for (int t = 0; t < 20 && layers.LayerOf(me) == 0; t++) g.Step(Walk(1000, 0));
            Assert.That(layers.LayerOf(me), Is.Not.EqualTo(0), "the leader is on the stair");
            g.Wait(1);
            SimVec3 leader = g.At(g.Player);
            int stair = layers.LayerOf(me);
            ulong member = MemberFor(g, Minus(leader, (Raw(450), 0)), 0, out SimVec3 anchor, out SimVec3 place);
            g.Order(SquadCommandKind.MoveTo, anchor, 0);
            Assert.That((g.Led.AnchorLayer, layers.LayerOf(me)), Is.EqualTo((0, stair)), "the anchor on the ground, the leader on the stair");
            Assert.That(Overlap(place, g.At(g.Player)), Is.True, "0.45 m apart");
            Assert.That(Math.Abs(place.Y.Raw - leader.Y.Raw), Is.LessThan(g.W.Read<Collider>(g.Player).Height.Raw), "less than a body's height apart");
            StandsAside(g, member, place, "member " + member);
        }
    }
}
