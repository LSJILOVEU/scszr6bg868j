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
        private EntityId[] members = Array.Empty<EntityId>();
        private SimVec3[] spots = Array.Empty<SimVec3>();

        public InterimOrders(EntityStore entities, ILocomotion locomotion, SimFix slotSpacing = default, IControlTarget controlled = null)
        {
            this.entities = entities ?? throw new ArgumentNullException(nameof(entities));
            this.locomotion = locomotion ?? throw new ArgumentNullException(nameof(locomotion));
            if (slotSpacing.Raw < 0) throw new ArgumentOutOfRangeException(nameof(slotSpacing));
            this.slotSpacing = slotSpacing;
            this.controlled = controlled;
            stage = new EntityStage(entities);
            intents = entities.Resolve<MoveIntent>();
            if (slotSpacing.Raw != 0) positions = entities.Resolve<Position>();
        }

        public int Order => 10;
        public string StateTypeId => "interim.orders";
        public int StateVersion => slotSpacing.Raw == 0 ? 1 : 2;

        public void Step(SimContext context)
        {
            if (!context.Input.Squad.HasValue)
                return;
            SquadCommand command = context.Input.Squad.Value;
            if (command.Kind != SquadCommandKind.MoveTo)
                return;
            if (slotSpacing.Raw == 0)
            {
                foreach (EntityId unit in stage.Capture())
                    if ((controlled == null || !unit.Equals(controlled.Current))
                        && entities.TryGet(intents, unit, out MoveIntent intent) && intent.Squad == command.Squad)
                        locomotion.Order(unit, command.TargetPoint);
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
                locomotion.Order(members[rank], command.TargetPoint + SlotOffset(rank, n, slotSpacing));
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
