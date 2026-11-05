using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class StableLifecycleTests
    {
        [Test]
        public void SpawnRequestsAreSortedBeforeCountersAndSurvivePendingSnapshot()
        {
            var a = new Simulation(1,config:new SimConfig(entityCapacity:32),identityMode:IdentityMode.StableWorld,identityCapacity:64);
            var b = new Simulation(1,config:new SimConfig(entityCapacity:32),identityMode:IdentityMode.StableWorld,identityCapacity:64);
            EntityId pa = a.Entities.Create(StableEntityKey.Placed(1,1));
            EntityId pb = b.Entities.Create(StableEntityKey.Placed(1,1));
            for (int i = 0; i < 8; i++)
            {
                a.Spawns.Enqueue(a.Entities.Link(pa),new StableRequestKey(1,0,a.Entities.StableOf(pa),2,(ulong)i,(ulong)(i%2)));
                int j = 7-i;
                b.Spawns.Enqueue(b.Entities.Link(pb),new StableRequestKey(1,0,b.Entities.StableOf(pb),2,(ulong)j,(ulong)(j%2)));
            }
            Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()),"pending requests");
            b.RestoreStableSnapshot(a.SaveStableSnapshot(),true);
            a.Spawns.Commit();
            b.Spawns.Commit();
            Assert.That(a.StateHashStable(),Is.EqualTo(b.StateHashStable()),"spawned");
            Assert.That(a.Spawns.Counter(a.Entities.Link(pa)),Is.EqualTo(8UL));
            for (ulong i = 0; i < 8; i++)
            {
                StableId id = StableEntityKey.Spawned(a.Entities.StableOf(pa),i,i%2).Id;
                Assert.That(a.Entities.TryResolve(id,out _),Is.True);
                Assert.That(b.Entities.TryResolve(id,out _),Is.True);
            }
        }

        [Test]
        public void ColdAndRetiredKeysRemainReservedAndWeakLinksNeverBindReusedSlots()
        {
            var sim = new Simulation(1,config:new SimConfig(entityCapacity:4),identityMode:IdentityMode.StableWorld,identityCapacity:12);
            var key = StableEntityKey.Placed(1,1);
            EntityId entity = sim.Entities.Create(key);
            EntityLink link = sim.Entities.Link(entity);
            sim.Entities.MakeCold(entity);
            Assert.That(sim.Entities.TryResolve(key.Id,out _),Is.False);
            Assert.That(sim.Entities.LifeOf(key.Id),Is.EqualTo(IdentityLife.Cold));
            Assert.Throws<StableIdentityConflictException>(() => sim.Entities.Create(key));
            EntityId active = sim.Entities.Reactivate(key.Id);
            Assert.That(sim.Entities.IsAlive(entity),Is.False);
            sim.Entities.Destroy(active);
            sim.CompleteSpatialBoundary();
            EntityId replacement = sim.Entities.Create(StableEntityKey.Placed(1,2));
            Assert.That(replacement.Id,Is.EqualTo(entity.Id));
            Assert.That(sim.Entities.TryResolve(link.Id,out _),Is.False);
            Assert.Throws<StableIdentityConflictException>(() => sim.Entities.Create(key));
            byte[] snapshot = sim.SaveStableSnapshot();
            ulong hash = sim.StateHashStable();
            sim.RestoreStableSnapshot(snapshot);
            Assert.That(sim.StateHashStable(),Is.EqualTo(hash));
        }

        [Test]
        public void ExactDigestCollisionReportsBothSourcesAndDoesNotConsumeCapacity()
        {
            var store = new EntityStore(4,IdentityMode.StableWorld);
            var a = StableEntityKey.Placed(1,1);
            var b = StableEntityKey.Placed(1,2);
            store.Create(a);
            var error = Assert.Throws<StableIdentityConflictException>(() => store.CreateIdentity(b,a.Id));
            Assert.That(error.Existing,Is.EqualTo(a));
            Assert.That(error.Requested,Is.EqualTo(b));
            Assert.That(error.Identity,Is.EqualTo(a.Id));
            Assert.That(store.RegisteredIdentityCount,Is.EqualTo(1));
        }

        [Test]
        public void ContentManifestIsFrozenAndRejectsKeyCollisionsAtBuild()
        {
            var entries = new[] {new StableContentKey("b",2),new StableContentKey("a",1)};
            var manifest = new StableContentManifest(7,entries);
            entries[0] = new StableContentKey("changed",9);
            Assert.That(manifest.Key("b"),Is.EqualTo(2));
            Assert.Throws<SimBoundaryException>(() => new StableContentManifest(7,new StableContentKey("a",1),new StableContentKey("b",1)));
            Assert.That(manifest.Digest,Is.EqualTo(new StableContentManifest(7,new StableContentKey("a",1),new StableContentKey("b",2)).Digest));
        }
    }
}
