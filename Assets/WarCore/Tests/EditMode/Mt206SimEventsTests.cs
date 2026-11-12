using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt206SimEventsTests
    {
        [TestCase(0)]
        [TestCase(-1)]
        public void EventTypeIdRejectsNonpositiveValue(int value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EventTypeId(value));
        }

        [TestCase(-1, 0, 0)]
        [TestCase(0, -1, 0)]
        [TestCase(0, 0, -1)]
        public void WorldChangeRejectsNegativeHalfExtent(int x, int y, int z)
        {
            SimVec3 halfExtents = SimVec3.Metres(x, y, z);
            Assert.Throws<ArgumentOutOfRangeException>(() => SimEvent.WorldChangeEvent(1, default, halfExtents, 7));
        }

        [Test]
        public void BeginTickRejectsSkippedTickAfterClosing()
        {
            var buffer = new SimEventBuffer(2, 1, 1);
            buffer.BeginTick(1);
            buffer.EndTick();
            Assert.Throws<InvalidOperationException>(() => buffer.BeginTick(3));
        }

        [Test]
        public void BeginTickRejectsNextTickWhileActive()
        {
            var buffer = new SimEventBuffer(2, 1, 1);
            buffer.BeginTick(1);
            Assert.Throws<InvalidOperationException>(() => buffer.BeginTick(2));
        }
    }
}
