using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class A5GridTests
    {
        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void A5_CleanupPreservesCellTraversalAndReusesAllFreedSlots(SpatialQueryKind kind)
        {
            // These masks put deaths at every link, consecutive links, alternating links and the entire cell.
            int[] masks = {1, 2, 4, 8, 16, 3, 12, 24, 21, 31};
            SimVec3 focus = SimVec3.Metres(4, 4, 4);
            foreach (int mask in masks)
            {
                var scene = new A5GridScenario();
                var residents = new EntityId[5];
                var entities = new EntityId[5];
                var hits = new RayHit[5];
                int alive = 0;
                for (int i = 0; i < residents.Length; i++)
                    residents[i] = scene.CreateBody(focus, 1, 2);
                Assert.That(scene.Grid.MembershipCount, Is.EqualTo(5), "Each resident must occupy the same single cell.");
                for (int i = 0; i < residents.Length; i++)
                {
                    if ((mask & (1 << i)) != 0)
                        scene.Store.Destroy(residents[i]);
                    else
                        alive++;
                }

                int count = Query(scene.Grid, kind, focus, entities, hits);
                Assert.That(count, Is.EqualTo(alive), "Cleanup lost a live neighbor or returned a dead handle; mask=" + mask);
                Assert.That(scene.Grid.MembershipCount, Is.EqualTo(5), "Query must not reclaim memberships.");
                scene.Grid.Synchronize();
                scene.Store.RecycleDestroyed();
                Assert.That(scene.Grid.MembershipCount, Is.EqualTo(alive), "Cleanup left dead memberships; mask=" + mask);
                int output = 0;
                for (int i = 0; i < residents.Length; i++)
                {
                    if ((mask & (1 << i)) != 0)
                    {
                        Assert.That(scene.Grid.Remove(residents[i]), Is.False);
                        continue;
                    }

                    AssertHit(kind, residents[i], output++, entities, hits);
                }

                // Refill through the public API to expose damaged free lists and stale generation caches.
                for (int i = 0; i < residents.Length; i++)
                {
                    if ((mask & (1 << i)) == 0)
                        continue;
                    EntityId old = residents[i];
                    residents[i] = scene.CreateBody(focus, 1, 2);
                    Assert.That(residents[i].Id, Is.EqualTo(old.Id));
                    Assert.That(residents[i].Generation, Is.Not.EqualTo(old.Generation));
                }

                Assert.That(Query(scene.Grid, kind, focus, entities, hits), Is.EqualTo(5));
                Assert.That(scene.Grid.MembershipCount, Is.EqualTo(5));
                for (int i = 0; i < residents.Length; i++)
                    AssertHit(kind, residents[i], i, entities, hits);
            }

            TestContext.WriteLine("A5 cell traversal: kind={0}; death layouts={1}; all survivors and reused slots checked.", kind, masks.Length);
        }

        [Test]
        public void A5_DeadCandidateOutsideQueryShapeReclaimsAllItsCellsWithAnEmptyOutputBuffer()
        {
            var scene = new A5GridScenario();
            EntityId dead = scene.CreateBody(default, 9, 20);
            Assert.That(scene.Grid.MembershipCount, Is.EqualTo(64));
            scene.Store.Destroy(dead);
            // This point shares a leaf with the capsule's bounds but lies outside its actual geometry.
            Assert.That(scene.Grid.Sphere(SimVec3.Metres(15, 15, 15), default, new EntityId[0]), Is.Zero);
            Assert.That(scene.Grid.MembershipCount, Is.EqualTo(64), "Empty-output query must remain read-only.");
            scene.Grid.Synchronize();
                scene.Store.RecycleDestroyed();
            Assert.That(scene.Grid.MembershipCount, Is.Zero);
            Assert.That(scene.Grid.Remove(dead), Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void A5_StaleUpsertCannotRemoveALiveReplacement(bool changeCell)
        {
            var scene = new A5GridScenario();
            SimVec3 focus = SimVec3.Metres(4, 4, 4);
            EntityId old = scene.CreateBody(focus, 1, 2);
            scene.Store.Destroy(old);
            scene.Store.RecycleDestroyed();
            if (changeCell)
                focus = SimVec3.Metres(20, 4, 4);
            EntityId replacement = scene.CreateBody(focus, 1, 2);
            Assert.That(replacement.Id, Is.EqualTo(old.Id));
            Assert.That(replacement.Generation, Is.Not.EqualTo(old.Generation));
            Assert.That(scene.Grid.Remove(old), Is.False);
            scene.Grid.Upsert(old);
            Assert.That(scene.Grid.MembershipCount, Is.EqualTo(1));
            var output = new EntityId[1];
            Assert.That(scene.Grid.Sphere(focus, default, output), Is.EqualTo(1));
            Assert.That(output[0], Is.EqualTo(replacement));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void A5_UpsertStillRemovesALiveOccupantThatLostComponents(bool hasPosition, bool hasCollider)
        {
            var scene = new A5GridScenario();
            EntityId entity = scene.CreateBody(default, 9, 20);
            if (!hasPosition)
                scene.Store.Remove(scene.Positions, entity);
            if (!hasCollider)
                scene.Store.Remove(scene.Colliders, entity);
            scene.Grid.Upsert(entity);
            Assert.That(scene.Store.IsAlive(entity), Is.True);
            Assert.That(scene.Grid.MembershipCount, Is.Zero);
            Assert.That(scene.Grid.Sphere(default, SimFix.FromInt(24), new EntityId[0]), Is.Zero);
        }

        [Test]
        public void A5_WarmQueryCleanupAndMissingComponentReuseAllocateNothing()
        {
            var windows = new AllocationWindow();
            do
            {
                var scene = new A5GridScenario();
                SimVec3 focus = SimVec3.Metres(4, 4, 4);
                EntityId survivor = scene.CreateBody(focus, 1, 2);
                var entities = new EntityId[8];
                var hits = new RayHit[8];
                scene.Store.CompleteWarmup();
                for (int round = 0; round < 32; round++)
                    Assert.That(CleanupCycle(scene, survivor, focus, entities, hits, round), Is.True);
                var meter = new AllocationMeter();
                windows.Begin(meter);
                bool valid = true;
                for (int round = 0; round < 512; round++)
                    valid &= CleanupCycle(scene, survivor, focus, entities, hits, round);
                AllocationSample allocated = windows.End();
                Assert.That(valid, Is.True, "Cleanup, slot reuse or survivor results changed during the allocation loop.");
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                Assert.That(scene.Grid.MembershipCount, Is.EqualTo(1));
                TestContext.WriteLine("A5 warm cleanup: rounds=512; synchronization cleanups=2560; missing-component reuses=2560; {0}.", allocated.Describe());
            }
            while (windows.NeedsRetry);
        }

        private static bool CleanupCycle(A5GridScenario scene, EntityId survivor, SimVec3 focus, EntityId[] entities, RayHit[] hits, int round)
        {
            bool valid = true;
            for (int query = 0; query < 5; query++)
            {
                EntityId dead = scene.CreateBody(focus, 9, 20);
                scene.Store.Destroy(dead);
                SpatialQueryKind kind = (SpatialQueryKind)query;
                int count = Query(scene.Grid, kind, focus, entities, hits);
                valid &= scene.Grid.MembershipCount > 1;
                scene.Grid.Synchronize();
                scene.Store.RecycleDestroyed();
                EntityId actual = kind == SpatialQueryKind.Ray ? hits[0].Entity : entities[0];
                valid &= count == 1 && actual.Equals(survivor) && scene.Grid.MembershipCount == 1;
                if (kind == SpatialQueryKind.Ray)
                    valid &= hits[0].Distance.Raw == 31 * SimFix.Scale;

                dead = scene.CreateBody(focus, 9, 20);
                scene.Store.Destroy(dead);
                scene.Store.RecycleDestroyed();
                EntityId replacement = scene.Store.Create();
                int components = (round + query) % 3;
                if (components == 1)
                    scene.Store.Set(scene.Positions, replacement, new Position(focus));
                if (components == 2)
                    scene.Store.Set(scene.Colliders, replacement, new Collider(SimFix.FromInt(1), SimFix.FromInt(2)));
                scene.Grid.Upsert(replacement);
                valid &= scene.Grid.MembershipCount == 1;
                scene.Store.Destroy(replacement);
                scene.Store.RecycleDestroyed();
            }

            return valid;
        }

        private static int Query(GridSpatialQuery grid, SpatialQueryKind kind, SimVec3 focus, EntityId[] entities, RayHit[] hits)
        {
            SimVec3 start = focus - SimVec3.Metres(32, 0, 0);
            switch (kind)
            {
                case SpatialQueryKind.Sphere:
                    return grid.Sphere(focus, SimFix.FromInt(24), entities);
                case SpatialQueryKind.Cylinder:
                    return grid.Cylinder(focus, SimFix.FromInt(24), SimFix.FromInt(16), entities);
                case SpatialQueryKind.Sector:
                    return grid.Sector(start, SimFix.FromInt(64), SimVec3.Metres(1, 0, 0), 600, SimFix.FromInt(16), entities);
                case SpatialQueryKind.Capsule:
                    return grid.Capsule(start, focus + SimVec3.Metres(32, 0, 0), SimFix.FromInt(4), entities);
                case SpatialQueryKind.Ray:
                    return grid.Ray(start, SimVec3.Metres(1, 0, 0), SimFix.FromInt(64), hits);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static void AssertHit(SpatialQueryKind kind, EntityId expected, int index, EntityId[] entities, RayHit[] hits)
        {
            if (kind == SpatialQueryKind.Ray)
            {
                Assert.That(hits[index].Entity, Is.EqualTo(expected));
                Assert.That(hits[index].Distance.Raw, Is.EqualTo(31 * SimFix.Scale));
            }
            else
                Assert.That(entities[index], Is.EqualTo(expected));
        }
    }
}
