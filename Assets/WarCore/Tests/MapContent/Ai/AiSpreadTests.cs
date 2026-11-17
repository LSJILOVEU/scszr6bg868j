using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-SPLIT-2: content ai.attackSpreadPoints spreads attackers over the k-th nearest points their team does not own.
    public sealed class AiSpreadTests
    {
        private const int Units = 100;

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["spread-test"] = "{ \"id\": \"spread-test\", \"extends\": \"rwr\"" + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), "spread-test");

        private static ControlPointDef Point(int id, int x, int owner) => new ControlPointDef(id, SimVec3.Metres(x, 0, 20), SimFix.FromInt(6), owner);

        // Unweaponed team-1 units; every unit thinks every tick. column: all units on x = 40 (z 26, 28, ...), else a 20 x 5 grid.
        private static M6World World(string ai, bool column, params ControlPointDef[] points)
        {
            var units = new UnitSpawn[Units];
            for (int i = 0; i < Units; i++)
                units[i] = column ? M6World.U((ulong)i + 1, 1, 40, 26 + i * 2) : M6World.U((ulong)i + 1, 1, 21 + i % 20 * 2, 30 + i / 20 * 2);
            WorldRecipe recipe = M6World.RecipeOf(units, M6World.Pack(points, think: 1)).WithRuleSet(MatchMode.TestNeverEndsId);
            if (ai != null) recipe = recipe.WithAiRules(Pack(ai));
            return new M6World(recipe, WcMapWriter.Write(Wp23.Make(160, column ? 240 : 40, 0, 4 * SimFix.Scale,
                Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null)));
        }

        // The split written out independently of the system: splitmix64 finalizer of (key ^ salt).
        private static ulong Finalize(ulong x)
        {
            unchecked
            {
                x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
                x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
                return x ^ (x >> 31);
            }
        }
        private static ulong Key(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key)).Value;
        private static int Rank(M6World w, ulong key, int spread, int notOwned) => (int)(Finalize(Key(w, key) ^ 0x3c6ef372fe94f82bUL) % (ulong)Math.Min(spread, notOwned));
        private static bool Holder(M6World w, ulong key, int permille) => Finalize(Key(w, key) ^ 0x5a17c0de0b5e55edUL) % 1000UL < (ulong)permille;

        private static bool Inside(SimVec3 at, ControlPointDef p) =>
            CombatSystem.Horizontal(at - p.Center).LengthSquared <= new SimVec3(p.Radius, default, default).LengthSquared;

        // Team 1 owns index 0. Seen from every unit (all west of x = 60) the points it does not own rank by distance as 2, 3, 1,
        // which is neither the index order nor its reverse.
        private static ControlPointDef[] Four() => new[] { Point(1, 5, 1), Point(2, 150, 2), Point(3, 80, 0), Point(4, 115, 2) };
        private static readonly int[] ByDistance = { 2, 3, 1 };

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(8)]
        public void Spread_AttackersTakeTheKthNearestPoint(int spread)
        {
            ControlPointDef[] points = Four();
            M6World w = World("{ \"attackSpreadPoints\": " + spread + ", \"holdSharePermille\": 0 }", false, points);
            w.Step();
            var expected = new int[points.Length];
            var actual = new int[points.Length];
            for (ulong key = 1; key <= Units; key++)
            {
                int want = ByDistance[Rank(w, key, spread, 3)];
                expected[want]++;
                MoveIntent intent = w.Read<MoveIntent>(key);
                Assert.That(intent.Active, Is.True, "unit " + key);
                for (int i = 0; i < points.Length; i++) if (Inside(intent.Target, points[i])) actual[i]++;
                Assert.That(Inside(intent.Target, points[want]), Is.True, "unit " + key);
            }
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(expected[0], Is.EqualTo(0));
            for (int k = 0; k < Math.Min(spread, 3); k++) Assert.That(expected[ByDistance[k]], Is.GreaterThan(0), "rank " + k);
            if (spread == 2) Assert.That(expected[1], Is.EqualTo(0));
            TestContext.Out.WriteLine("spread " + spread + ": per point " + string.Join(", ", expected));
        }

        // Every unit stands on x = 40, exactly between the two enemy points: rank 0 takes the lower index, rank 1 the other.
        [Test]
        public void Spread_EqualDistancesGoToTheLowerIndex()
        {
            ControlPointDef[] points = { Point(1, 120, 1), Point(2, 60, 2), Point(3, 20, 2) };
            M6World w = World("{ \"attackSpreadPoints\": 2 }", true, points);
            w.Step();
            int[] count = new int[2];
            for (ulong key = 1; key <= Units; key++)
            {
                int rank = Rank(w, key, 2, 2);
                count[rank]++;
                MoveIntent intent = w.Read<MoveIntent>(key);
                Assert.That(intent.Active, Is.True, "unit " + key);
                Assert.That(Inside(intent.Target, points[rank == 0 ? 1 : 2]), Is.True, "unit " + key);
            }
            Assert.That(count[0], Is.GreaterThan(0));
            Assert.That(count[1], Is.GreaterThan(0));
        }

        // With the hold share on, holders go to the team's front line (its only owned point) and attackers spread as above.
        [Test]
        public void Spread_WithHoldShare()
        {
            ControlPointDef[] points = Four();
            M6World w = World("{ \"attackSpreadPoints\": 3, \"holdSharePermille\": 300 }", false, points);
            w.Step();
            int holders = 0, attackers = 0;
            var expected = new int[points.Length];
            var actual = new int[points.Length];
            for (ulong key = 1; key <= Units; key++)
            {
                int want = Holder(w, key, 300) ? 0 : ByDistance[Rank(w, key, 3, 3)];
                if (want == 0) holders++; else attackers++;
                expected[want]++;
                MoveIntent intent = w.Read<MoveIntent>(key);
                Assert.That(intent.Active, Is.True, "unit " + key);
                for (int i = 0; i < points.Length; i++) if (Inside(intent.Target, points[i])) actual[i]++;
                Assert.That(Inside(intent.Target, points[want]), Is.True, "unit " + key);
            }
            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(holders, Is.GreaterThan(0));
            Assert.That(attackers, Is.GreaterThan(0));
            TestContext.Out.WriteLine("hold 300 + spread 3: per point " + string.Join(", ", expected));
        }

        // The team owns every point: no attack target, same as without the option (nobody moves).
        [Test]
        public void Spread_AllOwnedMeansNoOrders()
        {
            ControlPointDef[] points = { Point(1, 15, 1), Point(2, 40, 1), Point(3, 65, 1) };
            M6World w = World("{ \"attackSpreadPoints\": 3 }", false, points);
            w.Step();
            for (ulong key = 1; key <= Units; key++) Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "unit " + key);
        }

        private static M6World Battle(ulong seed, bool? fair, string ai) =>
            new M6World(FairEFixture.Recipe("control", seed, fair).WithAiRules(Pack(ai)), FairEFixture.Map("control"));

        [TestCase(null, "{ \"attackSpreadPoints\": 3 }")]
        [TestCase(false, "{ \"attackSpreadPoints\": 3 }")]
        [TestCase(true, "{ \"attackSpreadPoints\": 3 }")]
        [TestCase(false, "{ \"attackSpreadPoints\": 3, \"holdSharePermille\": 300 }")]
        [TestCase(true, "{ \"attackSpreadPoints\": 3, \"holdSharePermille\": 300 }")]
        public void Spread_RepeatAndRestoreEveryTick(bool? fair, string ai)
        {
            M6World first = Battle(41, fair, ai), repeat = Battle(41, fair, ai);
            Assert.That(repeat.Hash, Is.EqualTo(first.Hash));
            for (int tick = 0; tick < 600; tick++)
            {
                first.Step(); repeat.Step();
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();
            M6World restored = Battle(41, fair, ai);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            for (int tick = 0; tick < 600 && !first.Sim.IsFinished; tick++)
            {
                first.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
        }

        // The option changes the outcome against the same world without it (the spread is live in the battle).
        [Test]
        public void Spread_ChangesTheBattle()
        {
            M6World on = Battle(41, null, "{ \"attackSpreadPoints\": 3 }"), off = new M6World(FairEFixture.Recipe("control", 41, null), FairEFixture.Map("control"));
            on.Step(600); off.Step(600);
            bool differs = false;
            for (ulong key = 1; key <= 300 && !differs; key++)
                differs = !on.Read<Position>(key).Value.Equals(off.Read<Position>(key).Value);
            Assert.That(differs, Is.True);
        }

        // The digest a battle has without the spread, written out from the match pack, FAIR-E and the hold share.
        private static ulong Unspread(bool fair, int hold)
        {
            WorldRecipe recipe = FairEFixture.Recipe("control", 41, fair);
            ulong digest = recipe.Match.PolicyDigest;
            if (fair) { var h = new StateHash(); h.Add("battle.ai.fair-decisions.v1"); h.Add(digest); h.Add(true); h.Add(recipe.Seed); digest = h.Value; }
            if (hold > 0) { var h = new StateHash(); h.Add("battle.ai.hold-share.v1"); h.Add(digest); h.Add(hold); digest = h.Value; }
            return digest;
        }

        [TestCase(false, 0)]
        [TestCase(true, 0)]
        [TestCase(false, 300)]
        [TestCase(true, 300)]
        public void Spread_PolicyDigestAndSnapshots(bool fair, int hold)
        {
            M6World three = Battle(41, fair, "{ \"attackSpreadPoints\": 3, \"holdSharePermille\": " + hold + " }");
            M6World one = Battle(41, fair, "{ \"attackSpreadPoints\": 1, \"holdSharePermille\": " + hold + " }");
            SimpleAiSystem a = three.System<SimpleAiSystem>(), b = one.System<SimpleAiSystem>();
            Assert.That(a.StateVersion, Is.EqualTo(b.StateVersion));
            var wa = new SimStateWriter(); a.SaveState(wa);
            var wb = new SimStateWriter(); b.SaveState(wb);
            ulong digestA = new SimStateReader(wa.ToArray()).UInt64(), digestB = new SimStateReader(wb.ToArray()).UInt64();
            Assert.That(digestB, Is.EqualTo(Unspread(fair, hold)));
            var expected = new StateHash(); expected.Add("battle.ai.attack-spread.v1"); expected.Add(digestB); expected.Add(3);
            Assert.That(digestA, Is.EqualTo(expected.Value));
            Assert.That(digestA, Is.Not.EqualTo(digestB));
            var e1 = Assert.Throws<InvalidOperationException>(() => b.LoadState(new SimStateReader(wa.ToArray())));
            Assert.That(e1.Message, Is.EqualTo("Battle configuration differs from snapshot."));
            var e2 = Assert.Throws<InvalidOperationException>(() => a.LoadState(new SimStateReader(wb.ToArray())));
            Assert.That(e2.Message, Is.EqualTo("Battle configuration differs from snapshot."));
            three.Step(); one.Step();
            byte[] fromThree = three.Sim.SaveRetainedSnapshot(), fromOne = one.Sim.SaveRetainedSnapshot();
            Assert.Throws<InvalidOperationException>(() => one.Sim.RestoreRetainedSnapshot(fromThree));
            Assert.Throws<InvalidOperationException>(() => three.Sim.RestoreRetainedSnapshot(fromOne));
        }

        // One, absent and no ai section all leave the battle exactly as it was before the option existed.
        [TestCase(null)]
        [TestCase("{}")]
        [TestCase("{ \"attackSpreadPoints\": 1 }")]
        public void Spread_DefaultLeavesTheBattle(string ai)
        {
            WorldRecipe recipe = FairEFixture.Recipe("control", 41, null);
            M6World with = new M6World(ai == null ? recipe : recipe.WithAiRules(Pack(ai)), FairEFixture.Map("control"));
            M6World plain = new M6World(recipe, FairEFixture.Map("control"));
            Assert.That(with.Hash, Is.EqualTo(plain.Hash));
            for (int tick = 0; tick < 300; tick++)
            {
                with.Step(); plain.Step();
                Assert.That(with.Hash, Is.EqualTo(plain.Hash), "tick " + plain.Sim.Clock.Tick);
            }
            Assert.That(Pack(ai).Section<AiRules>().AttackSpreadPoints, Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(9)]
        public void Spread_OutOfRangeNamesPackEntryAndField(int value)
        {
            var e = Assert.Throws<ContentException>(() => Pack("{ \"attackSpreadPoints\": " + value + " }"));
            Assert.That(e.Message, Does.Contain("content pack 'spread-test'"));
            Assert.That(e.Message, Does.Contain("ai"));
            Assert.That(e.Message, Does.Contain("field 'attackSpreadPoints'"));
            Assert.That(e.Message, Does.Contain("out of range [1, 8]"));
        }

        [TestCase(1)]
        [TestCase(8)]
        public void Spread_InRangeIsRead(int value)
        {
            Assert.That(Pack("{ \"attackSpreadPoints\": " + value + " }").Section<AiRules>().AttackSpreadPoints, Is.EqualTo(value));
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // The spread choice runs on every think of every attacker; it must not allocate.
        [Test]
        public void Spread_StepAllocatesZero()
        {
            M6World warm = World("{ \"attackSpreadPoints\": 8 }", false, Four());
            warm.Step(30);
            M6World w = World("{ \"attackSpreadPoints\": 8 }", false, Four());
            w.Step(30);
            AllocationMeter.Stabilize();
            long total = 0;
            for (int i = 0; i < 30; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(default);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Assert.That(total, Is.EqualTo(0L));
        }
#endif
    }
}
