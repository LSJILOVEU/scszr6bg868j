using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.RespawnDigestLockTests;

namespace WarCore.Tests
{
    // C1c (8): match.respawnGuardTicks in content and in the pack constructor, and the demo recipe helpers.
    public sealed class C1cContentTests
    {
        private static MatchPack Direct(int respawn, int guard = 0) => new MatchPack(Points(), Bases(), 2, 1, false, 300, InterruptMode.Reset, 1,
            90, 30, 9000, SimFix.FromInt(35), 4, 4, SimFix.Ratio(3, 2), respawn, guard);
        private static MatchPack OldDirect(int respawn) => new MatchPack(Points(), Bases(), 2, 1, false, 300, InterruptMode.Reset, 1,
            90, 30, 9000, SimFix.FromInt(35), 4, 4, SimFix.Ratio(3, 2), respawn);
        private static ulong Digest(string extra) => Load(extra).BuildMatch(Points(), Bases()).PolicyDigest;

        // Recorded on the branch: the guard length is hashed.
        internal const ulong Guard90 = 6298217577412357680UL, Guard91 = 8530532984379947089UL;

        [Test]
        public void C1cContent_OffKeepsTheDigest()
        {
            ulong five = Digest(", \"respawnTicks\": 5");
            Assert.That(Digest(", \"respawnTicks\": 5, \"respawnGuardTicks\": 0"), Is.EqualTo(five));
            Assert.That(Direct(5).PolicyDigest, Is.EqualTo(five));
            Assert.That(OldDirect(5).PolicyDigest, Is.EqualTo(five));
            Assert.That(Digest(", \"respawnGuardTicks\": 0"), Is.EqualTo(RespawnDigestLockTests.Digest));
            Assert.That(Load(", \"respawnTicks\": 5").Match.RespawnGuardTicks, Is.EqualTo(0));
        }

        [Test]
        public void C1cContent_PositiveGuardDigestsAreRecorded_AndDependOnTheValue()
        {
            ulong g90 = Digest(", \"respawnTicks\": 5, \"respawnGuardTicks\": 90"), g91 = Digest(", \"respawnTicks\": 5, \"respawnGuardTicks\": 91");
            TestContext.Out.WriteLine($"guard 90 {g90}UL guard 91 {g91}UL");
            Assert.That(Load(", \"respawnTicks\": 5, \"respawnGuardTicks\": 90").Match.RespawnGuardTicks, Is.EqualTo(90));
            Assert.That(g90, Is.EqualTo(Guard90));
            Assert.That(g91, Is.EqualTo(Guard91));
            Assert.That(Direct(5, 90).PolicyDigest, Is.EqualTo(Guard90));
            Assert.That(g90, Is.Not.EqualTo(Digest(", \"respawnTicks\": 5")));
        }

        [TestCase("-1")]
        [TestCase("36001")]
        [TestCase("\"x\"")]
        public void C1cContent_BadGuardIsAContentError(string value) =>
            Assert.Throws<ContentException>(() => Load(", \"respawnTicks\": 5, \"respawnGuardTicks\": " + value));

        [Test]
        public void C1cContent_GuardWithoutRespawnIsAContentError()
        {
            var e = Assert.Throws<ContentException>(() => Load(", \"respawnGuardTicks\": 90").BuildMatch(Points(), Bases()));
            StringAssert.Contains("respawnGuardTicks", e.Message);
        }

        [Test]
        public void C1cContent_RangeEndsLoad()
        {
            Assert.That(Load(", \"respawnTicks\": 5, \"respawnGuardTicks\": 36000").BuildMatch(Points(), Bases()).RespawnGuardTicks, Is.EqualTo(36000));
            Assert.That(Load(", \"respawnTicks\": 5, \"respawnGuardTicks\": 0").BuildMatch(Points(), Bases()).RespawnGuardTicks, Is.EqualTo(0));
        }

        [TestCase(5, -1)]
        [TestCase(5, 36001)]
        [TestCase(0, 90)]
        public void C1cContent_ConstructorRejects(int respawn, int guard)
        {
            var e = Assert.Throws<ArgumentOutOfRangeException>(() => Direct(respawn, guard));
            Assert.That(e.ParamName, Is.EqualTo("respawnGuardTicks"));
        }

        [Test]
        public void C1cRecipe_TheRespawnDemoHasA90TickGuard()
        {
            WorldRecipe demo = DemoRecipes.MpRespawnDemo();
            Assert.That(demo.Match.RespawnTicks, Is.EqualTo(300));
            Assert.That(demo.Match.RespawnGuardTicks, Is.EqualTo(90));
            Assert.That(DemoRecipes.MpBlockDemo().Match.RespawnGuardTicks, Is.EqualTo(0));
        }

        private static MatchPack Odd(int guard) => new MatchPack(new[]
            {
                new ControlPointDef(4, SimVec3.Metres(30, 0, 20), SimFix.FromInt(3), 1, true, SimVec3.Metres(31, 0, 21)),
                new ControlPointDef(7, SimVec3.Metres(60, 0, 10), SimFix.FromInt(5), 2),
            }, new[] { new SpawnAnchor(1, SimVec3.Metres(5, 0, 38)), new SpawnAnchor(2, SimVec3.Metres(75, 0, 2)) },
            3, 2, true, 450, InterruptMode.Decay, 3, 900, 17, 12345, SimFix.FromInt(41), 3, 2, SimFix.FromInt(3), 7, guard);

        private static WorldRecipe OddRecipe(int guard) => new WorldRecipe(41, MoverKind.InterimCrowd, RespawnWorld.Units().ToArray(),
            Array.Empty<BridgeCommand>()).WithMatch(Odd(guard));

        private static void SameRest(MatchPack a, MatchPack b)
        {
            Assert.That(b.Points.ToArray(), Is.EqualTo(a.Points.ToArray()));
            Assert.That(b.Bases.ToArray(), Is.EqualTo(a.Bases.ToArray()));
            Assert.That(new object[] { b.Num, b.Den, b.Strict, b.CaptureTicks, b.Interrupt, b.DecayPerTick, b.IntervalTicks, b.ThinkInterval,
                    b.MaxTicks, b.Sight, b.GridColumns, b.GridRows, b.GridSpacing },
                Is.EqualTo(new object[] { a.Num, a.Den, a.Strict, a.CaptureTicks, a.Interrupt, a.DecayPerTick, a.IntervalTicks, a.ThinkInterval,
                    a.MaxTicks, a.Sight, a.GridColumns, a.GridRows, a.GridSpacing }));
        }

        [Test]
        public void C1cRecipe_WithRespawnKeepsTheGuard()
        {
            MatchPack copied = DemoRecipes.WithRespawn(OddRecipe(13), 90).Match;
            SameRest(Odd(13), copied);
            Assert.That(copied.RespawnTicks, Is.EqualTo(90));
            Assert.That(copied.RespawnGuardTicks, Is.EqualTo(13));
        }

        [Test]
        public void C1cRecipe_WithRespawnGuardKeepsEverythingElse()
        {
            MatchPack copied = DemoRecipes.WithRespawnGuard(OddRecipe(0), 25).Match;
            SameRest(Odd(0), copied);
            Assert.That(copied.RespawnTicks, Is.EqualTo(7));
            Assert.That(copied.RespawnGuardTicks, Is.EqualTo(25));
            Assert.That(copied.PolicyDigest, Is.EqualTo(Odd(25).PolicyDigest));
        }

        [Test]
        public void C1cRecipe_WithRespawnGuardWithoutMatchRules_Throws()
        {
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, RespawnWorld.Units().ToArray(), Array.Empty<BridgeCommand>());
            Assert.Throws<InvalidOperationException>(() => DemoRecipes.WithRespawnGuard(recipe, 90));
        }
    }
}
