namespace WarCore.Sim
{
    public sealed partial class EntityStore
    {
        // Resolve the component handle during assembly; traversal then reads arrays without lookups.
        public ComponentView<T> With<T>(ComponentType<T> type) where T : unmanaged, IStateHash
            => new ComponentView<T>(this, Require(type));

        public readonly struct ComponentView<T> where T : unmanaged, IStateHash
        {
            private readonly EntityStore owner;
            private readonly Components<T> storage;

            internal ComponentView(EntityStore owner, Components<T> storage)
            {
                this.owner = owner;
                this.storage = storage;
            }

            public ComponentEnumerator<T> GetEnumerator() => new ComponentEnumerator<T>(owner, storage);
        }

        // Capture the allocated slot limit so appending entities cannot extend a traversal forever.
        // Presence and liveness are read at each visit: removed/dead entries are skipped, new
        // components are visible, and a new generation in an unvisited recycled slot is visible.
        // Appended slots and already visited slots are not visited. End traversal before restoring.
        public struct ComponentEnumerator<T> where T : unmanaged, IStateHash
        {
            private readonly EntityStore owner;
            private readonly Components<T> storage;
            private readonly int limit;
            private int index;
            public EntityId Current { get; private set; }

            internal ComponentEnumerator(EntityStore owner, Components<T> storage)
            {
                this.owner = owner;
                this.storage = storage;
                limit = owner.allocated;
                index = -1;
                Current = default;
            }

            public bool MoveNext()
            {
                while (index < limit - 1)
                {
                    index++;
                    if (!owner.alive[index] || !storage.Present[index])
                        continue;
                    // Capture the generation at yield time so destroying Current cannot change its identity.
                    Current = new EntityId(index, owner.generations[index]);
                    return true;
                }
                return false;
            }
        }
    }
}
