using System;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt031MoveIntentTests
    {
        [Test]
        public void ConstructorRejectsTargetOutsideWorld()
        {
            var target = new SimVec3(SimFix.FromRaw(16384L * SimFix.Scale + 1), default, default);
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveIntent(target, SimFix.FromRaw(2), 1, true));
        }

        [Test]
        public void ConstructorAcceptsMinimumPositiveSpeed()
        {
            MoveIntent intent = default;
            Assert.DoesNotThrow(() => intent = new MoveIntent(default, SimFix.FromRaw(1), 1, true));
            Assert.That(intent.SpeedPerTick.Raw, Is.EqualTo(1L));
        }

        [Test]
        public void ConstructorRejectsNegativeSpeed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveIntent(default, SimFix.FromRaw(-1), 1, true));
        }

        [Test]
        public void ConstructorRejectsZeroSpeed()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MoveIntent(default, SimFix.FromRaw(0), 1, true));
        }
    }
}
