using System;

namespace WarCore.Sim
{
    internal sealed partial class Components<T>
    {
        public bool HasStableComponent(int slot) => Present[slot];
        public void WriteStableComponent(SimStateWriter writer, int slot)
        {
            if (Serializer is IStableReferenceValidator<T> validator)
                validator.ValidateReferences(writer.Entities,in Values[slot]);
            writer.String(Name);
            writer.Int32(SerializerId);
            writer.Int32(SerializerVersion);
            writer.Component(Serializer,in Values[slot]);
        }
        public void ReadStableComponent(SimStateReader reader, int slot)
        {
            SimStateReader.Require(Serializer is IStableComponentSerializer<T>,"An explicit stable component schema is required.");
            SimStateReader.Require(reader.String() == Name && reader.Int32() == SerializerId
                && reader.Int32() == SerializerVersion, "Stable component manifest mismatch.");
            SimStateReader section = reader.Section();
            Values[slot] = Serializer.Read(section);
            Present[slot] = true;
            MarkReferenceValue(slot);
            section.End();
        }
    }

    public sealed partial class EntityStore
    {
        private StateHash entityLeafHash;
        private SimStateWriter entityLeafWriter;
        private int[] identityOrder;
        private ulong identityOrderRevision = ulong.MaxValue;
        internal int OrderedIdentity(int index)
        {
            if (identityOrderRevision != IdentityRevision)
            {
                for (int i = 0; i < identityCount; i++)
                    identityOrder[i] = i;
                for (int root = identityCount / 2 - 1; root >= 0; root--)
                    SiftIdentity(root,identityCount);
                for (int end = identityCount - 1; end > 0; end--)
                {
                    SwapIdentity(0,end);
                    SiftIdentity(0,end);
                }
                identityOrderRevision = IdentityRevision;
            }
            return identityOrder[index];
        }
        private void SwapIdentity(int a, int b)
        {
            int value = identityOrder[a];
            identityOrder[a] = identityOrder[b];
            identityOrder[b] = value;
        }
        private bool BeforeIdentity(int a, int b) => identities[identityOrder[a]].Id.CompareTo(identities[identityOrder[b]].Id) < 0;
        private void SiftIdentity(int root, int length)
        {
            while (root < length / 2)
            {
                int child = root * 2 + 1;
                if (child + 1 < length && BeforeIdentity(child,child+1))
                    child++;
                if (!BeforeIdentity(root,child))
                    return;
                SwapIdentity(root,child);
                root = child;
            }
        }
        internal EntityId LogicalEntityAt(int orderedIndex) => identities[OrderedIdentity(orderedIndex)].Address;
        internal IdentityLife LogicalLifeAt(int orderedIndex) => identities[OrderedIdentity(orderedIndex)].Life;
        internal EntityId ResolveLogical(EntityLink link)
        {
            int index = link.HasValue ? FindIdentity(link.Id) : -1;
            if (index < 0 || identities[index].Life == IdentityLife.Retired)
                throw new SimSnapshotException("A logical entity reference is unresolved.");
            return identities[index].Address;
        }
        private void WriteLogicalEntity(SimStateWriter writer, in IdentityRecord record)
        {
            writer.String("warcore.entity-stable-v1");
            writer.UInt64(record.Id.Value);
            writer.Int32((int)record.Life);
            int slot = record.Address.Slot;
            int components = 0;
            foreach (IComponents storage in storages)
                if (storage.HasStableComponent(slot))
                    components++;
            writer.Int32(components);
            foreach (IComponents storage in storages)
                if (storage.HasStableComponent(slot))
                    storage.WriteStableComponent(writer,slot);
        }
        private static void WriteIdentityRecord(SimStateWriter writer, in IdentityRecord record)
        {
            writer.UInt64(record.Id.Value);
            writer.UInt64(record.Source.Domain);
            writer.UInt64(record.Source.First);
            writer.UInt64(record.Source.Second);
            writer.UInt64(record.Source.Third);
            writer.Int32((int)record.Life);
            writer.UInt64(record.Chunk);
            writer.Int64(record.RetiredAt);
            if (writer.Entities.UsesRetainedProtocol)
                writer.UInt64(record.RetiredSpawnSequence);
        }
        // Upgrade seam: replace recomputation with cached leaves after every ref-write is tracked.
        // The protocol is count plus modular sum, independent of any future cache layout.
        internal void WriteEntitySet(SimStateWriter writer)
        {
            ulong sum = 0;
            ulong count = 0;
            for (int i = 0; i < identityCount; i++)
            {
                if (identities[i].Life == IdentityLife.Retired)
                    continue;
                entityLeafWriter.ResetStable();
                WriteLogicalEntity(entityLeafWriter,in identities[i]);
                sum = unchecked(sum + StableEntityKey.Mix(entityLeafHash.Value));
                count++;
            }
            writer.UInt64(count);
            writer.UInt64(sum);
            sum = 0;
            for (int i = 0; i < identityCount; i++)
            {
                entityLeafWriter.ResetStable();
                WriteIdentityRecord(entityLeafWriter,in identities[i]);
                sum = unchecked(sum + StableEntityKey.Mix(entityLeafHash.Value));
            }
            writer.Int32(identityCount);
            writer.UInt64(sum);
            writer.Int64(RetiredRetentionTicks);
        }
        internal void SaveStableEntities(SimStateWriter writer)
        {
            writer.Int32(identityCount);
            for (int i = 0; i < identityCount; i++)
                WriteIdentityRecord(writer,in identities[OrderedIdentity(i)]);
            for (int i = 0; i < identityCount; i++)
            {
                ref IdentityRecord record = ref identities[OrderedIdentity(i)];
                if (record.Life == IdentityLife.Retired)
                    continue;
                WriteLogicalEntity(writer,in record);
            }
        }
        internal void LoadStableEntities(SimStateReader reader, bool reverseAllocation, IStableRestoreObserver observer = null)
        {
            int count = reader.Count(identities.Length);
            var restored = new IdentityRecord[count];
            ulong previous = 0;
            int living = 0;
            for (int i = 0; i < count; i++)
            {
                var id = new StableId(reader.UInt64());
                var source = new StableEntityKey(reader.UInt64(),reader.UInt64(),reader.UInt64(),reader.UInt64());
                var life = (IdentityLife)reader.Int32();
                ulong chunk = reader.UInt64();
                long retired = reader.Int64();
                ulong spawn = UsesRetainedProtocol ? reader.UInt64() : 0;
                SimStateReader.Require((i == 0 || id.Value > previous) && id.Equals(source.Id)
                    && life >= IdentityLife.Active && life <= IdentityLife.Retired && retired >= 0,
                    "Invalid, duplicate or unordered stable identity record.");
                restored[i] = new IdentityRecord { Id=id, Source=source, Life=life, Chunk=chunk, RetiredAt=retired, RetiredSpawnSequence=spawn };
                if (life != IdentityLife.Retired)
                    living++;
                previous = id.Value;
            }
            SimStateReader.Require(living <= Capacity,"Stable entity capacity exceeded.");
            ulong epoch = restoreGeneration;
            foreach (ulong generation in generations)
                if (generation > epoch)
                    epoch = generation;
            if (epoch == ulong.MaxValue)
                throw new SimBoundaryException("Entity restore generation exhausted.");
            restoreGeneration = epoch + 1;
            Array.Clear(alive,0,alive.Length);
            Array.Clear(pending,0,pending.Length);
            Array.Clear(identityBuckets,0,identityBuckets.Length);
            Array.Clear(identities,0,identities.Length);
            foreach (IComponents storage in storages)
                for (int slot = 0; slot < Capacity; slot++)
                    storage.Clear(slot);
            for (int slot = 0; slot < Capacity; slot++)
                generations[slot] = restoreGeneration;
            identityCount = count;
            ObserveIdentityCount();
            allocated = living;
            freeCount = 0;
            pendingCount = 0;
            int cursor = 0;
            for (int i = 0; i < count; i++)
            {
                IdentityRecord record = restored[i];
                if (record.Life != IdentityLife.Retired)
                {
                    int slot = reverseAllocation ? living - cursor - 1 : cursor;
                    cursor++;
                    record.Address = new EntityRef(slot,restoreGeneration);
                    alive[slot] = record.Life == IdentityLife.Active;
                    stableIds[slot] = record.Id;
                    identitySlots[slot] = i;
                }
                identities[i] = record;
                AddIdentityBucket(i);
            }
            IdentityChanged();
            ResetActiveSet();
            observer?.Reached(StableRestorePhase.IdentityMapped);
            for (int i = 0; i < count; i++)
            {
                IdentityRecord record = identities[i];
                if (record.Life == IdentityLife.Retired)
                    continue;
                SimStateReader.Require(reader.String() == "warcore.entity-stable-v1" && reader.UInt64() == record.Id.Value
                    && reader.Int32() == (int)record.Life,"Stable entity record mismatch.");
                int components = reader.Count(storages.Count);
                int at = 0;
                for (int c = 0; c < components; c++)
                {
                    // The reader peeks only the name; schema decoding still consumes the full record.
                    string name = reader.PeekString();
                    while (at < storages.Count && StringComparer.Ordinal.Compare(storages[at].Name,name) < 0)
                        at++;
                    SimStateReader.Require(at < storages.Count && storages[at].Name == name,"Unknown or unordered stable component.");
                    storages[at++].ReadStableComponent(reader,record.Address.Slot);
                }
                SimStateReader.Require(spawnSequences.Storage.Present[record.Address.Slot],"Stable entity is missing its required spawn counter.");
            }
            observer?.Reached(StableRestorePhase.ComponentsBound);
        }
    }
}
