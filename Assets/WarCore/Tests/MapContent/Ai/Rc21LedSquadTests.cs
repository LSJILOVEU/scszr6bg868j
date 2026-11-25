using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.LedGame;

namespace WarCore.Tests
{
    // RC2-1: the squad the controlled unit leads (Battle/Ai/LedSquad.cs), played in the showcase stages whose packs carry its
    // rules; the crowd rule that lets the leader through its own side; the two content fields; the bridge commands.
    public sealed class Rc21LedSquadTests
    {
        [Test]
        public void AStageWithTheRulesHasASquadThatCommandsNobodyUntilItIsCalled()
        {
            var g = new LedGame("battle");
            Assert.That(g.Led, Is.Not.Null);
            g.Wait(300);
            Assert.That((g.Led.Leader, g.Led.Mode, g.Led.MemberCount), Is.EqualTo((0UL, LedMode.None, 0)));
            foreach (ulong key in g.Keys) if (g.OnField(key)) Assert.That(g.Led.IsMember(g.Id(key)), Is.False);
        }

        [Test]
        public void FollowMakesTheNearestTeamMatesASquadOfTheRuleSize()
        {
            var g = new LedGame("battle");
            var before = new Dictionary<ulong, SimVec3>();
            foreach (ulong key in g.Keys) if (g.OnField(key)) before[key] = g.At(key);
            g.Order(SquadCommandKind.Follow);
            Assert.That(g.Led.Mode, Is.EqualTo(LedMode.Follow));
            Assert.That(g.Led.Leader, Is.EqualTo(g.Id(g.Player).Value));
            Assert.That(g.Led.MemberCount, Is.EqualTo(g.Led.Rules.Size));
            Assert.That(g.Led.IsMember(g.Id(g.Player)), Is.False, "the leader is not a member");
            var members = new HashSet<ulong>(g.Members());
            double farthest = 0, nearestOutside = double.MaxValue;
            foreach (KeyValuePair<ulong, SimVec3> unit in before)
            {
                if (unit.Key == g.Player || g.Team(unit.Key) != g.Team(g.Player)) { Assert.That(members.Contains(unit.Key), Is.False); continue; }
                double far = Metres(unit.Value, before[g.Player]);
                if (members.Contains(unit.Key)) farthest = Math.Max(farthest, far); else nearestOutside = Math.Min(nearestOutside, far);
            }
            Assert.That(farthest, Is.LessThanOrEqualTo(nearestOutside), "nearest first");
            Assert.That(farthest, Is.LessThanOrEqualTo(g.Led.Rules.RecruitRadiusMm / 1000.0));
        }

        [Test]
        public void EveryMemberGetsItsOwnPlaceAroundTheAnchorAndStandsOnIt()
        {
            var g = new LedGame("battle");
            g.Order(SquadCommandKind.Follow);
            g.Wait(90);
            SimVec3[] offsets = LedSquad.Formation(g.Led.Rules.Size, g.SpacingRaw);
            var spots = new HashSet<int>();
            foreach (ulong key in g.Members())
            {
                int spot = g.Led.SpotOf(g.Id(key));
                Assert.That(spot, Is.InRange(0, g.Led.Rules.Size - 1));
                Assert.That(spots.Add(spot), Is.True, "one member a spot");
                Assert.That(g.Led.TryPlace(g.Id(key), out SimVec3 place), Is.True);
                // Open ground: every member stands on its own spot's place.
                Assert.That((place.X.Raw, place.Z.Raw), Is.EqualTo((g.Led.Anchor.X.Raw + offsets[spot].X.Raw, g.Led.Anchor.Z.Raw + offsets[spot].Z.Raw)));
                Assert.That(Metres(g.At(key), place), Is.LessThanOrEqualTo(0.5), "member " + key);
            }
            Assert.That(g.Led.SpotOf(g.Id(g.Player)), Is.EqualTo(-1));
        }

        [Test]
        public void TheSquadKeepsUpWithItsLeaderAndStandsWithinThreeSecondsOfItsStop()
        {
            var g = new LedGame("battle");
            g.Order(SquadCommandKind.Follow);
            SimVec3 start = g.At(g.Player);
            double half = g.SpacingRaw / (double)SimFix.Scale / 2;
            for (int t = 0; t < 100; t++)
            {
                g.Step(Walk(1000, 0));
                // The anchor trails the leader by at most half a spacing, plus the step the leader took after the squad looked.
                Assert.That(Metres(g.Led.Anchor, g.At(g.Player)), Is.LessThanOrEqualTo(half + 0.25), "tick " + t);
            }
            Assert.That(Metres(start, g.At(g.Player)), Is.GreaterThanOrEqualTo(15), "the leader walked");
            g.Wait(90);
            Assert.That(g.Led.MemberCount, Is.EqualTo(g.Led.Rules.Size));
            Assert.That(g.InPlace(), Is.GreaterThanOrEqualTo(g.Led.Rules.Size - 1));
            foreach (ulong key in g.Members()) Assert.That(Metres(g.At(key), g.At(g.Player)), Is.LessThanOrEqualTo(8), "member " + key);
        }

        [Test]
        public void HoldKeepsTheSquadWhereItStoodWhileTheLeaderWalksAway()
        {
            var g = new LedGame("battle");
            g.Order(SquadCommandKind.Follow);
            g.Wait(60);
            g.Order(SquadCommandKind.Hold);
            Assert.That(g.Led.Mode, Is.EqualTo(LedMode.Hold));
            SimVec3 anchor = g.Led.Anchor;
            g.Wait(90);
            var stood = new Dictionary<ulong, SimVec3>();
            foreach (ulong key in g.Members()) stood[key] = g.At(key);
            SimVec3 from = g.At(g.Player);
            g.Step(Walk(0, 1000), 100);
            Assert.That(Metres(from, g.At(g.Player)), Is.GreaterThanOrEqualTo(15), "the leader walked away");
            g.Wait(200);
            Assert.That((g.Led.Anchor.X.Raw, g.Led.Anchor.Z.Raw, g.Led.Mode), Is.EqualTo((anchor.X.Raw, anchor.Z.Raw, LedMode.Hold)));
            int held = 0;
            foreach (ulong key in g.Members())
            {
                if (!stood.TryGetValue(key, out SimVec3 was)) continue;
                Assert.That(Metres(g.At(key), was), Is.LessThanOrEqualTo(0.25), "member " + key);
                held++;
            }
            Assert.That(held, Is.GreaterThanOrEqualTo(g.Led.Rules.Size - 2));
        }

        [Test]
        public void MoveToSendsTheSquadToThePointAndAPointNobodyCanStandOnChangesNothing()
        {
            var g = new LedGame("battle");
            SimVec3 nowhere = SimVec3.Metres(-200, 0, 61);
            g.Order(SquadCommandKind.MoveTo, nowhere);
            Assert.That((g.Led.Leader, g.Led.MemberCount), Is.EqualTo((0UL, 0)), "no squad is made for a point nobody can stand on");
            SimVec3 here = g.At(g.Player);
            var there = new SimVec3(here.X + SimFix.FromInt(20), here.Y, here.Z);
            // The point is given seven metres up in the air: the anchor is the ground under it.
            g.Order(SquadCommandKind.MoveTo, new SimVec3(there.X, there.Y + SimFix.FromInt(7), there.Z));
            Assert.That((g.Led.Mode, g.Led.Anchor.X.Raw, g.Led.Anchor.Y.Raw, g.Led.Anchor.Z.Raw), Is.EqualTo((LedMode.Hold, there.X.Raw, there.Y.Raw, there.Z.Raw)));
            g.Wait(300);
            Assert.That(g.InPlace(), Is.GreaterThanOrEqualTo(g.Led.Rules.Size - 1));
            foreach (ulong key in g.Members()) Assert.That(Metres(g.At(key), there), Is.LessThanOrEqualTo(8), "member " + key);
            ulong[] members = g.Members();
            g.Order(SquadCommandKind.MoveTo, nowhere);
            Assert.That((g.Led.Mode, g.Led.Anchor.X.Raw, g.Led.Anchor.Z.Raw), Is.EqualTo((LedMode.Hold, there.X.Raw, there.Z.Raw)));
            Assert.That(g.Members(), Is.EqualTo(members));
        }

        [Test]
        public void DismissBreaksTheSquadUpAndTheAiTakesItsSoldiersBack()
        {
            var g = new LedGame("battle");
            g.Order(SquadCommandKind.Follow);
            g.Wait(60);
            ulong[] members = g.Members();
            var stood = new Dictionary<ulong, SimVec3>();
            foreach (ulong key in members) stood[key] = g.At(key);
            g.Order(SquadCommandKind.Dismiss);
            Assert.That((g.Led.Leader, g.Led.Mode, g.Led.MemberCount), Is.EqualTo((0UL, LedMode.None, 0)));
            Assert.That((g.Led.Anchor.X.Raw, g.Led.Anchor.Y.Raw, g.Led.Anchor.Z.Raw, g.Led.AnchorLayer), Is.EqualTo((0L, 0L, 0L, 0)), "the anchor is forgotten");
            foreach (ulong key in members) Assert.That(g.Led.IsMember(g.Id(key)), Is.False);
            g.Wait(150);
            int moved = 0;
            foreach (ulong key in members) if (g.OnField(key) && Metres(g.At(key), stood[key]) > 1) moved++;
            Assert.That(moved, Is.GreaterThanOrEqualTo(members.Length / 2), "the scheduler gives them orders again");
            Assert.That(g.Led.MemberCount, Is.EqualTo(0), "and nobody is called back");
        }

        [Test]
        public void AFallenMemberIsReplacedAndTheOthersKeepTheirSpots()
        {
            // The same stage played twice: the first run tells who is in the squad, the second lets one of them fall at tick 31.
            var first = new LedGame("battle");
            first.Order(SquadCommandKind.Follow);
            first.Wait(29);
            ulong fallen = first.Members()[0];
            var g = new LedGame("battle", 31, fallen);
            g.Order(SquadCommandKind.Follow);
            g.Wait(29);
            ulong[] members = g.Members();
            Assert.That(members, Is.EqualTo(first.Members()));
            int spot = g.Led.SpotOf(g.Id(fallen));
            var spots = new Dictionary<ulong, int>();
            foreach (ulong key in members) spots[key] = g.Led.SpotOf(g.Id(key));
            g.Wait(2);
            Assert.That(g.W.Dead(fallen), Is.True);
            Assert.That(g.Led.MemberCount, Is.EqualTo(g.Led.Rules.Size), "the squad is full again");
            ulong joined = 0;
            foreach (ulong key in g.Members())
            {
                Assert.That(key, Is.Not.EqualTo(fallen));
                if (spots.TryGetValue(key, out int was)) Assert.That(g.Led.SpotOf(g.Id(key)), Is.EqualTo(was), "member " + key);
                else joined = key;
            }
            Assert.That(joined, Is.Not.EqualTo(0UL));
            Assert.That(g.Led.SpotOf(g.Id(joined)), Is.EqualTo(spot), "the newcomer takes the only free spot");
        }

        [Test]
        public void TheSquadBreaksUpWhenItsLeaderFalls()
        {
            var g = new LedGame("battle", 31, new LedGame("battle").Player);
            g.Order(SquadCommandKind.Follow);
            g.Wait(29);
            Assert.That(g.Led.MemberCount, Is.GreaterThan(0));
            g.Wait(2);
            Assert.That(g.W.Dead(g.Player), Is.True);
            Assert.That((g.Led.Leader, g.Led.Mode, g.Led.MemberCount), Is.EqualTo((0UL, LedMode.None, 0)));
        }

        // A squad on its way (to a point 20 m off in the field; to the upper floor of the house) is saved at tick 40 and loaded
        // into a world that played on without any order: from there on it plays tick for tick as the world never saved.
        [TestCase("battle", 20, 0, 0)]
        [TestCase("house", 20, 8, 2)]
        public void ASquadOnItsWaySavedAndLoadedPlaysOnTheSame(string id, int x, int z, int layer)
        {
            LedGame whole = new LedGame(id), early = new LedGame(id), resumed = new LedGame(id);
            foreach (LedGame g in new[] { whole, early })
            {
                SimVec3 here = g.At(g.Player);
                SimVec3 point = layer == 0 ? new SimVec3(here.X + SimFix.FromInt(x), here.Y, here.Z) : SimVec3.Metres(x, 3, z);
                g.Order(SquadCommandKind.Follow);
                g.Order(SquadCommandKind.MoveTo, point, layer);
                Assert.That(g.Led.Mode, Is.EqualTo(LedMode.Hold), "the point can be stood on");
                g.Wait(38);
            }
            Assert.That(whole.Led.MemberCount, Is.GreaterThan(0));
            Assert.That(whole.InPlace(), Is.LessThan(whole.Led.MemberCount), "still on the way");
            resumed.Wait(120);
            Assert.That(resumed.W.Hash, Is.Not.EqualTo(whole.W.Hash));
            resumed.W.Sim.RestoreRetainedSnapshot(early.W.Sim.SaveRetainedSnapshot());
            Assert.That(resumed.W.Hash, Is.EqualTo(whole.W.Hash), "the loaded world is the saved one");
            Assert.That(resumed.Led.MemberCount, Is.EqualTo(whole.Led.MemberCount));
            int most = 0;
            for (int t = 0; t < 300; t++)
            {
                whole.Wait(1); resumed.Wait(1);
                Assert.That(resumed.W.Hash, Is.EqualTo(whole.W.Hash), id + " step " + t);
                Assert.That(resumed.InPlace(), Is.EqualTo(whole.InPlace()), "the loaded squad knows its places, " + id + " step " + t);
                most = Math.Max(most, whole.InPlace());
            }
            Assert.That(most, Is.GreaterThanOrEqualTo(2), "members reached their places");
        }

        [Test]
        public void TheSquadClimbsToTheFloorItIsSentTo()
        {
            var g = new LedGame("house");
            g.Order(SquadCommandKind.Follow);
            Assert.That(g.Led.MemberCount, Is.GreaterThan(0));
            // The point is given at the height of the ground: the anchor is the floor of the layer above it.
            g.Order(SquadCommandKind.MoveTo, SimVec3.Metres(20, 0, 8), 2);
            Assert.That((g.Led.Mode, g.Led.AnchorLayer, g.Led.Anchor.Y.Raw), Is.EqualTo((LedMode.Hold, 2, SimFix.FromInt(3).Raw)));
            g.Wait(300);
            IUnitLayers layers = PlateLayersOf(g.W);
            int up = 0;
            foreach (ulong key in g.Members())
                if (layers.LayerOf(g.W.Unit(key)) == 2 && g.Led.TryPlace(g.Id(key), out SimVec3 place) && Metres(g.At(key), place) <= 0.5) up++;
            Assert.That(up * 2, Is.GreaterThanOrEqualTo(g.Led.MemberCount), "at least half of the squad stands on its places upstairs");
            g.Order(SquadCommandKind.Dismiss);
            Assert.That((g.Led.AnchorLayer, g.Led.Anchor.Y.Raw), Is.EqualTo((0, 0L)), "dismissed, the anchor and its layer are forgotten");
        }

        // The squad is on its way to places on the ground floor when it is sent to the same point one floor up: every member
        // is told the new floor at once, also those whose new place is straight above the old one. Then the same from
        // standing: the squad stands on its places downstairs, is sent up, and everybody climbs.
        [Test]
        public void SentOneFloorUpFromWhereItWalksOrStandsTheSquadClimbs()
        {
            var g = new LedGame("house", 1, 9, 10, 11, 12, 13, 14, 15, 16);
            IUnitLayers layers = PlateLayersOf(g.W);
            SimVec3 point = SimVec3.Metres(20, 0, 10);
            g.Order(SquadCommandKind.Follow);
            g.Order(SquadCommandKind.MoveTo, point);
            g.Wait(5);
            var downstairs = new Dictionary<ulong, SimVec3>();
            foreach (ulong key in g.Members())
            {
                Assert.That(g.Moves.TryGetOrder(g.Id(key), out SimVec3 target, out int layer), Is.True, "member " + key);
                Assert.That(layer, Is.EqualTo(0), "member " + key);
                downstairs[key] = target;
            }
            g.Order(SquadCommandKind.MoveTo, point, 2);
            int above = 0;
            foreach (ulong key in g.Members())
            {
                Assert.That(g.Moves.TryGetOrder(g.Id(key), out SimVec3 target, out int layer), Is.True, "member " + key);
                Assert.That((layer, target.Y.Raw), Is.EqualTo((2, SimFix.FromInt(3).Raw)), "member " + key);
                if (downstairs.TryGetValue(key, out SimVec3 was) && was.X.Raw == target.X.Raw && was.Z.Raw == target.Z.Raw) above++;
            }
            Assert.That(above, Is.GreaterThan(0), "some of the new places are straight above the old ones");

            g.Order(SquadCommandKind.MoveTo, point);
            for (int t = 0; t < 600 && g.InPlace() < g.Led.MemberCount; t++) g.Wait(1);
            Assert.That(g.InPlace(), Is.EqualTo(g.Led.MemberCount), "the squad stands downstairs");
            foreach (ulong key in g.Members()) Assert.That(layers.LayerOf(g.W.Unit(key)), Is.EqualTo(0), "member " + key);
            g.Order(SquadCommandKind.MoveTo, point, 2);
            g.Wait(150);
            foreach (ulong key in g.Members()) Assert.That(layers.LayerOf(g.W.Unit(key)), Is.EqualTo(2), "member " + key);
            Assert.That(g.InPlace(), Is.EqualTo(g.Led.MemberCount), "and upstairs");
        }

        // The upper floor of the house begins at x 15. A body does not fit within its first quarter metre, and a squad sent
        // to a point where nobody can stand stays as it was.
        [Test]
        public void UpstairsAUnitStandsOnTheFloorButNotAtItsEdge()
        {
            var g = new LedGame("house");
            Assert.That(g.Moves.TryStandAt(SimVec3.Metres(16, 0, 8), 2, out SimVec3 grounded), Is.True);
            Assert.That((grounded.X.Raw, grounded.Y.Raw, grounded.Z.Raw), Is.EqualTo((SimFix.FromInt(16).Raw, SimFix.FromInt(3).Raw, SimFix.FromInt(8).Raw)));
            var edge = new SimVec3(SimFix.Ratio(151, 10), default, SimFix.FromInt(8));
            Assert.That(g.Moves.TryStandAt(edge, 2, out _), Is.False);
            g.Order(SquadCommandKind.Follow);
            g.Order(SquadCommandKind.MoveTo, edge, 2);
            Assert.That((g.Led.Mode, g.Led.AnchorLayer), Is.EqualTo((LedMode.Follow, 0)));
        }

        // Acceptance 7, first half. The leader walks in through the west doorway and up the stair; told only to follow, the
        // squad comes up after it. The house's defenders fall on the first tick, so the climb is not a firefight. The leader
        // keeps to the middle of the stair's southern row: a unit whose footprint lies over both rows is not let onto the
        // stair (a gap of the walking code, older than the squad).
        // atTheTop: the leader stops on the first step it stands on the upper floor, less than half a spacing from where the
        // anchor was; the change of floor alone moves the anchor.
        [TestCase(false)]
        [TestCase(true)]
        public void TheSquadFollowsItsLeaderUpTheStair(bool atTheTop)
        {
            var g = new LedGame("house", 1, 9, 10, 11, 12, 13, 14, 15, 16);
            IUnitLayers layers = PlateLayersOf(g.W);
            EntityId me = g.W.Unit(g.Player);
            g.Order(SquadCommandKind.Follow);
            var way = new[] { (X: 12.5, Z: 12.5), (X: 16.5, Z: 12.5), (X: 22.5, Z: 12.5) };
            int leg = 0, ticks = 0;
            while (leg < way.Length && ticks < 300 && !(atTheTop && layers.LayerOf(me) == 2))
            {
                SimVec3 at = g.At(g.Player);
                double dx = way[leg].X - at.X.Raw / (double)SimFix.Scale, dz = way[leg].Z - at.Z.Raw / (double)SimFix.Scale, far = Math.Sqrt(dx * dx + dz * dz);
                if (far < 0.3) { leg++; continue; }
                g.Step(Walk((int)Math.Round(dx / far * 1000), (int)Math.Round(dz / far * 1000)));
                ticks++;
            }
            Assert.That(layers.LayerOf(me), Is.EqualTo(2), "the leader reached the upper floor");
            SimVec3 stop = g.At(g.Player), before = g.Led.Anchor;
            int beforeLayer = g.Led.AnchorLayer;
            g.Wait(1);
            if (atTheTop)
            {
                Assert.That(beforeLayer, Is.EqualTo(1), "the anchor was on the stair");
                Assert.That(Metres(before, stop) * 2, Is.LessThanOrEqualTo(g.SpacingRaw / (double)SimFix.Scale), "and less than half a spacing away");
            }
            Assert.That(g.Led.AnchorLayer, Is.EqualTo(2));
            Assert.That(g.Led.Anchor.X.Raw, Is.EqualTo(stop.X.Raw));
            Assert.That(g.Led.Anchor.Z.Raw, Is.EqualTo(stop.Z.Raw));
            int best = 0;
            for (int t = 1; t <= 300 && best * 2 < g.Led.MemberCount; t++)
            {
                g.Wait(1);
                int up = 0;
                foreach (ulong key in g.Members())
                    if (layers.LayerOf(g.W.Unit(key)) == 2 && g.Led.TryPlace(g.Id(key), out SimVec3 place) && Metres(g.At(key), place) <= 0.5) up++;
                if (up > best) best = up;
            }
            Assert.That(g.Led.MemberCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(best * 2, Is.GreaterThanOrEqualTo(g.Led.MemberCount), "at least half of the squad stands on its places upstairs");
        }

        private static IUnitLayers PlateLayersOf(M6World w) =>
            (IUnitLayers)typeof(LedSquad).GetField("layers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<LedSquad>());

        private static object PassRule(M6World w) =>
            typeof(InterimLocomotion).GetField("pass", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<InterimLocomotion>());

        private static bool Ask(object rule, string method, EntityId mover, EntityId other) =>
            (bool)typeof(ICrowdPass).GetMethod(method).Invoke(rule, new object[] { mover, other });

        // Every question the crowd code can ask about the units of the field: the rule of this world against the plain rule
        // of the same world. calledFor: the leader's own questions about these units must be answered yes.
        private static void SameAsPlainExceptForTheLeader(LedGame g, object rule, object plain, HashSet<ulong> calledFor, string when)
        {
            EntityId me = g.W.Unit(g.Player);
            var field = new List<ulong>();
            foreach (ulong key in g.Keys) if (g.OnField(key) && key != g.Player && (field.Count < 20 || calledFor.Contains(key))) field.Add(key);
            foreach (ulong key in field)
            {
                EntityId other = g.W.Unit(key);
                bool member = calledFor.Contains(key);
                Assert.That(Ask(rule, "MayPass", me, other), Is.EqualTo(member), when + ": the leader passes a standing member only, unit " + key);
                Assert.That(Ask(rule, "MayPassMoving", me, other), Is.EqualTo(member), when + ": and a walking member only, unit " + key);
                Assert.That(Ask(rule, "MayPass", other, me) || Ask(rule, "MayPassMoving", other, me), Is.False, when + ": nobody passes the leader");
                Assert.That(Ask(plain, "MayPass", me, other) || Ask(plain, "MayPassMoving", me, other), Is.False, "the plain rule never lets the player pass");
                foreach (ulong third in field)
                {
                    if (third == key) continue;
                    EntityId c = g.W.Unit(third);
                    Assert.That(Ask(rule, "MayPass", other, c), Is.EqualTo(Ask(plain, "MayPass", other, c)), when + ": everybody else as before");
                    Assert.That(Ask(rule, "MayPassMoving", other, c), Is.False);
                }
            }
        }

        [Test]
        public void TheLeaderPassesTheMembersOfItsSquadAndNothingElseChanges()
        {
            var g = new LedGame("battle");
            object rule = PassRule(g.W);
            Assert.That(((ICrowdPass)rule).Digest, Is.EqualTo(0x5445414D49444C32UL));
            // The rule the same world would have without a led squad.
            object plain = Activator.CreateInstance(rule.GetType(), BindingFlags.NonPublic | BindingFlags.Instance, null,
                new object[] { g.W.Sim, g.W.Combat, g.W.Recipe.Combat, g.W.Session.GetType().GetField("controlled", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(g.W.Session) }, null);
            Assert.That(((ICrowdPass)plain).Digest, Is.EqualTo(0x5445414D49444C31UL));
            ulong other = 0;
            foreach (ulong key in g.Keys) if (other == 0 && key != g.Player && g.OnField(key)) other = key;
            Assert.That(() => ((ICrowdPass)plain).MayPassMoving(g.W.Unit(g.Player), g.W.Unit(other)), Throws.Nothing, "a rule without a squad answers too");
            var nobody = new HashSet<ulong>();
            SameAsPlainExceptForTheLeader(g, rule, plain, nobody, "before any order");
            g.Wait(120);
            SameAsPlainExceptForTheLeader(g, rule, plain, nobody, "after two seconds without an order");
            g.Order(SquadCommandKind.Follow);
            SameAsPlainExceptForTheLeader(g, rule, plain, new HashSet<ulong>(g.Members()), "following");
            g.Order(SquadCommandKind.Hold);
            g.Wait(60);
            SameAsPlainExceptForTheLeader(g, rule, plain, new HashSet<ulong>(g.Members()), "holding");
            g.Order(SquadCommandKind.Dismiss);
            SameAsPlainExceptForTheLeader(g, rule, plain, nobody, "dismissed");
        }

        [Test]
        public void TheLeaderWalksStraightThroughAMemberThatHolds()
        {
            var g = new LedGame("battle");
            g.Order(SquadCommandKind.Follow);
            g.Wait(60);
            g.Order(SquadCommandKind.Hold);
            g.Wait(30);
            ulong member = g.Members()[0];
            SimVec3 from = g.At(g.Player), to = g.At(member);
            double far = Metres(from, to);
            Assert.That(far, Is.GreaterThan(1));
            int x = (int)Math.Round((to.X.Raw - from.X.Raw) / (double)SimFix.Scale / far * 1000), z = (int)Math.Round((to.Z.Raw - from.Z.Raw) / (double)SimFix.Scale / far * 1000);
            // Blocked, the leader would stop half a metre short of the member; it has time to go three metres past.
            for (int t = 0; t < 200 && Metres(from, g.At(g.Player)) < far + 1.5; t++) g.Step(Walk(x, z));
            Assert.That(Metres(from, g.At(g.Player)), Is.GreaterThanOrEqualTo(far + 1.5));
            Assert.That(Metres(to, g.At(member)), Is.LessThanOrEqualTo(0.25), "the member did not give way");
        }

        private static AiRules Rules(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["rc21-test"] = "{ \"id\": \"rc21-test\", \"extends\": \"rwr\", \"ai\": " + ai + " }"
        }, FileContentSource.Rwr()), "rc21-test").Section<AiRules>();

        [Test]
        public void TheTwoLedFieldsAreWrittenBothOrNoneAndWithinTheirRanges()
        {
            Assert.That(Rules("{ }").Led, Is.Null);
            LedRules led = Rules("{ \"ledSize\": 8, \"ledRecruitRadiusMm\": 30000 }").Led.Value;
            Assert.That((led.Size, led.RecruitRadiusMm), Is.EqualTo((8, 30000)));
            Assert.That(Rules("{ \"ledSize\": 1, \"ledRecruitRadiusMm\": 1 }").Led.Value.Size, Is.EqualTo(1));
            Assert.That(Rules("{ \"ledSize\": 30, \"ledRecruitRadiusMm\": 1 }").Led.Value.Size, Is.EqualTo(30));
            Assert.That(() => Rules("{ \"ledSize\": 8 }"), Throws.TypeOf<ContentException>().With.Message.Contains("is missing field 'ledRecruitRadiusMm'"));
            Assert.That(() => Rules("{ \"ledRecruitRadiusMm\": 30000 }"), Throws.TypeOf<ContentException>().With.Message.Contains("is missing field 'ledSize'"));
            Assert.That(Rules("{ \"ledSize\": 8, \"ledRecruitRadiusMm\": " + AiSection.MaxSquadRadiusMm + " }").Led.Value.RecruitRadiusMm, Is.EqualTo(AiSection.MaxSquadRadiusMm));
            Assert.Throws<ContentException>(() => Rules("{ \"ledSize\": 8, \"ledRecruitRadiusMm\": " + (AiSection.MaxSquadRadiusMm + 1) + " }"));
            Assert.Throws<ContentException>(() => Rules("{ \"ledSize\": 0, \"ledRecruitRadiusMm\": 30000 }"));
            Assert.Throws<ContentException>(() => Rules("{ \"ledSize\": 31, \"ledRecruitRadiusMm\": 30000 }"));
            Assert.Throws<ContentException>(() => Rules("{ \"ledSize\": 8, \"ledRecruitRadiusMm\": 0 }"));
        }

        // Sizes 9 and 15 need exactly the 36 and 60 places that three and four rings hold.
        [TestCase(1, 6)]
        [TestCase(2, 18)]
        [TestCase(8, 36)]
        [TestCase(9, 36)]
        [TestCase(12, 60)]
        [TestCase(15, 60)]
        [TestCase(30, 126)]
        public void AFormationIsCompleteRingsWithAtLeastFourPlacesAMember(int size, int places)
        {
            SimVec3[] offsets = LedSquad.Formation(size, 2 * SimFix.Scale);
            Assert.That(offsets.Length, Is.EqualTo(places));
            var seen = new HashSet<(long, long)>();
            // Ring by ring, nearest first: ring k is 6k places, between 0.866 k and k spacings from the anchor.
            for (int ring = 1, first = 0; first < offsets.Length; first += 6 * ring, ring++)
                for (int i = first; i < first + 6 * ring; i++)
                {
                    Assert.That(offsets[i].Y.Raw, Is.EqualTo(0));
                    Assert.That(seen.Add((offsets[i].X.Raw, offsets[i].Z.Raw)), Is.True);
                    Assert.That(Metres(offsets[i], default), Is.InRange(ring * 2 * 0.866 - 0.001, ring * 2 + 0.001), "ring " + ring + " place " + i);
                }
            Assert.Throws<ArgumentOutOfRangeException>(() => LedSquad.Formation(0, SimFix.Scale));
            Assert.Throws<ArgumentOutOfRangeException>(() => LedSquad.Formation(31, SimFix.Scale));
            Assert.Throws<ArgumentOutOfRangeException>(() => LedSquad.Formation(1, 0));
        }

        [Test]
        public void ACommandMayBeADismissAndNothingBeyondIt()
        {
            SquadCommand dismiss = default;
            Assert.That(() => dismiss = new SquadCommand(SquadCommandKind.Dismiss, SquadCommand.Led, default, default), Throws.Nothing);
            Assert.That((dismiss.Kind, dismiss.Squad), Is.EqualTo((SquadCommandKind.Dismiss, -1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadCommand((SquadCommandKind)4, SquadCommand.Led, default, default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SquadCommand((SquadCommandKind)(-1), SquadCommand.Led, default, default));
        }

        [Test]
        public void TheBridgeCarriesTheFourLedOrdersAndShowsTheSquadAsTheLeadersBand()
        {
            ShowcaseStage stage = Stage("battle");
            SimSession s = SimSession.Create(stage.Recipe(), stage.MapBytes());
            ulong me = s.ControlledKey.Value;
            Assert.That((s.LedOrders, s.LedOrder, s.LedCount), Is.EqualTo((true, 0, 0)));
            s.Enqueue(BridgeCommand.Follow(1, BridgeCommand.Led, 0));
            Assert.That(() => s.StepOnce(), Throws.Nothing, "a led Follow names no unit to follow");
            Assert.That((s.LedOrder, s.LedCount), Is.EqualTo((1, 8)));
            int banded = 0;
            foreach (ViewEntity e in s.Current.Entities)
            {
                if (e.Key == me) Assert.That(e.Band, Is.EqualTo(me), "the leader's band is its own key");
                else if (e.Band == me) { banded++; Assert.That((e.Mood, e.Regroup), Is.EqualTo((0, 0))); }
            }
            Assert.That(banded, Is.EqualTo(8));
            s.Enqueue(BridgeCommand.Hold(2, BridgeCommand.Led));
            s.StepOnce();
            Assert.That(s.LedOrder, Is.EqualTo(2));
            s.Enqueue(BridgeCommand.MoveTo(3, BridgeCommand.Led, 26000, 61000));
            s.StepOnce();
            Assert.That((s.LedOrder, s.LedCount), Is.EqualTo((2, 8)));
            s.Enqueue(BridgeCommand.Dismiss(4));
            s.StepOnce();
            Assert.That((s.LedOrder, s.LedCount), Is.EqualTo((0, 0)));
            foreach (ViewEntity e in s.Current.Entities) if (e.Key != me) Assert.That(e.Band, Is.Not.EqualTo(me));
        }
    }
}
