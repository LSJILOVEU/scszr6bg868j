using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 1(b) and 11 (the ai field) and 12 (assembly).
    public sealed class A3MemoryContentTests
    {
        [Test]
        public void ValueAndDefaults()
        {
            Assert.That(A3.Pack("", 300).Section<AiRules>().TargetMemoryTicks, Is.EqualTo(300));
            Assert.That(A3.Pack("", 1000000).Section<AiRules>().TargetMemoryTicks, Is.EqualTo(1000000));
            Assert.That(A3.Pack("", null).Section<AiRules>().TargetMemoryTicks, Is.EqualTo(0), "section left out");
            Assert.That(A3.Load(A3.PackJson("", "{ }")).Section<AiRules>().TargetMemoryTicks, Is.EqualTo(0), "field left out");
            // 1(b): the other field is read as before, with or without the new one.
            AiRules failedOnly = A3.Load(A3.PackJson("", "{ \"failedSlotTicks\": 7 }")).Section<AiRules>();
            Assert.That(failedOnly.FailedSlotTicks, Is.EqualTo(7)); Assert.That(failedOnly.TargetMemoryTicks, Is.EqualTo(0));
            AiRules both = A3.Load(A3.PackJson("", "{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 9 }")).Section<AiRules>();
            Assert.That(both.FailedSlotTicks, Is.EqualTo(7)); Assert.That(both.TargetMemoryTicks, Is.EqualTo(9));
            Assert.That(A3.Pack("", 300).Section<AiRules>().FailedSlotTicks, Is.EqualTo(0));
        }

        [TestCase("{ \"targetMemoryTicks\": -1 }", "content pack 'a2-test' layer 'a2-test' ai field 'targetMemoryTicks': out of range [0, 1000000]")]
        [TestCase("{ \"targetMemoryTicks\": 1000001 }", "content pack 'a2-test' layer 'a2-test' ai field 'targetMemoryTicks': out of range [0, 1000000]")]
        [TestCase("{ \"targetMemoryTicks\": 1.5 }", "content pack 'a2-test' layer 'a2-test' ai field 'targetMemoryTicks': must be an integer")]
        [TestCase("{ \"targetMemoryTicks\": \"5\" }", "content pack 'a2-test' layer 'a2-test' ai field 'targetMemoryTicks': must be an integer")]
        public void Errors(string ai, string message)
        {
            var e = Assert.Throws<ContentException>(() => A3.Load(A3.PackJson("", ai)));
            Assert.That(e.Message, Is.EqualTo(message));
        }

        private static UnitSpawn[] Units() => new[] { A3.Observer(), A3.Target(A3.KeyB, 2, A2.Cell(21, 18)) };
        private static M6World On(WcMap map) => new M6World(A3.Recipe(Units(), A3.Pack(A3.Grass500, 60)), WcMapWriter.Write(map));

        // 12: a positive length in a terrain world adds exactly one battle.ai-contacts (Requests, Order 13) right after the AI.
        [Test]
        public void Assembly_TerrainWorld_Registers()
        {
            M6World w = A2.D(A3.Pack("", 60));
            string expected = A3MemoryBaselineTests.Systems.Replace("battle.ai|0|12;", "battle.ai|0|12;battle.ai-contacts|0|13;");
            Assert.That(L4.Systems(w), Is.EqualTo(expected));
            Assert.That(w.System<TargetMemory>(), Is.Not.Null);
        }

        // 12: neither a surface table nor a height field: not a terrain world, nothing registered.
        [Test]
        public void Assembly_FlatMap_DoesNotRegister()
        {
            M6World w = On(Wp23.Make(A2.Width, A2.Depth, 0, 4 * A2.S, A2.DWalls, 2 * A2.S));
            Assert.That(L4.Systems(w), Does.Not.Contain("battle.ai-contacts"));
            Assert.That(w.System<TargetMemory>(), Is.Null);
        }

        // 12: a height field without a surface table is a terrain world (WcMap.HasTerrain): registered.
        [Test]
        public void Assembly_HeightFieldOnly_Registers()
        {
            M6World w = On(Wp23.Make(A2.Width, A2.Depth, 0, 4 * A2.S, A2.DWalls, 2 * A2.S, (x, z) => 0));
            Assert.That(L4.Systems(w), Does.Contain("battle.ai|0|12;battle.ai-contacts|0|13;"));
            Assert.That(w.System<TargetMemory>(), Is.Not.Null);
        }

        // The memory's own argument checks.
        [Test]
        public void Constructor_RefusesNonPositiveLength()
        {
            M6World w = A2.D(A3.Pack("", null));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new TargetMemory(w.Combat, 0)).ParamName, Is.EqualTo("ticks"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new TargetMemory(null, 1)).ParamName, Is.EqualTo("combat"));
        }
    }
}
