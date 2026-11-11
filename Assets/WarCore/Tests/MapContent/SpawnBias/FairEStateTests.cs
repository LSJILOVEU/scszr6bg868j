using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class FairEStateTests
    {
        [TestCase(false, 0)]
        [TestCase(false, 60)]
        [TestCase(true, 0)]
        [TestCase(true, 60)]
        public void FairE_OptionsAreIndependent(bool fair, int chase)
        {
            AiRules rules = FairEFixture.Pack("{ \"fairDecisions\": " + (fair ? "true" : "false")
                + ", \"meleeChaseRange\": " + chase + " }").Section<AiRules>();
            Assert.That(rules.FairDecisions, Is.EqualTo(fair));
            Assert.That(rules.MeleeChaseRange, Is.EqualTo(chase));
        }

        [TestCase(null, false)]
        [TestCase("{}", false)]
        [TestCase("{ \"fairDecisions\": false }", false)]
        [TestCase("{ \"fairDecisions\": true }", true)]
        public void FairE_ContentDefaultAndValues(string ai, bool expected)
        {
            Assert.That(FairEFixture.Pack(ai).Section<AiRules>().FairDecisions, Is.EqualTo(expected));
        }

        [TestCase(false, 3)]
        [TestCase(true, 4)]
        public void FairE_VersionPolicySaveAndHash(bool enabled, int version)
        {
            M6World w = FairEFixture.Create("M6", 41, enabled);
            SimpleAiSystem ai = w.System<SimpleAiSystem>();
            Assert.That(ai.StateVersion, Is.EqualTo(version));
            ulong policy = w.Recipe.Match.PolicyDigest;
            if (enabled)
            {
                var digest = new StateHash();
                digest.Add("battle.ai.fair-decisions.v1"); digest.Add(policy); digest.Add(true); digest.Add(41UL);
                policy = digest.Value;
            }
            var writer = new SimStateWriter(); ai.SaveState(writer);
            byte[] bytes = writer.ToArray();
            Assert.That(bytes.Length, Is.EqualTo(16));
            var reader = new SimStateReader(bytes);
            Assert.That(reader.UInt64(), Is.EqualTo(policy));
            Assert.That(reader.UInt64(), Is.EqualTo(w.Recipe.Combat.PolicyDigest));
            var expected = new StateHash();
            expected.Add("battle.ai"); expected.Add(version); expected.Add(policy); expected.Add(w.Recipe.Combat.PolicyDigest);
            var actual = new StateHash(); ai.WriteState(actual);
            Assert.That(actual.Value, Is.EqualTo(expected.Value));
        }

        [Test]
        public void FairE_OnSnapshotRequiresTheOptionAndSeed()
        {
            M6World on = FairEFixture.Create("M6", 41, true), off = FairEFixture.Create("M6", 41, false);
            var manifest = Assert.Throws<SimSnapshotException>(() => off.Sim.RestoreRetainedSnapshot(on.Sim.SaveRetainedSnapshot()));
            Assert.That(manifest.Message, Does.Contain("State registration manifest mismatch"));
            Assert.That(manifest.Message, Does.Contain("battle.ai"));
            Assert.That(manifest.Message, Does.Contain("v4"));
            var writer = new SimStateWriter(); on.System<SimpleAiSystem>().SaveState(writer);
            M6World otherSeed = FairEFixture.Create("M6", 42, true);
            var seed = Assert.Throws<InvalidOperationException>(() => otherSeed.System<SimpleAiSystem>().LoadState(new SimStateReader(writer.ToArray())));
            Assert.That(seed.Message, Is.EqualTo("Battle configuration differs from snapshot."));
        }
    }
}
