using System;

namespace WarCore.Sim
{
    internal readonly struct SpatialGridBounds
    {
        internal readonly long X0;
        internal readonly long Y0;
        internal readonly long Z0;
        internal readonly long X1;
        internal readonly long Y1;
        internal readonly long Z1;

        internal SpatialGridBounds(in SpatialBody body, long size)
        {
            // Boxes use their outward-rounded world AABB; capsule extents are the radius on both axes.
            long radiusX = SpatialBox.ExtentX(body.Collider);
            long radiusZ = SpatialBox.ExtentZ(body.Collider);
            long halfHeight = body.Collider.Height.Raw / 2;
            X0 = Floor(body.Position.X.Raw - radiusX, size);
            X1 = Floor(body.Position.X.Raw + radiusX, size);
            Y0 = Floor(body.Position.Y.Raw - halfHeight, size);
            Y1 = Floor(body.Position.Y.Raw + halfHeight, size);
            Z0 = Floor(body.Position.Z.Raw - radiusZ, size);
            Z1 = Floor(body.Position.Z.Raw + radiusZ, size);
        }

        internal static long Floor(long raw, long size) => raw >= 0 ? raw / size : (raw + 1) / size - 1;

        internal int Volume(int limit)
        {
            long x = X1 - X0 + 1;
            long y = Y1 - Y0 + 1;
            long z = Z1 - Z0 + 1;
            // World bounds give positive widths; budget division precedes every volume product.
            if (x > limit || y > limit / x || z > limit / x / y)
                return -1;
            return (int)(x * y * z);
        }
    }

    internal sealed class SpatialPartitionState
    {
        internal readonly int[] LargeIds;
        internal readonly int[] OrderedLargeIds;
        internal readonly SpatialCellTable Table;
        internal readonly int MembershipCapacity;
        internal int LargeCount;
        internal int BodyCount;
        internal int LeafCount;
        internal uint LayerUnion;
        private readonly string membershipError;

        internal SpatialPartitionState(int entities, int members, int cells, bool regions, bool stableOrder = false)
        {
            LargeIds = new int[entities];
            OrderedLargeIds = stableOrder ? new int[entities] : null;
            Table = new SpatialCellTable(cells, members * 5, regions);
            MembershipCapacity = members;
            membershipError = "Spatial leaf membership capacity " + members + " exhausted.";
        }

        internal void Reset()
        {
            Table.Reset();
            LargeCount = 0;
            BodyCount = 0;
            LeafCount = 0;
            LayerUnion = 0;
        }

        internal void Reserve(int volume)
        {
            if (volume > MembershipCapacity - LeafCount)
                throw new SimBoundaryException(membershipError);
            LeafCount += volume;
        }

        internal long AllocatedBytes => LargeIds.LongLength * 4 + (OrderedLargeIds == null ? 0 : OrderedLargeIds.LongLength * 4) + Table.AllocatedBytes;
        internal long UsedBytes => (long)LargeCount * (OrderedLargeIds == null ? 4 : 8) + Table.UsedBytes;
    }

    // One shared body bank and two homogeneous indexes publish as a single transaction.
    // The spare bank is preallocated sync workspace, never query scratch.
    internal sealed class SpatialGridState
    {
        internal readonly SpatialBodyColumns Bodies;
        internal readonly bool[] Present;
        internal readonly SpatialGridBounds[] Bounds;
        internal readonly SpatialPartitionState Static;
        internal readonly SpatialPartitionState Unit;
        internal int BodyCount => Static.BodyCount + Unit.BodyCount;
        internal int LargeCount => Static.LargeCount + Unit.LargeCount;
        internal int LeafCount => Static.LeafCount + Unit.LeafCount;
        internal uint LayerUnion => Static.LayerUnion | Unit.LayerUnion;

        internal SpatialGridState(int entities, int members, int cells, bool regions, int staticMembers, int staticCells, bool stableOrder = false)
        {
            Bodies = new SpatialBodyColumns(entities);
            Present = new bool[entities];
            Bounds = new SpatialGridBounds[entities];
            Static = new SpatialPartitionState(entities, staticMembers, staticCells, regions, stableOrder);
            Unit = new SpatialPartitionState(entities, members, cells, regions, stableOrder);
        }

        internal SpatialPartitionState Partition(int id) => Bodies.Colliders[id].IsStatic ? Static : Unit;
        internal long AllocatedBytes => Bodies.AllocatedBytes + Present.LongLength + Bounds.LongLength * 48 + Static.AllocatedBytes + Unit.AllocatedBytes;
        internal long UsedBytes => (long)BodyCount * 193 + Static.UsedBytes + Unit.UsedBytes;
    }
}
