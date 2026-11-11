using NUnit.Framework;
using WarCore.Combat;

namespace WarCore.Tests
{
    public sealed class Mt019IAutoFireTests
    {
        [Test]
        public void PassHasValueZero()
        {
            Assert.That((byte)AutoFireAction.Pass, Is.EqualTo(0));
        }

        [Test]
        public void FireHasValueOne()
        {
            Assert.That((byte)AutoFireAction.Fire, Is.EqualTo(1));
        }

        [Test]
        public void HoldFireHasValueTwo()
        {
            Assert.That((byte)AutoFireAction.HoldFire, Is.EqualTo(2));
        }
    }
}
