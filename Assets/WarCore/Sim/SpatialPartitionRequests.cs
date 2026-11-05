using System;

namespace WarCore.Sim
{
    public sealed partial class Sim
    {
        private ComponentType<Collider> spatialColliders;
        private EntityId[] staticRequestOwners;
        private byte[] staticRequestFlags;

        public void RequestColliderStatic(EntityId entity, bool isStatic)
        {
            RequireLegacyMotion();
            RequestColliderStaticCore(entity,isStatic);
        }

        private void RequestColliderStaticCore(EntityId entity, bool isStatic)
        {
            RequireSpatialEntity(entity);
            if (!Entities.TryGet(spatialColliders, entity, out _))
                throw new SimBoundaryException("Static changes require a live Collider.");
            if (entity.Id >= staticRequestFlags.Length)
                throw new SimBoundaryException("Static request entity exceeds the reserved spatial capacity.");
            // The last request wins; publication stays at the engine's fixed sync boundary.
            staticRequestOwners[entity.Id] = entity;
            staticRequestFlags[entity.Id] = isStatic ? (byte)2 : (byte)1;
        }

        private bool HasStaticRequest(int id) => staticRequestFlags[id] != 0
            && Entities.TryGet(spatialColliders, staticRequestOwners[id], out _);

        private int StaticRequestCount()
        {
            int count = 0;
            if (staticRequestFlags != null)
                for (int i = 0; i < staticRequestFlags.Length; i++)
                    if (HasStaticRequest(i))
                        count++;
            return count;
        }

        private void SettleStaticRequests()
        {
            if (spatialIndex == null)
                return;
            for (int i = 0; i < staticRequestFlags.Length; i++)
            {
                if (staticRequestFlags[i] != 0 && Entities.TryGet(spatialColliders, staticRequestOwners[i], out Collider collider))
                    Entities.Set(spatialColliders, staticRequestOwners[i], collider.WithStatic(staticRequestFlags[i] == 2));
                staticRequestFlags[i] = 0;
            }
        }

        private void WriteStaticRequests(StateHash hash)
        {
            // A separate tail preserves the old component manifest and every omitted-static byte.
            if (hash.IgnoreColliderStatic)
                return;
            int count = StaticRequestCount();
            if (count == 0)
                return;
            hash.Add("warcore.static-requests-v1");
            hash.Add(count);
            for (int i = 0; i < staticRequestFlags.Length; i++)
                if (HasStaticRequest(i))
                {
                    staticRequestOwners[i].WriteState(hash);
                    hash.Add(staticRequestFlags[i] == 2);
                }
        }

        private void SaveStaticRequests(SimStateWriter writer)
        {
            writer.Int32(StaticRequestCount());
            if (staticRequestFlags == null)
                return;
            for (int i = 0; i < staticRequestFlags.Length; i++)
                if (HasStaticRequest(i))
                {
                    SnapshotValues.Entity(writer, staticRequestOwners[i]);
                    writer.Bool(staticRequestFlags[i] == 2);
                }
        }

        private void LoadStaticRequests(SimStateReader reader)
        {
            if (staticRequestFlags != null)
                Array.Clear(staticRequestFlags, 0, staticRequestFlags.Length);
            int count = reader.Count(reader.Remaining / 13);
            SimStateReader.Require(count <= (staticRequestFlags == null ? 0 : staticRequestFlags.Length), "Static request capacity mismatch.");
            int previous = -1;
            for (int i = 0; i < count; i++)
            {
                EntityId owner = SnapshotValues.Entity(reader);
                bool stationary = reader.Bool();
                SimStateReader.Require(owner.Id > previous && owner.Id < staticRequestFlags.Length
                    && Entities.TryGet(spatialColliders, owner, out _), "Invalid static request owner or order.");
                staticRequestOwners[owner.Id] = owner;
                staticRequestFlags[owner.Id] = stationary ? (byte)2 : (byte)1;
                previous = owner.Id;
            }
        }
    }
}
