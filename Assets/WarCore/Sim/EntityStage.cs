using System;

namespace WarCore.Sim
{
    // Consumers share one published array. A view is valid only until the next synchronization.
    public sealed class EntityStage
    {
        private readonly EntityStore store;
        public EntityStage(EntityStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }
        /// <summary>Borrow the published stable set. A standalone store publishes on its first Capture only;
        /// later births require an explicit SynchronizeStableEntities call before consumers can see them.</summary>
        public View Capture() => store.IdentityMode == IdentityMode.StableWorld
            ? store.CaptureStableEntities() : new View(store,null,0);
        public readonly struct View
        {
            private readonly EntityStore store;
            private readonly EntityId[] members;
            private readonly int count;
            internal View(EntityStore store, EntityId[] members, int count)
            {
                this.store = store;
                this.members = members;
                this.count = count;
            }
            public Enumerator GetEnumerator() => new Enumerator(store, members, count);
        }
        public struct Enumerator
        {
            private readonly EntityStore store;
            private readonly EntityId[] members;
            private readonly int count;
            private EntityStore.EntityEnumerator legacy;
            private int index;
            public EntityId Current { get; private set; }
            internal Enumerator(EntityStore store, EntityId[] members, int count)
            {
                this.store = store;
                this.members = members;
                this.count = count;
                legacy = store.GetEnumerator();
                index = 0;
                Current = default;
            }
            public bool MoveNext()
            {
                if (members == null)
                {
                    bool found = legacy.MoveNext();
                    if (found)
                        Current = legacy.Current;
                    return found;
                }
                while (index < count)
                {
                    Current = members[index++];
                    if (store.IsAlive(Current))
                        return true;
                }
                return false;
            }
        }
    }
}
