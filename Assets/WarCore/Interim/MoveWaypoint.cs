using WarCore.Sim;

namespace WarCore.Interim
{
    public readonly struct MoveWaypoint : IStateHash
    {
        public const string ComponentId = "interim.move-waypoint";
        public SimVec3 Point { get; }
        public bool Has { get; }
        public MoveWaypoint(SimVec3 point, bool has) { Point = point; Has = has; }
        public void WriteState(StateHash hash) { Point.WriteState(hash); hash.Add(Has); }
    }

    public sealed class MoveWaypointSerializer : IStableComponentSerializer<MoveWaypoint>
    {
        public int TypeId => 2002;
        public int Version => 1;
        public void Write(SimStateWriter writer, in MoveWaypoint value)
        {
            writer.Int64(value.Point.X.Raw); writer.Int64(value.Point.Y.Raw); writer.Int64(value.Point.Z.Raw);
            writer.Bool(value.Has);
        }
        public MoveWaypoint Read(SimStateReader reader) => new MoveWaypoint(
            new SimVec3(SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64())), reader.Bool());
    }
}
