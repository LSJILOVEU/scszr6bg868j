using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt040ExtensionContractsTests
    {
        [Test]
        public void EmptyContainerAcceptsZeroCapacity()
        {
            Container container = default;
            Assert.DoesNotThrow(() => container = new Container(0));
            Assert.That(container.Capacity, Is.EqualTo(0));
            Assert.That(container.Count, Is.EqualTo(0));
        }

        [Test]
        public void ContainerAcceptsCountEqualToCapacity()
        {
            Container container = default;
            Assert.DoesNotThrow(() => container = new Container(1, 1));
            Assert.That(container.Capacity, Is.EqualTo(1));
            Assert.That(container.Count, Is.EqualTo(1));
        }

        [Test]
        public void IntelAcceptsObservationAtTickZero()
        {
            ObservedIntel observed = default;
            Assert.DoesNotThrow(() => observed = new ObservedIntel(default, default, 0, SimFix.Ratio(1, 2)));
            Assert.That(observed.ObservedTick, Is.EqualTo(0));
            Assert.That(observed.Confidence, Is.EqualTo(SimFix.Ratio(1, 2)));
        }

        [Test]
        public void IntelAcceptsFullConfidence()
        {
            ObservedIntel observed = default;
            Assert.DoesNotThrow(() => observed = new ObservedIntel(default, default, 7, SimFix.FromInt(1)));
            Assert.That(observed.ObservedTick, Is.EqualTo(7));
            Assert.That(observed.Confidence, Is.EqualTo(SimFix.FromInt(1)));
        }

        [Test]
        public void SoundAcceptsFactionZero()
        {
            SoundEvent sound = default;
            Assert.DoesNotThrow(() => sound = new SoundEvent(default, SimFix.FromInt(1), 0, 77));
            Assert.That(sound.SourceFaction, Is.EqualTo(0));
            Assert.That(sound.TypeTag, Is.EqualTo(77));
        }

        [Test]
        public void SoundDecodeRejectsNonzeroAuxYWithZeroAuxZ()
        {
            EventTypeId type = new EventTypeRegistry().Register("sound");
            var item = new SimEvent(type,
                aux: new SimVec3(SimFix.FromRaw(3), SimFix.FromRaw(1), default));
            Assert.Throws<ArgumentException>(() => SoundEvent.FromEvent(item, type));
        }
    }
}
