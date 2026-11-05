using WarCore.Sim;

namespace WarCore.Interim
{
    public sealed class MoveIntentSerializer : IStableComponentSerializer<MoveIntent>
    {
        public int TypeId => 2001;
        public int Version => 1;

        public void Write(SimStateWriter writer, in MoveIntent value)
        {
            writer.Int64(value.Target.X.Raw);
            writer.Int64(value.Target.Y.Raw);
            writer.Int64(value.Target.Z.Raw);
            writer.Int64(value.SpeedPerTick.Raw);
            writer.Int32(value.Squad);
            writer.Bool(value.Active);
        }

        public MoveIntent Read(SimStateReader reader)
        {
            var target = new SimVec3(SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()),
                SimFix.FromRaw(reader.Int64()));
            return new MoveIntent(target, SimFix.FromRaw(reader.Int64()), reader.Int32(), reader.Bool());
        }
    }
}
