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
    // RC2-1: the rules of the led squad one at a time, on a small flat field (LedGame.Small: 80 m by 40 m, a 2 m formation,
    // nobody armed, and nobody walks unless the squad walks it).
    public sealed class Rc21LedRulesTests
    {
        // The leader at (10, 20); two mates three metres off, one on either side; one exactly on the 30 m radius and one a
        // metre beyond it; an enemy at arm's length.
        private static UnitSpawn[] Field() => new[] { Soldier(1, 10, 20), Soldier(2, 13, 20), Soldier(3, 7, 20), Soldier(4, 40, 20), Soldier(5, 41, 20), Soldier(6, 12, 22, 2) };

        // The member's place is its own: the anchor plus the offset of its spot.
        private static void OnItsOwnPlace(LedGame g, ulong key, SimVec3[] offsets)
        {
            Assert.That(g.Led.TryPlace(g.Id(key), out SimVec3 place), Is.True, "member " + key);
            SimVec3 own = g.Led.Anchor + offsets[g.Led.SpotOf(g.Id(key))];
            Assert.That((place.X.Raw, place.Z.Raw), Is.EqualTo((own.X.Raw, own.Z.Raw)), "member " + key);
        }

        [Test]
        public void TheTeamMatesWithinTheRadiusJoinNearestFirst()
        {
            LedGame g = Small(8, 30000, Field());
            g.Wait(60);
            foreach (ulong key in g.Keys) Assert.That(g.Walking(key), Is.False, "nobody walks before the squad is called, unit " + key);
            g.Order(SquadCommandKind.Follow);
            Assert.That(g.Led.MemberCount, Is.EqualTo(3));
            Assert.That(g.Members(), Is.EquivalentTo(new ulong[] { 2, 3, 4 }), "the mate on the radius joins; the one beyond it, the enemy and the leader do not");
            for (int i = 1; i < g.Led.MemberCount; i++) Assert.That(g.Led.MemberAt(i), Is.GreaterThan(g.Led.MemberAt(i - 1)), "the list is in key order");
            foreach (ulong key in g.Keys) Assert.That(g.Moves.IsCommanded(g.Id(key)), Is.EqualTo(key >= 2 && key <= 4), "a member counts as commanded, unit " + key);
            // The spot nearest to unit 4 went to unit 2, who joined first; the next two are equally near and it has the lower.
            Assert.That(g.Led.SpotOf(g.Id(4)), Is.EqualTo(1));
            g.Order(SquadCommandKind.Dismiss);
            foreach (ulong key in g.Keys) Assert.That(g.Moves.IsCommanded(g.Id(key)), Is.False, "dismissed, unit " + key);

            LedGame two = Small(2, 30000, Field());
            two.Order(SquadCommandKind.Follow);
            Assert.That(two.Members(), Is.EquivalentTo(new ulong[] { 2, 3 }), "a squad of two takes the two nearest");
        }

        // Units 2 and 3 stand equally near. Which of them a squad of one takes is decided by a mix of their keys, so it is
        // the same one whichever was put into the world first.
        [Test]
        public void OfTwoEquallyNearMatesTheSameOneJoinsWhateverTheOrderOfTheUnitList()
        {
            UnitSpawn[] units = Field();
            LedGame listed = Small(1, 30000, units);
            listed.Order(SquadCommandKind.Follow);
            Assert.That(listed.Members(), Is.EqualTo(new ulong[] { 2 }));
            LedGame swapped = Small(1, 30000, new[] { units[0], units[2], units[1], units[3], units[4], units[5] });
            swapped.Order(SquadCommandKind.Follow);
            Assert.That(swapped.Members(), Is.EqualTo(new ulong[] { 2 }));
        }

        [Test]
        public void AFallenMateAndOneSomebodyElseCommandsAreNotCalled()
        {
            var fallen = new LedGame(SmallWorld(8, 30000, Field()), 1, 2);
            fallen.Wait(3);
            Assert.That(fallen.W.Dead(2) && fallen.W.Has<Position>(2), Is.True, "the fallen soldier still lies on the field");
            fallen.Order(SquadCommandKind.Follow);
            Assert.That(fallen.Members(), Is.EquivalentTo(new ulong[] { 3, 4 }));
            fallen.Wait(40);
            Assert.That(fallen.Members(), Is.EquivalentTo(new ulong[] { 3, 4 }));

            LedGame g = Small(8, 30000, Field());
            var orders = new LedOrders(g.Moves);
            orders.Commanded.Add(g.Id(2).Value);
            g.Moves = orders;
            g.Order(SquadCommandKind.Follow);
            Assert.That(g.Members(), Is.EquivalentTo(new ulong[] { 3, 4 }), "unit 2 is under somebody else's command");

            // Move orders that call nobody commanded: the squad itself keeps a member from joining twice.
            LedGame plain = Small(8, 30000, Field());
            plain.Moves = new LedOrders(plain.Moves) { NobodyCommanded = true };
            plain.Order(SquadCommandKind.Follow);
            plain.Wait(5);
            Assert.That(plain.Led.MemberCount, Is.EqualTo(3));
            Assert.That(plain.Members(), Is.EquivalentTo(new ulong[] { 2, 3, 4 }));
        }

        // A 2 m formation around the leader at (10, 20): spot 2 is two metres east, spots 3 and 4 are south-east and
        // south-west, spot 7 is in the second ring, due north.
        [Test]
        public void AMemberTakesTheFreeSpotNearestToItAndTheLowerOfTwoEquallyNear()
        {
            LedGame g = Small(8, 30000, new[] { Soldier(1, 10, 20), Soldier(2, 10, 23), Soldier(3, 10, 17), Soldier(4, 16, 20) });
            g.Order(SquadCommandKind.Follow);
            Assert.That(g.Led.SpotOf(g.Id(2)), Is.EqualTo(7), "the nearest, though it is in the second ring");
            Assert.That(g.Led.SpotOf(g.Id(3)), Is.EqualTo(3), "spots 3 and 4 are equally near: the lower");
            Assert.That(g.Led.SpotOf(g.Id(4)), Is.EqualTo(2));
            SimVec3[] offsets = LedSquad.Formation(8, g.SpacingRaw);
            foreach (ulong key in g.Members()) OnItsOwnPlace(g, key, offsets);
        }

        // The radius is measured from the leader, not from the anchor. Radius 5 m: unit 2 stands two metres from the
        // leader, unit 3 fifteen. The squad holds; the leader walks towards unit 3.
        [Test]
        public void AMateTheLeaderComesNearLaterJoinsAHoldingSquadAndGetsAPlace()
        {
            LedGame g = Small(4, 5000, new[] { Soldier(1, 10, 20), Soldier(2, 12, 20), Soldier(3, 25, 20) });
            g.Order(SquadCommandKind.Follow);
            g.Order(SquadCommandKind.Hold);
            Assert.That(g.Members(), Is.EqualTo(new ulong[] { 2 }));
            SimVec3 anchor = g.Led.Anchor;
            double before = 0, joinedAt = 0;
            for (int t = 0; t < 80 && g.Led.MemberCount == 1; t++)
            {
                before = joinedAt;
                joinedAt = Metres(g.At(1), g.At(3));
                g.Step(Walk(1000, 0));
            }
            Assert.That(g.Members(), Is.EquivalentTo(new ulong[] { 2, 3 }));
            Assert.That(joinedAt, Is.LessThanOrEqualTo(5.0), "it joined on the first tick the leader stood within the radius");
            Assert.That(before, Is.GreaterThan(5.0));
            Assert.That((g.Led.Anchor.X.Raw, g.Led.Anchor.Z.Raw, g.Led.Mode), Is.EqualTo((anchor.X.Raw, anchor.Z.Raw, LedMode.Hold)), "the squad holds where it held");
            Assert.That(g.Led.SpotOf(g.Id(2)), Is.EqualTo(2));
            Assert.That(g.Led.SpotOf(g.Id(3)), Is.EqualTo(1));
            SimVec3[] offsets = LedSquad.Formation(4, g.SpacingRaw);
            foreach (ulong key in g.Members()) OnItsOwnPlace(g, key, offsets);
            g.Wait(150);
            Assert.That(g.InPlace(), Is.EqualTo(2), "and the newcomer walked to its place");
        }

        [Test]
        public void WhenAMemberFallsAndNobodyCanReplaceItTheOthersKeepTheirSpotsAndPlaces()
        {
            var g = new LedGame(SmallWorld(8, 30000, new[] { Soldier(1, 10, 20), Soldier(2, 13, 20), Soldier(3, 7, 20), Soldier(4, 16, 20) }), 40, 3);
            g.Order(SquadCommandKind.Follow);
            g.Wait(38);
            Assert.That(g.Members().Length, Is.EqualTo(3));
            Assert.That(g.Members()[0], Is.EqualTo(3UL), "the one who falls is the first of the list");
            int two = g.Led.SpotOf(g.Id(2)), four = g.Led.SpotOf(g.Id(4));
            g.Wait(2);
            Assert.That(g.W.Dead(3), Is.True);
            Assert.That(g.Members(), Is.EquivalentTo(new ulong[] { 2, 4 }));
            Assert.That((g.Led.SpotOf(g.Id(2)), g.Led.SpotOf(g.Id(4)), g.Led.SpotOf(g.Id(3))), Is.EqualTo((two, four, -1)));
            SimVec3[] offsets = LedSquad.Formation(8, g.SpacingRaw);
            foreach (ulong key in g.Members()) OnItsOwnPlace(g, key, offsets);
            g.Wait(60);
            Assert.That(g.Members(), Is.EquivalentTo(new ulong[] { 2, 4 }), "the fallen is not called back");
            Assert.That(g.InPlace(), Is.EqualTo(2));
        }

        // A leader of 7.5 m/s walks a quarter metre a tick: on every fourth tick the squad sees it exactly half a spacing
        // (1 m) from the anchor, which is not yet "more than half".
        [Test]
        public void TheAnchorFollowsOnceTheLeaderIsMoreThanHalfASpacingAway()
        {
            LedGame g = Small(8, 30000, new[] { M6World.U(1, 1, 10, 20, speed: 7500), Soldier(2, 12, 20), Soldier(3, 8, 20) });
            g.Order(SquadCommandKind.Follow);
            long half = g.SpacingRaw / 2;
            int onTheLine = 0, moved = 0;
            for (int t = 0; t < 11; t++)
            {
                // The squad looks before the leader takes this tick's step.
                SimVec3 seen = g.At(1), old = g.Led.Anchor;
                g.Step(Walk(1000, 0));
                long gap = seen.X.Raw - old.X.Raw;
                if (gap == half) onTheLine++;
                if (gap <= half) Assert.That(g.Led.Anchor.X.Raw, Is.EqualTo(old.X.Raw), "tick " + t + ": the anchor stays");
                else
                {
                    Assert.That(g.Led.Anchor.X.Raw, Is.EqualTo(seen.X.Raw), "tick " + t + ": the anchor is where the leader stood");
                    moved++;
                }
            }
            Assert.That((onTheLine, moved), Is.EqualTo((2, 2)));
        }

        // A wall from (20, 10) to (21, 30); the squad is sent to (19, 20), a metre west of it. The places of spots 1, 2 and 3
        // are in the wall. Their members take, in the order of the list, the first places of the formation that are free,
        // can be stood on and are seen from the anchor: 13, 14 and 15. Places 9 to 11 can be stood on too, behind the wall.
        [Test]
        public void APlaceInAWallOrBehindItIsLeftOutAndItsMemberTakesTheNextFreeOne()
        {
            UnitSpawn[] units = { Soldier(1, 10, 20), Soldier(2, 8, 18), Soldier(3, 8, 20), Soldier(4, 8, 22), Soldier(5, 10, 18), Soldier(6, 10, 22), Soldier(7, 12, 18), Soldier(8, 12, 20), Soldier(9, 12, 22) };
            LedGame g = Small(8, 30000, units, (20, 10, 1, 20));
            g.Order(SquadCommandKind.Follow);
            g.Order(SquadCommandKind.MoveTo, SimVec3.Metres(19, 0, 20));
            SimVec3[] offsets = LedSquad.Formation(8, g.SpacingRaw);
            SimVec3 anchor = g.Led.Anchor;
            Assert.That((anchor.X.Raw, anchor.Z.Raw), Is.EqualTo((SimFix.FromInt(19).Raw, SimFix.FromInt(20).Raw)));
            var taken = new HashSet<(long, long)>();
            var elsewhere = new List<ulong>();
            var spots = new List<int>();
            foreach (ulong key in g.Members())
            {
                Assert.That(g.Led.TryPlace(g.Id(key), out SimVec3 place), Is.True, "member " + key);
                Assert.That(taken.Add((place.X.Raw, place.Z.Raw)), Is.True, "one member a place, member " + key);
                Assert.That(g.Moves.CanStandAt(place), Is.True, "member " + key);
                Assert.That(place.X.Raw, Is.LessThan(SimFix.FromInt(20).Raw), "on the anchor's side of the wall, member " + key);
                int spot = g.Led.SpotOf(g.Id(key));
                SimVec3 own = anchor + offsets[spot];
                if (place.X.Raw == own.X.Raw && place.Z.Raw == own.Z.Raw) continue;
                elsewhere.Add(key); spots.Add(spot);
                SimVec3 next = anchor + offsets[12 + elsewhere.Count];
                Assert.That((place.X.Raw, place.Z.Raw), Is.EqualTo((next.X.Raw, next.Z.Raw)), "member " + key);
            }
            Assert.That(g.Led.MemberCount, Is.EqualTo(8));
            Assert.That(elsewhere.ToArray(), Is.EqualTo(new ulong[] { 5, 8, 9 }));
            Assert.That(spots.ToArray(), Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(g.Moves.TryStandAt(anchor + offsets[9], 0, out _), Is.True, "a unit could stand behind the wall");
        }

        // A corridor two metres wide and thirty long. Of the formation's places only the six on its middle line can be used
        // in it: two of the eight members get none, and stop where they are.
        [Test]
        public void AMemberForWhomNoPlaceIsLeftIsStopped()
        {
            UnitSpawn[] units = { Soldier(1, 45, 20), Soldier(2, 47, 18), Soldier(3, 47, 20), Soldier(4, 47, 22), Soldier(5, 49, 18), Soldier(6, 49, 20), Soldier(7, 49, 22), Soldier(8, 51, 19), Soldier(9, 51, 21) };
            LedGame g = Small(8, 30000, units, (5, 18, 30, 1), (5, 21, 30, 1));
            g.Order(SquadCommandKind.Follow);
            g.Order(SquadCommandKind.MoveTo, SimVec3.Metres(60, 0, 20));
            g.Wait(5);
            Assert.That(g.Led.MemberCount, Is.EqualTo(8));
            foreach (ulong key in g.Members()) Assert.That(g.Walking(key), Is.True, "on its way east, member " + key);
            g.Order(SquadCommandKind.MoveTo, SimVec3.Metres(20, 0, 20));
            int without = 0;
            foreach (ulong key in g.Members())
            {
                if (g.Led.TryPlace(g.Id(key), out SimVec3 place))
                {
                    Assert.That(place.Z.Raw, Is.EqualTo(SimFix.FromInt(20).Raw), "on the middle line, member " + key);
                    Assert.That(g.Walking(key), Is.True, "member " + key);
                }
                else
                {
                    without++;
                    Assert.That(g.Walking(key), Is.False, "no place: stopped, member " + key);
                }
            }
            Assert.That(without, Is.EqualTo(2));
        }

        // The squad's arrival rule is the AI's (SimpleAiSystem.Arrived): a member stands as soon as it is within a quarter
        // metre of its place, short of the point itself.
        [Test]
        public void AMemberStopsAsSoonAsItHasArrived()
        {
            LedGame g = Small(8, 30000, new[] { Soldier(1, 10, 20), Soldier(2, 13, 20), Soldier(3, 7, 20), Soldier(4, 16, 20) });
            g.Order(SquadCommandKind.Follow);
            g.Wait(120);
            Assert.That(g.Led.MemberCount, Is.EqualTo(3));
            foreach (ulong key in g.Members())
            {
                Assert.That(g.Led.TryPlace(g.Id(key), out SimVec3 place), Is.True, "member " + key);
                Assert.That(g.Walking(key), Is.False, "member " + key);
                Assert.That(Metres(g.At(key), place), Is.InRange(0.01, 0.25), "member " + key);
            }
        }

        // A squad of one. Its member comes from twenty metres away while the leader walks straight at it: the leader is not
        // slowed for a single tick, and the two come closer than two bodies can stand side by side (half a metre).
        [Test]
        public void TheLeaderWalksThroughAMemberThatWalks()
        {
            LedGame g = Small(1, 30000, new[] { Soldier(1, 10, 20), Soldier(2, 30, 20) });
            g.Order(SquadCommandKind.Follow);
            g.Order(SquadCommandKind.Hold);
            double nearest = double.MaxValue;
            bool walkingThen = false;
            for (int t = 0; t < 70; t++)
            {
                SimVec3 from = g.At(1);
                g.Step(g.Towards(g.At(2)));
                Assert.That(Metres(from, g.At(1)), Is.GreaterThanOrEqualTo(0.199), "the leader took a whole step on tick " + t);
                double gap = Metres(g.At(1), g.At(2));
                if (gap < nearest) { nearest = gap; walkingThen = g.Walking(2); }
            }
            Assert.That(nearest, Is.LessThan(0.3));
            Assert.That(walkingThen, Is.True, "the member was walking then");
        }

        [Test]
        public void ACommandToAnotherSquadCallsNobody()
        {
            LedGame g = Small(8, 30000, Field());
            foreach (int squad in new[] { 0, 1 })
                foreach (SquadCommandKind kind in new[] { SquadCommandKind.Hold, SquadCommandKind.MoveTo, SquadCommandKind.Follow, SquadCommandKind.Dismiss })
                {
                    g.W.Sim.Step(new InputFrame(default, default, squad: new SquadCommand(kind, squad, SimVec3.Metres(15, 0, 20), default)));
                    Assert.That((g.Led.Leader, g.Led.MemberCount), Is.EqualTo((0UL, 0)), kind + " to squad " + squad);
                }
        }

        [Test]
        public void APlayerOfNoTeamLeadsNobody()
        {
            LedGame g = Small(8, 30000, new[] { Soldier(1, 10, 20, 0), Soldier(2, 12, 20, 0), Soldier(3, 14, 20, 0) });
            g.Order(SquadCommandKind.Follow);
            Assert.That((g.Led.Leader, g.Led.Mode, g.Led.MemberCount), Is.EqualTo((0UL, LedMode.None, 0)));
        }

        [Test]
        public void AWorldHasALedSquadOnlyWithBothTheRulesAndAPlayer()
        {
            WorldRecipe noPlayer = M6World.RecipeOf(Field()).WithRuleSet(MatchMode.TestNeverEndsId).WithPack(SmallPack(8, 30000));
            M6World w = null;
            Assert.That(() => w = new M6World(noPlayer), Throws.Nothing);
            Assert.That(w.Session.ControlledKey.HasValue, Is.False);
            Assert.That(w.System<LedSquad>(), Is.Null);
            Assert.That(() => w.Step(3), Throws.Nothing);

            WorldRecipe noRules = M6World.RecipeOf(Field()).WithRuleSet(MatchMode.TestNeverEndsId).WithPlayer(1);
            Assert.That(() => w = new M6World(noRules), Throws.Nothing);
            Assert.That(w.Session.ControlledKey.HasValue, Is.True);
            Assert.That(w.System<LedSquad>(), Is.Null);
        }

        // Move orders that say only where a unit can stand: the interface's own answer to "where would it stand".
        private sealed class BareOrders : IMoveOrders
        {
            public bool CanStandAt(SimVec3 at) => at.X.Raw >= 0;
            public void Order(StableId unit, SimVec3 target) { }
            public void Stop(StableId unit) { }
            public bool TryGetOrder(StableId unit, out SimVec3 target) { target = default; return false; }
        }

        [Test]
        public void WhereAUnitWouldStand()
        {
            LedGame g = Small(8, 30000, Field());
            Assert.That(g.Moves.TryStandAt(SimVec3.Metres(15, 7, 20), 0, out SimVec3 grounded), Is.True);
            Assert.That((grounded.X.Raw, grounded.Y.Raw, grounded.Z.Raw), Is.EqualTo((SimFix.FromInt(15).Raw, 0L, SimFix.FromInt(20).Raw)), "on the ground under the point");
            Assert.That(g.Moves.TryStandAt(SimVec3.Metres(-5, 0, 20), 0, out _), Is.False, "off the map");
            // A field without floor plates has no floor above the ground, and says so without an error.
            bool stands = true;
            Assert.That(() => stands = g.Moves.TryStandAt(SimVec3.Metres(15, 0, 20), 1, out _), Throws.Nothing);
            Assert.That(stands, Is.False);

            IMoveOrders bare = new BareOrders();
            SimVec3 point = SimVec3.Metres(3, 4, 5);
            Assert.That(bare.TryStandAt(point, 0, out grounded), Is.True);
            Assert.That((grounded.X.Raw, grounded.Y.Raw, grounded.Z.Raw), Is.EqualTo((point.X.Raw, point.Y.Raw, point.Z.Raw)), "move orders that know no ground leave the point as it is");
            Assert.That(bare.TryStandAt(SimVec3.Metres(-3, 4, 5), 0, out _), Is.False);
            Assert.That(bare.TryStandAt(point, 1, out _), Is.False);
        }

        [Test]
        public void ASquadIsMadeOfWholeParts()
        {
            LedGame g = Small(8, 30000, Field());
            var controlled = (IControlTarget)g.W.Session.GetType().GetField("controlled", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(g.W.Session);
            var rules = new LedRules(8, 30000);
            Assert.That(() => new LedSquad(g.W.Combat, g.Moves, controlled, rules, g.SpacingRaw), Throws.Nothing);
            Assert.That(() => new LedSquad(null, g.Moves, controlled, rules, g.SpacingRaw), Throws.ArgumentNullException);
            Assert.That(() => new LedSquad(g.W.Combat, null, controlled, rules, g.SpacingRaw), Throws.ArgumentNullException);
            Assert.That(() => new LedSquad(g.W.Combat, g.Moves, null, rules, g.SpacingRaw), Throws.ArgumentNullException);
            Assert.That(() => new LedSquad(g.W.Combat, g.Moves, controlled, default, g.SpacingRaw), Throws.TypeOf<ArgumentOutOfRangeException>(), "rules that were never set");
            Assert.That(() => new LedSquad(g.W.Combat, g.Moves, controlled, rules, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new LedRules(0, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new LedRules(LedRules.MaxSize + 1, 1), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => new LedRules(1, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
            var widest = new LedRules(LedRules.MaxSize, 1);
            Assert.That((widest.Size, widest.RecruitRadiusMm), Is.EqualTo((30, 1)));
        }
    }
}
