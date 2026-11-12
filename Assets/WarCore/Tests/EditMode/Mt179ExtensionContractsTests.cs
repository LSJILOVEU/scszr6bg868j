using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt179ExtensionContractsTests
    {
        [TestCase(-1, 0)]
        [TestCase(1, -1)]
        [TestCase(1, 2)]
        public void ContainerRejectsInvalidCapacityOrCount(int capacity, int count)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new Container(capacity, count));
        }

        [TestCase(-1L, 0L)]
        [TestCase(0L, -1L)]
        [TestCase(0L, SimFix.Scale + 1)]
        public void ObservationRejectsInvalidTickOrConfidence(long tick, long confidenceRaw)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ObservedIntel(default, default, tick, SimFix.FromRaw(confidenceRaw)));
        }

        [Test]
        public void SoundWithoutCapturedIdentityRejectsStableConversion()
        {
            var sound = new SoundEvent(default, SimFix.FromInt(5), 2, 19, new EntityId(1, 1));
            Assert.Throws<SimBoundaryException>(() => sound.ToStableEvent(new EventTypeId(100)));
        }
    }
}
