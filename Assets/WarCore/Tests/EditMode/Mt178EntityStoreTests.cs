using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt178EntityStoreTests
    {
        [TestCase(null)]
        [TestCase("")]
        public void RegisterRejectsMissingComponentId(string stableId)
        {
            var store = new EntityStore();
            Assert.Throws<ArgumentException>(() => store.Register<Health>(stableId));
        }

        [Test]
        public void ReserveRejectsStableWorldGrowth()
        {
            var store = new EntityStore(4, IdentityMode.StableWorld);
            Assert.Throws<SimBoundaryException>(() => store.Reserve(5));
            Assert.That(store.Capacity, Is.EqualTo(4));
        }

        [Test]
        public void RefGetRejectsAbsentComponent()
        {
            var store = new EntityStore();
            ComponentType<Health> health = store.Register<Health>("health");
            EntityId entity = store.Create();
            Assert.Throws<InvalidOperationException>(() => store.Get(health, entity));
        }
    }
}
