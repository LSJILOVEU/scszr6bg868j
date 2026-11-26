using System;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Commander tuning (attack only). All score weights are integers in one score unit.
    // PLACEHOLDER: Default holds starting values, not tuned yet.
    public readonly struct CommanderQuotaRules
    {
        // At most this many points are attacked at once (values below 1 act as 1).
        public readonly int MaxAttackTargets;
        // Attack score, lower is better: enemies * EnemyWeight + whole metres to the anchor * DistanceWeightPerMetre,
        // minus Stickiness for a point that was attacked last time.
        public readonly int EnemyWeight;
        public readonly int DistanceWeightPerMetre;
        public readonly int Stickiness;

        // Upper bounds: at most this many targets; every weight at most MaxWeight (with the coordinate limit below, a score
        // stays far inside a long).
        public const int MaxTargets = 8, MaxWeight = 1000000;

        public CommanderQuotaRules(int maxAttackTargets, int enemyWeight, int distanceWeightPerMetre, int stickiness)
        {
            if (maxAttackTargets < 1) throw new ArgumentOutOfRangeException(nameof(maxAttackTargets));
            if (enemyWeight < 0 || distanceWeightPerMetre < 0 || stickiness < 0) throw new ArgumentOutOfRangeException(nameof(enemyWeight));
            if (maxAttackTargets > MaxTargets) throw new ArgumentOutOfRangeException(nameof(maxAttackTargets));
            if (enemyWeight > MaxWeight || distanceWeightPerMetre > MaxWeight || stickiness > MaxWeight) throw new ArgumentOutOfRangeException(nameof(enemyWeight));
            MaxAttackTargets = maxAttackTargets;
            EnemyWeight = enemyWeight;
            DistanceWeightPerMetre = distanceWeightPerMetre;
            Stickiness = stickiness;
        }

        public static CommanderQuotaRules Default => new CommanderQuotaRules(3, 200, 1, 600);
    }

    // Team-level commander as pure functions: which points to attack and how many soldiers each of them wants.
    // No state, no allocation, integers only; the caller owns every buffer. Any number of teams: owner 0 is neutral,
    // "enemy" is every owner other than the planning team. Ties always go to the lower point index.
    // Called by the commander system (Commander), which keeps the saved assignment.
    public static class CommanderQuota
    {
        // Positions are compared on the ground plane (X, Z). Within this bound every product below fits a long.
        public const long CoordinateLimitMetres = 8192;
        private const long CoordinateLimitRaw = CoordinateLimitMetres * SimFix.Scale;

        // adjacentOut[i * count + j]: i and j are neighbours iff no third point lies strictly inside the circle whose
        // diameter is ij, i.e. (k - i) . (k - j) < 0 for no k. Symmetric; the diagonal is false.
        public static void Adjacency(SimVec3[] centers, int count, bool[] adjacentOut)
        {
            if (centers == null) throw new ArgumentNullException(nameof(centers));
            if (adjacentOut == null) throw new ArgumentNullException(nameof(adjacentOut));
            if (count < 0 || count > centers.Length || (long)count * count > adjacentOut.Length) throw new ArgumentOutOfRangeException(nameof(count));
            for (int i = 0; i < count; i++) CheckBounds(centers[i]);
            for (int i = 0; i < count; i++)
            {
                adjacentOut[i * count + i] = false;
                for (int j = i + 1; j < count; j++)
                {
                    bool open = true;
                    for (int k = 0; k < count && open; k++)
                    {
                        if (k == i || k == j) continue;
                        long ax = centers[k].X.Raw - centers[i].X.Raw, az = centers[k].Z.Raw - centers[i].Z.Raw;
                        long bx = centers[k].X.Raw - centers[j].X.Raw, bz = centers[k].Z.Raw - centers[j].Z.Raw;
                        open = ax * bx + az * bz >= 0;
                    }
                    adjacentOut[i * count + j] = open;
                    adjacentOut[j * count + i] = open;
                }
            }
        }

        // owners[i]: team number of point i (0 = neutral). enemiesNear[i]: living soldiers of other teams the caller
        // counted near point i. adjacent: count x count table, e.g. from Adjacency. soldiers: this team's available
        // soldiers. anchor: this team's reference point for attack distance. previousAttack: last result's attack
        // flags (read only, must not be attackOut). underWay[i] (read only): a group of this team has an order to point i
        // (its order before this plan). Writes desiredOut[i] (soldiers wanted at i) and attackOut[i]: an own point wants
        // nobody; all the soldiers are split over the attack targets (at least one each). rankOut (optional): rankOut[i] is
        // the order in which target i was picked (0 first), -1 for a point that is no target.
        public static void Plan(int team, int count, int[] owners, SimVec3[] centers, int[] enemiesNear, bool[] adjacent,
            int soldiers, SimVec3 anchor, bool[] previousAttack, in CommanderQuotaRules rules, int[] desiredOut, bool[] attackOut,
            bool[] underWay, int[] rankOut = null)
        {
            if (team <= 0) throw new ArgumentOutOfRangeException(nameof(team));
            if (owners == null || centers == null || enemiesNear == null || adjacent == null || previousAttack == null
                || desiredOut == null || attackOut == null || underWay == null) throw new ArgumentNullException(nameof(owners));
            if (ReferenceEquals(previousAttack, attackOut)) throw new ArgumentException("previousAttack must not be attackOut.", nameof(attackOut));
            if (count < 0 || count > owners.Length || count > centers.Length || count > enemiesNear.Length || count > previousAttack.Length
                || count > desiredOut.Length || count > attackOut.Length || count > underWay.Length || (long)count * count > adjacent.Length
                || (rankOut != null && count > rankOut.Length))
                throw new ArgumentOutOfRangeException(nameof(count));
            if (soldiers < 0) throw new ArgumentOutOfRangeException(nameof(soldiers));
            CheckBounds(anchor);
            for (int i = 0; i < count; i++)
            {
                if (enemiesNear[i] < 0) throw new ArgumentOutOfRangeException(nameof(enemiesNear));
                CheckBounds(centers[i]);
            }

            int touching = 0;
            for (int i = 0; i < count; i++)
            {
                desiredOut[i] = 0;
                attackOut[i] = false;
                if (rankOut != null) rankOut[i] = -1;
                if (owners[i] != team && Touches(i, team, count, owners, adjacent)) touching++;
            }

            // Candidates: the non-own points that border an own one, and the non-own points a group of this team is on its
            // way to (so a point under attack stays one when the front falls back). No non-own point borders an own one (e.g.
            // the team holds nothing): every non-own point is a candidate.
            bool fallback = touching == 0;
            int candidates = 0;
            for (int i = 0; i < count; i++)
                if (Candidate(i, team, fallback, count, owners, adjacent, underWay)) candidates++;

            if (candidates == 0) return;

            int targets = Math.Min(candidates, Math.Max(1, rules.MaxAttackTargets));
            int pool = Math.Max(targets, soldiers);
            for (int k = 0; k < targets; k++)
            {
                int best = -1;
                long bestScore = 0;
                for (int i = 0; i < count; i++)
                {
                    if (attackOut[i] || !Candidate(i, team, fallback, count, owners, adjacent, underWay)) continue;
                    long score = Score(enemiesNear[i], centers[i], anchor, previousAttack[i], rules);
                    if (best < 0 || score < bestScore)
                    {
                        best = i;
                        bestScore = score;
                    }
                }
                attackOut[best] = true;
                desiredOut[best] = pool / targets + (k < pool % targets ? 1 : 0);
                if (rankOut != null) rankOut[best] = k;
            }
        }

        private static bool Candidate(int i, int team, bool fallback, int count, int[] owners, bool[] adjacent, bool[] underWay) =>
            owners[i] != team && (fallback || Touches(i, team, count, owners, adjacent) || underWay[i]);

        // Does i have a neighbour owned by team.
        private static bool Touches(int i, int team, int count, int[] owners, bool[] adjacent)
        {
            int row = i * count;
            for (int j = 0; j < count; j++)
                if (j != i && adjacent[row + j] && owners[j] == team) return true;
            return false;
        }

        private static long Score(int enemies, SimVec3 center, SimVec3 anchor, bool previous, in CommanderQuotaRules rules)
        {
            SimFix dx = center.X - anchor.X, dz = center.Z - anchor.Z;
            long metres = SimMath.Sqrt(dx * dx + dz * dz).Raw / SimFix.Scale;
            return (long)enemies * rules.EnemyWeight + metres * rules.DistanceWeightPerMetre - (previous ? rules.Stickiness : 0);
        }

        internal static void CheckBounds(SimVec3 at)
        {
            if (at.X.Raw > CoordinateLimitRaw || at.X.Raw < -CoordinateLimitRaw || at.Z.Raw > CoordinateLimitRaw || at.Z.Raw < -CoordinateLimitRaw)
                throw new ArgumentOutOfRangeException(nameof(at));
        }
    }
}
