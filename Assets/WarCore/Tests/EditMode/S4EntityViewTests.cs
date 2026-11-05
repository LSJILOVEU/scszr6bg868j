using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("S4View")]
    public sealed class S4EntityViewTests
    {
        private static object Begin(EntityStore store, ComponentType<Health> type)
        {
            MethodInfo method = typeof(EntityStore).GetMethod("With");
            Assert.That(method, Is.Not.Null, "EntityStore.With");
            object view = method.MakeGenericMethod(typeof(Health)).Invoke(store, new object[] { type });
            Assert.That(view.GetType().IsValueType, Is.True);
            object iterator = view.GetType().GetMethod("GetEnumerator").Invoke(view, null);
            Assert.That(iterator.GetType().IsValueType, Is.True);
            Assert.That(iterator.GetType().GetInterfaces(), Is.Empty, "Pattern enumeration must not expose a boxing route.");
            return iterator;
        }

        private static bool Next(object iterator) => (bool)iterator.GetType().GetMethod("MoveNext").Invoke(iterator, null);
        private static EntityId Current(object iterator) => (EntityId)iterator.GetType().GetProperty("Current").GetValue(iterator);

        [Test]
        public void FilterVisitsOnlyLiveOwnersInIncreasingSlotOrder()
        {
            var store = new EntityStore(8);
            ComponentType<Health> health = store.Register<Health>("health");
            var ids = new EntityId[6];
            for (int i = 0; i < ids.Length; i++)
            {
                ids[i] = store.Create();
                if (i % 2 == 0)
                    store.Set(health, ids[i], new Health { Value = i });
            }
            store.Destroy(ids[2]);
            object iterator = Begin(store, health);
            Assert.That(Next(iterator), Is.True);
            Assert.That(Current(iterator), Is.EqualTo(ids[0]));
            Assert.That(Next(iterator), Is.True);
            Assert.That(Current(iterator), Is.EqualTo(ids[4]));
            Assert.That(Next(iterator), Is.False);
            Assert.That(Next(iterator), Is.False);
        }

        [Test]
        public void CapturedSlotLimitBoundsIterationAndPresenceIsReadAtVisitTime()
        {
            var store = new EntityStore(8);
            ComponentType<Health> health = store.Register<Health>("health");
            EntityId first = store.Create();
            EntityId destroyed = store.Create();
            EntityId removed = store.Create();
            EntityId lateComponent = store.Create();
            store.Set(health, first, default);
            store.Set(health, destroyed, default);
            store.Set(health, removed, default);
            object iterator = Begin(store, health);
            Assert.That(Next(iterator), Is.True);
            Assert.That(Current(iterator), Is.EqualTo(first));
            store.Destroy(destroyed);
            store.Remove(health, removed);
            store.Set(health, lateComponent, default);
            EntityId appended = store.Create();
            store.Set(health, appended, default);
            Assert.That(Next(iterator), Is.True);
            Assert.That(Current(iterator), Is.EqualTo(lateComponent));
            Assert.That(Next(iterator), Is.False, "Appended slots are outside this traversal.");
        }

        [Test]
        public void ReusedUnvisitedSlotsAreVisibleButVisitedSlotsAreNeverRevisited()
        {
            var store = new EntityStore(8);
            ComponentType<Health> health = store.Register<Health>("health");
            EntityId first = store.Create();
            EntityId second = store.Create();
            store.Set(health, first, default);
            store.Set(health, second, default);
            object iterator = Begin(store, health);
            Assert.That(Next(iterator), Is.True);
            store.Destroy(first);
            store.Destroy(second);
            store.RecycleDestroyed();
            EntityId reusedFirst = store.Create();
            EntityId reusedSecond = store.Create();
            store.Set(health, reusedFirst, default);
            store.Set(health, reusedSecond, default);
            Assert.That(Current(iterator), Is.EqualTo(first), "Current is the entity yielded, not the slot's new generation.");
            Assert.That(Next(iterator), Is.True);
            Assert.That(Current(iterator), Is.EqualTo(reusedSecond));
            Assert.That(Next(iterator), Is.False);
        }

        [Test]
        public void EmptyViewAndWrongStoreHandleHaveExplicitResults()
        {
            var a = new EntityStore();
            var b = new EntityStore();
            ComponentType<Health> health = a.Register<Health>("health");
            Assert.That(Next(Begin(a, health)), Is.False);
            TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => Begin(b, health));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void AllocatingSnapshotNameIsExplicitAndTheCopySurvivesMutation()
        {
            MethodInfo method = typeof(EntityStore).GetMethod("SnapshotEntitiesForTests");
            Assert.That(method, Is.Not.Null);
            var store = new EntityStore();
            EntityId id = store.Create();
            var snapshot = (IReadOnlyList<EntityId>)method.Invoke(store, null);
            store.Destroy(id);
            Assert.That(snapshot, Is.EqualTo(new[] { id }));
            MethodInfo legacy = typeof(EntityStore).GetMethod("SnapshotEntities");
            Assert.That(legacy == null || legacy.IsDefined(typeof(ObsoleteAttribute)), Is.True);
        }

        private static bool AllocatingSnapshotCall(string source) => Regex.IsMatch(SourceRules.StripComments(source),
            @"\bSnapshotEntities(?:ForTests)?\s*\(");

        [Test]
        public void ProductCodeCannotUseTheAllocatingSnapshotApi()
        {
            Assert.That(AllocatingSnapshotCall("store.SnapshotEntitiesForTests();"), Is.True);
            Assert.That(AllocatingSnapshotCall("store.SnapshotEntities();"), Is.True);
            Assert.That(AllocatingSnapshotCall("// store.SnapshotEntities();"), Is.False);
            foreach (string path in Directory.GetFiles("Assets/WarCore", "*.cs", SearchOption.AllDirectories)
                .Select(path => path.Replace('\\', '/'))
                .Where(path => !path.Contains("/Tests/") && !path.EndsWith("/EntityStore.cs", StringComparison.Ordinal)))
                Assert.That(AllocatingSnapshotCall(File.ReadAllText(path)), Is.False, path);
            // The owning file may declare the API, but must not call either name from another member.
            string owner = File.ReadAllText("Assets/WarCore/Sim/EntityStore.cs");
            owner = Regex.Replace(owner, @"public IReadOnlyList<EntityId> SnapshotEntities(?:ForTests)?\(\)", "snapshotDeclaration");
            Assert.That(AllocatingSnapshotCall(owner), Is.False);
        }
    }
}
