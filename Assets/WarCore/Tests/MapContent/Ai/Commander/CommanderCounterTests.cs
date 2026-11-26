using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.CommanderBoard;

namespace WarCore.Tests
{
    // RC2-3 card appendix 7c: thirteen tries to break "an order that still serves is never changed", each with why it
    // cannot. Every board has the checker attached (I7 there: a row that leaves a point has a reason). The sixth, a leader
    // falling, is CommanderRuleTests.DingTheMembersOfAFallenLeaderKeepTheirRows.
    // Most boards: A (team 1, x 80) between B (x 20) and C (x 140), both team 2's; neighbours B-A and A-C, so B and C are
    // team 1's targets. Point indices: A 0, B 1, C 2.
    public sealed class CommanderCounterTests
    {
        internal const string Squads = "\"squadMinSize\": 4, \"squadMaxSize\": 9, \"squadSkirmisherPermille\": 300, \"squadRecruitRadiusMm\": 30000, "
            + "\"squadFormTicks\": 15, \"squadSpreadMinMm\": 1500, \"squadSpreadMaxMm\": 6000";
        private static ControlPointDef[] Abc() => new[] { Point(1, 80, 1), Point(2, 20, 2), Point(3, 140, 2) };
        private static SpawnAnchor[] Bases() => new[] { Base(1, 80, 36), Base(2, 156, 36) };

        private static List<ulong> Squad(CommanderBoard b, ulong leader, IEnumerable<ulong> keys)
        {
            var members = new List<ulong>();
            foreach (ulong key in keys)
                if (key != leader && b.Squads.TryLeader(b.Id(key), out StableId l, out _) && l.Value == b.Id(leader).Value) members.Add(key);
            return members;
        }
        private static ulong[] Range(ulong from, int count) { var keys = new ulong[count]; for (int i = 0; i < count; i++) keys[i] = from + (ulong)i; return keys; }
        private static void Clean(CommanderBoard b) => Assert.That(b.Checker.Clean, Is.True, b.Checker.First);

        // 1. The front falls back: team 1 holds 0 (x 20) and 1 (x 80) and its group goes for 2 (x 140); team 2 takes 1. Then
        // 2 borders no point of team 1's, but its group is on its way there (underWay), so 2 stays a candidate and the group
        // keeps it. Only when the targets are at the cap and 2 loses on score is it let go (one target, distance alone: 1
        // is nearer the base) — and then the team attacks as many points as it may (I8).
        [TestCase(3)]
        [TestCase(1)]
        public void TheFrontFallsBack(int cap)
        {
            var units = new List<UnitSpawn> { Man(1, 1, 100) };
            for (int i = 0; i < 4; i++) units.Add(Man((ulong)(100 + i), 2, 150, 30 + 2 * i));
            var b = new CommanderBoard(new[] { Point(1, 20, 1), Point(2, 80, 1), Point(3, 140, 2) }, units.ToArray(),
                new[] { Base(1, 4), Base(2, 156, 36) }, 160, cap == 1 ? Ai(targets: 1, enemy: 0, distance: 1, stick: 0) : null, capture: 30);
            for (int i = 0; i < 4; i++) b.Move(5, (ulong)(100 + i), 78 + i, 20);
            b.To(4);
            Assert.That(b.Row(1), Is.EqualTo(2), "the group goes for 2");
            long lost = -1;
            while (lost < 0 && b.Tick < 300) { b.Step(); if (b.Owner(1) != 1) lost = b.Tick; else Assert.That(b.Row(1), Is.EqualTo(2)); }
            Assert.That(lost, Is.GreaterThan(0), "team 2 takes 1");
            for (int t = 0; t < 30; t++)
            {
                Assert.That(b.Commander.Attacks(1, 1), Is.True, "tick " + b.Tick + ": the lost point is a target");
                if (cap == 3)
                {
                    Assert.That(b.Commander.Attacks(1, 2), Is.True, "tick " + b.Tick + ": 2 is still a target and kept");
                    Assert.That(b.Row(1), Is.EqualTo(2), "tick " + b.Tick + ": 2 is still a target and kept");
                }
                else
                {
                    Assert.That(b.Commander.Attacks(1, 2), Is.False, "tick " + b.Tick + ": 2 lost on score, the group goes for 1");
                    Assert.That(b.Row(1), Is.EqualTo(1), "tick " + b.Tick + ": 2 lost on score, the group goes for 1");
                }
                b.Step();
            }
            Clean(b);
        }

        // 2. The target changes hands on this tick (it becomes team 1's): it is no target any more, so the group is let go
        // on that very tick (the commander runs after the capture) and sent on; it never names the point again.
        [Test]
        public void TheTargetBecomesTheTeamsOwn()
        {
            var b = new CommanderBoard(new[] { Point(1, 20, 1), Point(2, 60, 0), Point(3, 140, 2) },
                new[] { Man(1, 1, 58), Man(100, 2, 150, 34) }, new[] { Base(1, 4), Base(2, 156, 36) }, 160, capture: 30);
            long taken = -1;
            while (taken < 0 && b.Tick < 300)
            {
                b.Step();
                if (b.Owner(1) == 1) taken = b.Tick;
                else Assert.That(b.Row(1), Is.EqualTo(1), "tick " + b.Tick + ": the group goes for the neutral point");
            }
            Assert.That(taken, Is.GreaterThan(0), "team 1 takes it");
            for (int t = 0; t < 30; t++)
            {
                Assert.That(b.Commander.Attacks(1, 1), Is.False, "tick " + b.Tick + ": let go and sent to the next target");
                Assert.That(b.Row(1), Is.EqualTo(2), "tick " + b.Tick + ": let go and sent to the next target");
                b.Step();
            }
            Clean(b);
        }

        // 3. Desired changes on a tick: more soldiers (a held batch let go), fewer (one falls), a target added (team 1 takes C,
        // so D behind it becomes a target). Not one group whose order is still a target changes it.
        [Test]
        public void DesiredChanges()
        {
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 4; i++) units.Add(Man((ulong)i + 1, 1, 76 + 2 * i, 12));
            for (int i = 0; i < 6; i++) units.Add(Man((ulong)(11 + i), 1, 60 + 2 * i, 36));
            units.Add(Man(100, 2, 210, 4));
            var b = new CommanderBoard(new[] { Point(1, 80, 1), Point(2, 20, 2), Point(3, 140, 2), Point(4, 200, 2) }, units.ToArray(),
                new[] { Base(1, 80, 36), Base(2, 216, 36) }, 220, capture: 30, standingOrders: true);
            foreach (ulong key in Range(11, 6)) b.Hold(key);
            b.To(20);
            int[] first = b.Rows(1, 2, 3, 4);
            Assert.That(first, Has.Member(1).And.Member(2), "the four go for B and C");
            int wanted = b.Commander.Desired(1, 1) + b.Commander.Desired(1, 2);
            foreach (ulong key in Range(11, 6)) b.Hold(key, false);
            b.Fall(61, 1);
            for (int t = 21; t <= 100; t++)
            {
                b.Step();
                if (t == 21) Assert.That(b.Commander.Desired(1, 1) + b.Commander.Desired(1, 2), Is.GreaterThan(wanted), "more soldiers, more wanted");
                if (t == 62) Assert.That(b.Alive(1), Is.False);
                ulong[] keys = t <= 61 ? new ulong[] { 1, 2, 3, 4 } : new ulong[] { 2, 3, 4 };
                foreach (ulong key in keys) Assert.That(b.Row(key), Is.EqualTo(first[key - 1]), "tick " + t + ": unit " + key);
            }
            // Everybody whose order is C walks onto C at once; when C is taken, D becomes a target and B's groups stay.
            var toB = new List<ulong>();
            foreach (ulong key in Range(2, 3)) if (b.Row(key) == 1) toB.Add(key);
            foreach (ulong key in Range(11, 6)) if (b.Row(key) == 1) toB.Add(key);
            int x = 136;
            foreach (ulong key in Range(2, 3)) if (b.Row(key) == 2) b.Move(101, key, x++, 20);
            foreach (ulong key in Range(11, 6)) if (b.Row(key) == 2) b.Move(101, key, x++, 20);
            Assert.That(toB, Is.Not.Empty);
            long taken = -1;
            while (b.Tick < 300 && (taken < 0 || b.Tick < taken + 30))
            {
                b.Step();
                if (taken < 0 && b.Owner(2) == 1) { taken = b.Tick; Assert.That(b.Commander.Attacks(1, 3), Is.True, "D is a target once C is taken"); }
                foreach (ulong key in toB) Assert.That(b.Row(key), Is.EqualTo(1), "tick " + b.Tick + ": unit " + key + " keeps B");
            }
            Assert.That(taken, Is.GreaterThan(0), "team 1 takes C");
            Clean(b);
        }

        // 4. Enemies come to an own point (its count goes from 0 up): no group changes its order and no row names the point.
        [Test]
        public void EnemiesComeToAnOwnPoint()
        {
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 4; i++) units.Add(Man((ulong)i + 1, 1, 74 + 4 * i, 12));
            for (int i = 0; i < 6; i++) units.Add(Man((ulong)(100 + i), 2, 146 + 2 * i, 36));
            var b = new CommanderBoard(Abc(), units.ToArray(), Bases(), 160);
            for (int i = 0; i < 6; i++) b.Move(30, (ulong)(100 + i), 77 + i, 32);
            b.To(29);
            Assert.That(b.Commander.EnemiesNear(1, 0), Is.EqualTo(0));
            int[] rows = b.Rows(1, 2, 3, 4);
            for (int t = 30; t <= 90; t++)
            {
                b.Step();
                if (t == 30) Assert.That(b.Commander.EnemiesNear(1, 0), Is.EqualTo(6), "six enemies near A");
                Assert.That(b.Rows(1, 2, 3, 4), Is.EqualTo(rows), "tick " + t);
                Assert.That(b.Commander.Attacks(1, 0), Is.False, "tick " + t);
                Assert.That(b.Commander.Desired(1, 0), Is.EqualTo(0), "tick " + t);
            }
            Clean(b);
        }

        // 5. An own point is lost: it becomes a target; the groups whose orders still serve keep them, and only a group
        // without an order (a batch let go afterwards) is sent there — first, as the one target nobody goes to.
        [Test]
        public void AnOwnPointIsLost()
        {
            var units = new List<UnitSpawn> { Man(1, 1, 70, 12), Man(2, 1, 90, 12), Man(11, 1, 80, 36) };
            for (int i = 0; i < 4; i++) units.Add(Man((ulong)(100 + i), 2, 146 + 2 * i, 36));
            var b = new CommanderBoard(Abc(), units.ToArray(), Bases(), 160, capture: 30, standingOrders: true);
            b.Hold(11);
            for (int i = 0; i < 4; i++) b.Move(5, (ulong)(100 + i), 78 + i, 20);
            b.To(4);
            int[] rows = b.Rows(1, 2);
            Assert.That(rows, Is.EquivalentTo(new[] { 1, 2 }), "one goes for B, one for C");
            long lost = -1;
            while (lost < 0 && b.Tick < 300) { b.Step(); if (b.Owner(0) != 1) lost = b.Tick; Assert.That(b.Rows(1, 2), Is.EqualTo(rows)); }
            Assert.That(lost, Is.GreaterThan(0), "team 2 takes A");
            for (int t = 0; t < 30; t++)
            {
                Assert.That(b.Commander.Attacks(1, 0), Is.True, "tick " + b.Tick);
                Assert.That(b.Rows(1, 2), Is.EqualTo(rows), "tick " + b.Tick);
                if (t == 5) b.Hold(11, false);
                if (t == 6) Assert.That(b.Row(11), Is.EqualTo(0), "the batch goes for the lost point");
                b.Step();
            }
            Clean(b);
        }

        // 7. The leader is still there but has no row this tick: the player takes the leader's body (after the squads'
        // step, before the commander's). The members still follow that leader (TryLeader), so their group's old order is the
        // row of its unit with the smallest key that has one: they keep it. Rows: the squad and three stragglers go for C, one
        // straggler for B, so a group let go here would be sent to B (larger deficit).
        [Test]
        public void TheLeaderHasNoRowThisTick()
        {
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 6; i++) units.Add(Man((ulong)i + 1, 1, 76 + 2 * (i % 3), 16 + 2 * (i / 3)));
            units.Add(Man(21, 1, 40, 4)); units.Add(Man(22, 1, 120, 4)); units.Add(Man(23, 1, 40, 36)); units.Add(Man(24, 1, 120, 36));
            units.Add(Man(50, 1, 6, 36)); units.Add(Man(100, 2, 156, 20));
            // Recruiting within 8 m: the cluster forms one squad, the stragglers (32 m and more apart) stay on their own.
            var b = new CommanderBoard(Abc(), units.ToArray(), Bases(), 160, Ai(extra: Squads.Replace("30000", "8000")), player: 50);
            b.To(30);
            foreach (ulong key in new ulong[] { 21, 22, 23, 24 }) Assert.That(b.Group(key), Is.EqualTo(key), "straggler " + key + " is on his own");
            ulong leader = 0; List<ulong> members = null;
            foreach (ulong key in Range(1, 6))
                if (leader == 0 && b.Squads.SizeOf(b.Id(key)) > 0 && Squad(b, key, Range(1, 6)).Count >= 2) { leader = key; members = Squad(b, key, Range(1, 6)); }
            Assert.That(leader, Is.Not.EqualTo(0UL), "a squad formed in the cluster");
            var rows = new List<(ulong, int)> { (leader, 2), (21, 2), (22, 2), (23, 2), (24, 1) };
            foreach (ulong key in Range(1, 6)) if (key != leader) rows.Add((key, 2));
            b.Give(rows.ToArray());
            b.Take(31, leader);
            b.Step();
            Assert.That(b.Row(leader), Is.EqualTo(-1), "the leader's body is the player's");
            foreach (ulong m in members) Assert.That(b.Group(m), Is.EqualTo(leader), "member " + m + " still follows the leader");
            for (int t = 31; t <= 60; t++)
            {
                foreach (ulong key in Range(1, 6)) if (key != leader) Assert.That(b.Row(key), Is.EqualTo(2), "tick " + b.Tick + ": unit " + key);
                Assert.That(b.Rows(21, 22, 23, 24), Is.EqualTo(new[] { 2, 2, 2, 1 }), "tick " + b.Tick + ": the stragglers");
                b.Step();
            }
            Clean(b);
        }

        // 8. A straggler is taken into a squad: he changes group and follows the new group's order (its leader's row). Before
        // the first form tick every unit is a group of its own and the commander sends them to both targets; on the form tick
        // a member whose row is not his leader's takes the leader's.
        [Test]
        public void AStragglerJoinsASquad()
        {
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 8; i++) units.Add(Man((ulong)i + 1, 1, 74 + 2 * (i % 4), 16 + 2 * (i / 4)));
            units.Add(Man(100, 2, 156, 20));
            var b = new CommanderBoard(Abc(), units.ToArray(), Bases(), 160, Ai(extra: Squads));
            ulong[] keys = Range(1, 8);
            int[] before = null;
            long formed = -1;
            while (formed < 0 && b.Tick < 100)
            {
                int[] rows = b.Rows(keys);
                b.Step();
                foreach (ulong key in keys) if (b.Group(key) != key) formed = b.Tick;
                if (formed > 0) before = rows;
            }
            Assert.That(formed, Is.GreaterThan(1), "the squads form after the commander's first ticks");
            Assert.That(before, Has.Member(1).And.Member(2), "the stragglers went for both targets");
            int moved = 0;
            foreach (ulong key in keys)
            {
                ulong group = b.Group(key);
                if (group != key) Assert.That(b.Row(key), Is.EqualTo(b.Row(group)), "unit " + key + " follows its leader " + group);
                if (b.Row(key) != before[key - 1]) { moved++; Assert.That(group, Is.Not.EqualTo(key), "unit " + key + " changed rows only by changing group"); }
            }
            Assert.That(moved, Is.GreaterThan(0), "somebody took his new leader's order");
            Clean(b);
        }

        // 9. A squad starts regrouping (Squads.RegroupOf Active): none of its units has a row while it does; when it stops it
        // is a group without an order and is sent on that tick.
        [Test]
        public void ASquadRegroupsAndComesBack()
        {
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 10; i++) units.Add(Man((ulong)i + 1, 1, 72 + 2 * (i % 5), 16 + 2 * (i / 5)));
            units.Add(Man(100, 2, 156, 20));
            string regroup = Squads + ", \"squadRegroupBelowPermille\": 500, \"squadRegroupUntilPermille\": 750, \"squadRegroupMaxTicks\": 60";
            var b = new CommanderBoard(Abc(), units.ToArray(), Bases(), 160, Ai(extra: regroup));
            b.To(20);
            ulong leader = 0; List<ulong> members = null;
            foreach (ulong key in Range(1, 10))
                if (leader == 0 && b.Squads.SizeOf(b.Id(key)) > 0 && Squad(b, key, Range(1, 10)).Count >= 3) { leader = key; members = Squad(b, key, Range(1, 10)); }
            Assert.That(leader, Is.Not.EqualTo(0UL), "a squad of four or more formed");
            ulong kept = members[0];
            members.RemoveAt(0);
            b.Fall(21, members.ToArray());
            long started = -1, ended = -1;
            while (ended < 0 && b.Tick < 600)
            {
                b.Step();
                bool active = b.Squads.RegroupOf(b.Id(leader)) == SquadRally.Active;
                if (active && started < 0) started = b.Tick;
                if (active) Assert.That(b.Rows(leader, kept), Is.EqualTo(new[] { -1, -1 }), "tick " + b.Tick + ": a regrouping squad keeps no rows");
                if (!active && started > 0) ended = b.Tick;
            }
            Assert.That(started, Is.GreaterThan(0), "the squad regrouped");
            Assert.That(ended, Is.GreaterThan(started), "and came back");
            Assert.That(b.Row(leader), Is.EqualTo(1).Or.EqualTo(2), "sent on the tick it came back");
            Assert.That(b.Row(kept), Is.EqualTo(b.Row(leader)));
            Clean(b);
        }

        // 10. Units the player leads count as commanded (R0): no rows while led; let go, they are groups without orders and
        // are sent on that tick. The unit the squad did not take keeps its row throughout.
        [Test]
        public void UnitsLedByThePlayerAndLetGo()
        {
            var units = new[] { Man(1, 1, 40), Man(2, 1, 42), Man(3, 1, 44), Man(4, 1, 46), Man(5, 1, 70, 36), Man(100, 2, 156, 20) };
            var b = new CommanderBoard(Abc(), units, Bases(), 160, Ai(extra: "\"ledSize\": 3, \"ledRecruitRadiusMm\": 20000"), player: 1);
            b.To(20);
            int far = b.Row(5);
            Assert.That(b.Rows(2, 3, 4), Has.None.EqualTo(-1));
            b.Order(SquadCommandKind.Follow);
            var led = b.W.System<LedSquad>();
            Assert.That(led.MemberCount, Is.EqualTo(3));
            for (int t = 0; t < 30; t++)
            {
                Assert.That(b.Rows(2, 3, 4), Is.EqualTo(new[] { -1, -1, -1 }), "tick " + b.Tick + ": led units have no rows");
                Assert.That(b.Row(5), Is.EqualTo(far));
                b.Step();
            }
            b.Order(SquadCommandKind.Dismiss);
            Assert.That(led.MemberCount, Is.EqualTo(0));
            Assert.That(b.Rows(2, 3, 4), Has.None.EqualTo(-1), "let go, they are sent on that tick");
            Assert.That(b.Row(5), Is.EqualTo(far));
            Assert.That(b.Row(1), Is.EqualTo(-1), "the player's own body has no row");
            Clean(b);
        }

        // 11. The player takes another body: that unit leaves the commander's hands (no row from that tick), the body left
        // behind is the computer's again and is sent on that tick; the others keep their rows.
        [Test]
        public void ThePlayerTakesAnotherBody()
        {
            var units = new[] { Man(1, 1, 40, 36), Man(2, 1, 70, 12), Man(3, 1, 90, 12), Man(100, 2, 156, 20) };
            var b = new CommanderBoard(Abc(), units, Bases(), 160, player: 1);
            b.To(20);
            Assert.That(b.Row(1), Is.EqualTo(-1));
            int third = b.Row(3);
            Assert.That(b.Row(2), Is.Not.EqualTo(-1));
            b.Take(21, 2);
            b.Step();
            Assert.That(b.Row(2), Is.EqualTo(-1), "the new body has no row, the other keeps his");
            Assert.That(b.Row(3), Is.EqualTo(third), "the new body has no row, the other keeps his");
            Assert.That(b.Row(1), Is.EqualTo(1).Or.EqualTo(2), "the body left behind is sent on that tick");
            b.Step(30);
            Assert.That(b.Row(3), Is.EqualTo(third));
            Clean(b);
        }

        // 12. A unit falls and a new one comes in: the fallen one's row goes, the newcomer is a group without an order and is
        // sent on the tick it comes in; the others keep theirs.
        [Test]
        public void AUnitFallsAndANewOneComesIn()
        {
            var units = new[] { Man(1, 1, 70, 12), Man(2, 1, 80, 12), Man(3, 1, 90, 12), Man(11, 1, 80, 36, reserve: true), Man(100, 2, 156, 20) };
            var b = new CommanderBoard(Abc(), units, Bases(), 160, interval: 120);
            b.To(20);
            int[] rows = b.Rows(1, 2, 3);
            b.Fall(30, 1);
            long came = -1;
            while (b.Tick < 400 && came < 0)
            {
                b.Step();
                if (b.Tick > 30) Assert.That(b.Present(1), Is.False);
                if (b.Tick > 30) Assert.That(b.Row(1), Is.EqualTo(-1), "tick " + b.Tick + ": a fallen unit has no row");
                Assert.That(b.Rows(2, 3), Is.EqualTo(new[] { rows[1], rows[2] }), "tick " + b.Tick);
                if (b.Present(11)) { came = b.Tick; Assert.That(b.Row(11), Is.EqualTo(1).Or.EqualTo(2), "the newcomer is sent on the tick it comes in"); }
            }
            Assert.That(came, Is.GreaterThan(30), "the reserve came in");
            Clean(b);
        }

        // 13. A save loaded in between: the first tick after the load (and the next ones) gives the rows and the hash of the
        // run that was never saved; the loaded world's checker remembers the loaded rows and finds nothing.
        [Test]
        public void ASaveLoadedInBetween()
        {
            CommanderBoard Board()
            {
                var units = new List<UnitSpawn>();
                for (int i = 0; i < 8; i++) units.Add(Man((ulong)i + 1, 1, 74 + 2 * (i % 4), 16 + 2 * (i / 4)));
                for (int i = 0; i < 4; i++) units.Add(Man((ulong)(100 + i), 2, 150, 30 + 2 * i));
                return new CommanderBoard(Abc(), units.ToArray(), Bases(), 160, Ai(extra: Squads));
            }
            CommanderBoard run = Board();
            run.To(60);
            byte[] saved = run.W.Sim.SaveRetainedSnapshot();
            CommanderBoard loaded = Board();
            loaded.To(7);
            loaded.W.Sim.RestoreRetainedSnapshot(saved);
            Assert.That(loaded.W.Hash, Is.EqualTo(run.W.Hash));
            ulong[] keys = Range(1, 8);
            for (int t = 0; t < 60; t++)
            {
                run.Step(); loaded.Step();
                Assert.That(loaded.Rows(keys), Is.EqualTo(run.Rows(keys)), "tick " + run.Tick);
                Assert.That(loaded.W.Hash, Is.EqualTo(run.W.Hash), "tick " + run.Tick);
            }
            Assert.That(loaded.Checker.Checks, Is.EqualTo(67));
            Clean(run); Clean(loaded);
        }
    }
}
