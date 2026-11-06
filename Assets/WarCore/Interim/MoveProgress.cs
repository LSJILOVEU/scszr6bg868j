using WarCore.Sim;

namespace WarCore.Interim
{
    // Progress bookkeeping for one move order in a terrain world (T1a-2a). It exists only there, so flat worlds keep
    // their exact snapshots and hashes. Distances are horizontal, squared, in raw units.
    public struct MoveProgress : IStateHash
    {
        public const string ComponentId = "interim.move-progress";
        // Smallest distance so far to the current waypoint; reset when the waypoint changes.
        public long BestDist2;
        // Steps since BestDist2 last shrank, and the re-plans spent on this order (never reset by a waypoint change).
        public int Stall, Retries;
        // 1 when the order gave up: no route, or no progress within the limits. Cleared by the next Order, Stop or arrival.
        public byte Failed;
        // The order-level guard: smallest remaining route length so far (distance to the waypoint plus the planned route
        // beyond it), counted only when it shrank by at least a quarter step, and the steps since. Neither is reset by a
        // waypoint change, so a unit shuffling between two cells, or creeping sideways, still runs out of time, while a long
        // detour that heads away from the target keeps shrinking it.
        public long OrderBest;
        public int OrderStall;
        // Length (raw) of the planned route beyond the current waypoint. The order-level progress is the distance to the
        // waypoint plus this, so a long detour that leads away from the target still counts as getting closer.
        public long Tail;

        public void WriteState(StateHash hash)
        {
            hash.Add(BestDist2); hash.Add(Stall); hash.Add(Retries); hash.Add(Failed);
            hash.Add(OrderBest); hash.Add(OrderStall); hash.Add(Tail);
        }
    }

    public sealed class MoveProgressSerializer : IStableComponentSerializer<MoveProgress>
    {
        public int TypeId => 2003;
        public int Version => 1;

        public void Write(SimStateWriter writer, in MoveProgress value)
        {
            writer.Int64(value.BestDist2); writer.Int32(value.Stall); writer.Int32(value.Retries);
            writer.Int32(value.Failed);
            writer.Int64(value.OrderBest); writer.Int32(value.OrderStall); writer.Int64(value.Tail);
        }

        public MoveProgress Read(SimStateReader reader) => new MoveProgress
        {
            BestDist2 = reader.Int64(), Stall = reader.Int32(), Retries = reader.Int32(), Failed = (byte)reader.Int32(),
            OrderBest = reader.Int64(), OrderStall = reader.Int32(), Tail = reader.Int64(),
        };
    }
}
