using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Cover: where a unit that is told to take cover walks to. A spot is cover from a threat (a point on the ground: where
    // the fire that pressed the unit came from) when a wall stands within `depth` of the spot on the sight line from the
    // spot toward the threat (aim height at both ends, walls only). Close behind a wall the unit is hidden from the
    // threat and from most of what stands near the threat; a spot that merely lies in the shadow of a far wall is not cover.
    //
    // The spots a unit tries lie on its own lattice: `step` apart, shifted by a part of a step that comes from its key, so
    // two units rarely pick the same spot and one unit tries the same spots at every think. Among the spots within
    // `radius` of the unit (horizontally) it takes the nearest that is standable, is cover from the threat and can be
    // walked to in a straight line: the sight line from the unit to the spot is clear, and the unit's own body moved
    // along the last Approach of that line would touch no living teammate (nobody stands on the spot or right in front
    // of it; teammates farther back on the way are walked round). Ties go to the lower z, then the lower x. A unit that
    // cannot get into any spot stands where it is rather than push at the others.
    // Stateless: nothing is saved.
    //
    // Flat maps only: a spot takes the unit's own height.
    internal sealed class Cover
    {
        private const ulong SaltX = 0x1f83d9abfb41bd6bUL, SaltZ = 0x5be0cd19137e2179UL;
        // PLACEHOLDER: the stretch before the spot that must be free of teammates. A little over the diagonal of a 1 m
        // navigation cell: inside the spot's own cell a unit heads straight for the spot.
        private static readonly SimFix Approach = SimFix.Ratio(3, 2);
        private readonly CombatSystem combat;
        private readonly IMoveOrders moves;
        private readonly long radius, step, reach;
        private readonly SimFix depth;
        private readonly SimWide radiusSquared;

        internal Cover(CombatSystem combat, IMoveOrders moves, long radiusRaw, long stepRaw, long depthRaw)
        {
            if (radiusRaw <= 0) throw new ArgumentOutOfRangeException(nameof(radiusRaw));
            if (stepRaw <= 0) throw new ArgumentOutOfRangeException(nameof(stepRaw));
            if (depthRaw <= 0) throw new ArgumentOutOfRangeException(nameof(depthRaw));
            this.combat = combat; this.moves = moves; radius = radiusRaw; step = stepRaw; depth = SimFix.FromRaw(depthRaw);
            reach = (radiusRaw + stepRaw - 1) / stepRaw;
            radiusSquared = new SimVec3(SimFix.FromRaw(radiusRaw), default, default).LengthSquared;
        }

        internal ulong Fold(ulong digest)
        {
            var hash = new StateHash(); hash.Add("battle.ai.cover.v1"); hash.Add(digest); hash.Add(radius); hash.Add(step); hash.Add(depth.Raw);
            return hash.Value;
        }

        // A unit standing at this point has a wall close in front of it, toward the threat.
        internal bool Covered(SimVec3 threat, SimVec3 at) => combat.WallWithin(at, threat, depth);

        // The spot can be walked to in a straight line: no wall on it, and on its last Approach the unit's body would
        // touch no living teammate.
        private bool Walkable(EntityId unit, SimVec3 here, SimVec3 at)
        {
            if (!combat.Sees(here, at)) return false;
            SimVec3 walk = CombatSystem.Horizontal(at - here);
            return !combat.FriendOnWalk(unit, walk.Length <= Approach ? here : at - walk.Normalized * Approach, at);
        }

        // An order already under way is kept while its target is within the radius, still is cover and can still be
        // walked to.
        internal bool Keeps(EntityId unit, SimVec3 here, SimVec3 target, SimVec3 threat) =>
            CombatSystem.Horizontal(target - here).LengthSquared <= radiusSquared && Covered(threat, target) && Walkable(unit, here, target);

        internal bool TryFind(EntityId unit, ulong key, SimVec3 here, SimVec3 threat, out SimVec3 spot)
        {
            spot = default;
            bool found = false; SimWide best = default;
            long shiftX = (long)(AiParts.Mix(key ^ SaltX) % (ulong)step), shiftZ = (long)(AiParts.Mix(key ^ SaltZ) % (ulong)step);
            long column = Floor(here.X.Raw - shiftX), row = Floor(here.Z.Raw - shiftZ);
            for (long j = row - reach; j <= row + reach + 1; j++)
                for (long i = column - reach; i <= column + reach + 1; i++)
                {
                    var at = new SimVec3(SimFix.FromRaw(i * step + shiftX), here.Y, SimFix.FromRaw(j * step + shiftZ));
                    SimWide distance = CombatSystem.Horizontal(at - here).LengthSquared;
                    if (distance > radiusSquared || (found && distance >= best)) continue;
                    if (!moves.CanStandAt(at) || !Covered(threat, at) || !Walkable(unit, here, at)) continue;
                    spot = at; best = distance; found = true;
                }
            return found;
        }

        // Floor of raw / step, for negative coordinates too.
        private long Floor(long raw) => raw >= 0 ? raw / step : -((-raw + step - 1) / step);
    }
}
