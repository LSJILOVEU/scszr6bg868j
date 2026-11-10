using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt038EntityStoreTests
    {
        [Test]
        public void ConstructorRejectsZeroCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new EntityStore(0));
        }

        [TestCase(IdentityMode.Legacy, true)]
        [TestCase(IdentityMode.StableWorld, false)]
        public void ReserveCurrentCapacityIsNoOp(IdentityMode identityMode, bool warmup)
        {
            var store = new EntityStore(4, identityMode);
            if (warmup)
                store.CompleteWarmup();

            Assert.DoesNotThrow(() => store.Reserve(store.Capacity));
            Assert.That(store.Capacity, Is.EqualTo(4));
        }
    }
}
