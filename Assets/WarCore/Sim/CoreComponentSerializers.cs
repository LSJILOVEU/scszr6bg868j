namespace WarCore.Sim
{
    // Ids 1..999 are reserved for the engine; content owns its explicit ids above that range.
    public sealed class PositionSerializer : IStableComponentSerializer<Position>
    {
        public int TypeId => 1;
        public int Version => 1;
        public void Write(SimStateWriter writer, in Position value) => SnapshotValues.Vector(writer, value.Value);
        public Position Read(SimStateReader reader) => new Position(SnapshotValues.Vector(reader));
    }

    public sealed class ColliderSerializer : IStableComponentSerializer<Collider>
    {
        public int TypeId => 2;
        public int Version => 3;
        public void Write(SimStateWriter writer, in Collider value)
        {
            writer.Int32((int)value.ShapeKind);
            writer.Int64(value.Radius.Raw);
            writer.Int64(value.Height.Raw);
            writer.UInt64(value.Layer);
            writer.Bool(value.IsStatic);
            // Capsule records are byte-identical to version 3; only boxes append their payload.
            if (value.ShapeKind == ShapeKind.OrientedBox)
            {
                writer.Int64(value.HalfDepth.Raw);
                writer.Int32(value.Yaw.M);
                writer.Int32(value.Yaw.N);
            }
        }

        public Collider Read(SimStateReader reader)
        {
            // Validate the full int before narrowing to the byte-sized enum.
            int shape = reader.Int32();
            SimStateReader.Require(shape == (int)ShapeKind.VerticalCapsule || shape == (int)ShapeKind.OrientedBox, "Unknown collider shape.");
            SimFix radius = SimFix.FromRaw(reader.Int64());
            SimFix height = SimFix.FromRaw(reader.Int64());
            ulong layer = reader.UInt64();
            SimStateReader.Require(layer <= uint.MaxValue, "Collider layer must have zero high 32 bits.");
            bool isStatic = reader.Bool();
            if (shape == (int)ShapeKind.VerticalCapsule)
                return new Collider(radius, height, (uint)layer, ShapeKind.VerticalCapsule, isStatic);
            long depth = reader.Int64();
            int m = reader.Int32();
            int n = reader.Int32();
            SimStateReader.Require(BoxYaw.IsCanonical(m, n), "Collider yaw must be a canonical generator.");
            SimStateReader.Require(depth >= 0 && depth <= Collider.BoxHalfExtentLimit, "Collider box depth is out of range.");
            // Checked here so a corrupt record fails as a snapshot error, not as Collider.Box's argument exception.
            SimStateReader.Require(radius.Raw >= 0 && radius.Raw <= Collider.BoxHalfExtentLimit, "Collider box width is out of range.");
            SimStateReader.Require(height.Raw >= 0 && height.Raw <= Collider.HeightLimit, "Collider box height is out of range.");
            return Collider.Box(radius, height, SimFix.FromRaw(depth), BoxYaw.FromGenerator(m, n), (uint)layer, isStatic);
        }
    }

    public sealed class SquadIdSerializer : IStableComponentSerializer<SquadId>
    {
        public int TypeId => 3;
        public int Version => 1;
        public void Write(SimStateWriter writer, in SquadId value) => writer.Int32(value.Value);
        public SquadId Read(SimStateReader reader) => new SquadId(reader.Int32());
    }
}
