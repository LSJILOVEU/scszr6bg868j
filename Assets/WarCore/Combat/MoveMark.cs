using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // AIM-1 combat.move-mark: before movement (Movement phase, order 19) it copies the position of every unit with combat
    // state into a table by entity slot, stamped with the tick. The combat step compares a shooter's position with it, so a
    // shot knows how far its shooter really moved this tick (not what it was asked to do). It keeps nothing between ticks:
    // the table is rewritten every tick and is not saved; its state block is empty. Registered only for a pack with
    // combat.moveSpread.
    public sealed class MoveMark : IRetainedGameplaySystem, ISimPhaseSystem
    {
        public const int StepOrder = 19;
        private readonly EntityStore entities;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly long[] x, z, tickOf;
        private readonly ulong[] generation;

        public MoveMark(EntityStore entities, ComponentType<Position> positions, ComponentType<Combatant> combatants)
        {
            this.entities = entities ?? throw new ArgumentNullException(nameof(entities));
            this.positions = positions; this.combatants = combatants;
            stage = new EntityStage(entities);
            x = new long[entities.Capacity]; z = new long[entities.Capacity];
            tickOf = new long[entities.Capacity]; generation = new ulong[entities.Capacity];
            Forget();
        }

        public int Order => StepOrder;
        public SimSystemPhase Phase => SimSystemPhase.Movement;
        public string StateTypeId => "combat.move-mark";
        public int StateVersion => 1;

        public void Step(SimContext c)
        {
            long tick = c.Clock.Tick;
            foreach (EntityId unit in stage.Capture())
            {
                if (!entities.TryGet(combatants, unit, out _) || !entities.TryGet(positions, unit, out Position at)) continue;
                int slot = unit.Id;
                x[slot] = at.Value.X.Raw; z[slot] = at.Value.Z.Raw; tickOf[slot] = tick; generation[slot] = unit.Generation;
            }
        }

        // The horizontal position of unit before this tick's movement; false when it was not marked this tick (it had no
        // position or combat state then, or the slot now holds another entity).
        public bool TryBefore(EntityId unit, long tick, out long xRaw, out long zRaw)
        {
            int slot = unit.Id;
            if ((uint)slot < (uint)tickOf.Length && tickOf[slot] == tick && generation[slot] == unit.Generation)
            {
                xRaw = x[slot]; zRaw = z[slot];
                return true;
            }
            xRaw = zRaw = 0;
            return false;
        }

        private void Forget() { for (int i = 0; i < tickOf.Length; i++) tickOf[i] = -1; }

        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) => Forget();
        public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
    }
}
