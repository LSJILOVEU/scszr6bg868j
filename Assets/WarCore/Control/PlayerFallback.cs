using System;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Control
{
    public sealed class PlayerFallback : IRetainedGameplaySystem
    {
        private readonly CombatSystem combat;
        private readonly IControlTarget controlled;
        private readonly ILocomotion locomotion;
        private readonly Func<bool> matchEnded;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        public int Order => 6;
        public string StateTypeId => "control.player-fallback";
        public int StateVersion => 1;

        // The bridge supplies the match latch without a Control -> Battle dependency.
        public PlayerFallback(CombatSystem combat, IControlTarget controlled, ILocomotion locomotion, Func<bool> matchEnded)
        {
            this.combat = combat; this.controlled = controlled; this.locomotion = locomotion;
            this.matchEnded = matchEnded ?? throw new ArgumentNullException(nameof(matchEnded));
            stage = new EntityStage(combat.Entities);
            positions = combat.Entities.Resolve<Position>();
            combatants = combat.Entities.Resolve<Combatant>();
        }

        public void Step(SimContext context)
        {
            EntityId previous = controlled.Current;
            if (matchEnded() || !combat.Entities.IsAlive(previous) || !combat.IsDead(previous)) return;
            SimVec3 origin = combat.Point(previous);
            int team = combat.Unit(previous).Team;
            EntityId nearest = default;
            StableId nearestId = default;
            SimWide best = default;
            bool found = false;
            foreach (EntityId unit in stage.Capture())
            {
                if (!combat.Entities.TryGet(combatants, unit, out Combatant candidate) || candidate.Team != team
                    || !combat.Entities.TryGet(positions, unit, out Position point) || combat.IsDead(unit)) continue;
                SimWide distance = CombatSystem.Horizontal(point.Value - origin).LengthSquared;
                StableId id = combat.Entities.StableOf(unit);
                if (!found || distance < best || (distance == best && id.Value < nearestId.Value))
                { found = true; nearest = unit; nearestId = id; best = distance; }
            }
            if (!found) return;
            controlled.Switch(context, nearest);
            // Read Current again: Switch does not update an already captured EntityId.
            locomotion.Stop(controlled.Current);
        }

        // All mutable state belongs to ControlTargetSystem, components, and the existing match system.
        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }
        public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
    }
}
