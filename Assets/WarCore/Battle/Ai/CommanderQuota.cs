using System;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Commander tuning. All ratios are permille, all score weights are integers in one score unit.
    // PLACEHOLDER: Default is the starting point of the older prototype, not tuned for this game.
    public readonly struct CommanderQuotaRules
    {
        // Garrison of an own point with E enemies near it: ceil(E * DefenseRatioPermille / 1000) + 1.
        public readonly int DefenseRatioPermille;
        // Garrison of an own point with no enemies near but a non-own neighbour.
        public readonly int FrontierGarrison;
        // All garrisons together are capped at floor(soldiers * MaxDefenseSharePermille / 1000).
        public readonly int MaxDefenseSharePermille;
        // At most this many points are attacked at once (values below 1 act as 1).
        public readonly int MaxAttackTargets;
        // Attack score, lower is better: enemies * EnemyWeight + whole metres to the anchor * DistanceWeightPerMetre,
        // minus Stickiness for a point that was attacked last time.
        public readonly int EnemyWeight;
        public readonly int DistanceWeightPerMetre;
        public readonly int Stickiness;

        public CommanderQuotaRules(int defenseRatioPermille, int frontierGarrison, int maxDefenseSharePermille,
            int maxAttackTargets, int enemyWeight, int distanceWeightPerMetre, int stickiness)
        {
            if (defenseRatioPermille < 0 || defenseRatioPermille > 100000) throw new ArgumentOutOfRangeException(nameof(defenseRatioPermille));
            if (frontierGarrison < 0) throw new ArgumentOutOfRangeException(nameof(frontierGarrison));
            if (maxDefenseSharePermille < 0 || maxDefenseSharePermille > 1000) throw new ArgumentOutOfRangeException(nameof(maxDefenseSharePermille));
            if (maxAttackTargets < 1) throw new ArgumentOutOfRangeException(nameof(maxAttackTargets));
            if (enemyWeight < 0 || distanceWeightPerMetre < 0 || stickiness < 0) throw new ArgumentOutOfRangeException(nameof(enemyWeight));
            DefenseRatioPermille = defenseRatioPermille;
            FrontierGarrison = frontierGarrison;
            MaxDefenseSharePermille = maxDefenseSharePermille;
            MaxAttackTargets = maxAttackTargets;
            EnemyWeight = enemyWeight;
            DistanceWeightPerMetre = distanceWeightPerMetre;
            Stickiness = stickiness;
        }

        public static CommanderQuotaRules Default => new CommanderQuotaRules(1300, 2, 400, 3, 200, 1, 600);
    }

    // Team-level commander as pure functions: how many soldiers each control point wants and which points to attack.
    // No state, no allocation, integers only; the caller owns every buffer. Any number of teams: owner 0 is neutral,
    // "enemy" is every owner other than the planning team. Ties always go to the lower point index.
    // Not called by any system yet (wiring and saved assignment belong to the dispatch component).
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
        // flags (read only, must not be attackOut). Writes desiredOut[i] (soldiers wanted at i) and attackOut[i].
        public static void Plan(int team, int count, int[] owners, SimVec3[] centers, int[] enemiesNear, bool[] adjacent,
            int soldiers, SimVec3 anchor, bool[] previousAttack, in CommanderQuotaRules rules, int[] desiredOut, bool[] attackOut)
        {
            if (team <= 0) throw new ArgumentOutOfRangeException(nameof(team));
            if (owners == null || centers == null || enemiesNear == null || adjacent == null || previousAttack == null
                || desiredOut == null || attackOut == null) throw new ArgumentNullException(nameof(owners));
            if (ReferenceEquals(previousAttack, attackOut)) throw new ArgumentException("previousAttack must not be attackOut.", nameof(attackOut));
            if (count < 0 || count > owners.Length || count > centers.Length || count > enemiesNear.Length || count > previousAttack.Length
                || count > desiredOut.Length || count > attackOut.Length || (long)count * count > adjacent.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (soldiers < 0) throw new ArgumentOutOfRangeException(nameof(soldiers));
            CheckBounds(anchor);
            for (int i = 0; i < count; i++)
            {
                if (enemiesNear[i] < 0) throw new ArgumentOutOfRangeException(nameof(enemiesNear));
                CheckBounds(centers[i]);
            }

            int defense = 0, candidates = 0;
            for (int i = 0; i < count; i++)
            {
                desiredOut[i] = 0;
                attackOut[i] = false;
                if (owners[i] == team)
                {
                    if (enemiesNear[i] > 0) desiredOut[i] = checked((int)(((long)enemiesNear[i] * rules.DefenseRatioPermille + 999) / 1000 + 1));
                    else if (Touches(i, team, false, count, owners, adjacent)) desiredOut[i] = rules.FrontierGarrison;
                    defense = checked(defense + desiredOut[i]);
                }
                else if (Touches(i, team, true, count, owners, adjacent)) candidates++;
            }

            // No non-own point borders an own one (e.g. the team holds nothing): every non-own point is a candidate.
            bool fallback = candidates == 0;
            if (fallback)
                for (int i = 0; i < count; i++)
                    if (owners[i] != team) candidates++;

            int maxDefense = (int)((long)soldiers * rules.MaxDefenseSharePermille / 1000);
            if (defense > maxDefense)
            {
                int wanted = defense;
                defense = 0;
                for (int i = 0; i < count; i++)
                {
                    if (owners[i] != team || desiredOut[i] == 0) continue;
                    desiredOut[i] = Math.Max(1, (int)((long)desiredOut[i] * maxDefense / wanted));
                    defense += desiredOut[i];
                }
            }
            if (candidates == 0) return;

            int targets = Math.Min(candidates, Math.Max(1, rules.MaxAttackTargets));
            int pool = Math.Max(targets, soldiers - defense);
            for (int k = 0; k < targets; k++)
            {
                int best = -1;
                long bestScore = 0;
                for (int i = 0; i < count; i++)
                {
                    if (attackOut[i] || owners[i] == team || !(fallback || Touches(i, team, true, count, owners, adjacent))) continue;
                    long score = Score(enemiesNear[i], centers[i], anchor, previousAttack[i], rules);
                    if (best < 0 || score < bestScore)
                    {
                        best = i;
                        bestScore = score;
                    }
                }
                attackOut[best] = true;
                desiredOut[best] = pool / targets + (k < pool % targets ? 1 : 0);
            }
        }

        // ownNeighbour: does i have a neighbour owned by team; otherwise: does i have a neighbour not owned by team.
        private static bool Touches(int i, int team, bool ownNeighbour, int count, int[] owners, bool[] adjacent)
        {
            int row = i * count;
            for (int j = 0; j < count; j++)
                if (j != i && adjacent[row + j] && (owners[j] == team) == ownNeighbour) return true;
            return false;
        }

        private static long Score(int enemies, SimVec3 center, SimVec3 anchor, bool previous, in CommanderQuotaRules rules)
        {
            SimFix dx = center.X - anchor.X, dz = center.Z - anchor.Z;
            long metres = SimMath.Sqrt(dx * dx + dz * dz).Raw / SimFix.Scale;
            return (long)enemies * rules.EnemyWeight + metres * rules.DistanceWeightPerMetre - (previous ? rules.Stickiness : 0);
        }

        private static void CheckBounds(SimVec3 at)
        {
            if (at.X.Raw > CoordinateLimitRaw || at.X.Raw < -CoordinateLimitRaw || at.Z.Raw > CoordinateLimitRaw || at.Z.Raw < -CoordinateLimitRaw)
                throw new ArgumentOutOfRangeException(nameof(at));
        }
    }
}
