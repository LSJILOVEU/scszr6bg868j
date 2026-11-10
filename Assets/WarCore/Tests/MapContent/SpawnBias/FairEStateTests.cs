using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FairEStateTests
    {
        internal const ulong Policy = 0xEC1C696AA9E47530UL;

        [Test]
        public void FairE_PolicyDigest()
        {
            foreach (string json in new[] { null, "{}", "{ \"fairDecisions\": false }" })
            {
                AiRules rules = FairE.Rules(json);
                Assert.That(rules.FairDecisions, Is.False);
                Assert.That(rules.PolicyDigest, Is.EqualTo(0UL));
            }
            AiRules enabled = FairE.Rules("{ \"fairDecisions\": true }");
            Assert.That(enabled.FairDecisions, Is.True);
            Assert.That(enabled.PolicyDigest, Is.EqualTo(Policy));
            var error = Assert.Throws<ContentException>(() => FairE.Rules("{ \"fairDecisions\": 1 }"));
            Assert.That(error.Message, Is.EqualTo("content pack 'fair-e' layer 'fair-e' ai field 'fairDecisions': must be true or false"));
        }

        [TestCase(false, 3, 16)]
        [TestCase(true, 4, 32)]
        public void FairE_StateEncoding(bool enabled, int version, int bytes)
        {
            M6World w = FairE.World("control", 41, enabled);
            SimpleAiSystem ai = w.System<SimpleAiSystem>();
            Assert.That(ai.StateVersion, Is.EqualTo(version));
            var expected = new SimStateWriter();
            expected.UInt64(w.Recipe.Match.PolicyDigest); expected.UInt64(w.Recipe.Combat.PolicyDigest);
            if (enabled) { expected.UInt64(Policy); expected.UInt64(41UL); }
            byte[] payload = FairE.Payload(ai);
            Assert.That(payload.Length, Is.EqualTo(bytes));
            Assert.That(payload, Is.EqualTo(expected.ToArray()));
            var reader = new SimStateReader(payload);
            ai.LoadState(reader);
            Assert.That(reader.Remaining, Is.EqualTo(0));

            var hash = new StateHash();
            hash.Add("battle.ai"); hash.Add(version); hash.Add(w.Recipe.Match.PolicyDigest); hash.Add(w.Recipe.Combat.PolicyDigest);
            if (enabled) { hash.Add(Policy); hash.Add(41UL); }
            var actual = new StateHash(); ai.WriteState(actual);
            Assert.That(actual.Value, Is.EqualTo(hash.Value));
        }

        [Test]
        public void FairE_LoadRejectsOtherDecisionSeed()
        {
            SimpleAiSystem ai = FairE.World("control", 41, true).System<SimpleAiSystem>();
            byte[] payload = FairE.Payload(ai);
            Array.Copy(BitConverter.GetBytes(42UL), 0, payload, 24, 8);
            var error = Assert.Throws<SimSnapshotException>(() => ai.LoadState(new SimStateReader(payload)));
            Assert.That(error.Message, Is.EqualTo("AI decision seed differs."));
        }

        [Test]
        public void FairE_FlatPackKeepsOtherAiOptionsInactive()
        {
            M6World plain = FairE.World("M6", 41, null);
            var recipe = FairE.Recipe("M6", 41, true).WithTerrainRules(FairE.Pack("{ \"fairDecisions\": true,"
                + " \"burstShots\": 3, \"burstPauseTicks\": 5, \"failedSlotTicks\": 5, \"targetMemoryTicks\": 5,"
                + " \"searchTicks\": 5, \"suppressTicks\": 5 }"));
            var enabled = new M6World(recipe, FairE.Map("M6"));
            Assert.That(enabled.System<SimpleAiSystem>().StateVersion, Is.EqualTo(4));
            Assert.That(L4.Systems(enabled), Is.EqualTo(L4.Systems(plain)));
            Assert.That(enabled.System<ReactionFire>(), Is.Null);
            Assert.That(enabled.System<FailedSlotMemory>(), Is.Null);
            Assert.That(enabled.System<TargetMemory>(), Is.Null);
        }
    }
}
