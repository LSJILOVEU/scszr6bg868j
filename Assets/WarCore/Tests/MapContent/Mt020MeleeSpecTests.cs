using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt020MeleeSpecTests
    {
        [Test]
        public void ActiveTicksAboveMaximumAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MeleeWorld.Spec(active: 601));
        }

        [Test]
        public void RecoveryTicksAboveMaximumAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MeleeWorld.Spec(recovery: 601));
        }

        [Test]
        public void LineHalfWidthOneRawUnitAboveMaximumIsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MeleeSpec(6, 2, 9, SimFix.FromInt(2), 500,
                SimFix.FromInt(1), SimFix.FromInt(10), 3, SimFix.Ratio(18, 10), SimFix.FromInt(16) + SimFix.FromRaw(1)));
        }

        [Test]
        public void SwingStartingAtTickZeroIsPending()
        {
            Assert.That(new MeleeState(0, startTick: 0).Pending, Is.True);
        }
    }
}
