using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Default dispatch ("nearest"): a holder goes to its team's front-line point, an attacker to the nearest point its
    // team does not own, or with a spread to the k-th nearest.
    public sealed class NearestDispatch : IAiDispatch
    {
        private readonly int holdSharePermille, attackSpreadPoints;
        // holdSharePermille: share, in thousandths, of each team's units that hold; 0 turns it off.
        // attackSpreadPoints: number of nearest points an attacker spreads over; 1 sends all to the nearest.
        public NearestDispatch(int holdSharePermille = 0, int attackSpreadPoints = 1)
        { this.holdSharePermille = holdSharePermille; this.attackSpreadPoints = attackSpreadPoints; }

        public ulong Fold(ulong policyDigest)
        {
            if (holdSharePermille > 0)
            {
                var hash = new StateHash(); hash.Add("battle.ai.hold-share.v1"); hash.Add(policyDigest); hash.Add(holdSharePermille);
                policyDigest = hash.Value;
            }
            if (attackSpreadPoints > 1)
            {
                var hash = new StateHash(); hash.Add("battle.ai.attack-spread.v1"); hash.Add(policyDigest); hash.Add(attackSpreadPoints);
                policyDigest = hash.Value;
            }
            return policyDigest;
        }

        public int Pick(in AiUnit unit, AiField field)
        {
            // A holder goes to its team's front-line point; without one it attacks like the rest.
            int nearest = holdSharePermille > 0 && Holds(unit.Id.Value, holdSharePermille) ? field.Frontline(unit.Team) : -1;
            bool attack = nearest < 0; SimWide best = default;
            if (attack && attackSpreadPoints > 1) { nearest = SpreadTarget(unit.Team, unit.Here, unit.Id.Value, field); attack = false; }
            for (int i = 0; attack && i < field.PointCount; i++)
            {
                if (field.Owner(i) == unit.Team) continue;
                SimWide distance = CombatSystem.Horizontal(unit.Here - field.Center(i)).LengthSquared;
                if (nearest < 0 || distance < best) { nearest = i; best = distance; }
            }
            return nearest;
        }

        // Stateless split by unit key: the same unit is a holder on every tick and after a restore.
        private const ulong HoldSalt = 0x5a17c0de0b5e55edUL;
        private static bool Holds(ulong id, int permille) => AiParts.Mix(id ^ HoldSalt) % 1000UL < (ulong)permille;

        // Stateless spread by unit key: rank k = Mix(key ^ salt) % min(spread, points not owned); the k-th nearest such point
        // (0 = nearest; equal distances to the lower point index); -1 when the team owns every point. Distances are measured
        // from the team's front-line point, so the choice holds while the unit walks and changes only when a point flips;
        // a team without a front line measures from its first spawn anchor, and without one from the unit itself.
        private const ulong SpreadSalt = 0x3c6ef372fe94f82bUL;
        private int SpreadTarget(int team, SimVec3 here, ulong key, AiField field)
        {
            int front = field.Frontline(team);
            SimVec3 from = here;
            if (front >= 0) from = field.Center(front);
            else if (field.TryBase(team, out SimVec3 anchor)) from = anchor;
            int count = 0;
            for (int i = 0; i < field.PointCount; i++) if (field.Owner(i) != team) count++;
            if (count == 0) return -1;
            int rank = (int)(AiParts.Mix(key ^ SpreadSalt) % (ulong)Math.Min(attackSpreadPoints, count));
            int last = -1; SimWide lastDistance = default;
            for (int r = 0; r <= rank; r++)
            {
                int pick = -1; SimWide best = default;
                for (int i = 0; i < field.PointCount; i++)
                {
                    if (field.Owner(i) == team) continue;
                    SimWide distance = CombatSystem.Horizontal(from - field.Center(i)).LengthSquared;
                    if (last >= 0 && (distance < lastDistance || (distance == lastDistance && i <= last))) continue;
                    if (pick < 0 || distance < best) { pick = i; best = distance; }
                }
                last = pick; lastDistance = best;
            }
            return last;
        }
    }
}
