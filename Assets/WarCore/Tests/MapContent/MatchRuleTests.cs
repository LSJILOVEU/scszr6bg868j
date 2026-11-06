using NUnit.Framework;
using WarCore.Battle;

namespace WarCore.Tests
{
    // The end-of-match rule parts, driven through the mode with a fake read-only world.
    public sealed class MatchRuleTests
    {
        private sealed class Fake : IMatchReader
        {
            internal int Points = 3, OwnedOne, OwnedTwo, AliveOne = 5, AliveTwo = 5;
            public int PointCount => Points;
            public void CountAlive(out int teamOne, out int teamTwo) { teamOne = AliveOne; teamTwo = AliveTwo; }
            public int PointsOwnedBy(int team) => team == 1 ? OwnedOne : OwnedTwo;
        }

        private static (MatchMode mode, Fake world) Make(int maxTicks = 100)
        {
            var mode = new MatchMode(M6World.Pack(max: maxTicks));
            var world = new Fake();
            mode.Bind(world);
            return (mode, world);
        }

        private static void Expect(MatchMode mode, int winner, MatchEndReason reason, long tick)
        {
            Assert.That((mode.Winner, mode.EndReason, mode.EndTick), Is.EqualTo((winner, reason, tick)));
        }

        [Test]
        public void NothingEnds_WhileNoRuleHolds()
        {
            var (mode, world) = Make();
            world.OwnedOne = 2; world.OwnedTwo = 1;
            mode.Evaluate(99);
            Expect(mode, 0, MatchEndReason.None, 0);
            Assert.That(mode.IsFinished, Is.False);
        }

        [TestCase(3, 0, 1), TestCase(0, 3, 2)]
        public void AllPoints_TheOwnerWins(int one, int two, int winner)
        {
            var (mode, world) = Make();
            world.OwnedOne = one; world.OwnedTwo = two;
            mode.Evaluate(7);
            Expect(mode, winner, MatchEndReason.AllPoints, 7);
        }

        [TestCase(0, 4, 2, MatchEndReason.Exhausted), TestCase(4, 0, 1, MatchEndReason.Exhausted), TestCase(0, 0, -1, MatchEndReason.Draw)]
        public void Exhausted_TheTeamWithUnitsLeftWins_BothEmptyIsADraw(int aliveOne, int aliveTwo, int winner, MatchEndReason reason)
        {
            var (mode, world) = Make();
            world.AliveOne = aliveOne; world.AliveTwo = aliveTwo;
            mode.Evaluate(12);
            Expect(mode, winner, reason, 12);
        }

        [TestCase(2, 1, 5, 5, 1, MatchEndReason.Time), TestCase(1, 2, 9, 1, 2, MatchEndReason.Time),
         TestCase(1, 1, 6, 5, 1, MatchEndReason.Time), TestCase(1, 1, 5, 6, 2, MatchEndReason.Time),
         TestCase(1, 1, 5, 5, -1, MatchEndReason.Draw)]
        public void TimeLimit_MorePoints_ThenMoreAlive_ElseADraw(int pointsOne, int pointsTwo, int aliveOne, int aliveTwo, int winner, MatchEndReason reason)
        {
            var (mode, world) = Make(100);
            world.OwnedOne = pointsOne; world.OwnedTwo = pointsTwo; world.AliveOne = aliveOne; world.AliveTwo = aliveTwo;
            mode.Evaluate(99);
            Assert.That(mode.IsFinished, Is.False, "one tick before the limit");
            mode.Evaluate(100);
            Expect(mode, winner, reason, 100);
        }

        [Test]
        public void TheFirstRuleThatHoldsDecides()
        {
            var (allAndExhausted, w1) = Make(100);
            w1.OwnedOne = 3; w1.AliveTwo = 0;
            allAndExhausted.Evaluate(100);
            Expect(allAndExhausted, 1, MatchEndReason.AllPoints, 100);
            var (exhaustedAndTime, w2) = Make(100);
            w2.AliveOne = 0; w2.AliveTwo = 3;
            exhaustedAndTime.Evaluate(100);
            Expect(exhaustedAndTime, 2, MatchEndReason.Exhausted, 100);
        }

        [Test]
        public void AResultIsKept_AndTheNeverEndsIdJudgesNothing()
        {
            var (mode, world) = Make();
            world.OwnedOne = 3;
            mode.Evaluate(5);
            world.OwnedOne = 0; world.OwnedTwo = 3;
            mode.Evaluate(6);
            Expect(mode, 1, MatchEndReason.AllPoints, 5);
            var never = new MatchMode(M6World.Pack(), MatchMode.TestNeverEndsId);
            var all = new Fake { OwnedOne = 3 };
            never.Bind(all);
            never.Evaluate(1000);
            Assert.That(never.IsFinished, Is.False);
        }
    }
}
