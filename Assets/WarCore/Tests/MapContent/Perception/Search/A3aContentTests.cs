using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 9 (the ai field) and 10 (assembly).
    public sealed class A3aContentTests
    {
        private static AiRules Rules(string ai) => A3a.Load(A3a.PackJson(ai)).Section<AiRules>();

        [Test]
        public void ValueAndDefaults()
        {
            Assert.That(Rules("{ \"searchTicks\": 60 }").SearchTicks, Is.EqualTo(60));
            Assert.That(Rules("{ \"searchTicks\": 1000000 }").SearchTicks, Is.EqualTo(1000000));
            Assert.That(Rules(null).SearchTicks, Is.EqualTo(0), "section left out");
            Assert.That(Rules("{ }").SearchTicks, Is.EqualTo(0), "field left out");
            AiRules all = Rules("{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 9, \"searchTicks\": 11 }");
            Assert.That(new[] { all.FailedSlotTicks, all.TargetMemoryTicks, all.SearchTicks }, Is.EqualTo(new[] { 7, 9, 11 }));
            AiRules old = Rules("{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 9 }");
            Assert.That(new[] { old.FailedSlotTicks, old.TargetMemoryTicks, old.SearchTicks }, Is.EqualTo(new[] { 7, 9, 0 }));
        }

        [TestCase("{ \"searchTicks\": -1 }", "content pack 'a3a-test' layer 'a3a-test' ai field 'searchTicks': out of range [0, 1000000]")]
        [TestCase("{ \"searchTicks\": 1000001 }", "content pack 'a3a-test' layer 'a3a-test' ai field 'searchTicks': out of range [0, 1000000]")]
        [TestCase("{ \"searchTicks\": 1.5 }", "content pack 'a3a-test' layer 'a3a-test' ai field 'searchTicks': must be an integer")]
        [TestCase("{ \"searchTicks\": \"5\" }", "content pack 'a3a-test' layer 'a3a-test' ai field 'searchTicks': must be an integer")]
        public void Errors(string ai, string message)
        {
            var e = Assert.Throws<ContentException>(() => A3a.Load(A3a.PackJson(ai)));
            Assert.That(e.Message, Is.EqualTo(message));
        }

        private static M6World On(WcMap map, string ai) =>
            new M6World(A3a.Recipe(new[] { A3a.Observer(), A3a.Target(A3a.BStart) }, A3a.Load(A3a.PackJson(ai))), WcMapWriter.Write(map));

        // 10: memory and search on in a terrain world: exactly one battle.ai-search (Requests, Order 17).
        [Test]
        public void Assembly_TerrainWorld_Registers()
        {
            M6World w = A3a.World(A3a.BStart, A3a.Ai(60, 60));
            string expected = A3aBaselineTests.Systems.Replace("battle.ai-contacts|0|13;", "battle.ai-contacts|0|13;battle.ai-search|0|17;");
            Assert.That(L4.Systems(w), Is.EqualTo(expected));
            Assert.That(w.System<TargetSearch>(), Is.Not.Null);
        }

        // 10: a search without the memory adds nothing.
        [Test]
        public void Assembly_NoMemory_DoesNotRegister()
        {
            M6World w = A3a.World(A3a.BStart, "{ \"searchTicks\": 60 }");
            Assert.That(L4.Systems(w), Does.Not.Contain("battle.ai-search"));
            Assert.That(L4.Systems(w), Does.Not.Contain("battle.ai-contacts"));
            Assert.That(w.System<TargetSearch>(), Is.Null);
        }

        // 10: a flat map (no surface table, no height field): nothing registered.
        [Test]
        public void Assembly_FlatMap_DoesNotRegister()
        {
            M6World w = On(Wp23.Make(A2.Width, A2.Depth, 0, 4 * A2.S, A2.DWalls, 2 * A2.S), A3a.Ai(60, 60));
            Assert.That(L4.Systems(w), Does.Not.Contain("battle.ai-search"));
            Assert.That(L4.Systems(w), Does.Not.Contain("battle.ai-contacts"));
            Assert.That(w.System<TargetSearch>(), Is.Null);
            Assert.That(w.System<TargetMemory>(), Is.Null);
        }

        // 10: a height field without a surface table is a terrain world: registered.
        [Test]
        public void Assembly_HeightFieldOnly_Registers()
        {
            M6World w = On(Wp23.Make(A2.Width, A2.Depth, 0, 4 * A2.S, A2.DWalls, 2 * A2.S, (x, z) => 0), A3a.Ai(60, 60));
            Assert.That(L4.Systems(w), Does.Contain("battle.ai-contacts|0|13;battle.ai-search|0|17;"));
            Assert.That(w.System<TargetSearch>(), Is.Not.Null);
        }

        [Test]
        public void Constructor_RefusesBadArguments()
        {
            M6World w = A3a.World(A3a.BStart, A3a.Ai(60));
            TargetMemory memory = w.System<TargetMemory>();
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new TargetSearch(w.Sim.Entities, memory, 0)).ParamName, Is.EqualTo("ticks"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new TargetSearch(null, memory, 1)).ParamName, Is.EqualTo("entities"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new TargetSearch(w.Sim.Entities, null, 1)).ParamName, Is.EqualTo("contacts"));
        }
    }
}
