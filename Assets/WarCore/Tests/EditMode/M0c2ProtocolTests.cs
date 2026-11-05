using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("M0c2")]
    public sealed class M0c2ProtocolTests
    {
        [Test]
        public void EmptyGameplayUsesV8ThroughRetainedEntry()
        {
            var content = new StableContentManifest(0);
            var sim = Simulation.CreateRetainedWorld(123, new PlacedCatalog(content), gameplaySchema:new GameplaySchema(0));
            Assert.That(BitConverter.ToUInt64(sim.SaveRetainedSnapshot(),8), Is.EqualTo(8),
                "S0 requires explicit gameplay schema and retained v6/v8.");
        }

        [Test]
        public void IndependentEmptyVectorsMatch()
        {
            var cases = VectorJson.Load("m0c2-empty-v6-v8.json")["cases"];
            for (int i = 0; i < cases.Count; i++)
            {
                var item = cases[i];
                var sim = M0c2Fixture.Create(Convert.ToUInt64(item["rules"].Text,16));
                Assert.That(sim.StateHashRetained().ToString("X16"),Is.EqualTo(item["hash"].Text));
                Assert.That(sim.SaveRetainedSnapshot(),Is.EqualTo(M0c1ProtocolTests.Hex(item["snapshot"].Text)));
                sim.RestoreRetainedSnapshot(M0c1ProtocolTests.Hex(item["snapshot"].Text),true);
                Assert.That(sim.StateHashRetained().ToString("X16"),Is.EqualTo(item["hash"].Text));
            }
        }
        [TestCase(0UL),TestCase(ulong.MaxValue)]
        public void EveryEmptyCheckpointContinues(ulong rules)
        {
            var sim = M0c2Fixture.Create(rules);
            for (int tick = 0; tick <= 300; tick++)
            {
                var restored = M0c2Fixture.Create(rules);
                restored.RestoreRetainedSnapshot(sim.SaveRetainedSnapshot(),true);
                Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
                sim.Step(default); restored.Step(default);
                Assert.That(restored.StateHashRetained(),Is.EqualTo(sim.StateHashRetained()));
            }
        }
        [TestCase(0),TestCase(8),TestCase(16),TestCase(24),TestCase(32),TestCase(40),TestCase(48),
         TestCase(56),TestCase(64),TestCase(72),TestCase(80),TestCase(88),TestCase(96),TestCase(104),
         TestCase(112),TestCase(120),TestCase(128),TestCase(136),TestCase(144),TestCase(152),
         TestCase(160),TestCase(168),TestCase(176),TestCase(184),TestCase(368)]
        public void CorruptGameplayWordIsRejectedAndFaulted(int offset)
        {
            var sim = M0c2Fixture.Create();
            byte[] bytes = sim.SaveRetainedSnapshot(); bytes[bytes.Length-376+offset] ^= 1;
            Assert.Throws<SimSnapshotException>(() => sim.RestoreRetainedSnapshot(bytes));
            Assert.Throws<InvalidOperationException>(() => sim.Step(default));
        }
        [Test]
        public void CrossProtocolAndRulesAreRejected()
        {
            var old = Simulation.CreateRetainedWorld(123,new PlacedCatalog(new StableContentManifest(0)));
            var fresh = M0c2Fixture.Create();
            Assert.Throws<SimSnapshotException>(() => fresh.RestoreRetainedSnapshot(old.SaveRetainedSnapshot()));
            Assert.Throws<SimSnapshotException>(() => old.RestoreRetainedSnapshot(fresh.SaveRetainedSnapshot()));
            Assert.Throws<SimSnapshotException>(() => M0c2Fixture.Create(1).RestoreRetainedSnapshot(fresh.SaveRetainedSnapshot()));
            foreach (int size in new[]{-1,1})
            {
                byte[] bad = fresh.SaveRetainedSnapshot(); Array.Resize(ref bad,bad.Length+size);
                Assert.Throws<SimSnapshotException>(() => M0c2Fixture.Create().RestoreRetainedSnapshot(bad));
            }
        }
        [Test]
        public void InputLeafMatchesIndependentFullRowAfterEveryTick()
        {
            var sim = M0c2Fixture.Create();
            for (int tick = 1; tick <= 301; tick++)
            {
                sim.Step(default);
                byte[] bytes = sim.SaveRetainedSnapshot(); int start = bytes.Length-376;
                ulong hash = 14695981039346656037UL;
                foreach (ulong word in new[]{2UL,(ulong)tick,0UL})
                    for (int b = 0; b < 8; b++) hash = unchecked((hash^((word>>(8*b))&255))*1099511628211UL);
                hash = unchecked((hash^(hash>>30))*0xBF58476D1CE4E5B9UL);
                hash = unchecked((hash^(hash>>27))*0x94D049BB133111EBUL); hash ^= hash>>31;
                Assert.That(BitConverter.ToUInt64(bytes,start+144),Is.EqualTo(hash));
                Assert.That(BitConverter.ToUInt64(bytes,start+120),Is.EqualTo(tick));
            }
        }
    }
}
