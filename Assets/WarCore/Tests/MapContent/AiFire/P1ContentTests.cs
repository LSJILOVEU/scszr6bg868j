using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 7 (the ai field) and 8 (assembly, the extension hooks, the constructor).
    public sealed class P1ContentTests
    {
        private static AiRules Rules(string ai) => A3.Load(P1.PackJson(ai)).Section<AiRules>();

        [Test]
        public void ValueAndDefaults()
        {
            Assert.That(Rules("{ \"reactionTicks\": 60 }").ReactionTicks, Is.EqualTo(60));
            Assert.That(Rules("{ \"reactionTicks\": 1000000 }").ReactionTicks, Is.EqualTo(1000000));
            Assert.That(Rules(null).ReactionTicks, Is.EqualTo(0), "section left out");
            Assert.That(Rules("{ }").ReactionTicks, Is.EqualTo(0), "field left out");
            AiRules all = Rules("{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 9, \"searchTicks\": 11, \"reactionTicks\": 13 }");
            Assert.That(new[] { all.FailedSlotTicks, all.TargetMemoryTicks, all.SearchTicks, all.ReactionTicks }, Is.EqualTo(new[] { 7, 9, 11, 13 }));
            AiRules old = Rules("{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 9, \"searchTicks\": 11 }");
            Assert.That(new[] { old.FailedSlotTicks, old.TargetMemoryTicks, old.SearchTicks, old.ReactionTicks }, Is.EqualTo(new[] { 7, 9, 11, 0 }));
        }

        [TestCase("{ \"reactionTicks\": -1 }", "content pack 'a2-test' layer 'a2-test' ai field 'reactionTicks': out of range [0, 1000000]")]
        [TestCase("{ \"reactionTicks\": 1000001 }", "content pack 'a2-test' layer 'a2-test' ai field 'reactionTicks': out of range [0, 1000000]")]
        [TestCase("{ \"reactionTicks\": 1.5 }", "content pack 'a2-test' layer 'a2-test' ai field 'reactionTicks': must be an integer")]
        [TestCase("{ \"reactionTicks\": \"5\" }", "content pack 'a2-test' layer 'a2-test' ai field 'reactionTicks': must be an integer")]
        public void Errors(string ai, string message)
        {
            var e = Assert.Throws<ContentException>(() => A3.Load(P1.PackJson(ai)));
            Assert.That(e.Message, Is.EqualTo(message));
        }

        // 8: on in a terrain world (only the reaction time set): exactly one battle.ai-fire (Requests, Order 14), and the
        // registered object is the one combat calls (the reaction time shows: the first shot on t + 15).
        [Test]
        public void Assembly_TerrainWorld_Registers()
        {
            P1.Trace r = P1.Run(P1.Cast(), P1.Walk, P1.Ai(15), P1.T + 20);
            string expected = P1BaselineTests.Systems.Replace("battle.ai|0|12;", "battle.ai|0|12;battle.ai-fire|0|14;");
            Assert.That(P1.Systems(r.W), Is.EqualTo(expected));
            Assert.That(r.W.System<ReactionFire>(), Is.Not.Null);
            Assert.That(r.FirstFire(P1.KeyA), Is.EqualTo(P1.T + 15));
        }

        // A decision that never fires, installed by a test extension in RegisterSystems or in RegisterRules.
        private sealed class Silent : IAutoFire
        {
            public void Sense(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position, long tick) { }
            public AutoFireAction TryAim(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position,
                in WeaponDef weapon, long tick, out SimVec3 direction) { direction = default; return AutoFireAction.Pass; }
        }
        private sealed class EarlySilence : WorldFeature
        {
            public override string OptionId => "test.p1-early-silence";
            public override void RegisterSystems(WorldAssembly a) => a.Combat.UseAutoFire(new Silent());
        }
        private sealed class LateSilence : WorldStageFeature
        {
            public override string OptionId => "test.p1-late-silence";
            public override void RegisterRules(WorldAssembly a) => a.Combat.UseAutoFire(new Silent());
        }

        // 8: a decision an extension installs before RegisterRules is replaced by the reaction time.
        [Test]
        public void Extension_EarlierHook_IsReplaced()
        {
            P1.Trace r = P1.Run(P1.Cast(), P1.Walk, P1.Ai(15), P1.T + 20, false, new EarlySilence());
            Assert.That(r.FirstFire(P1.KeyA), Is.EqualTo(P1.T + 15));
        }

        // 8: one installed in RegisterRules (extensions run after the built-in features) replaces it; the system stays.
        [Test]
        public void Extension_RegisterRules_Replaces()
        {
            P1.Trace r = P1.Run(P1.Cast(), P1.Walk, P1.Ai(15), 600, false, new LateSilence());
            Assert.That(r.FirstSelect(P1.KeyA, P1.KeyB), Is.EqualTo(P1.T));
            Assert.That(r.FirstFire(P1.KeyA), Is.EqualTo(-1L), "never fires in 600 ticks");
            Assert.That(P1.Systems(r.W), Does.Contain("battle.ai-fire|0|14;"));
        }

        // 8: a terrain world without combat: RegisterRules refuses with the dispatch's message (single-argument constructor).
        [Test]
        public void Assembly_WithoutCombat_Throws()
        {
            var a = new WorldAssembly(P1.Recipe(P1.Cast(), P1.Ai(15)), WcMapWriter.Write(A2.Map(A2.DWalls)), false);
            Assert.That(a.Combat, Is.Null);
            var e = Assert.Throws<ArgumentException>(() => AiFireFeature.Instance.RegisterRules(a));
            Assert.That(e.Message, Is.EqualTo("The AI reaction time needs combat."));
            Assert.That(e.ParamName, Is.Null);
        }

        // 8: the constructor's checks; 0 is legal (the installer decides whether to register).
        [Test]
        public void Constructor_Checks()
        {
            M6World w = P1.World(P1.Cast(), null);
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, -1)).ParamName, Is.EqualTo("ticks"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 1000001)).ParamName, Is.EqualTo("ticks"));
            Assert.DoesNotThrow(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0));
            Assert.DoesNotThrow(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 1000000));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new ReactionFire(null, w.Combat.BuiltInAutoFire, 1)).ParamName, Is.EqualTo("combat"));
            Assert.That(Assert.Throws<ArgumentNullException>(() => new ReactionFire(w.Combat, null, 1)).ParamName, Is.EqualTo("inner"));
        }
    }
}
