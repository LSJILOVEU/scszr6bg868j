using NUnit.Framework;
using WarCore.Sim;
namespace WarCore.Tests
{
    [TestFixture,Category("M0c2S3Capture")]
    public sealed class Mt125GameplaySourceSnapshotTests
    {
        [Test]
        public void MaximumActionsIsThirtyTwo()
        {
            Assert.That(GameplaySourceSnapshot.MaximumActions,Is.EqualTo(32));
        }
        [Test]
        public void MaximumAttributesIsThirtyTwo()
        {
            Assert.That(GameplaySourceSnapshot.MaximumAttributes,Is.EqualTo(32));
        }
        [Test]
        public void TwoArgumentConstructorDefaultsAbilityKeyToZero()
        {
            var source=new GameplaySourceSnapshot(new GameplayStamp(M0c2S3Fixture.Actor.Id),7);
            Assert.That(source.AbilityKey,Is.EqualTo(0UL));
        }
        [Test]
        public void AttributeLookupFindsFirstKeyAndClearsMissingValue()
        {
            var sim=M0c2S3Fixture.Create();
            M0c2S3Fixture.Input(sim,M0c2S3Fixture.Command(1));
            var source=sim.GameplayCastAt(0).SourceSnapshot;
            Assert.That(source.TryAttribute(1,out long raw),Is.True);
            Assert.That(raw,Is.EqualTo(8000));
            Assert.That(source.TryAttribute(99,out raw),Is.False);
            Assert.That(raw,Is.EqualTo(0));
        }
    }
}
