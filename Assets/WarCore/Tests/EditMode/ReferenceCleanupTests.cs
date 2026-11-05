using System;
using System.Collections.Generic;
using NUnit.Framework;
using Simulation = WarCore.Sim.Sim;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("MF5ReferenceCleanup")]
    public sealed class ReferenceCleanupTests
    {
        [Test]
        public void LateSerializerRegistrationIncludesAlreadyPresentReferences()
        {
            var store = new EntityStore(128,IdentityMode.StableWorld);
            var seats = store.Register<Seat>("seat");
            EntityId host = store.Create(StableEntityKey.Placed(1,0));
            EntityId owner = store.Create(StableEntityKey.Placed(1,1));
            store.Set(seats,owner,new Seat(store.Link(host),0));
            store.RegisterSerializer(seats,new SeatSerializer());
            store.Destroy(host);
            Assert.That(store.TryGet(seats,owner,out _),Is.False,
                "Destroy must clean references set before serializer registration.");
        }

        private sealed class Notices : IEntityRetirementObserver
        {
            internal readonly List<string> Items = new List<string>();
            public void ReferenceCleared(StableRequestKind kind, EntityLink owner, EntityLink retired)
                => Items.Add(kind+":"+owner.Id.Value+":"+retired.Id.Value);
        }
        private sealed class World
        {
            internal readonly Simulation Sim;
            internal readonly ComponentType<Seat> Seats;
            internal readonly ComponentType<Occupant> Occupants;
            internal readonly ComponentType<SpatialAttachment> Attachments;
            internal readonly Notices Observer = new Notices();
            internal World(bool naive)
            {
                Sim = Simulation.CreateRetainedWorld(913,new SimConfig(entityCapacity:256),options:new RetainedWorldOptions(identityCapacity:4096));
                Seats = Sim.Entities.Register<Seat>("seat",new SeatSerializer());
                Occupants = Sim.Entities.Register<Occupant>("occupant",new OccupantSerializer());
                var positions = Sim.Entities.Register<Position>("position",new PositionSerializer());
                var colliders = Sim.Entities.Register<Collider>("collider",new ColliderSerializer());
                Sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(4));
                Attachments = Sim.Entities.Resolve<SpatialAttachment>();
                Sim.Entities.RegisterRetirementObserver(Observer);
                if (naive)
                {
                    NaiveReferenceCleanup<Seat>.Install(Sim.Entities,Seats);
                    NaiveReferenceCleanup<Occupant>.Install(Sim.Entities,Occupants);
                    NaiveReferenceCleanup<SpatialAttachment>.Install(Sim.Entities,Attachments);
                }
            }
            internal void Apply(int operation, EntityLink owner, EntityLink target, EntityLink other)
            {
                EntityStore store = Sim.Entities;
                EntityId entity = store.Resolve(owner);
                switch (operation)
                {
                    case 0: store.Set(Seats,entity,new Seat(target,3,other)); break;
                    case 1: store.Set(Occupants,entity,new Occupant(target)); break;
                    case 2: store.Set(Attachments,entity,new SpatialAttachment(store.Resolve(target),default,target)); break;
                    case 3: store.Remove(Seats,entity); break;
                    case 4: store.Remove(Occupants,entity); break;
                    case 5: store.Remove(Attachments,entity); break;
                    default: store.Destroy(entity); break;
                }
                Sim.CompleteSpatialBoundary();
            }
        }
        [TestCase(913), TestCase(2401), TestCase(104729), TestCase(887)]
        public void FuzzMatchesFullScanIncludingOrderedNoticesRestoreAndReuse(int seed)
        {
            var random = new Random(seed);
            var a = new World(false);
            var b = new World(true);
            var links = new EntityLink[48];
            ulong next = 0;
            for (int i = 0; i < links.Length; i++)
            {
                StableEntityKey key = StableEntityKey.Placed(1,next++);
                links[i] = a.Sim.Entities.Link(a.Sim.Entities.Create(key));
                b.Sim.Entities.Create(key);
            }
            int reused = 0, restores = 0, updates = 0, removed = 0;
            for (int step = 0; step < 1200; step++)
            {
                int owner = random.Next(links.Length);
                int operation = random.Next(9);
                EntityLink target = links[random.Next(links.Length)];
                EntityLink other = links[random.Next(links.Length)];
                a.Apply(operation,links[owner],target,other);
                b.Apply(operation,links[owner],target,other);
                CollectionAssert.AreEqual(b.Observer.Items,a.Observer.Items,"seed="+seed+" step="+step);
                foreach (string notice in a.Observer.Items)
                    if (notice.StartsWith("SeatOccupant:")) updates++; else removed++;
                a.Observer.Items.Clear(); b.Observer.Items.Clear();
                if (operation >= 6)
                {
                    StableEntityKey key = StableEntityKey.Placed(1,next++);
                    links[owner] = a.Sim.Entities.Link(a.Sim.Entities.Create(key));
                    b.Sim.Entities.Create(key);
                    reused++;
                }
                Assert.That(a.Sim.StateHashRetained(),Is.EqualTo(b.Sim.StateHashRetained()),"seed="+seed+" step="+step);
                if (step % 73 == 0)
                {
                    byte[] snapshot = a.Sim.SaveRetainedSnapshot();
                    CollectionAssert.AreEqual(snapshot,b.Sim.SaveRetainedSnapshot());
                    if (step % 146 == 0) { a = new World(false); b = new World(true); }
                    a.Sim.RestoreRetainedSnapshot(snapshot,step % 2 == 0);
                    b.Sim.RestoreRetainedSnapshot(snapshot,step % 2 == 0);
                    CollectionAssert.AreEqual(snapshot,a.Sim.SaveRetainedSnapshot());
                    restores++;
                }
            }
            Assert.That(reused,Is.GreaterThan(300));
            Assert.That(updates,Is.GreaterThan(0));
            Assert.That(removed,Is.GreaterThan(0));
            TestContext.Out.WriteLine("MF5-fuzz|seed="+seed+"; steps=1200; reused="+reused+"; restores="+restores
                +"; updated="+updates+"; removed="+removed+"; state/snapshot/ordered-notices=equal");
        }

        [Test]
        public void RemovingLastValueAndRestoringEmptyThenReusingSlotCleansExactlyOnce()
        {
            var world = new World(false);
            var store = world.Sim.Entities;
            EntityId host = store.Create(StableEntityKey.Placed(1,0));
            EntityId owner = store.Create(StableEntityKey.Placed(1,1));
            EntityLink hostLink = store.Link(host), ownerLink = store.Link(owner);
            for (int i = 0; i < 50; i++)
            {
                store.Set(world.Occupants,owner,new Occupant(hostLink));
                store.Set(world.Occupants,owner,new Occupant(hostLink));
                store.Remove(world.Occupants,owner);
            }
            byte[] empty = world.Sim.SaveRetainedSnapshot();
            store.Set(world.Occupants,owner,new Occupant(hostLink));
            world.Sim.RestoreRetainedSnapshot(empty,true);
            owner = store.Resolve(ownerLink); host = store.Resolve(hostLink);
            store.Destroy(host);
            Assert.That(world.Observer.Items,Is.Empty);
            world.Sim.CompleteSpatialBoundary();
            host = store.Create(StableEntityKey.Placed(1,2));
            store.Set(world.Occupants,owner,new Occupant(store.Link(host)));
            store.Destroy(host);
            Assert.That(world.Observer.Items.Count,Is.EqualTo(1));
            Assert.That(store.TryGet(world.Occupants,owner,out _),Is.False);
        }

        [Test]
        public void SparseSlotsAcrossRadixBytesNotifyInOriginalAscendingOrder()
        {
            var store = new EntityStore(1100,IdentityMode.StableWorld);
            var seats = store.Register<Seat>("seat",new SeatSerializer());
            var observer = new Notices(); store.RegisterRetirementObserver(observer);
            var entities = new EntityId[1050];
            for (int i = 0; i < entities.Length; i++) entities[i] = store.Create(StableEntityKey.Placed(1,(ulong)i));
            EntityLink host = store.Link(entities[512]);
            int[] slots = { 1,254,255,256,510,511,513,1024 };
            for (int i = slots.Length-1; i >= 0; i--) store.Set(seats,entities[slots[i]],new Seat(host,0));
            store.Remove(seats,entities[255]); store.Set(seats,entities[255],new Seat(host,0));
            store.Destroy(entities[512]);
            var expected = new List<string>();
            foreach (int slot in slots) expected.Add("SeatCarrier:"+store.Link(entities[slot]).Id.Value+":"+host.Id.Value);
            CollectionAssert.AreEqual(expected,observer.Items);
        }
        private sealed class ChangingObserver : IEntityRetirementObserver
        {
            internal EntityStore Store;
            internal ComponentType<SpatialAttachment> Type;
            internal EntityId[] Entities;
            internal readonly List<int> Slots = new List<int>();
            public void ReferenceCleared(StableRequestKind kind, EntityLink owner, EntityLink retired)
            {
                int slot = Store.Resolve(owner).Id;
                Slots.Add(slot);
                if (slot != 1) return;
                Store.Remove(Type,Entities[3]);
                Store.Set(Type,Entities[4],new SpatialAttachment(Entities[0],default,retired));
                Store.Set(Type,Entities[1],new SpatialAttachment(Entities[0],default,retired));
            }
        }
        [TestCase(false), TestCase(true)]
        public void ObserverChangesResumeAfterCurrentSlotLikeFullScan(bool naive)
        {
            var store = new EntityStore(16,IdentityMode.StableWorld);
            var type = store.Register<SpatialAttachment>("attachment",new SpatialAttachmentSerializer());
            var entities = new EntityId[6];
            for (int i = 0; i < entities.Length; i++) entities[i] = store.Create(StableEntityKey.Placed(1,(ulong)i));
            var observer = new ChangingObserver { Store=store,Type=type,Entities=entities };
            store.RegisterRetirementObserver(observer);
            if (naive) NaiveReferenceCleanup<SpatialAttachment>.Install(store,type);
            foreach (int slot in new[] {5,3,1})
                store.Set(type,entities[slot],new SpatialAttachment(entities[0],default,store.Link(entities[0])));
            store.Destroy(entities[0]);
            CollectionAssert.AreEqual(new[] {1,4,5},observer.Slots);
            Assert.That(store.TryGet(type,entities[1],out _),Is.True,"Old scan does not revisit the current slot.");
        }
    }
}
