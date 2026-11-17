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
    // AI-SPLIT-1: content ai.holdSharePermille splits each team into holders (front-line owned point) and attackers.
    public sealed class AiSplitTests
    {
        private const int Units = 100;

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["split-test"] = "{ \"id\": \"split-test\", \"extends\": \"rwr\"" + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), "split-test");

        private static ControlPointDef Point(int id, int x, int owner) => new ControlPointDef(id, SimVec3.Metres(x, 0, 20), SimFix.FromInt(6), owner);

        // Unweaponed team-1 units on a 2 m grid north of the points; every unit thinks every tick.
        private static M6World World(int permille, params ControlPointDef[] points)
        {
            var units = new UnitSpawn[Units];
            for (int i = 0; i < Units; i++) units[i] = M6World.U((ulong)i + 1, 1, 21 + i % 20 * 2, 30 + i / 20 * 2);
            WorldRecipe recipe = M6World.RecipeOf(units, M6World.Pack(points, think: 1)).WithRuleSet(MatchMode.TestNeverEndsId);
            if (permille >= 0) recipe = recipe.WithAiRules(Pack("{ \"holdSharePermille\": " + permille + " }"));
            return new M6World(recipe, WcMapWriter.Write(Wp23.Make(80, 40, 0, 4 * SimFix.Scale,
                Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null)));
        }

        // The split written out independently of the system: splitmix64 finalizer of (key ^ salt), mod 1000.
        private static bool Holder(M6World w, ulong key, int permille)
        {
            ulong x = w.Sim.Entities.StableOf(w.Unit(key)).Value ^ 0x5a17c0de0b5e55edUL;
            unchecked
            {
                x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
                x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
                x ^= x >> 31;
            }
            return x % 1000UL < (ulong)permille;
        }

        private static bool Inside(SimVec3 at, ControlPointDef p) =>
            CombatSystem.Horizontal(at - p.Center).LengthSquared <= new SimVec3(p.Radius, default, default).LengthSquared;

        [Test]
        public void Split_HoldersGoHomeAttackersGoOut()
        {
            ControlPointDef own = Point(1, 15, 1), mid = Point(2, 40, 0), far = Point(3, 65, 2);
            M6World w = World(500, own, mid, far);
            w.Step();
            int holders = 0, formula = 0;
            for (ulong key = 1; key <= Units; key++)
            {
                bool holder = Holder(w, key, 500);
                if (holder) formula++;
                MoveIntent intent = w.Read<MoveIntent>(key);
                Assert.That(intent.Active, Is.True, "unit " + key);
                if (Inside(intent.Target, own)) holders++;
                Assert.That(Inside(intent.Target, own), Is.EqualTo(holder), "unit " + key);
                if (!holder) Assert.That(Inside(intent.Target, mid) || Inside(intent.Target, far), Is.True, "unit " + key);
            }
            Assert.That(formula, Is.EqualTo(50));
            Assert.That(holders, Is.EqualTo(formula));
        }

        // The front line is the owned point nearest to any point the team does not own; equal distances go to the lower index.
        [TestCase(10, 35, 65, 1, 1, 2, 1)]
        [TestCase(10, 40, 70, 1, 0, 1, 0)]
        [TestCase(65, 40, 10, 1, 1, 2, 1)]
        public void Split_FrontlineChoice(int xa, int xb, int xc, int oa, int ob, int oc, int front)
        {
            ControlPointDef[] points = { Point(1, xa, oa), Point(2, xb, ob), Point(3, xc, oc) };
            M6World w = World(1000, points);
            w.Step();
            for (ulong key = 1; key <= Units; key++)
            {
                MoveIntent intent = w.Read<MoveIntent>(key);
                Assert.That(intent.Active, Is.True, "unit " + key);
                Assert.That(Inside(intent.Target, points[front]), Is.True, "unit " + key);
            }
        }

        // No owned point: holders attack, so every order equals the order of a world without the option.
        [Test]
        public void Split_NoOwnedPointMeansNoHolders()
        {
            ControlPointDef[] points = { Point(1, 15, 0), Point(2, 40, 0), Point(3, 65, 2) };
            M6World on = World(500, points), off = World(-1, points);
            on.Step(); off.Step();
            for (ulong key = 1; key <= Units; key++)
            {
                MoveIntent a = on.Read<MoveIntent>(key), b = off.Read<MoveIntent>(key);
                Assert.That(a.Active, Is.True, "unit " + key);
                Assert.That(a.Target, Is.EqualTo(b.Target), "unit " + key);
            }
        }

        private static M6World Battle(ulong seed, bool? fair, int permille) =>
            new M6World(FairEFixture.Recipe("control", seed, fair).WithAiRules(Pack("{ \"holdSharePermille\": " + permille + " }")), FairEFixture.Map("control"));

        [TestCase(null)]
        [TestCase(false)]
        [TestCase(true)]
        public void Split_RepeatAndRestoreEveryTick(bool? fair)
        {
            M6World first = Battle(41, fair, 500), repeat = Battle(41, fair, 500);
            Assert.That(repeat.Hash, Is.EqualTo(first.Hash));
            for (int tick = 0; tick < 600; tick++)
            {
                first.Step(); repeat.Step();
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();
            M6World restored = Battle(41, fair, 500);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            for (int tick = 0; tick < 600 && !first.Sim.IsFinished; tick++)
            {
                first.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
        }

        // The option changes the outcome against the same world without it (the split is live in the battle).
        [Test]
        public void Split_ChangesTheBattle()
        {
            M6World on = Battle(41, null, 500), off = new M6World(FairEFixture.Recipe("control", 41, null), FairEFixture.Map("control"));
            on.Step(600); off.Step(600);
            bool differs = false;
            for (ulong key = 1; key <= 300 && !differs; key++)
                differs = !on.Read<Position>(key).Value.Equals(off.Read<Position>(key).Value);
            Assert.That(differs, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Split_PolicyDigestAndSnapshots(bool fair)
        {
            M6World full = Battle(41, fair, 1000), none = Battle(41, fair, 0);
            SimpleAiSystem a = full.System<SimpleAiSystem>(), b = none.System<SimpleAiSystem>();
            Assert.That(a.StateVersion, Is.EqualTo(b.StateVersion));
            var wa = new SimStateWriter(); a.SaveState(wa);
            var wb = new SimStateWriter(); b.SaveState(wb);
            ulong digestA = new SimStateReader(wa.ToArray()).UInt64(), digestB = new SimStateReader(wb.ToArray()).UInt64();
            var expected = new StateHash(); expected.Add("battle.ai.hold-share.v1"); expected.Add(digestB); expected.Add(1000);
            Assert.That(digestA, Is.EqualTo(expected.Value));
            Assert.That(digestA, Is.Not.EqualTo(digestB));
            var e1 = Assert.Throws<InvalidOperationException>(() => b.LoadState(new SimStateReader(wa.ToArray())));
            Assert.That(e1.Message, Is.EqualTo("Battle configuration differs from snapshot."));
            var e2 = Assert.Throws<InvalidOperationException>(() => a.LoadState(new SimStateReader(wb.ToArray())));
            Assert.That(e2.Message, Is.EqualTo("Battle configuration differs from snapshot."));
            full.Step(); none.Step();
            byte[] fromFull = full.Sim.SaveRetainedSnapshot(), fromNone = none.Sim.SaveRetainedSnapshot();
            Assert.Throws<InvalidOperationException>(() => none.Sim.RestoreRetainedSnapshot(fromFull));
            Assert.Throws<InvalidOperationException>(() => full.Sim.RestoreRetainedSnapshot(fromNone));
        }

        // Zero, absent and no ai section all leave the digest as it was before the option existed.
        [TestCase(null)]
        [TestCase("{}")]
        [TestCase("{ \"holdSharePermille\": 0 }")]
        public void Split_DefaultLeavesDigest(string ai)
        {
            WorldRecipe recipe = FairEFixture.Recipe("control", 41, null);
            M6World with = new M6World(ai == null ? recipe : recipe.WithAiRules(Pack(ai)), FairEFixture.Map("control"));
            M6World plain = new M6World(recipe, FairEFixture.Map("control"));
            Assert.That(with.Hash, Is.EqualTo(plain.Hash));
            with.Step(300); plain.Step(300);
            Assert.That(with.Hash, Is.EqualTo(plain.Hash));
            Assert.That(Pack(ai).Section<AiRules>().HoldSharePermille, Is.EqualTo(0));
        }

        [TestCase(-1)]
        [TestCase(1001)]
        public void Split_OutOfRangeNamesPackEntryAndField(int value)
        {
            var e = Assert.Throws<ContentException>(() => Pack("{ \"holdSharePermille\": " + value + " }"));
            Assert.That(e.Message, Does.Contain("content pack 'split-test'"));
            Assert.That(e.Message, Does.Contain("ai"));
            Assert.That(e.Message, Does.Contain("field 'holdSharePermille'"));
            Assert.That(e.Message, Does.Contain("out of range [0, 1000]"));
        }

        [TestCase(1)]
        [TestCase(1000)]
        public void Split_InRangeIsRead(int value)
        {
            Assert.That(Pack("{ \"holdSharePermille\": " + value + " }").Section<AiRules>().HoldSharePermille, Is.EqualTo(value));
        }
    }
}
