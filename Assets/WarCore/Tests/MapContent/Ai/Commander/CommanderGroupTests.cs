using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.CommanderBoard;

namespace WarCore.Tests
{
    // RC2-3: what the boards of single soldiers cannot show, because there a group is one head: a squad counts as its heads
    // in the pool (R3) and in the deficits (R5); of two groups as near, the smaller key goes (R5); without the leader's row
    // the smallest key's row is the group's order (R8). Boards as CommanderCounterTests: A (team 1, x 80) between B (x 20)
    // and C (x 140), both team 2's; point indices A 0, B 1, C 2; B and C are 62 m from team 1's base each and nobody stands
    // near them, so they tie on score and B, the lower index, is picked first.
    public sealed class CommanderGroupTests
    {
        private static ControlPointDef[] Abc() => new[] { Point(1, 80, 1), Point(2, 20, 2), Point(3, 140, 2) };
        private static SpawnAnchor[] Bases() => new[] { Base(1, 80, 36), Base(2, 156, 36) };
        private static void Clean(CommanderBoard b) => Assert.That(b.Checker.Clean, Is.True, b.Checker.First);

        // Recruiting within 8 m, nobody a born skirmisher and every squad of five members: the six of a cluster form one
        // squad, units 32 m and more away stay alone.
        private static readonly string OneSquad = CommanderCounterTests.Squads.Replace("30000", "8000")
            .Replace("\"squadSkirmisherPermille\": 300", "\"squadSkirmisherPermille\": 0")
            .Replace("\"squadMinSize\": 4, \"squadMaxSize\": 9", "\"squadMinSize\": 5, \"squadMaxSize\": 5");

        // Six by B (16 m and more from its centre), unit 21 20 m from C, unit 22 far from both (62 m); the one enemy stands
        // outside every threat radius. At tick 30 the six are one squad: 8 soldiers of team 1 in 3 groups.
        private static CommanderBoard SquadBoard()
        {
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 6; i++) units.Add(Man((ulong)i + 1, 1, 36 + 2 * (i % 3), 16 + 2 * (i / 3)));
            units.Add(Man(21, 1, 120, 20)); units.Add(Man(22, 1, 80, 4)); units.Add(Man(100, 2, 50, 4));
            var b = new CommanderBoard(Abc(), units.ToArray(), Bases(), 160, Ai(extra: OneSquad));
            b.To(30);
            ulong leader = 0;
            for (ulong key = 1; key <= 6; key++)
                if (b.Squads.SizeOf(b.Id(key)) > 0) { Assert.That(leader, Is.EqualTo(0UL), "one squad only"); leader = key; }
            Assert.That(leader, Is.Not.EqualTo(0UL), "the six formed a squad");
            for (ulong key = 1; key <= 6; key++) Assert.That(b.Group(key), Is.EqualTo(leader), "unit " + key + " is in the squad");
            Assert.That(b.Group(21), Is.EqualTo(21UL), "unit 21 is on his own");
            Assert.That(b.Group(22), Is.EqualTo(22UL), "unit 22 is on his own");
            return b;
        }

        // R3: the soldiers of a team are heads. 8 soldiers over two targets: 4 and 4. (Counted as groups: max(2, 3) = 3, so
        // 2 and 1.)
        [Test]
        public void ThePoolCountsEveryHeadOfASquad()
        {
            CommanderBoard b = SquadBoard();
            b.Step();
            Assert.That(b.Commander.Desired(1, 1), Is.EqualTo(4));
            Assert.That(b.Commander.Desired(1, 2), Is.EqualTo(4));
            Clean(b);
        }

        // R5: a group that keeps its order fills its target by its heads. The squad (6) keeps B and unit 21 keeps C; both
        // want 4, so B is 2 over and C 3 short: unit 22, the one group without an order, goes to C. (Counted as groups, B
        // and C would both be 3 short and B, the lower index, would get it.)
        [Test]
        public void AKeptSquadFillsItsTargetByItsHeads()
        {
            CommanderBoard b = SquadBoard();
            b.Give((1, 1), (2, 1), (3, 1), (4, 1), (5, 1), (6, 1), (21, 2));
            b.Step();
            Assert.That(b.Commander.Desired(1, 1), Is.EqualTo(4));
            Assert.That(b.Commander.Desired(1, 2), Is.EqualTo(4));
            Assert.That(b.Rows(1, 2, 3, 4, 5, 6, 21), Is.EqualTo(new[] { 1, 1, 1, 1, 1, 1, 2 }));
            Assert.That(b.Row(22), Is.EqualTo(2));
            Clean(b);
        }

        // R5: a group that is sent fills its target by its heads. Nobody has an order. B is served first (no group yet,
        // lower index) by the nearest group, the squad of six, and is then 2 over; C (no group yet) takes its nearest, unit
        // 21, and is 3 short, so unit 22 goes to C as well. (Counted as groups, both would be 3 short and unit 22 would go
        // to B, the lower index.)
        [Test]
        public void ASentSquadFillsItsTargetByItsHeads()
        {
            CommanderBoard b = SquadBoard();
            b.Give();
            b.Step();
            Assert.That(b.Rows(1, 2, 3, 4, 5, 6), Is.EqualTo(new[] { 1, 1, 1, 1, 1, 1 }));
            Assert.That(b.Rows(21, 22), Is.EqualTo(new[] { 2, 2 }));
            Clean(b);
        }

        // R5: of two groups as near to a target, the one with the smaller key goes. Units 3 and 4 stand mirrored about the
        // line of the points, 40 m along and 10 m across from B each; B and C want one each; B is served first and takes
        // unit 3, then C takes unit 4. (Unit 3 has the smaller stable id; the test checks that first.)
        [Test]
        public void OfTwoGroupsAsNearTheSmallerKeyGoes()
        {
            var b = new CommanderBoard(Abc(), new[] { Man(3, 1, 60, 10), Man(4, 1, 60, 30), Man(100, 2, 50, 4) }, Bases(), 160);
            Assert.That(b.Id(3).Value, Is.LessThan(b.Id(4).Value));
            b.Step();
            Assert.That(b.Commander.Desired(1, 1), Is.EqualTo(1));
            Assert.That(b.Commander.Desired(1, 2), Is.EqualTo(1));
            Assert.That(b.Rows(3, 4), Is.EqualTo(new[] { 1, 2 }));
            Clean(b);
        }

        // R8: without a row of its leader, a group's old order is the row of its unit with the smallest key that has one.
        // The board of counter-example 7 (the player takes the leader's body after the squads' step, so the members still
        // follow a leader who has no row), but the member with the smallest key was going for B and the others for C: B is
        // a target they can walk to, so all of them go for B. ("Smallest key" is the smallest stable id.)
        [Test]
        public void WithoutTheLeadersRowTheSmallestKeysRowIsTheGroupsOrder()
        {
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 6; i++) units.Add(Man((ulong)i + 1, 1, 76 + 2 * (i % 3), 16 + 2 * (i / 3)));
            units.Add(Man(21, 1, 40, 4)); units.Add(Man(22, 1, 120, 4)); units.Add(Man(23, 1, 40, 36)); units.Add(Man(24, 1, 120, 36));
            units.Add(Man(50, 1, 6, 36)); units.Add(Man(100, 2, 156, 20));
            var b = new CommanderBoard(Abc(), units.ToArray(), Bases(), 160, Ai(extra: CommanderCounterTests.Squads.Replace("30000", "8000")), player: 50);
            b.To(30);
            ulong leader = 0;
            var members = new List<ulong>();
            for (ulong key = 1; key <= 6 && leader == 0; key++)
            {
                if (b.Squads.SizeOf(b.Id(key)) == 0) continue;
                members.Clear();
                for (ulong other = 1; other <= 6; other++) if (other != key && b.Group(other) == key) members.Add(other);
                if (members.Count >= 2) leader = key;
            }
            Assert.That(leader, Is.Not.EqualTo(0UL), "a squad with two members or more formed in the cluster");
            // The commander's keys are the units' stable ids, which do not follow the spawn keys.
            ulong first = members[0];
            foreach (ulong m in members) if (b.Id(m).Value < b.Id(first).Value) first = m;
            var rows = new List<(ulong, int)> { (leader, 2), (21, 2), (22, 2), (23, 2), (24, 1) };
            for (ulong key = 1; key <= 6; key++) if (key != leader) rows.Add((key, key == first ? 1 : 2));
            b.Give(rows.ToArray());
            b.Take(31, leader);
            b.Step();
            Assert.That(b.Row(leader), Is.EqualTo(-1), "the leader's body is the player's");
            foreach (ulong m in members)
            {
                Assert.That(b.Group(m), Is.EqualTo(leader), "member " + m + " still follows the leader");
                Assert.That(b.Row(m), Is.EqualTo(1), "member " + m + " takes the row of member " + first);
            }
            Clean(b);
        }
    }
}
