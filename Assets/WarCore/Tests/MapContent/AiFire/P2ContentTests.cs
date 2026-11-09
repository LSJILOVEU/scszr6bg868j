using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 7 (the ai field senseTicks) and 8 (assembly and the constructor's checks).
    public sealed class P2ContentTests
    {
        private static AiRules Rules(string ai) => A3.Load(P1.PackJson(ai)).Section<AiRules>();
        private const string Prefix = "content pack 'a2-test' layer 'a2-test' ai field 'senseTicks': ";

        [Test]
        public void ValueAndDefaults()
        {
            Assert.That(Rules("{ \"senseTicks\": 1 }").SenseTicks, Is.EqualTo(1));
            Assert.That(Rules("{ \"senseTicks\": 10 }").SenseTicks, Is.EqualTo(10));
            Assert.That(Rules("{ \"senseTicks\": 1000000 }").SenseTicks, Is.EqualTo(1000000));
            Assert.That(Rules(null).SenseTicks, Is.EqualTo(1), "section left out");
            Assert.That(Rules("{ }").SenseTicks, Is.EqualTo(1), "field left out");
            AiRules all = Rules("{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 9, \"searchTicks\": 11, \"reactionTicks\": 13, \"senseTicks\": 17 }");
            Assert.That(new[] { all.FailedSlotTicks, all.TargetMemoryTicks, all.SearchTicks, all.ReactionTicks, all.SenseTicks }, Is.EqualTo(new[] { 7, 9, 11, 13, 17 }));
            AiRules old = Rules("{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 9, \"searchTicks\": 11, \"reactionTicks\": 13 }");
            Assert.That(new[] { old.FailedSlotTicks, old.TargetMemoryTicks, old.SearchTicks, old.ReactionTicks, old.SenseTicks }, Is.EqualTo(new[] { 7, 9, 11, 13, 1 }));
        }

        [TestCase("{ \"senseTicks\": 0 }", Prefix + "out of range [1, 1000000]")]
        [TestCase("{ \"senseTicks\": -1 }", Prefix + "out of range [1, 1000000]")]
        [TestCase("{ \"senseTicks\": 1000001 }", Prefix + "out of range [1, 1000000]")]
        [TestCase("{ \"senseTicks\": 1.5 }", Prefix + "must be an integer")]
        [TestCase("{ \"senseTicks\": \"5\" }", Prefix + "must be an integer")]
        public void Errors(string ai, string message)
        {
            var e = Assert.Throws<ContentException>(() => A3.Load(P1.PackJson(ai)));
            Assert.That(e.Message, Is.EqualTo(message));
        }

        // 8: a terrain world with the reaction time 0 and the interval 10: exactly one battle.ai-fire (Requests, Order 14),
        // the registered object is version 2 and it is the decision combat calls (the first shot on A's first sample tick).
        [Test]
        public void Assembly_IntervalAlone_Registers()
        {
            P2.Trace r = P2.Run(P2.Cast(), P1.Walk, P2.Ai(0, 10), 60);
            Assert.That(P2.Systems(r.W), Is.EqualTo(P2BaselineTests.Systems));
            Assert.That(P2.Systems(r.W), Is.EqualTo(P1BaselineTests.Systems.Replace("battle.ai|0|12;", "battle.ai|0|12;battle.ai-fire|0|14;")));
            Assert.That(r.W.System<ReactionFire>(), Is.Not.Null);
            Assert.That(r.W.System<ReactionFire>().StateVersion, Is.EqualTo(2));
            Assert.That(r.A.FirstFire(), Is.EqualTo(P2TimingTests.TsA));
        }

        // 8: a terrain world without combat: RegisterRules refuses with P1's message.
        [Test]
        public void Assembly_WithoutCombat_Throws()
        {
            var a = new WorldAssembly(P2.Recipe(P2.Cast(), P2.Ai(0, 10)), WcMapWriter.Write(A2.Map(A2.DWalls)), false);
            Assert.That(a.Combat, Is.Null);
            var e = Assert.Throws<ArgumentException>(() => AiFireFeature.Instance.RegisterRules(a));
            Assert.That(e.Message, Is.EqualTo("The AI reaction time needs combat."));
            Assert.That(e.ParamName, Is.Null);
        }

        private sealed class Silent : IAutoFire
        {
            public void Sense(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position, long tick) { }
            public AutoFireAction TryAim(CombatSystem combat, EntityId shooter, in Combatant unit, in Position position,
                in WeaponDef weapon, long tick, out SimVec3 direction) { direction = default; return AutoFireAction.Pass; }
        }

        internal const string InnerMessage = "The AI fire decision wraps only its own combat system's built-in decision.";

        // 8: the constructor's checks, after P1's three (ticks, combat, inner): the interval's range, then the inner decision
        // must be this combat system's built-in one (not another implementation, not another world's built-in decision).
        [Test]
        public void Constructor_Checks()
        {
            M6World w = P2.World(P2.Cast(), null), other = P2.World(P2.Cast(), null);
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 0)).ParamName, Is.EqualTo("senseTicks"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1000001)).ParamName, Is.EqualTo("senseTicks"));
            Assert.DoesNotThrow(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1));
            Assert.DoesNotThrow(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1000000));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, -1, 0)).ParamName, Is.EqualTo("ticks"), "P1's check comes first");
            Assert.That(Assert.Throws<ArgumentNullException>(() => new ReactionFire(w.Combat, null, 0, 0)).ParamName, Is.EqualTo("inner"), "the null check comes before the range");
            var silent = Assert.Throws<ArgumentException>(() => new ReactionFire(w.Combat, new Silent(), 0, 10));
            Assert.That(silent.ParamName, Is.EqualTo("inner"));
            Assert.That(silent.Message, Does.StartWith(InnerMessage), "the runtime appends its own parameter suffix");
            Assert.That(ReferenceEquals(other.Combat.BuiltInAutoFire, w.Combat.BuiltInAutoFire), Is.False);
            var foreign = Assert.Throws<ArgumentException>(() => new ReactionFire(w.Combat, other.Combat.BuiltInAutoFire, 0, 10));
            Assert.That(foreign.ParamName, Is.EqualTo("inner"));
            Assert.That(foreign.Message, Does.StartWith(InnerMessage));
            var one = Assert.Throws<ArgumentException>(() => new ReactionFire(w.Combat, other.Combat.BuiltInAutoFire, 15));
            Assert.That(one.ParamName, Is.EqualTo("inner"), "also with the interval left at 1");
        }
    }
}
