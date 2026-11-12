using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture]
    public sealed class Mt192GameplaySourceSnapshotTests
    {
        [Test]
        public void FourthAttributePreservesKeyAndRawValue()
        {
            var snapshot = new GameplaySourceSnapshot(default, 1);
            for (ulong key = 1; key <= 4; key++)
                snapshot = snapshot.WithAttribute(new GameplayCapturedAttribute(key, (long)key * 10));

            Assert.That(snapshot.AttributeCount, Is.EqualTo(4));
            Assert.That(snapshot.AttributeAt(3).Key, Is.EqualTo(4UL));
            Assert.That(snapshot.AttributeAt(3).Raw, Is.EqualTo(40L));
        }

        [Test]
        public void SixthActionPreservesCapturedFields()
        {
            var snapshot = new GameplaySourceSnapshot(default, 1);
            for (uint ordinal = 1; ordinal <= 6; ordinal++)
                snapshot = snapshot.WithActionCapture(new GameplayCapturedAction(
                    100UL + ordinal, ordinal, 200UL + ordinal, 300UL + ordinal));

            Assert.That(snapshot.ActionCaptureCount, Is.EqualTo(6));
            var action = snapshot.ActionCaptureAt(5);
            Assert.That(action.DefinitionKey, Is.EqualTo(106UL));
            Assert.That(action.Ordinal, Is.EqualTo(6U));
            Assert.That(action.DamageTypeKey, Is.EqualTo(206UL));
            Assert.That(action.PipelineKey, Is.EqualTo(306UL));
        }

        [Test]
        public void MissingAttributeReturnsFalseAndZero()
        {
            var snapshot = new GameplaySourceSnapshot(default, 1)
                .WithAttribute(new GameplayCapturedAttribute(1, 40));

            Assert.That(snapshot.TryAttribute(2, out long raw), Is.False);
            Assert.That(raw, Is.EqualTo(0L));
        }
    }
}
