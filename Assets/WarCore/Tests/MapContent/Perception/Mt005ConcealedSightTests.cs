using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests.Perception
{
    public sealed class Mt005ConcealedSightTests
    {
        [Test]
        public void NullInner_IsRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new ConcealedSight(null, A2.Grid(1, 1), new short[] { 1000 }));
        }

        [TestCase(0)]
        [TestCase(2)]
        public void CoverLengthMismatch_IsRejected(int length)
        {
            Assert.Throws<ArgumentException>(() => new ConcealedSight(A2.Limited(), A2.Grid(1, 1), A2.Cover(length, 1, (x, z) => 1000)));
        }

        [TestCase(0)]
        [TestCase(1001)]
        public void CoverFactorOutOfRange_IsRejected(int factor)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConcealedSight(A2.Limited(), A2.Grid(1, 1), new short[] { (short)factor }));
        }

        [Test]
        public void OutsideTheGrid_AtSightLimit_IsSelected()
        {
            const long S = SimFix.Scale, Z0 = 20 * S + S / 2;
            M5TestWorld w = A2.Direct(1);
            A2.Place(w, 0, S, Z0);
            A2.Place(w, 1, 21 * S, Z0);
            var selector = new ConcealedSight(A2.Limited(), A2.Grid(10, 40), A2.Cover(10, 40, (x, z) => 500));
            Assert.That(A2.Pick(w, selector, SimFix.FromInt(30)), Is.EqualTo((true, 1)));
        }
    }
}
