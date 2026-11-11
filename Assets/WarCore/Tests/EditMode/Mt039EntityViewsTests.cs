using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("S4View")]
    public sealed class Mt039EntityViewsTests
    {
        [Test]
        public void ComponentViewSkipsColdEntitiesWithRetainedComponents()
        {
            var store = new EntityStore(1, IdentityMode.StableWorld);
            ComponentType<Health> health = store.Register<Health>("health");
            EntityId entity = store.Create(StableEntityKey.Placed(1, 1));
            store.Set(health, entity, new Health { Value = 7 });
            store.MakeCold(entity);

            var iterator = store.With(health).GetEnumerator();
            Assert.That(iterator.MoveNext(), Is.False);
        }
    }
}
