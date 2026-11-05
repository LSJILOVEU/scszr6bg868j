using System;

namespace WarCore.Sim
{
    // Both partitions share these columns and the geometry view by entity slot.
    // Numeric kernels consume bodies by reference; materialize once during publication,
    // rather than reconstructing the 72-byte value for every query candidate.
    internal sealed class SpatialBodyColumns
    {
        internal readonly EntityId[] Entities;
        internal readonly SimVec3[] Positions;
        internal readonly Collider[] Colliders;
        internal readonly SpatialBody[] Geometry;
        internal int Length => Entities.Length;

        internal SpatialBodyColumns(int capacity)
        {
            Entities = new EntityId[capacity];
            Positions = new SimVec3[capacity];
            Colliders = new Collider[capacity];
            Geometry = new SpatialBody[capacity];
        }

        internal SpatialBody this[int id]
        {
            get => SpatialBody.FromValidated(Entities[id], Positions[id], Colliders[id]);
            set
            {
                Entities[id] = value.Entity;
                Positions[id] = value.Position;
                Colliders[id] = value.Collider;
                Geometry[id] = value;
            }
        }

        internal void CopyTo(SpatialBodyColumns other)
        {
            Array.Copy(Entities, other.Entities, Length);
            Array.Copy(Positions, other.Positions, Length);
            Array.Copy(Colliders, other.Colliders, Length);
            Array.Copy(Geometry, other.Geometry, Length);
        }

        internal long AllocatedBytes => (long)Length * 144;
    }
}
