namespace WarCore.Sim
{
    public sealed class SeatSerializer : IStableComponentSerializer<Seat>, IStableReferenceValidator<Seat>, IStableReferenceCleanup<Seat>
    {
        public ReferenceCleanup ClearRetiredReference(ref Seat value, EntityLink retired, out StableRequestKind kind)
        {
            kind = StableRequestKind.SeatCarrier;
            if (value.CarrierLink.Equals(retired))
                return ReferenceCleanup.Removed;
            kind = StableRequestKind.SeatOccupant;
            if (!value.OccupantLink.Equals(retired))
                return ReferenceCleanup.None;
            value = new Seat(value.CarrierLink,value.Index);
            return ReferenceCleanup.Updated;
        }
        public void ValidateReferences(EntityStore store, in Seat value)
        {
            if (!value.StableLinks)
                throw new SimBoundaryException("Stable Seat requires captured entity links.");
            store.ValidateLink(value.CarrierLink,true,false);
            store.ValidateLink(value.OccupantLink,true);
        }
        public int TypeId => 7;
        public int Version => 1;
        public void Write(SimStateWriter writer, in Seat value)
        {
            if (writer.IsStable)
            {
                if (!value.StableLinks)
                    throw new SimBoundaryException("Stable Seat requires captured entity links.");
                writer.Link(value.CarrierLink);
                writer.Int32(value.Index);
                writer.Link(value.OccupantLink);
                return;
            }
            SnapshotValues.Entity(writer, value.Carrier);
            writer.Int32(value.Index);
            SnapshotValues.Entity(writer, value.Occupant);
        }

        public Seat Read(SimStateReader reader)
        {
            if (!reader.IsStable)
                return new Seat(SnapshotValues.Entity(reader),reader.Int32(),SnapshotValues.Entity(reader));
            EntityLink carrier = reader.Link();
            int index = reader.Int32();
            EntityLink occupant = reader.Link();
            reader.Entities.ResolveLogical(carrier);
            if (occupant.HasValue)
                reader.Entities.ResolveLogical(occupant);
            return new Seat(carrier,index,occupant);
        }
    }

    public sealed class OccupantSerializer : IStableComponentSerializer<Occupant>, IStableReferenceValidator<Occupant>, IStableReferenceCleanup<Occupant>
    {
        public ReferenceCleanup ClearRetiredReference(ref Occupant value, EntityLink retired, out StableRequestKind kind)
        {
            kind = StableRequestKind.OccupantSeat;
            return value.SeatLink.Equals(retired) ? ReferenceCleanup.Removed : ReferenceCleanup.None;
        }
        public void ValidateReferences(EntityStore store, in Occupant value)
        {
            if (!value.StableLinks)
                throw new SimBoundaryException("Stable Occupant requires a captured entity link.");
            store.ValidateLink(value.SeatLink,true,false);
        }
        public int TypeId => 8;
        public int Version => 1;
        public void Write(SimStateWriter writer, in Occupant value)
        {
            if (writer.IsStable)
            {
                if (!value.StableLinks)
                    throw new SimBoundaryException("Stable Occupant requires a captured entity link.");
                writer.Link(value.SeatLink);
            }
            else
                SnapshotValues.Entity(writer,value.Seat);
        }
        public Occupant Read(SimStateReader reader)
        {
            if (!reader.IsStable)
                return new Occupant(SnapshotValues.Entity(reader));
            EntityLink link = reader.Link();
            reader.Entities.ResolveLogical(link);
            return new Occupant(link);
        }
    }

    public sealed class ContainerSerializer : IStableComponentSerializer<Container>
    {
        public int TypeId => 9;
        public int Version => 1;
        public void Write(SimStateWriter writer, in Container value)
        {
            writer.Int32(value.Capacity);
            writer.Int32(value.Count);
        }

        public Container Read(SimStateReader reader) => new Container(reader.Int32(), reader.Int32());
    }

    public sealed class MassClassSerializer : IStableComponentSerializer<MassClass>
    {
        public int TypeId => 10;
        public int Version => 1;
        public void Write(SimStateWriter writer, in MassClass value) => writer.Int32(value.Value);
        public MassClass Read(SimStateReader reader) => new MassClass(reader.Int32());
    }

    public sealed class StructureHealthSerializer : IStableComponentSerializer<StructureHealth>
    {
        public int TypeId => 11;
        public int Version => 1;
        public void Write(SimStateWriter writer, in StructureHealth value)
        {
            writer.Int64(value.Health.Raw);
            writer.Int64(value.CollapseThreshold.Raw);
            writer.Bool(value.Collapsed);
        }

        public StructureHealth Read(SimStateReader reader) => new StructureHealth(
            SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()), reader.Bool());
    }
}
