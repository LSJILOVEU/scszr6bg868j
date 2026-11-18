using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-P3: commander quota as pure functions (CommanderQuota). Expected values are worked out by hand in the comments.
    // Score = enemies * 200 + whole metres to the anchor - 600 if attacked last time; lower wins, ties to lower index.
    public sealed class CommanderQuotaTests
    {
        private const int Blue = 1, Red = 2, Green = 3, Neutral = 0;
        private const int N = 5;

        // 0 blue HQ (-120, 0) - 1 blue outpost (-60, 0) - 2 left (0, -40) / 3 right (0, 40) - 4 red HQ (120, 0).
        // Red HQ always has 6 defenders. Distances from the blue anchor (-120, 0): 2 and 3 are sqrt(16000) = 126.49 -> 126,
        // 4 is 240. Distances from (120, 0): 0 is 240, 1 is 180, 2 and 3 are 126, 4 is 0.
        private static readonly SimVec3[] Centers =
        {
            SimVec3.Metres(-120, 0, 0), SimVec3.Metres(-60, 0, 0), SimVec3.Metres(0, 0, -40), SimVec3.Metres(0, 0, 40), SimVec3.Metres(120, 0, 0)
        };

        private static readonly SimVec3 BlueHome = SimVec3.Metres(-120, 0, 0);
        private static readonly SimVec3 RedHome = SimVec3.Metres(120, 0, 0);

        private static CommanderQuotaRules OneTarget => new CommanderQuotaRules(1300, 2, 400, 1, 200, 1, 600);

        private static bool[] Table()
        {
            var adjacent = new bool[N * N];
            CommanderQuota.Adjacency(Centers, N, adjacent);
            return adjacent;
        }

        private static int[] Owners(int left = Neutral) => new[] { Blue, Blue, left, Neutral, Red };

        private static (int[] desired, bool[] attack) Plan(int team, int[] owners, int[] enemies, int soldiers, SimVec3 anchor,
            bool[] previous, CommanderQuotaRules rules)
        {
            var desired = new int[N];
            var attack = new bool[N];
            CommanderQuota.Plan(team, N, owners, Centers, enemies, Table(), soldiers, anchor, previous ?? new bool[N], rules, desired, attack);
            return (desired, attack);
        }

        private static bool[] Only(params int[] points)
        {
            var flags = new bool[N];
            foreach (int p in points) flags[p] = true;
            return flags;
        }

        [Test]
        public void Adjacency_BoardFollowsTheDiameterCircleRule()
        {
            // 0-1, 1-2, 1-3, 2-4, 3-4, and 2-3: their diameter circle (centre (0,0), r 40) holds no point.
            bool[] a = Table();
            var expected = new bool[N * N];
            foreach ((int i, int j) in new[] { (0, 1), (1, 2), (1, 3), (2, 3), (2, 4), (3, 4) }) expected[i * N + j] = expected[j * N + i] = true;
            Assert.That(a, Is.EqualTo(expected));
        }

        [Test]
        public void Adjacency_CollinearMiddleBlocksTheEnds()
        {
            var c = new[] { SimVec3.Metres(0, 0, 0), SimVec3.Metres(10, 0, 0), SimVec3.Metres(20, 0, 0) };
            var a = new bool[9];
            CommanderQuota.Adjacency(c, 3, a);
            Assert.That(a, Is.EqualTo(new[] { false, true, false, true, false, true, false, true, false }));
        }

        [Test]
        public void Adjacency_EquilateralTriangleIsFullyConnected()
        {
            // Every angle is 60 degrees, so each third point is outside the other two's diameter circle.
            var c = new[] { SimVec3.Metres(0, 0, 0), SimVec3.Metres(100, 0, 0), new SimVec3(SimFix.FromInt(50), SimFix.FromInt(0), SimFix.Ratio(866025, 10000)) };
            var a = new bool[9];
            CommanderQuota.Adjacency(c, 3, a);
            Assert.That(a, Is.EqualTo(new[] { false, true, true, true, false, true, true, true, false }));
        }

        [TestCase(512)]
        [TestCase(8192)]
        public void Adjacency_MapCornersDoNotOverflow(int m)
        {
            // Square corners plus the centre. A side's diameter circle passes exactly through the centre (dot = 0):
            // on the circle is not inside, so sides stay neighbours. A diagonal's circle holds the centre strictly.
            var c = new[] { SimVec3.Metres(-m, 0, -m), SimVec3.Metres(m, 0, -m), SimVec3.Metres(m, 0, m), SimVec3.Metres(-m, 0, m), SimVec3.Metres(0, 0, 0) };
            var a = new bool[25];
            CommanderQuota.Adjacency(c, 5, a);
            var expected = new bool[25];
            foreach ((int i, int j) in new[] { (0, 1), (1, 2), (2, 3), (3, 0), (0, 4), (1, 4), (2, 4), (3, 4) }) expected[i * 5 + j] = expected[j * 5 + i] = true;
            Assert.That(a, Is.EqualTo(expected));
        }

        [Test]
        public void Adjacency_RejectsPointsBeyondTheLimit()
        {
            var c = new[] { SimVec3.Metres(0, 0, 0), SimVec3.Metres(8193, 0, 0) };
            Assert.Throws<ArgumentOutOfRangeException>(() => CommanderQuota.Adjacency(c, 2, new bool[4]));
        }

        // Scenario 1. Blue, 40 soldiers, one target, enemies left 1 / right 2.
        // Own: 0 has only own neighbours -> 0; 1 borders neutral 2, 3 -> frontier 2. Defense 2 <= cap 40*400/1000 = 16.
        // Candidates 2, 3 (4 has no blue neighbour). Scores 2: 200+126 = 326, 3: 400+126 = 526 -> 2. Pool 40-2 = 38.
        [Test]
        public void Scenario1_FirstTargetIsTheSofterOne()
        {
            var (desired, attack) = Plan(Blue, Owners(), new[] { 0, 0, 1, 2, 6 }, 40, BlueHome, null, OneTarget);
            Assert.That(attack, Is.EqualTo(Only(2)));
            Assert.That(desired, Is.EqualTo(new[] { 0, 2, 38, 0, 0 }));
        }

        // Scenario 2. Left now has 4, right 2, left was attacked: 800+126-600 = 326 < 526 -> stay on 2.
        [Test]
        public void Scenario2_TargetHoldsWhenThreatWobbles()
        {
            var (desired, attack) = Plan(Blue, Owners(), new[] { 0, 0, 4, 2, 6 }, 40, BlueHome, Only(2), OneTarget);
            Assert.That(attack, Is.EqualTo(Only(2)));
            Assert.That(desired, Is.EqualTo(new[] { 0, 2, 38, 0, 0 }));
        }

        // Scenario 3: same as scenario 1 with an empty (not null) previous set.
        [Test]
        public void Scenario3_FirstTargetAgain()
        {
            var (desired, attack) = Plan(Blue, Owners(), new[] { 0, 0, 1, 2, 6 }, 40, BlueHome, new bool[N], OneTarget);
            Assert.That(attack, Is.EqualTo(Only(2)));
            Assert.That(desired, Is.EqualTo(new[] { 0, 2, 38, 0, 0 }));
        }

        // Scenario 4. Left taken (blue, 0 enemies), previous {2}. Own: 0 -> 0; 1 borders 3 -> 2;
        // 2 borders 3, 4 -> 2. Defense 4. Candidates 3 (borders 1, 2), 4 (borders 2). 3: 400+126 = 526,
        // 4: 1200+240 = 1440 -> 3. Pool 36.
        [Test]
        public void Scenario4_TakenTargetIsDroppedForTheNext()
        {
            var (desired, attack) = Plan(Blue, Owners(Blue), new[] { 0, 0, 0, 2, 6 }, 40, BlueHome, Only(2), OneTarget);
            Assert.That(attack, Is.EqualTo(Only(3)));
            Assert.That(desired, Is.EqualTo(new[] { 0, 2, 2, 36, 0 }));
        }

        // Scenario 5, previous carried from scenario 4 = {3}. Left neutral with 9: 1800+126 = 1926;
        // right 400+126-600 = -74 -> 3. Pool 38.
        [Test]
        public void Scenario5_HeavyDefenceKeepsTheArmyAway()
        {
            var (desired, attack) = Plan(Blue, Owners(), new[] { 0, 0, 9, 2, 6 }, 40, BlueHome, Only(3), OneTarget);
            Assert.That(attack, Is.EqualTo(Only(3)));
            Assert.That(desired, Is.EqualTo(new[] { 0, 2, 0, 38, 0 }));
        }

        // Stickiness edge, previous {2}, right fixed at 526. Left 5: 1000+126-600 = 526, a tie -> lower index 2.
        // Left 6: 726 > 526 -> switch to 3. Left 9: 1326 -> 3 (hopeless, switch).
        [TestCase(5, 2)]
        [TestCase(6, 3)]
        [TestCase(9, 3)]
        public void Own1_StickinessHoldsUpToThreeExtraEnemies(int left, int chosen)
        {
            var (desired, attack) = Plan(Blue, Owners(), new[] { 0, 0, left, 2, 6 }, 40, BlueHome, Only(2), OneTarget);
            Assert.That(attack, Is.EqualTo(Only(chosen)));
            Assert.That(desired[chosen], Is.EqualTo(38));
        }

        // Threatened own points, 100 soldiers (cap 40). 0 with 1 enemy: ceil(1.3) + 1 = 3; 1 with 3: ceil(3.9) + 1 = 5.
        // Defense 8. One target 2 (326 < 526). Pool 92.
        [Test]
        public void Own2_GarrisonRoundsUp()
        {
            var (desired, attack) = Plan(Blue, Owners(), new[] { 1, 3, 1, 2, 6 }, 100, BlueHome, null, OneTarget);
            Assert.That(attack, Is.EqualTo(Only(2)));
            Assert.That(desired, Is.EqualTo(new[] { 3, 5, 92, 0, 0 }));
        }

        // 40 soldiers, cap 16. 0 with 1 enemy -> 3; 1 with 10 -> 13 + 1 = 14 (exact, no extra). Wanted 17 > 16:
        // 3*16/17 = 2, 14*16/17 = 13 -> defense 15. Pool 40-15 = 25.
        [Test]
        public void Own3_GarrisonsAreCappedAtFortyPercent()
        {
            var (desired, attack) = Plan(Blue, Owners(), new[] { 1, 10, 1, 2, 6 }, 40, BlueHome, null, OneTarget);
            Assert.That(attack, Is.EqualTo(Only(2)));
            Assert.That(desired, Is.EqualTo(new[] { 2, 13, 25, 0, 0 }));
        }

        // 2 soldiers, cap 2*400/1000 = 0. Wanted 3 + 2 = 5: each scaled to 0, raised to 1 -> defense 2.
        // Pool max(1 target, 2-2) = 1.
        [Test]
        public void Own4_CapNeverLeavesAThreatenedPointEmpty()
        {
            var (desired, attack) = Plan(Blue, Owners(), new[] { 1, 0, 1, 2, 6 }, 2, BlueHome, null, OneTarget);
            Assert.That(attack, Is.EqualTo(Only(2)));
            Assert.That(desired, Is.EqualTo(new[] { 1, 1, 1, 0, 0 }));
        }

        // Blue holds nothing: every non-blue point is a candidate. Anchor (120, 0), default rules (3 targets).
        // 0: 240; 1: 180; 2: 200+126 = 326; 3: 400+126 = 526; 4: 1200+0 = 1200. Order 1, 0, 2. Pool 40 over 3:
        // 14, 13, 13, the extra one to the best score (point 1), not the lowest index.
        [Test]
        public void Own5_NoOwnPointsAttacksTheThreeBestByScore()
        {
            var (desired, attack) = Plan(Blue, new[] { Neutral, Neutral, Neutral, Neutral, Red }, new[] { 0, 0, 1, 2, 6 }, 40, RedHome, null,
                CommanderQuotaRules.Default);
            Assert.That(attack, Is.EqualTo(Only(0, 1, 2)));
            Assert.That(desired, Is.EqualTo(new[] { 13, 14, 13, 0, 0 }));
        }

        // Three targets allowed but only 2 and 3 border blue: 2 targets. No enemies anywhere: 2 and 3 both 126, tie.
        // Defense = frontier 2 at point 1. Pool 38 -> 19, 19.
        [Test]
        public void Own6_NoEnemiesSplitsOverTheFrontCandidates()
        {
            var (desired, attack) = Plan(Blue, Owners(), new int[N], 40, BlueHome, null, CommanderQuotaRules.Default);
            Assert.That(attack, Is.EqualTo(Only(2, 3)));
            Assert.That(desired, Is.EqualTo(new[] { 0, 2, 19, 19, 0 }));
        }

        // Blue holds every point: nothing to attack, no frontier; 2 enemies at 4 -> ceil(2.6) + 1 = 4.
        [Test]
        public void Own7_AllPointsHeldOnlyDefends()
        {
            var (desired, attack) = Plan(Blue, new[] { Blue, Blue, Blue, Blue, Blue }, new[] { 0, 0, 0, 0, 2 }, 40, BlueHome, Only(3),
                CommanderQuotaRules.Default);
            Assert.That(attack, Is.EqualTo(new bool[N]));
            Assert.That(desired, Is.EqualTo(new[] { 0, 0, 0, 0, 4 }));
        }

        // Three teams, red plans from 4. Point 3 belongs to a third team and is a target like neutral 2.
        // Own 4 borders 2, 3 -> frontier 2. Candidates 2, 3 (0 and 1 do not border 4). Anchor (120, 0):
        // 2: 3*200+126 = 726; 3: 200+126 = 326 -> 3. Pool 38.
        [Test]
        public void Own8_AnyOtherTeamIsTheEnemy()
        {
            var (desired, attack) = Plan(Red, new[] { Blue, Blue, Neutral, Green, Red }, new[] { 0, 0, 3, 1, 0 }, 40, RedHome, null, OneTarget);
            Assert.That(attack, Is.EqualTo(Only(3)));
            Assert.That(desired, Is.EqualTo(new[] { 0, 0, 0, 38, 2 }));
        }

        [Test]
        public void Plan_RejectsPreviousAliasingTheOutput()
        {
            var flags = new bool[N];
            Assert.Throws<ArgumentException>(() => CommanderQuota.Plan(Blue, N, Owners(), Centers, new int[N], Table(), 40, BlueHome, flags,
                CommanderQuotaRules.Default, new int[N], flags));
        }

        [Test]
        public void Plan_SameInputSameBytes()
        {
            var first = Plan(Blue, Owners(), new[] { 1, 10, 1, 2, 6 }, 40, BlueHome, Only(3), CommanderQuotaRules.Default);
            var second = Plan(Blue, Owners(), new[] { 1, 10, 1, 2, 6 }, 40, BlueHome, Only(3), CommanderQuotaRules.Default);
            Assert.That(Bytes(second.desired, second.attack), Is.EqualTo(Bytes(first.desired, first.attack)));
        }

        private static byte[] Bytes(int[] desired, bool[] attack)
        {
            var bytes = new byte[desired.Length * 5];
            for (int i = 0; i < desired.Length; i++)
            {
                BitConverter.GetBytes(desired[i]).CopyTo(bytes, i * 5);
                bytes[i * 5 + 4] = attack[i] ? (byte)1 : (byte)0;
            }
            return bytes;
        }

        [Test]
        public void SteadyStateCallsAllocateNothing()
        {
            int[] owners = Owners(), enemies = { 1, 10, 1, 2, 6 }, desired = new int[N];
            bool[] adjacent = new bool[N * N], previous = Only(2), attack = new bool[N];
            CommanderQuotaRules rules = CommanderQuotaRules.Default;
            CommanderQuota.Adjacency(Centers, N, adjacent);
            CommanderQuota.Plan(Blue, N, owners, Centers, enemies, adjacent, 40, BlueHome, previous, rules, desired, attack);
            // Unity's Mono has no direct per-thread byte counter: there the test is inconclusive (pattern of
            // CachedManifestRegressionTests); CoreCLR headless runs the strict direct check.
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Strict zero needs a passing direct positive control.");
            Assert.That(meter.DirectControlDelta.Bytes, Is.GreaterThanOrEqualTo(32768));
            AllocationSample before = meter.Read();
            for (int i = 0; i < 100; i++)
            {
                CommanderQuota.Adjacency(Centers, N, adjacent);
                CommanderQuota.Plan(Blue, N, owners, Centers, enemies, adjacent, 40, BlueHome, previous, rules, desired, attack);
            }
            AllocationSample sample = meter.Difference(before);
            TestContext.WriteLine("AI-P3 commander quota: control_bytes={0}; {1}", meter.DirectControlDelta.Bytes, sample.Describe());
            AllocationAssertions.Zero(sample);
            // Own3's garrisons (2, 13, pool 25); 3 targets allowed, 2 and 3 are candidates, previous {2}:
            // 2: 326-600 = -274, 3: 526 -> order 2, 3 -> 13, 12.
            Assert.That(desired, Is.EqualTo(new[] { 2, 13, 13, 12, 0 }));
        }
    }
}
