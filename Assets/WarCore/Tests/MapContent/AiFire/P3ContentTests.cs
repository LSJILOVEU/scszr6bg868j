using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 10 (the ai fields burstShots and burstPauseTicks) and 11 (assembly and the constructor's checks).
    public sealed class P3ContentTests
    {
        private static AiRules Rules(string ai) => A3.Load(P1.PackJson(ai)).Section<AiRules>();
        private const string Prefix = "content pack 'a2-test' layer 'a2-test' ai field '";

        [Test]
        public void ValuesAndDefaults()
        {
            Assert.That(Rules("{ \"burstShots\": 0 }").BurstShots, Is.EqualTo(0));
            Assert.That(Rules("{ \"burstShots\": 3 }").BurstShots, Is.EqualTo(3));
            Assert.That(Rules("{ \"burstShots\": 1000000 }").BurstShots, Is.EqualTo(1000000));
            Assert.That(Rules("{ \"burstPauseTicks\": 0 }").BurstPauseTicks, Is.EqualTo(0));
            Assert.That(Rules("{ \"burstPauseTicks\": 20 }").BurstPauseTicks, Is.EqualTo(20));
            Assert.That(Rules("{ \"burstPauseTicks\": 1000000 }").BurstPauseTicks, Is.EqualTo(1000000));
            Assert.That((Rules(null).BurstShots, Rules(null).BurstPauseTicks), Is.EqualTo((0, 0)), "section left out");
            Assert.That((Rules("{ }").BurstShots, Rules("{ }").BurstPauseTicks), Is.EqualTo((0, 0)), "fields left out");
            AiRules all = Rules("{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 9, \"searchTicks\": 11, \"reactionTicks\": 13, \"senseTicks\": 17, "
                + "\"burstShots\": 19, \"burstPauseTicks\": 23 }");
            Assert.That(new[] { all.FailedSlotTicks, all.TargetMemoryTicks, all.SearchTicks, all.ReactionTicks, all.SenseTicks, all.BurstShots, all.BurstPauseTicks },
                Is.EqualTo(new[] { 7, 9, 11, 13, 17, 19, 23 }));
            AiRules old = Rules("{ \"failedSlotTicks\": 7, \"targetMemoryTicks\": 9, \"searchTicks\": 11, \"reactionTicks\": 13, \"senseTicks\": 17 }");
            Assert.That(new[] { old.FailedSlotTicks, old.TargetMemoryTicks, old.SearchTicks, old.ReactionTicks, old.SenseTicks, old.BurstShots, old.BurstPauseTicks },
                Is.EqualTo(new[] { 7, 9, 11, 13, 17, 0, 0 }));
        }

        [TestCase("{ \"burstShots\": -1 }", "burstShots", "out of range [0, 1000000]")]
        [TestCase("{ \"burstShots\": 1000001 }", "burstShots", "out of range [0, 1000000]")]
        [TestCase("{ \"burstShots\": 1.5 }", "burstShots", "must be an integer")]
        [TestCase("{ \"burstShots\": \"5\" }", "burstShots", "must be an integer")]
        [TestCase("{ \"burstPauseTicks\": -1 }", "burstPauseTicks", "out of range [0, 1000000]")]
        [TestCase("{ \"burstPauseTicks\": 1000001 }", "burstPauseTicks", "out of range [0, 1000000]")]
        [TestCase("{ \"burstPauseTicks\": 1.5 }", "burstPauseTicks", "must be an integer")]
        [TestCase("{ \"burstPauseTicks\": \"5\" }", "burstPauseTicks", "must be an integer")]
        public void Errors(string ai, string field, string message)
        {
            var e = Assert.Throws<ContentException>(() => A3.Load(P1.PackJson(ai)));
            Assert.That(e.Message, Is.EqualTo(Prefix + field + "': " + message));
        }

        // 11: a terrain world with only the burst set: exactly one battle.ai-fire (Requests, Order 14), version 3, and it is
        // the decision combat calls (the rhythm of acceptance 2). With the pause left out (0) it registers and fires
        // continuously.
        [Test]
        public void Assembly_BurstAlone_Registers()
        {
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, P3.Ai(0, null, 3, 20), 300);
            Assert.That(P3.Systems(r.W), Is.EqualTo(P2BaselineTests.Systems));
            Assert.That(P3.Systems(r.W), Is.EqualTo(P1BaselineTests.Systems.Replace("battle.ai|0|12;", "battle.ai|0|12;battle.ai-fire|0|14;")));
            Assert.That(r.W.System<ReactionFire>(), Is.Not.Null);
            Assert.That(r.W.System<ReactionFire>().StateVersion, Is.EqualTo(3));
            Assert.That(r.A.FireTicks(300), Is.EqualTo(P3TimingTests.Burst3Pause20), "the burst shows: the decision is installed");
            P3.Trace noPause = P3.Run(P2.Cast(), P1.Walk, P3.Ai(0, null, 3), 300);
            Assert.That(P3.Systems(noPause.W), Is.EqualTo(P2BaselineTests.Systems));
            Assert.That(noPause.W.System<ReactionFire>().StateVersion, Is.EqualTo(3));
            Assert.That(noPause.A.FireTicks(300), Is.EqualTo(P3BaselineTests.Fires0), "a pause of 0 changes nothing");
        }

        [Test]
        public void Assembly_WithoutCombat_Throws()
        {
            var a = new WorldAssembly(P3.Recipe(P2.Cast(), P3.Ai(0, null, 3, 20)), WcMapWriter.Write(A2.Map(A2.DWalls)), false);
            Assert.That(a.Combat, Is.Null);
            var e = Assert.Throws<ArgumentException>(() => AiFireFeature.Instance.RegisterRules(a));
            Assert.That(e.Message, Is.EqualTo("The AI reaction time needs combat."));
            Assert.That(e.ParamName, Is.Null);
        }

        // 11: the constructor's checks come after P1's three and P2's two: the burst range, then the pause range.
        [Test]
        public void Constructor_Checks()
        {
            M6World w = P3.World(P2.Cast(), null), other = P3.World(P2.Cast(), null);
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1, -1)).ParamName, Is.EqualTo("burstShots"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1, 1000001)).ParamName, Is.EqualTo("burstShots"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1, 3, -1)).ParamName, Is.EqualTo("burstPauseTicks"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1, 3, 1000001)).ParamName, Is.EqualTo("burstPauseTicks"));
            Assert.DoesNotThrow(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1, 0, 0));
            Assert.DoesNotThrow(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1, 1000000, 1000000));
            Assert.DoesNotThrow(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1, 0, 1000000), "a pause without a burst is legal");
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 0, -1)).ParamName,
                Is.EqualTo("senseTicks"), "P2's range check comes first");
            Assert.That(ReferenceEquals(other.Combat.BuiltInAutoFire, w.Combat.BuiltInAutoFire), Is.False);
            var foreign = Assert.Throws<ArgumentException>(() => new ReactionFire(w.Combat, other.Combat.BuiltInAutoFire, 0, 1, -1));
            Assert.That(foreign.ParamName, Is.EqualTo("inner"), "P2's inner check comes before the burst range");
            Assert.That(foreign.Message, Does.StartWith(P2ContentTests.InnerMessage));
        }
    }
}
