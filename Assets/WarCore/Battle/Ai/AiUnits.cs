using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Which units the computer steers: the one place that answers it (tools/ruler/one-home.tsv, "ai-steered").
    public static class AiUnits
    {
        // A unit the computer steers has a team (1 or more), is alive, has a position, is not the unit the player controls
        // and is not commanded (IMoveOrders.IsCommanded: a squad command or a led squad of the player's). The number of
        // teams is not checked here. id, team and at are set when it returns true.
        public static bool Steered(CombatSystem combat, ComponentType<Combatant> combatants, ComponentType<Position> positions,
            IMoveOrders moves, IControlTarget controlled, EntityId entity, out StableId id, out int team, out SimVec3 at)
        {
            id = default; team = 0; at = default;
            if (controlled != null && entity.Equals(controlled.Current)) return false;
            EntityStore store = combat.Entities;
            if (!store.TryGet(combatants, entity, out Combatant unit) || unit.Team < 1
                || !store.TryGet(positions, entity, out Position position) || combat.IsDead(entity)) return false;
            StableId key = store.StableOf(entity);
            if (moves.IsCommanded(key)) return false;
            id = key; team = unit.Team; at = position.Value;
            return true;
        }
    }
}
