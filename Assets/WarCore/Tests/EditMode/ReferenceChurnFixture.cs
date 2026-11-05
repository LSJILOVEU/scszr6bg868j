using WarCore.Sim;

namespace WarCore.Tests
{
    internal sealed class ReferenceChurnFixture : IEntityRetirementObserver
    {
        internal const int Count = 64;
        private readonly EntityStore store;
        private readonly EntityId[] entities;
        private readonly EntityId[] owners = new EntityId[Count];
        private readonly ComponentType<Seat> seats;
        private readonly ComponentType<Occupant> occupants;
        private readonly ComponentType<SpatialAttachment> attachments;
        internal readonly IComponents[] Cleanup;
        internal long Notices;
        internal ReferenceChurnFixture(EntityStore store, EntityId[] entities, bool naive)
        {
            this.store = store;
            this.entities = entities;
            seats = store.Register<Seat>("seat",new SeatSerializer());
            occupants = store.Register<Occupant>("occupant",new OccupantSerializer());
            attachments = store.Resolve<SpatialAttachment>();
            // Same ordinal storage order as EntityStore: occupant, seat, warcore.spatial-attachment.
            Cleanup = new IComponents[] { occupants.Storage,seats.Storage,attachments.Storage };
            if (naive)
            {
                Cleanup[0] = NaiveReferenceCleanup<Occupant>.Install(store,occupants);
                Cleanup[1] = NaiveReferenceCleanup<Seat>.Install(store,seats);
                Cleanup[2] = NaiveReferenceCleanup<SpatialAttachment>.Install(store,attachments);
            }
            store.RegisterRetirementObserver(this);
        }
        internal void SaveState(SimStateWriter writer)
        {
            writer.Int64(Notices); writer.Int32(owners.Length);
            for (int i = 0; i < owners.Length; i++) writer.Link(store.Link(owners[i]));
        }
        internal void LoadState(SimStateReader reader)
        {
            Notices = reader.Int64();
            SimStateReader.Require(Notices >= 0 && reader.Int32() == owners.Length,"Invalid reference fixture state.");
            for (int i = 0; i < owners.Length; i++)
                owners[i] = store.Resolve(reader.Link());
        }
        internal void Prepare(int cursor)
        {
            for (int i = 0; i < Count; i++)
            {
                if (store.IsAlive(owners[i]))
                {
                    store.Remove(seats,owners[i]);
                    store.Remove(occupants,owners[i]);
                    store.Remove(attachments,owners[i]);
                }
            }
            for (int i = 0; i < Count; i++)
            {
                EntityId owner = entities[(cursor+Round4ChurnScene.ChurnPerTick+i)%entities.Length];
                EntityId target = entities[(cursor+i)%entities.Length];
                owners[i] = owner;
                EntityLink link = store.Link(target);
                store.Set(seats,owner,new Seat(store.Link(owner),i,link));
                store.Set(occupants,owner,new Occupant(link));
                store.Set(attachments,owner,new SpatialAttachment(target,default,link));
            }
        }
        internal void VerifyPrepared()
        {
            foreach (IComponents storage in Cleanup)
            {
                int count = 0;
                for (int slot = 0; slot < store.Capacity; slot++)
                    if (storage.HasStableComponent(slot)) count++;
                Round4LongRun.Require(count == Count,"Reference churn must start with exactly 64 values per storage.");
            }
        }
        public void ReferenceCleared(StableRequestKind kind, EntityLink owner, EntityLink retired) => Notices++;
    }
}
