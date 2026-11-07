using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Cover on the ground: a target standing on a cell with cover factor k (thousandths, 1..1000) is noticed only within
    // L * k / 1000, where L is the distance the wrapped selector would use. The factor of the target's cell counts, not the
    // shooter's; a point outside the grid has no cover. Otherwise the choice is NearestVisibleEnemy's: nearest, ties to the
    // lower StableId, visible only. Wraps NearestVisibleEnemy or SightLimited(NearestVisibleEnemy, s) and nothing else.
    public sealed class ConcealedSight : ITargetSelector
    {
        private const ulong NearestKey = 0x4E45415245535401UL;
        private readonly NavGrid grid;
        private readonly short[] conceal;
        private readonly bool limited;
        private readonly SimFix sight;
        public ulong PolicyKey { get; }

        public ConcealedSight(ITargetSelector inner, NavGrid grid, short[] concealMilli)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (concealMilli == null) throw new ArgumentNullException(nameof(concealMilli));
            if (concealMilli.Length != grid.Count) throw new ArgumentException("One cover factor per grid cell is required.", nameof(concealMilli));
            foreach (short k in concealMilli)
                if (k < 1 || k > 1000) throw new ArgumentOutOfRangeException(nameof(concealMilli));
            if (inner is SightLimited s && new SightLimited(new NearestVisibleEnemy(), s.Sight).PolicyKey == s.PolicyKey)
            { limited = true; sight = s.Sight; }
            else if (inner.PolicyKey != NearestKey)
                throw new NotSupportedException("Cover wraps only the nearest-visible selector, optionally sight-limited.");
            conceal = (short[])concealMilli.Clone();
            var hash = new StateHash();
            hash.Add("battle.conceal.v1"); hash.Add(inner.PolicyKey);
            hash.Add(grid.Width); hash.Add(grid.Depth); hash.Add(grid.OriginX.Raw); hash.Add(grid.OriginZ.Raw);
            hash.Add(conceal.Length);
            foreach (short k in conceal) hash.Add((int)k);
            PolicyKey = hash.Value;
        }

        // floor(limit * k / 1000) for limit >= 0 and k in 1..1000, without forming limit * k (weapon ranges have no upper bound).
        internal static long Scaled(long limitRaw, int k) => limitRaw / 1000 * k + limitRaw % 1000 * k / 1000;

        public bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target)
        {
            SimFix limit = limited && sight < range ? sight : range;
            target = default;
            if (!world.MayHaveEnemyWithin(shooter, limit)) return false;
            bool found = false;
            SimWide best = default;
            StableId bestId = default;
            foreach (var hit in world.Candidates(shooter, limit))
            {
                EntityId candidate = hit.Entity;
                if (!world.IsEnemy(shooter, candidate)) continue;
                SimVec3 at = world.Point(candidate);
                int k = grid.TryCell(at, out int key) ? conceal[key] : 1000;
                SimWide cap = new SimVec3(SimFix.FromRaw(Scaled(limit.Raw, k)), default, default).LengthSquared;
                SimWide distance = CombatSystem.Horizontal(at - world.Point(shooter)).LengthSquared;
                StableId id = world.Entities.StableOf(candidate);
                if (distance > cap || (found && (distance > best || (distance == best && id.CompareTo(bestId) >= 0)))) continue;
                if (!world.Visible(shooter, candidate)) continue;
                target = candidate; best = distance; bestId = id; found = true;
            }
            return found;
        }
    }
}
