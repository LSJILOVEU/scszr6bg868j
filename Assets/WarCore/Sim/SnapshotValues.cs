namespace WarCore.Sim
{
    public static class SnapshotValues
    {
        public static void Vector(SimStateWriter writer, SimVec3 value)
        {
            writer.Int64(value.X.Raw);
            writer.Int64(value.Y.Raw);
            writer.Int64(value.Z.Raw);
        }

        public static SimVec3 Vector(SimStateReader reader) => new SimVec3(
            SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()));
        public static void Entity(SimStateWriter writer, EntityId value)
        {
            if (writer.IsStable)
                throw new SimBoundaryException("Stable schemas must encode captured EntityLink values.");
            writer.Int32(value.Id);
            writer.UInt64(value.Generation);
        }

        public static EntityId Entity(SimStateReader reader)
        {
            if (reader.IsStable)
                throw new SimBoundaryException("Stable schemas must decode EntityLink values.");
            return new EntityId(reader.Int32(),reader.UInt64());
        }
        public static void Event(SimStateWriter writer, SimEvent value)
        {
            writer.Int32(value.Type.Value);
            writer.Int64(value.Tick);
            writer.Int64(value.Sequence);
            Entity(writer, value.Source);
            Entity(writer, value.Target);
            Vector(writer, value.Position);
            Vector(writer, value.Aux);
            writer.Int64(value.Magnitude.Raw);
            writer.Int64(value.Radius.Raw);
            writer.Int32(value.Tag);
        }

        public static SimEvent Event(SimStateReader reader)
        {
            int type = reader.Int32();
            long tick = reader.Int64();
            long sequence = reader.Int64();
            SimStateReader.Require(type > 0 && tick >= 0 && sequence >= 0, "Invalid snapshot event header.");
            EntityId source = Entity(reader);
            EntityId target = Entity(reader);
            SimVec3 position = Vector(reader);
            SimVec3 aux = Vector(reader);
            SimFix magnitude = SimFix.FromRaw(reader.Int64());
            SimFix radius = SimFix.FromRaw(reader.Int64());
            return new SimEvent(new EventTypeId(type), tick, source, target, position, aux, magnitude, radius, reader.Int32(), sequence);
        }

        public static void Input(SimStateWriter writer, InputFrame value)
        {
            Vector(writer, value.Move);
            Vector(writer, value.Aim);
            writer.Bool(value.PrimaryAttack);
            writer.Bool(value.Block);
            writer.Bool(value.Throw);
            writer.Int32(value.SkillSlot);
            writer.Bool(value.Squad.HasValue);
            if (!value.Squad.HasValue)
                return;
            SquadCommand squad = value.Squad.Value;
            writer.Int32((int)squad.Kind);
            writer.Int32(squad.Squad);
            Vector(writer, squad.TargetPoint);
            Entity(writer, squad.TargetEntity);
        }

        public static InputFrame Input(SimStateReader reader)
        {
            SimVec3 move = Vector(reader);
            SimVec3 aim = Vector(reader);
            bool primary = reader.Bool();
            bool block = reader.Bool();
            bool throwing = reader.Bool();
            int skill = reader.Int32();
            SquadCommand? squad = null;
            if (reader.Bool())
                squad = new SquadCommand((SquadCommandKind)reader.Int32(), reader.Int32(), Vector(reader), Entity(reader));
            return new InputFrame(move, aim, primary, block, throwing, skill, squad);
        }
    }
}
