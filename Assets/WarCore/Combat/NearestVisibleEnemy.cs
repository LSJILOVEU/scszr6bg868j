using WarCore.Sim;

namespace WarCore.Combat
{
    public sealed class NearestVisibleEnemy : ITargetSelector
    {
        public ulong PolicyKey => 0x4E45415245535401UL;
        public bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target)
        {
            if (!world.MayHaveEnemyWithin(shooter, range)) { target = default; return false; }
            target = default;
            bool found = false;
            SimWide best = default;
            StableId bestId = default;
            SimWide limit = new SimVec3(range, default, default).LengthSquared;
            foreach (var hit in world.Candidates(shooter, range))
            {
                EntityId candidate = hit.Entity;
                if (!world.IsEnemy(shooter, candidate)) continue;
                SimWide distance = CombatSystem.Horizontal(world.Point(candidate) - world.Point(shooter)).LengthSquared;
                StableId id = world.Entities.StableOf(candidate);
                if (distance > limit || (found && (distance > best || (distance == best && id.CompareTo(bestId) >= 0)))) continue;
                if (!world.Visible(shooter, candidate)) continue;
                target = candidate; best = distance; bestId = id; found = true;
            }
            return found;
        }
    }
}
