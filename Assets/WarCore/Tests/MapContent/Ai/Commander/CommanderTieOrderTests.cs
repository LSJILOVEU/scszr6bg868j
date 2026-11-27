using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.CommanderBoard;

namespace WarCore.Tests
{
    // RC2-3c: R5's two ties (a target nobody goes to, largest wanted; the largest deficit) go to the target CommanderQuota.Plan
    // picked earlier (rankOut), not to the lower point index. Board: five points on a line 28 m apart, index i at
    // x = 24 + 28 i (24, 52, 80, 108, 136), z 20; team 1's base at x 6, team 2's at x 154. On a line only neighbouring points
    // border each other. Team 2 scores the points from its base: 4 is 18 m away, 3 46 m, 2 74 m, 1 102 m, 0 130 m, so it picks
    // them in the opposite order of their indices; team 1, from its base, picks them in index order. Unless a test says
    // otherwise, every unit stands within 30 m of the point next to its own base and nearest to it, so it counts there as an
    // enemy for the other team: 200 more each on the score of the point that team scores highest anyway. One tick: no
    // attack flags yet, so no stickiness.
    public sealed class CommanderTieOrderTests
    {
        private static ControlPointDef[] Line(int[] owners)
        {
            var points = new ControlPointDef[5];
            for (int i = 0; i < 5; i++) points[i] = Point(i + 1, 24 + 28 * i, owners[i]);
            return points;
        }
        private static SpawnAnchor[] Bases() => new[] { Base(1, 6), Base(2, 154) };
        private static readonly int[] Neutral = { 0, 0, 0, 0, 0 };

        // First tie (the mirrored opening). Every point is neutral; one unit per team at its base. Team 2 owns nothing, so
        // every point is a candidate; scores 4: 18, 3: 46, 2: 74, 1: 102, 0: 200 + 130 = 330. Three targets, picked in the
        // order 4 (rank 0), 3 (rank 1), 2 (rank 2). One soldier: pool max(3, 1) = 3, one wanted at each. Unit 20 has no order
        // and 4, 3 and 2 have nobody and want 1 each: a tie. Picked first: 4 (18 m away). (The lower index would be 2, 74 m.)
        // Team 1 picks 0 (18 m), 1 (46 m), 2 (74 m) in index order, so its unit 10 goes to 0 under either rule.
        [Test]
        public void TiedTargetsNobodyGoesToGoToTheOnePickedFirst()
        {
            var b = new CommanderBoard(Line(Neutral), new[] { Man(10, 1, 6), Man(20, 2, 154) }, Bases(), 160);
            b.Step();
            for (int p = 0; p < 5; p++) Assert.That(b.Commander.Attacks(2, p), Is.EqualTo(p >= 2), "team 2 attacks 4, 3 and 2: point " + p);
            Assert.That(new[] { b.Commander.Desired(2, 4), b.Commander.Desired(2, 3), b.Commander.Desired(2, 2) }, Is.EqualTo(new[] { 1, 1, 1 }));
            Assert.That(b.Row(20), Is.EqualTo(4), "the target picked first");
            Assert.That(b.Row(10), Is.EqualTo(0), "team 1 picks in index order");
            Assert.That(b.Checker.Clean, Is.True, b.Checker.First);
        }

        // Second tie, on both sides. Every point is neutral; each team has five units (five groups of one). Team 2: 21 keeps
        // 4, 22 keeps 3, 23 and 24 keep 2, 25 has no order. Targets as above: 4 (rank 0), 3 (rank 1), 2 (rank 2). Five
        // soldiers: pool 5, 5 / 3 = 1 and 5 % 3 = 2, so ranks 0 and 1 want 2 and rank 2 wants 1. Every order names a target,
        // so all are kept. Deficits: 4: 2 - 1 = 1, 3: 2 - 1 = 1, 2: 1 - 2 = -1. Every target has a group, so the first step
        // picks nothing; the largest deficit, 1, ties at 3 and 4. Picked first: 4, so unit 25 goes to 4. (The lower index
        // would send it to 3.) Team 1 is the mirror image: 11 keeps 0, 12 keeps 1, 13 and 14 keep 2, 15 has no order; its
        // targets 0 (rank 0), 1 (rank 1), 2 (rank 2) want 2, 2 and 1; the deficits 1, 1, -1 tie at 0 and 1; unit 15 goes to 0,
        // picked first. (There the answer is the lower index and the first point looked at; it must stay the pick.)
        [Test]
        public void TiedDeficitsGoToTheTargetPickedFirst()
        {
            var b = new CommanderBoard(Line(Neutral), new[] { Man(11, 1, 10, 12), Man(12, 1, 10, 16), Man(13, 1, 10, 24), Man(14, 1, 10, 28),
                Man(15, 1, 6), Man(21, 2, 150, 12), Man(22, 2, 150, 16), Man(23, 2, 150, 24), Man(24, 2, 150, 28), Man(25, 2, 154) }, Bases(), 160);
            b.Give((11, 0), (12, 1), (13, 2), (14, 2), (21, 4), (22, 3), (23, 2), (24, 2));
            b.Step();
            for (int p = 0; p < 5; p++) Assert.That(b.Commander.Attacks(2, p), Is.EqualTo(p >= 2), "team 2 attacks 4, 3 and 2: point " + p);
            for (int p = 0; p < 5; p++) Assert.That(b.Commander.Attacks(1, p), Is.EqualTo(p <= 2), "team 1 attacks 0, 1 and 2: point " + p);
            Assert.That(new[] { b.Commander.Desired(2, 4), b.Commander.Desired(2, 3), b.Commander.Desired(2, 2) }, Is.EqualTo(new[] { 2, 2, 1 }));
            Assert.That(new[] { b.Commander.Desired(1, 0), b.Commander.Desired(1, 1), b.Commander.Desired(1, 2) }, Is.EqualTo(new[] { 2, 2, 1 }));
            Assert.That(b.Rows(21, 22, 23, 24, 25), Is.EqualTo(new[] { 4, 3, 2, 2, 4 }));
            Assert.That(b.Rows(11, 12, 13, 14, 15), Is.EqualTo(new[] { 0, 1, 2, 2, 0 }));
            Assert.That(b.Checker.Clean, Is.True, b.Checker.First);
        }

        // The deficit comes before the order of picking. The board of the second tie, but 21 and 22 keep 4, 23 keeps 3, 24
        // keeps 2, 25 has no order. Wanted as there: 4 (rank 0) 2, 3 (rank 1) 2, 2 (rank 2) 1. Deficits 4: 2 - 2 = 0,
        // 3: 2 - 1 = 1, 2: 1 - 1 = 0. Every target has a group, so the first step picks nothing; the largest deficit is 3's
        // alone, so unit 25 goes to 3, though 4 was picked earlier. (The order of picking alone would send it to 4.)
        [Test]
        public void ALargerDeficitComesBeforeTheOrderOfPicking()
        {
            var b = new CommanderBoard(Line(Neutral), new[] { Man(10, 1, 6), Man(21, 2, 150, 12), Man(22, 2, 150, 16), Man(23, 2, 150, 24),
                Man(24, 2, 150, 28), Man(25, 2, 154) }, Bases(), 160);
            b.Give((21, 4), (22, 4), (23, 3), (24, 2));
            b.Step();
            for (int p = 0; p < 5; p++) Assert.That(b.Commander.Attacks(2, p), Is.EqualTo(p >= 2), "team 2 attacks 4, 3 and 2: point " + p);
            Assert.That(new[] { b.Commander.Desired(2, 4), b.Commander.Desired(2, 3), b.Commander.Desired(2, 2) }, Is.EqualTo(new[] { 2, 2, 1 }));
            Assert.That(b.Rows(21, 22, 23, 24, 25), Is.EqualTo(new[] { 4, 4, 3, 2, 3 }));
            Assert.That(b.Checker.Clean, Is.True, b.Checker.First);
        }

        // The first step's direction, on both sides. Every point is neutral; each team has four units. Team 2: 21, 22 and 23
        // keep 3, 24 has no order. Targets as above: 4 (rank 0), 3 (rank 1), 2 (rank 2). Four soldiers: pool 4, 4 / 3 = 1
        // and 4 % 3 = 1, so rank 0 wants 2 and ranks 1 and 2 want 1. Nobody goes to 4 (wanted 2) and nobody to 2 (wanted 1);
        // the one that wants more comes first, so unit 24, the only one without an order, goes to 4. (The one that wants less
        // first would send it to 2.) Team 1 is the mirror image: 11, 12 and 13 keep 1, 14 has no order; nobody goes to 0
        // (wanted 2) and nobody to 2 (wanted 1), so unit 14 goes to 0. (For team 2 the one that wants more is looked at
        // last, for team 1 first: a later target that merely wants a different number must not replace it.)
        [Test]
        public void OfTheTargetsNobodyGoesToTheOneWantingMoreComesFirst()
        {
            var b = new CommanderBoard(Line(Neutral), new[] { Man(11, 1, 10, 12), Man(12, 1, 10, 16), Man(13, 1, 10, 24), Man(14, 1, 6),
                Man(21, 2, 150, 12), Man(22, 2, 150, 16), Man(23, 2, 150, 24), Man(24, 2, 154) }, Bases(), 160);
            b.Give((11, 1), (12, 1), (13, 1), (21, 3), (22, 3), (23, 3));
            b.Step();
            for (int p = 0; p < 5; p++) Assert.That(b.Commander.Attacks(2, p), Is.EqualTo(p >= 2), "team 2 attacks 4, 3 and 2: point " + p);
            for (int p = 0; p < 5; p++) Assert.That(b.Commander.Attacks(1, p), Is.EqualTo(p <= 2), "team 1 attacks 0, 1 and 2: point " + p);
            Assert.That(new[] { b.Commander.Desired(2, 4), b.Commander.Desired(2, 3), b.Commander.Desired(2, 2) }, Is.EqualTo(new[] { 2, 1, 1 }));
            Assert.That(new[] { b.Commander.Desired(1, 0), b.Commander.Desired(1, 1), b.Commander.Desired(1, 2) }, Is.EqualTo(new[] { 2, 1, 1 }));
            Assert.That(b.Rows(21, 22, 23, 24), Is.EqualTo(new[] { 3, 3, 3, 4 }));
            Assert.That(b.Rows(11, 12, 13, 14), Is.EqualTo(new[] { 1, 1, 1, 0 }));
            Assert.That(b.Checker.Clean, Is.True, b.Checker.First);
        }

        // A target nobody goes to comes before the largest deficit, also when it is point 0. Every point is neutral. Team 1's
        // two units stand on points 3 and 4 (one enemy each for team 2: 200 more on each score), so team 2's scores are
        // 2: 74, 1: 102, 0: 130, 4: 18 + 200 = 218, 3: 46 + 200 = 246 and it picks 2 (rank 0), 1 (rank 1), 0 (rank 2). Four
        // soldiers: 2 wants 2, 1 and 0 want 1. 21 keeps 2, 22 and 23 keep 1, 24 has no order. Nobody goes to 0, so unit 24
        // goes there. (By the deficits alone, 2: 2 - 1 = 1, 1: 1 - 2 = -1, 0: 1 - 0 = 1, the tie of 2 and 0 would go to 2,
        // picked first.)
        [Test]
        public void ATargetNobodyGoesToComesFirstAlsoWhenItIsPointZero()
        {
            var b = new CommanderBoard(Line(Neutral), new[] { Man(11, 1, 108), Man(12, 1, 136), Man(21, 2, 150, 12), Man(22, 2, 150, 16),
                Man(23, 2, 150, 24), Man(24, 2, 154) }, Bases(), 160);
            b.Give((21, 2), (22, 1), (23, 1));
            b.Step();
            for (int p = 0; p < 5; p++) Assert.That(b.Commander.Attacks(2, p), Is.EqualTo(p <= 2), "team 2 attacks 2, 1 and 0: point " + p);
            Assert.That(new[] { b.Commander.Desired(2, 2), b.Commander.Desired(2, 1), b.Commander.Desired(2, 0) }, Is.EqualTo(new[] { 2, 1, 1 }));
            Assert.That(b.Rows(21, 22, 23, 24), Is.EqualTo(new[] { 2, 1, 1, 0 }));
            Assert.That(b.Checker.Clean, Is.True, b.Checker.First);
        }

        // No tie: the order of picking does not matter, though it is the opposite of the index order. Team 2 owns 4; unit 21
        // is on its way to 1, 22 and 23 have no order. Candidates: 3 (it borders 4) and 1 (a group is on its way to it);
        // scores 3: 46, 1: 102, so 3 is rank 0 and 1 rank 1. Three soldiers over two targets: 3 / 2 = 1 and 3 % 2 = 1, so 3
        // wants 2 and 1 wants 1. Unit 21 keeps 1. Deficits 3: 2 (nobody there), 1: 1 - 1 = 0. First step: 3 is the only target
        // nobody goes to, so one of 22 and 23 goes there; then the deficits are 3: 1 and 1: 0, so the other goes to 3 too.
        [Test]
        public void WithoutATieThePickIsTheSame()
        {
            var b = new CommanderBoard(Line(new[] { 0, 0, 0, 0, 2 }), new[] { Man(10, 1, 6), Man(21, 2, 150, 12), Man(22, 2, 150, 16),
                Man(23, 2, 150, 24) }, Bases(), 160);
            b.Give((21, 1));
            b.Step();
            for (int p = 0; p < 5; p++) Assert.That(b.Commander.Attacks(2, p), Is.EqualTo(p == 1 || p == 3), "team 2 attacks 3 and 1: point " + p);
            Assert.That(b.Commander.Desired(2, 3), Is.EqualTo(2));
            Assert.That(b.Commander.Desired(2, 1), Is.EqualTo(1));
            Assert.That(b.Rows(21, 22, 23), Is.EqualTo(new[] { 1, 3, 3 }));
            Assert.That(b.Checker.Clean, Is.True, b.Checker.First);
        }

        // CommanderQuota.Plan's rankOut: the order in which each target was picked (0 first), -1 for every other point, written
        // over whatever the buffer held. Team 2's plan on the board above: 4 (18), 3 (46), 2 (74). A rankOut shorter than the
        // points is refused like every other buffer.
        [Test]
        public void ThePlanReportsTheOrderItPickedTheTargetsIn()
        {
            var centers = new SimVec3[5];
            for (int i = 0; i < 5; i++) centers[i] = SimVec3.Metres(24 + 28 * i, 0, 20);
            var adjacent = new bool[25];
            CommanderQuota.Adjacency(centers, 5, adjacent);
            var desired = new int[5];
            var attack = new bool[5];
            int[] rank = { 7, 7, 7, 7, 7 };
            CommanderQuota.Plan(2, 5, new int[5], centers, new int[5], adjacent, 1, SimVec3.Metres(154, 0, 20), new bool[5], CommanderQuotaRules.Default,
                desired, attack, new bool[5], rank);
            Assert.That(attack, Is.EqualTo(new[] { false, false, true, true, true }));
            Assert.That(rank, Is.EqualTo(new[] { -1, -1, 2, 1, 0 }));
            Assert.That(() => CommanderQuota.Plan(2, 5, new int[5], centers, new int[5], adjacent, 1, SimVec3.Metres(154, 0, 20), new bool[5],
                CommanderQuotaRules.Default, desired, attack, new bool[5], new int[4]), Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }
    }
}
