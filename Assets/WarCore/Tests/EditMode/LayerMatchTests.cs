using NUnit.Framework;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class LayerMatchTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ColliderMatchFreezesBothHashesAndRestoresEveryContinuationTick(bool fresh)
        {
            Simulation baseline = LayerHashFixture.Create(123);
            for (int tick = 1; tick <= 510; tick++)
                baseline.Step(Fixtures.Input(tick));
            byte[] snapshot = baseline.SaveSnapshot();
            var included = new ulong[490];
            var ignored = new ulong[490];
            for (int tick = 511; tick <= 1000; tick++)
            {
                baseline.Step(Fixtures.Input(tick));
                included[tick - 511] = baseline.StateHash();
                ignored[tick - 511] = LayerHashFixture.Hash(baseline, true);
            }
            Assert.That(LayerHashFixture.Hash(baseline, false), Is.EqualTo(LayerHashFixture.IncludedHash));
            Assert.That(LayerHashFixture.Hash(baseline, true), Is.EqualTo(LayerHashFixture.IgnoredHash));
            Assert.That(LayerHashFixture.IncludedHash, Is.Not.EqualTo(LayerHashFixture.IgnoredHash));
            Simulation restored = fresh ? LayerHashFixture.Create(999) : baseline;
            restored.RestoreSnapshot(snapshot);
            Assert.That(restored.SaveSnapshot(), Is.EqualTo(snapshot));
            for (int tick = 511; tick <= 1000; tick++)
            {
                restored.Step(Fixtures.Input(tick));
                Assert.That(restored.StateHash(), Is.EqualTo(included[tick - 511]), "included tick=" + tick);
                Assert.That(LayerHashFixture.Hash(restored, true), Is.EqualTo(ignored[tick - 511]), "ignored tick=" + tick);
            }
            TestContext.WriteLine("Collider match seed=123; ticks=1000; layers=80000001/80000000/FFFFFFFF; included=0x{0:X16}; ignored=0x{1:X16}; fresh={2}; 490 continuation pairs matched.",
                LayerHashFixture.IncludedHash, LayerHashFixture.IgnoredHash, fresh);
        }
    }
}
