using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.CommanderBoard;

namespace WarCore.Tests
{
    // RC2-3 card appendix 8: the checker catches each rule broken on purpose. A view changes what the checker reads of the
    // commander from tick 40 on; up to tick 39 the board is clean, from then on the rule is reported. The board: A (team 1,
    // x 80) between B (x 20) and C (x 140), both team 2's; four soldiers of team 1 at A, so B (1) and C (2) are team 1's
    // targets and the soldiers go two to each.
    public sealed class CommanderCheckerTests
    {
        private const long From = 40;

        public enum Break { Unsorted, Undispatched, OutOfRange, SplitGroup, NotATarget, OwnPoint, Switch, HiddenAndDropped, DropAll, DropOne }

        private sealed class Broken : CommanderChecker.View
        {
            private readonly CommanderBoard board;
            private readonly Break kind;
            private readonly HashSet<ulong> teamOne = new HashSet<ulong>();
            private long built = -1;
            private List<(ulong Key, int Point)> rows;

            internal Broken(CommanderBoard board, Break kind) : base(board.Commander)
            {
                this.board = board; this.kind = kind;
                foreach (UnitSpawn unit in board.W.Recipe.Units) if (unit.Team == 1) teamOne.Add(board.Id(unit.Key).Value);
            }

            private bool On => board.Tick >= From;

            private List<(ulong Key, int Point)> Rows()
            {
                if (built == board.Tick) return rows;
                built = board.Tick;
                rows = new List<(ulong, int)>();
                for (int i = 0; i < Commander.OrderCount; i++) rows.Add((Commander.OrderKeyAt(i), Commander.OrderPointAt(i)));
                if (On) Apply(rows);
                return rows;
            }

            private static int Other(int point) => point == 1 ? 2 : 1;

            private void Apply(List<(ulong Key, int Point)> list)
            {
                int first = list.FindIndex(r => teamOne.Contains(r.Key));
                switch (kind)
                {
                    case Break.Unsorted: (list[0], list[1]) = (list[1], list[0]); break;
                    case Break.Undispatched: list.Add((list[list.Count - 1].Key + 1, list[list.Count - 1].Point)); break;
                    case Break.OutOfRange: list[first] = (list[first].Key, Commander.PointCount); break;
                    case Break.SplitGroup:
                        int member = list.FindIndex(r => teamOne.Contains(r.Key) && Commander.GroupOf(new StableId(r.Key)) != r.Key);
                        list[member] = (list[member].Key, Other(list[member].Point));
                        break;
                    case Break.OwnPoint: list[first] = (list[first].Key, 0); break;
                    case Break.Switch: if (board.Tick == From) list[first] = (list[first].Key, Other(list[first].Point)); break;
                    case Break.HiddenAndDropped: list.RemoveAll(r => teamOne.Contains(r.Key) && r.Point == 1); break;
                    case Break.DropAll: list.RemoveAll(r => teamOne.Contains(r.Key)); break;
                    case Break.DropOne: list.RemoveAll(r => teamOne.Contains(r.Key) && r.Point == 2); break;
                }
            }

            internal override int OrderCount => Rows().Count;
            internal override ulong OrderKeyAt(int index) => Rows()[index].Key;
            internal override int OrderPointAt(int index) => Rows()[index].Point;
            internal override bool Attacks(int team, int point)
            {
                if (On && team == 1 && kind == Break.NotATarget && point == Rows()[Rows().FindIndex(r => teamOne.Contains(r.Key))].Point) return false;
                if (On && team == 1 && kind == Break.HiddenAndDropped && point == 1) return false;
                return Commander.Attacks(team, point);
            }
        }

        private static CommanderChecker Board(Break kind)
        {
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 4; i++) units.Add(Man((ulong)i + 1, 1, 77 + 2 * (i % 2), 18 + 2 * (i / 2)));
            for (int i = 0; i < 4; i++) units.Add(Man((ulong)(100 + i), 2, 150, 30 + 2 * i));
            // A squad for the split group; everybody alone otherwise.
            string ai = kind == Break.SplitGroup ? Ai(extra: CommanderCounterTests.Squads) : null;
            var b = new CommanderBoard(new[] { Point(1, 80, 1), Point(2, 20, 2), Point(3, 140, 2) }, units.ToArray(),
                new[] { Base(1, 80, 36), Base(2, 156, 36) }, 160, ai, check: false);
            CommanderChecker checker = CommanderChecker.Attach(b.W, b.Commander, view: new Broken(b, kind));
            b.To(From - 1);
            Assert.That(checker.Clean, Is.True, "unbroken: " + checker.First);
            Assert.That(b.Rows(1, 2, 3, 4), Is.EquivalentTo(new[] { 1, 1, 2, 2 }), "two soldiers to each target");
            if (kind == Break.SplitGroup)
            {
                int members = 0;
                for (ulong key = 1; key <= 4; key++) if (b.Group(key) != key) members++;
                Assert.That(members, Is.GreaterThan(0), "a squad formed");
            }
            b.To(From + 5);
            return checker;
        }

        // I1: keys not strictly increasing, a row for a unit the commander does not dispatch, a point out of range, two rows
        // in one group.
        [TestCase(Break.Unsorted)]
        [TestCase(Break.Undispatched)]
        [TestCase(Break.OutOfRange)]
        [TestCase(Break.SplitGroup)]
        public void I1(Break kind) => Assert.That(Board(kind).Count("I1"), Is.GreaterThan(0));

        // I2: a row whose point is no target of the unit's team; a row naming the team's own point is also counted as such.
        [Test]
        public void I2() => Assert.That(Board(Break.NotATarget).Count("I2"), Is.GreaterThan(0));

        [Test]
        public void I2OwnPoint()
        {
            CommanderChecker checker = Board(Break.OwnPoint);
            Assert.That(checker.Count("I2") > 0, Is.True);
            Assert.That(checker.Count("own") > 0, Is.True);
            Assert.That(checker.OwnPointRows > 0, Is.True);
        }

        // I7: a unit is sent from one target to the other for one tick, while both are targets it can walk to.
        [Test]
        public void I7() => Assert.That(Board(Break.Switch).Count("I7"), Is.GreaterThan(0));

        // I8: B stops being a target and its two soldiers (two groups) lose their rows on that tick (no I7: B is no target),
        // while team 1 attacks one point of three.
        [Test]
        public void I8()
        {
            CommanderChecker checker = Board(Break.HiddenAndDropped);
            Assert.That(checker.Count("I8"), Is.EqualTo(2));
            Assert.That(checker.Count("I7"), Is.EqualTo(0));
        }

        // L2: team 1 keeps its targets but nobody has a row.
        [Test]
        public void L2() => Assert.That(Board(Break.DropAll).Count("L2"), Is.GreaterThan(0));

        // L3: C keeps nobody, while the soldiers that had it have no row and can walk there; B is still attacked (no L2).
        [Test]
        public void L3()
        {
            CommanderChecker checker = Board(Break.DropOne);
            Assert.That(checker.Count("L3") > 0, Is.True);
            Assert.That(checker.Count("L2"), Is.EqualTo(0));
        }
    }
}
