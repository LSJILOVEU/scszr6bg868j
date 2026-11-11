using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    public sealed class InterimOrders : IRetainedGameplaySystem
    {
        private readonly EntityStore entities;
        private readonly EntityStage stage;
        private readonly ComponentType<MoveIntent> intents;
        private readonly ILocomotion locomotion;
        private readonly SimFix slotSpacing;
        private readonly ComponentType<Position> positions;
        private readonly IControlTarget controlled;
        private readonly ComponentType<SquadStanding>? standing;
        // Follow keeps this far (horizontal) from the followed unit, and re-aims when the followed unit has moved this far from the order.
        private readonly SimWide followStopSquared = new SimVec3(SimFix.FromInt(3), default, default).LengthSquared;
        private readonly SimWide followRetargetSquared = new SimVec3(SimFix.FromInt(1), default, default).LengthSquared;
        private EntityId[] members = Array.Empty<EntityId>();
        private SimVec3[] spots = Array.Empty<SimVec3>();

        public InterimOrders(EntityStore entities, ILocomotion locomotion, SimFix slotSpacing = default, IControlTarget controlled = null,
            ComponentType<SquadStanding>? standing = null)
        {
            this.entities = entities ?? throw new ArgumentNullException(nameof(entities));
            this.locomotion = locomotion ?? throw new ArgumentNullException(nameof(locomotion));
            if (slotSpacing.Raw < 0) throw new ArgumentOutOfRangeException(nameof(slotSpacing));
            this.slotSpacing = slotSpacing;
            this.controlled = controlled;
            this.standing = standing;
            members = new EntityId[entities.Capacity];
            spots = new SimVec3[entities.Capacity];
            stage = new EntityStage(entities);
            intents = entities.Resolve<MoveIntent>();
            if (slotSpacing.Raw != 0 || standing.HasValue) positions = entities.Resolve<Position>();
        }

        public int Order => 10;
        public string StateTypeId => "interim.orders";
        public int StateVersion => slotSpacing.Raw == 0 ? 1 : 2;

        public void Step(SimContext context)
        {
            if (context.Input.Squad.HasValue) Command(context.Input.Squad.Value);
            if (standing.HasValue) Maintain();
        }

        private void Mark(EntityId unit, SquadStanding value)
        {
            if (standing.HasValue) entities.Set(standing.Value, unit, value);
        }

        private void Command(SquadCommand command)
        {
            if (command.Kind == SquadCommandKind.Hold && standing.HasValue)
            {
                foreach (EntityId unit in stage.Capture())
                    if ((controlled == null || !unit.Equals(controlled.Current))
                        && entities.TryGet(intents, unit, out MoveIntent intent) && intent.Squad == command.Squad)
                    {
                        locomotion.Stop(unit);
                        Mark(unit, new SquadStanding(StandingKind.Hold));
                    }
                return;
            }
            if (command.Kind == SquadCommandKind.Follow && standing.HasValue)
            {
                // A target that is gone or has no movement intent (dead units lose it) is ignored.
                if (!entities.IsAlive(command.TargetEntity) || !entities.TryGet(intents, command.TargetEntity, out _)) return;
                ulong target = entities.StableOf(command.TargetEntity).Value;
                foreach (EntityId unit in stage.Capture())
                    if ((controlled == null || !unit.Equals(controlled.Current)) && !unit.Equals(command.TargetEntity)
                        && entities.TryGet(intents, unit, out MoveIntent intent) && intent.Squad == command.Squad)
                        Mark(unit, new SquadStanding(StandingKind.Follow, target));
                return;
            }
            if (command.Kind != SquadCommandKind.MoveTo)
                return;
            if (slotSpacing.Raw == 0)
            {
                foreach (EntityId unit in stage.Capture())
                    if ((controlled == null || !unit.Equals(controlled.Current))
                        && entities.TryGet(intents, unit, out MoveIntent intent) && intent.Squad == command.Squad)
                    {
                        locomotion.Order(unit, command.TargetPoint);
                        Mark(unit, new SquadStanding(StandingKind.Move));
                    }
                return;
            }
            // Slots go to units in row-major order of where they stand (Z, then X, then capture order), so a
            // squad that stands in a block keeps its shape and the routes do not cross.
            int n = 0;
            foreach (EntityId member in stage.Capture())
            {
                if (controlled != null && member.Equals(controlled.Current)) continue;
                if (!entities.TryGet(intents, member, out MoveIntent value) || value.Squad != command.Squad) continue;
                if (n == members.Length)
                {
                    Array.Resize(ref members, n < 8 ? 16 : n * 2);
                    Array.Resize(ref spots, members.Length);
                }
                SimVec3 at = entities.Get(positions, member).Value;
                int slot = n++;
                while (slot > 0 && Before(at, spots[slot - 1]))
                {
                    members[slot] = members[slot - 1];
                    spots[slot] = spots[slot - 1];
                    slot--;
                }
                members[slot] = member;
                spots[slot] = at;
            }
            for (int rank = 0; rank < n; rank++)
            {
                locomotion.Order(members[rank], command.TargetPoint + SlotOffset(rank, n, slotSpacing));
                Mark(members[rank], new SquadStanding(StandingKind.Move));
            }
        }

        // Standing orders run every tick: a move ends on arrival, a hold stays, a follow keeps up with the followed unit.
        private void Maintain()
        {
            ComponentType<SquadStanding> type = standing.Value;
            foreach (EntityId unit in stage.Capture())
            {
                if (!entities.TryGet(type, unit, out SquadStanding value)) continue;
                if (!entities.TryGet(intents, unit, out MoveIntent intent)) { entities.Remove(type, unit); continue; }
                if (value.Kind == StandingKind.Move) { if (!intent.Active) entities.Remove(type, unit); continue; }
                if (value.Kind != StandingKind.Follow) continue;
                if (!entities.TryResolve(new StableId(value.Target), out EntityRef found)
                    || !entities.TryGet(intents, (EntityId)found, out _) || !entities.TryGet(positions, (EntityId)found, out Position there))
                {
                    // The followed unit is gone: the follower stops and is free again.
                    if (intent.Active) locomotion.Stop(unit);
                    entities.Remove(type, unit);
                    continue;
                }
                SimVec3 gap = there.Value - entities.Get(positions, unit).Value;
                if (new SimVec3(gap.X, default, gap.Z).LengthSquared <= followStopSquared)
                {
                    if (intent.Active) locomotion.Stop(unit);
                    continue;
                }
                SimVec3 moved = there.Value - intent.Target;
                if (intent.Active && new SimVec3(moved.X, default, moved.Z).LengthSquared <= followRetargetSquared) continue;
                locomotion.Order(unit, there.Value);
            }
        }

        private static bool Before(SimVec3 a, SimVec3 b) => a.Z.Raw != b.Z.Raw ? a.Z.Raw < b.Z.Raw : a.X.Raw < b.X.Raw;

        public static SimVec3 SlotOffset(int rank, int n, SimFix spacing)
        {
            if (n < 1 || rank < 0 || rank >= n) throw new ArgumentOutOfRangeException(nameof(rank));
            int cols = 1;
            while ((long)cols * cols < n) cols++;
            int rows = (n + cols - 1) / cols;
            return new SimVec3(SimFix.Ratio(2 * (rank % cols) - (cols - 1), 2) * spacing,
                default, SimFix.Ratio(2 * (rank / cols) - (rows - 1), 2) * spacing);
        }

        // Commands are consumed in this tick; this system retains no mutable state.
        public void SaveState(SimStateWriter writer)
        {
            if (slotSpacing.Raw != 0) writer.Int64(slotSpacing.Raw);
        }

        public void LoadState(SimStateReader reader)
        {
            if (slotSpacing.Raw != 0 && reader.Int64() != slotSpacing.Raw)
                throw new InvalidOperationException("Slot spacing differs from the saved configuration.");
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId);
            hash.Add(StateVersion);
            if (slotSpacing.Raw != 0) hash.Add(slotSpacing.Raw);
        }
    }
}
