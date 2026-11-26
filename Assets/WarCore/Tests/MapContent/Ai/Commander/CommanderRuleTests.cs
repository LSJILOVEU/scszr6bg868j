using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // RC2-3 card appendix 7: ding (a leader falls), ren (underWay per team, appendix 7b), the three anchors of R3, the spawn
    // anchors' coordinate limit, and the saved state (appendix 5).
    public sealed class CommanderRuleTests
    {
        private const string Squads = "\"squadMinSize\": 4, \"squadMaxSize\": 9, \"squadSkirmisherPermille\": 300, \"squadRecruitRadiusMm\": 30000, "
            + "\"squadFormTicks\": 15, \"squadSpreadMinMm\": 1500, \"squadSpreadMaxMm\": 6000";

        // Ding: a squad's leader falls. From then on its members are groups of their own (TryLeader fails, the squad is
        // dissolved) and each keeps its own row, which is the squad's order; when the squad forms again the new leader's row
        // is everybody's. Board: A (team 1, x 80) between B (x 20) and C (x 140), both team 2's and both targets.
        [Test]
        public void DingTheMembersOfAFallenLeaderKeepTheirRows()
        {
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 12; i++) units.Add(CommanderBoard.Man((ulong)i + 1, 1, 74 + i % 4 * 2, 16 + i / 4 * 2));
            units.Add(CommanderBoard.Man(100, 2, 150, 34));
            var b = new CommanderBoard(new[] { CommanderBoard.Point(1, 80, 1), CommanderBoard.Point(2, 20, 2), CommanderBoard.Point(3, 140, 2) },
                units.ToArray(), new[] { CommanderBoard.Base(1, 80, 36), CommanderBoard.Base(2, 156, 36) }, 160, CommanderBoard.Ai(extra: Squads));
            b.To(40);
            ulong leader = 0;
            var members = new List<ulong>();
            for (ulong key = 1; key <= 12 && leader == 0; key++)
            {
                if (b.Squads.SizeOf(b.Id(key)) == 0) continue;
                var mine = new List<ulong>();
                for (ulong other = 1; other <= 12; other++)
                    if (other != key && b.Squads.TryLeader(b.Id(other), out StableId l, out _) && l.Value == b.Id(key).Value) mine.Add(other);
                if (mine.Count >= 3) { leader = key; members = mine; }
            }
            Assert.That(leader, Is.Not.EqualTo(0UL), "a squad of at least four formed");
            int order = b.Row(leader);
            Assert.That(order, Is.EqualTo(1).Or.EqualTo(2), "the squad attacks B or C");
            foreach (ulong m in members) Assert.That(b.Row(m), Is.EqualTo(order), "member " + m + " remembers the squad's order");
            b.Fall(41, leader);
            b.To(41);
            for (int t = 0; t < 60; t++)
            {
                b.Step();
                Assert.That(b.Alive(leader), Is.False);
                foreach (ulong m in members) Assert.That(b.Row(m), Is.EqualTo(order), "tick " + b.Tick + ": member " + m);
            }
            bool reformed = false;
            foreach (ulong m in members) reformed |= b.Squads.SizeOf(b.Id(m)) > 0;
            Assert.That(reformed, Is.True, "the members formed a squad again");
            Assert.That(b.Checker.Clean, Is.True, b.Checker.First);
        }

        // Ren (appendix 7b): four points on a line 60 m apart, team 1 holds 0 and team 2 holds 3. For 200 ticks team 1
        // attacks 1 and not 2, team 2 attacks 2 and not 1: a team's underWay holds only its own groups' old orders.
        [TestCase(false)]
        [TestCase(true)]
        public void RenEachTeamsUnderWayHoldsOnlyItsOwnGroups(bool squads)
        {
            var points = new[] { CommanderBoard.Point(1, 30, 1), CommanderBoard.Point(2, 90, 0), CommanderBoard.Point(3, 150, 0), CommanderBoard.Point(4, 210, 2) };
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 6; i++) units.Add(M6World.U((ulong)i + 1, 1, 28 + i % 3 * 2, 18 + i / 3 * 4, weapon: -1, speed: 6000));
            for (int i = 0; i < 6; i++) units.Add(M6World.U((ulong)(101 + i), 2, 208 + i % 3 * 2, 18 + i / 3 * 4, weapon: -1, speed: 6000));
            var b = new CommanderBoard(points, units.ToArray(), new[] { CommanderBoard.Base(1, 30), CommanderBoard.Base(2, 210) }, 240,
                CommanderBoard.Ai(extra: squads ? Squads : null));
            for (int t = 1; t <= 200; t++)
            {
                b.Step();
                Assert.That(b.Commander.Attacks(1, 1), Is.True, "tick " + t);
                Assert.That(b.Commander.Attacks(1, 2), Is.False, "tick " + t);
                Assert.That(b.Commander.Attacks(2, 2), Is.True, "tick " + t);
                Assert.That(b.Commander.Attacks(2, 1), Is.False, "tick " + t);
                Assert.That(new[] { b.Owner(0), b.Owner(1), b.Owner(2), b.Owner(3) }, Is.EqualTo(new[] { 1, 0, 0, 2 }), "tick " + t);
            }
            Assert.That(b.Rows(1, 2, 3, 4, 5, 6), Is.EqualTo(new[] { 1, 1, 1, 1, 1, 1 }));
            Assert.That(b.Rows(101, 102, 103, 104, 105, 106), Is.EqualTo(new[] { 2, 2, 2, 2, 2, 2 }));
            Assert.That(b.Checker.Clean, Is.True, b.Checker.First);
        }

        // R3's anchor, one target scored by distance alone (1 target, weights 0, 1, 0): the target is the candidate nearest
        // the anchor. Points on a line (index: x) 1: 100, 2: 150, 0: 210, 3: 250, 4: 265; neighbours 1-2, 2-0, 0-3, 3-4.
        private static CommanderBoard Anchored(int[] owners, SpawnAnchor[] bases)
        {
            int[] x = { 210, 100, 150, 250, 265 };
            var points = new ControlPointDef[5];
            for (int i = 0; i < 5; i++) points[i] = CommanderBoard.Point(i + 1, x[i], owners[i]);
            var b = new CommanderBoard(points, new[] { CommanderBoard.Man(1, 1, 150, 32), CommanderBoard.Man(2, 2, 300, 32) }, bases, 320,
                CommanderBoard.Ai(targets: 1, enemy: 0, distance: 1, stick: 0));
            b.Step();
            return b;
        }
        private static bool[] Targets(CommanderBoard b) { var t = new bool[5]; for (int p = 0; p < 5; p++) t[p] = b.Commander.Attacks(1, p); return t; }

        // 1. The team's spawn anchor first: team 1 holds 2 (neighbours 1 and 0) and has a base at x 300: 0 is nearer the base
        // (90 m against 200 m); from point 2 it would be 1 (50 m against 60 m).
        [Test]
        public void AnchorIsTheTeamsBaseFirst()
        {
            CommanderBoard b = Anchored(new[] { 2, 2, 1, 2, 2 }, new[] { CommanderBoard.Base(1, 300), CommanderBoard.Base(2, 5, 36) });
            Assert.That(Targets(b), Is.EqualTo(new[] { true, false, false, false, false }));
        }

        // 2. Without a base, the centre of the team's own point with the smallest index: team 1 holds 2 and 4 (candidates 1, 0
        // and 3): from 2 the nearest is 1 (50 m); from point 0's centre it would be 0, from 4 it would be 3.
        [Test]
        public void AnchorIsTheFirstOwnPointWithoutABase()
        {
            CommanderBoard b = Anchored(new[] { 2, 2, 1, 2, 1 }, new[] { CommanderBoard.Base(2, 5, 36) });
            Assert.That(Targets(b), Is.EqualTo(new[] { false, true, false, false, false }));
        }

        // 3. Without a base and without an own point, point 0's centre: every other point is a candidate and 0 is nearest
        // (from the origin it would be 1).
        [Test]
        public void AnchorIsPointZeroWithoutABaseOrAnOwnPoint()
        {
            CommanderBoard b = Anchored(new[] { 2, 2, 2, 0, 2 }, new[] { CommanderBoard.Base(2, 5, 36) });
            Assert.That(Targets(b), Is.EqualTo(new[] { true, false, false, false, false }));
        }

        // Card appendix 10, 9: a spawn anchor beyond the coordinate limit (8192 m) throws when the commander is built, as a
        // point beyond it does; on the limit it is accepted.
        [Test]
        public void ASpawnAnchorBeyondTheCoordinateLimitThrowsBeforeTheMatch()
        {
            var b = new CommanderBoard(new[] { CommanderBoard.Point(1, 20, 1), CommanderBoard.Point(2, 100, 2) },
                new[] { CommanderBoard.Man(1, 1, 10), CommanderBoard.Man(2, 2, 110) }, new[] { CommanderBoard.Base(1, 4), CommanderBoard.Base(2, 116) }, 120);
            var rules = new CommanderRules(new CommanderQuotaRules(3, 200, 1, 600), 30000);
            Commander Build(long x, long z) => new Commander(b.W.Combat, M6World.Pack(b.W.Recipe.Match.Points.ToArray(), bases: new[]
                { new SpawnAnchor(1, new SimVec3(SimFix.FromInt(x), default, SimFix.FromInt(z))), CommanderBoard.Base(2, 116) }), b.W.Capture, M7GuardTests.Adapter(b.W), null, null, rules);
            Assert.That(() => Build(8192, 20), Throws.Nothing);
            Assert.That(() => Build(-8192, -8192), Throws.Nothing);
            Assert.That(() => Build(8193, 20), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Build(-8193, 20), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Build(20, 8193), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => Build(20, -8193), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        // Appendix 5: the state is the five numbers, the attack flags (teams x points), the rows in key order. A well-formed
        // state loads and is read back; the four refusals of the commander and the reader's count bound each name themselves.
        private static CommanderBoard Two() => new CommanderBoard(new[] { CommanderBoard.Point(1, 20, 1), CommanderBoard.Point(2, 100, 2) },
            new[] { CommanderBoard.Man(1, 1, 10), CommanderBoard.Man(2, 2, 110) }, new[] { CommanderBoard.Base(1, 4), CommanderBoard.Base(2, 116) }, 120);

        private static byte[] State(int[] five, int flags, (ulong Key, int Point)[] rows, int count = -1)
        {
            var w = new SimStateWriter();
            foreach (int n in five) w.Int32(n);
            w.Int32(flags);
            for (int i = 0; i < flags; i++) w.Bool(i % 3 == 0);
            w.Int32(count >= 0 ? count : rows.Length);
            foreach ((ulong key, int point) in rows) { w.UInt64(key); w.Int32(point); }
            return w.ToArray();
        }
        private static readonly int[] Five = { 3, 200, 1, 600, 30000 };

        [Test]
        public void AWellFormedStateLoadsAndIsSavedAgainByteForByte()
        {
            Commander c = Two().Commander;
            byte[] state = State(Five, 4, new[] { (5UL, 1), (9UL, 0) });
            c.LoadState(new SimStateReader(state));
            Assert.That(c.OrderCount, Is.EqualTo(2));
            Assert.That(c.OrderKeyAt(0), Is.EqualTo(5UL));
            Assert.That(c.OrderPointAt(0), Is.EqualTo(1));
            Assert.That(c.OrderKeyAt(1), Is.EqualTo(9UL));
            Assert.That(c.OrderPointAt(1), Is.EqualTo(0));
            Assert.That(c.Attacks(1, 0), Is.True);
            Assert.That(c.Attacks(1, 1), Is.False);
            Assert.That(c.Attacks(2, 0), Is.False);
            Assert.That(c.Attacks(2, 1), Is.True);
            Assert.That(c.CommandOf(5), Is.EqualTo(1));
            Assert.That(c.CommandOf(9), Is.EqualTo(0));
            Assert.That(c.CommandOf(7), Is.EqualTo(-1));
            Assert.That(CommanderStageTests.Section(c), Is.EqualTo(state));
        }

        [Test]
        public void AStateThatCannotBeIsRefused()
        {
            Commander c = Two().Commander;
            int capacity = Two().W.Sim.Entities.Capacity;
            var tooMany = new (ulong, int)[0];
            var refused = new List<(string Why, byte[] State, string Says)>();
            for (int i = 0; i < 5; i++)
            {
                int[] other = (int[])Five.Clone(); other[i]++;
                refused.Add(("number " + i, State(other, 4, new[] { (5UL, 1) }), "Commander rules differ."));
            }
            refused.Add(("fewer flags", State(Five, 3, new[] { (5UL, 1) }), "Commander teams or points differ."));
            refused.Add(("more flags", State(Five, 6, new[] { (5UL, 1) }), "Commander teams or points differ."));
            refused.Add(("a key twice", State(Five, 4, new[] { (5UL, 1), (5UL, 0) }), "Commander rows are not in strictly increasing key order."));
            refused.Add(("keys out of order", State(Five, 4, new[] { (9UL, 1), (5UL, 0) }), "Commander rows are not in strictly increasing key order."));
            refused.Add(("a point below the first", State(Five, 4, new[] { (5UL, -1) }), "Commander row point is out of range."));
            refused.Add(("a point beyond the last", State(Five, 4, new[] { (5UL, 2) }), "Commander row point is out of range."));
            refused.Add(("more rows than units", State(Five, 4, tooMany, capacity + 1), "Snapshot count exceeds its section bounds."));
            foreach ((string why, byte[] state, string says) in refused)
            {
                var e = Assert.Throws<SimSnapshotException>(() => c.LoadState(new SimStateReader(state)), why);
                Assert.That(e.Message, Is.EqualTo(says), why);
            }
            Assert.That(() => c.LoadState(new SimStateReader(State(Five, 4, new[] { (5UL, 1) }))), Throws.Nothing);
        }

        // "Teams or points differ" compares the product: the section of a world with 2 teams and 4 points is refused by one
        // with 2 teams and 2 points (rules equal), and the other way round.
        [Test]
        public void AStateOfAWorldWithOtherPointsIsRefused()
        {
            CommanderBoard two = Two();
            var four = new CommanderBoard(new[] { CommanderBoard.Point(1, 20, 1), CommanderBoard.Point(2, 60, 0), CommanderBoard.Point(3, 100, 0), CommanderBoard.Point(4, 140, 2) },
                new[] { CommanderBoard.Man(1, 1, 10), CommanderBoard.Man(2, 2, 150) }, new[] { CommanderBoard.Base(1, 4), CommanderBoard.Base(2, 156) }, 160);
            two.Step(); four.Step();
            foreach ((Commander from, Commander into) in new[] { (two.Commander, four.Commander), (four.Commander, two.Commander) })
            {
                var e = Assert.Throws<SimSnapshotException>(() => into.LoadState(new SimStateReader(CommanderStageTests.Section(from))));
                Assert.That(e.Message, Is.EqualTo("Commander teams or points differ."));
            }
        }
    }
}
