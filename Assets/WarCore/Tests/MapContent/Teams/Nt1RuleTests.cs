using System;
using NUnit.Framework;
using WarCore.Battle;

namespace WarCore.Tests
{
    // NT-1 A7: the three end rules with more than two teams, driven through the mode with a fake read-only world.
    public sealed class Nt1RuleTests
    {
        // A reader that knows TeamCount teams; Owned and Alive are per team (index team - 1).
        private sealed class Fake : IMatchReader
        {
            internal int Points = 5;
            internal int[] Owned = new int[3], Alive = { 5, 5, 5 };
            public int PointCount => Points;
            public int TeamCount => Owned.Length;
            public void CountAlive(out int teamOne, out int teamTwo) => throw new InvalidOperationException("the rules read the span overload");
            public void CountAlive(Span<int> alive) { alive.Clear(); for (int i = 0; i < Alive.Length; i++) alive[i] = Alive[i]; }
            public int PointsOwnedBy(int team) => Owned[team - 1];
        }

        // A reader that keeps both defaults: two teams, the span filled from the old two-value method.
        private sealed class TwoTeamFake : IMatchReader
        {
            internal int Points = 3, OwnedOne, OwnedTwo, AliveOne = 5, AliveTwo = 5;
            public int PointCount => Points;
            public void CountAlive(out int teamOne, out int teamTwo) { teamOne = AliveOne; teamTwo = AliveTwo; }
            public int PointsOwnedBy(int team) => team == 1 ? OwnedOne : team == 2 ? OwnedTwo : throw new ArgumentOutOfRangeException(nameof(team));
        }

        private const int Max = 100;

        private static MatchMode Mode(IMatchReader world)
        {
            var mode = new MatchMode(M6World.Pack(max: Max));
            mode.Bind(world);
            return mode;
        }

        private static void Expect(MatchMode mode, int winner, MatchEndReason reason, long tick) =>
            Assert.That((mode.Winner, mode.EndReason, mode.EndTick), Is.EqualTo((winner, reason, tick)));

        [Test]
        public void Nt1AllPoints_ThirdTeamOwnsEveryPoint_Wins()
        {
            var world = new Fake { Owned = new[] { 0, 0, 5 } };
            var mode = Mode(world); mode.Evaluate(7);
            Expect(mode, 3, MatchEndReason.AllPoints, 7);
        }

        [Test]
        public void Nt1AllPoints_NobodyOwnsEveryPoint_GoesOn()
        {
            var world = new Fake { Owned = new[] { 2, 1, 2 } };
            var mode = Mode(world); mode.Evaluate(Max - 1);
            Expect(mode, 0, MatchEndReason.None, 0);
        }

        // Searched from the highest team down: two teams that both report every point give the higher one, as the old rule gave 2.
        [Test]
        public void Nt1AllPoints_TwoTeamsReportEveryPoint_HigherTeamWins()
        {
            var two = new TwoTeamFake { OwnedOne = 3, OwnedTwo = 3 };
            var mode = Mode(two); mode.Evaluate(4);
            Expect(mode, 2, MatchEndReason.AllPoints, 4);
            var three = new Fake { Owned = new[] { 5, 5, 0 } };
            mode = Mode(three); mode.Evaluate(4);
            Expect(mode, 2, MatchEndReason.AllPoints, 4);
        }

        [Test]
        public void Nt1Exhausted_TwoTeamsStanding_GoesOn()
        {
            var world = new Fake { Owned = new[] { 1, 1, 1 }, Alive = new[] { 3, 0, 2 } };
            var mode = Mode(world); mode.Evaluate(Max - 1);
            Expect(mode, 0, MatchEndReason.None, 0);
        }

        [Test]
        public void Nt1Exhausted_OnlyTheThirdTeamStanding_Wins()
        {
            var world = new Fake { Owned = new[] { 1, 1, 1 }, Alive = new[] { 0, 0, 4 } };
            var mode = Mode(world); mode.Evaluate(9);
            Expect(mode, 3, MatchEndReason.Exhausted, 9);
        }

        [Test]
        public void Nt1Exhausted_OnlyTheFirstTeamStanding_Wins()
        {
            var world = new Fake { Owned = new[] { 1, 1, 1 }, Alive = new[] { 6, 0, 0 } };
            var mode = Mode(world); mode.Evaluate(9);
            Expect(mode, 1, MatchEndReason.Exhausted, 9);
        }

        [Test]
        public void Nt1Exhausted_NobodyStanding_Draw()
        {
            var world = new Fake { Owned = new[] { 1, 1, 1 }, Alive = new[] { 0, 0, 0 } };
            var mode = Mode(world); mode.Evaluate(9);
            Expect(mode, -1, MatchEndReason.Draw, 9);
        }

        // The third team has the most units but is not tied for the most points, so it never competes on units.
        [Test]
        public void Nt1Time_TieForMostPoints_OnlyTiedTeamsCompareUnits_Draw()
        {
            var world = new Fake { Owned = new[] { 2, 2, 1 }, Alive = new[] { 5, 5, 9 } };
            var mode = Mode(world); mode.Evaluate(Max);
            Expect(mode, -1, MatchEndReason.Draw, Max);
        }

        [Test]
        public void Nt1Time_TieForMostPoints_MoreUnitsAmongTiedWins()
        {
            var world = new Fake { Owned = new[] { 2, 2, 1 }, Alive = new[] { 5, 6, 9 } };
            var mode = Mode(world); mode.Evaluate(Max);
            Expect(mode, 2, MatchEndReason.Time, Max);
        }

        [Test]
        public void Nt1Time_TieForMostPoints_FirstTiedTeamHasMoreUnits_Wins()
        {
            var world = new Fake { Owned = new[] { 2, 1, 2 }, Alive = new[] { 7, 9, 6 } };
            var mode = Mode(world); mode.Evaluate(Max);
            Expect(mode, 1, MatchEndReason.Time, Max);
        }

        [Test]
        public void Nt1Time_OneTeamWithMostPoints_WinsWithoutCountingUnits()
        {
            var world = new Fake { Owned = new[] { 1, 3, 1 }, Alive = new[] { 9, 1, 9 } };
            var mode = Mode(world); mode.Evaluate(Max);
            Expect(mode, 2, MatchEndReason.Time, Max);
            world = new Fake { Owned = new[] { 1, 1, 3 }, Alive = new[] { 9, 9, 1 } };
            mode = Mode(world); mode.Evaluate(Max);
            Expect(mode, 3, MatchEndReason.Time, Max);
        }

        [Test]
        public void Nt1Time_BeforeTheLimit_GoesOn()
        {
            var world = new Fake { Owned = new[] { 1, 3, 1 } };
            var mode = Mode(world); mode.Evaluate(Max - 1);
            Expect(mode, 0, MatchEndReason.None, 0);
        }

        // The default span overload: the two old counts in slots 0 and 1, every other slot cleared.
        [Test]
        public void Nt1DefaultSpanCount_FillsTwoSlotsAndClearsTheRest()
        {
            IMatchReader two = new TwoTeamFake { AliveOne = 4, AliveTwo = 7 };
            Span<int> alive = stackalloc int[8];
            alive.Fill(9);
            Assert.That(two.TeamCount, Is.EqualTo(2));
            two.CountAlive(alive);
            Assert.That(alive.ToArray(), Is.EqualTo(new[] { 4, 7, 0, 0, 0, 0, 0, 0 }));
        }
    }
}
