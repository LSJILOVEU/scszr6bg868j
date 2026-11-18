using System;
using System.Collections.Generic;

namespace WarCore.Replay
{
    // Four layout numbers that turn "the army moves as one block" into assertable values.
    // All inputs are positions in metres of one team's alive, deployed units.
    public static class Metrics
    {
        // A unit counts as attacking point p when p is its nearest point, p is not owned by its team,
        // and it stands within this distance of p's centre.
        public const double AttackRange = 25.0;
        // Bearing spread needs at least this many attackers; point share and nearest-neighbour spread need at
        // least this many units on the field (with a handful left the numbers are noise).
        public const int MinAttackers = 5, MinUnits = 10;

        // (1) Largest share of the team standing inside a single point circle. 0..1; NaN below MinUnits.
        public static double MaxPointShare(IReadOnlyList<(double x, double z)> units, IReadOnlyList<(double x, double z, double r)> points)
        {
            if (units.Count < MinUnits) return double.NaN;
            int best = 0;
            foreach (var p in points)
            {
                int inside = 0;
                foreach (var u in units)
                {
                    double dx = u.x - p.x, dz = u.z - p.z;
                    if (dx * dx + dz * dz <= p.r * p.r) inside++;
                }
                best = Math.Max(best, inside);
            }
            return (double)best / units.Count;
        }

        // (2) Spread of each unit's distance to its nearest teammate: interquartile range / median.
        // A perfect lattice gives 0. Quartiles, not std / mean, so a few stragglers far from the main body
        // do not swamp the number. NaN below MinUnits or when the median is 0.
        public static double NearestNeighbourSpread(IReadOnlyList<(double x, double z)> units)
        {
            int n = units.Count;
            if (n < MinUnits) return double.NaN;
            var nearest = new double[n];
            for (int i = 0; i < n; i++)
            {
                double best = double.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    double dx = units[i].x - units[j].x, dz = units[i].z - units[j].z, d = dx * dx + dz * dz;
                    if (d < best) best = d;
                }
                nearest[i] = Math.Sqrt(best);
            }
            Array.Sort(nearest);
            double median = Quantile(nearest, 0.5);
            if (median <= 1e-9) return double.NaN;
            double spread = (Quantile(nearest, 0.75) - Quantile(nearest, 0.25)) / median;
            return spread < 1e-9 ? 0 : spread;
        }

        // Linear interpolation between closest ranks on a sorted array.
        private static double Quantile(double[] sorted, double q)
        {
            double pos = q * (sorted.Length - 1);
            int lo = (int)Math.Floor(pos), hi = Math.Min(lo + 1, sorted.Length - 1);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
        }

        // (3) Circular variance (1 - mean resultant length) of attacker bearings around the point
        // that has the most attackers. 0 = all attack from one side, 1 = evenly from all sides.
        // owners[p] is the owning team of point p (0 = neutral). NaN when no point has MinAttackers.
        public static double AttackBearingSpread(IReadOnlyList<(double x, double z)> units, int team,
            IReadOnlyList<(double x, double z, double r)> points, IReadOnlyList<int> owners)
        {
            int pc = points.Count;
            if (pc == 0) return double.NaN;
            var count = new int[pc]; var sx = new double[pc]; var sz = new double[pc];
            foreach (var u in units)
            {
                int nearest = -1; double best = double.MaxValue;
                for (int p = 0; p < pc; p++)
                {
                    double dx = u.x - points[p].x, dz = u.z - points[p].z, d = dx * dx + dz * dz;
                    if (d < best) { best = d; nearest = p; }
                }
                if (owners[nearest] == team || best > AttackRange * AttackRange || best == 0) continue;
                double len = Math.Sqrt(best);
                count[nearest]++;
                sx[nearest] += (u.x - points[nearest].x) / len; sz[nearest] += (u.z - points[nearest].z) / len;
            }
            int target = -1;
            for (int p = 0; p < pc; p++) if (count[p] >= MinAttackers && (target < 0 || count[p] > count[target])) target = p;
            if (target < 0) return double.NaN;
            double resultant = Math.Sqrt(sx[target] * sx[target] + sz[target] * sz[target]) / count[target];
            return 1 - resultant;
        }

        // A unit is close to a teammate below this horizontal distance (metres): shoulder to shoulder.
        public const double CloseDistance = 1.2;
        // A unit idles once its position has not changed for this many ticks.
        public const int IdleTicks = 60;

        // (4) Share of the team whose nearest teammate is closer than CloseDistance. 0..1; NaN below MinUnits.
        public static double CloseShare(IReadOnlyList<(double x, double z)> units)
        {
            int n = units.Count;
            if (n < MinUnits) return double.NaN;
            int close = 0;
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    double dx = units[i].x - units[j].x, dz = units[i].z - units[j].z;
                    if (dx * dx + dz * dz < CloseDistance * CloseDistance) { close++; break; }
                }
            return (double)close / n;
        }

        // (6) CloseShare restricted to units near a point: among units within 2 x radius of some point's centre (the range
        // depends on the map only), the share whose nearest teammate in the whole team is closer than CloseDistance.
        // 0..1; NaN when fewer than MinUnits units are near a point.
        public static double CloseShareNearPoints(IReadOnlyList<(double x, double z)> units, IReadOnlyList<(double x, double z, double r)> points)
        {
            int n = units.Count, near = 0, close = 0;
            for (int i = 0; i < n; i++)
            {
                bool isNear = false;
                foreach (var p in points)
                {
                    double dx = units[i].x - p.x, dz = units[i].z - p.z;
                    if (dx * dx + dz * dz <= 4 * p.r * p.r) { isNear = true; break; }
                }
                if (!isNear) continue;
                near++;
                for (int j = 0; j < n; j++)
                {
                    if (i == j) continue;
                    double dx = units[i].x - units[j].x, dz = units[i].z - units[j].z;
                    if (dx * dx + dz * dz < CloseDistance * CloseDistance) { close++; break; }
                }
            }
            return near < MinUnits ? double.NaN : (double)close / near;
        }

        // (5) Share of the team that idles (idle[i]: position unchanged for IdleTicks) outside every slot zone, i.e. farther
        // than radius + ring from every point's centre. 0..1; NaN below MinUnits.
        public static double IdleOutside(IReadOnlyList<(double x, double z)> units, IReadOnlyList<bool> idle,
            IReadOnlyList<(double x, double z, double r)> points, double ring)
        {
            int n = units.Count;
            if (n < MinUnits) return double.NaN;
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (!idle[i]) continue;
                bool outside = true;
                foreach (var p in points)
                {
                    double dx = units[i].x - p.x, dz = units[i].z - p.z, zone = p.r + ring;
                    if (dx * dx + dz * dz <= zone * zone) { outside = false; break; }
                }
                if (outside) count++;
            }
            return (double)count / n;
        }

        // An enemy counts as near within this horizontal distance (metres); idleNoEnemy looks for enemies within IdleEnemyRange.
        public const double NearEnemyRange = 20.0, IdleEnemyRange = 45.0;

        // True when some enemy stands within range (horizontal, inclusive) of u.
        private static bool AnyWithin((double x, double z) u, IReadOnlyList<(double x, double z)> enemies, double range)
        {
            foreach (var e in enemies)
            {
                double dx = u.x - e.x, dz = u.z - e.z;
                if (dx * dx + dz * dz <= range * range) return true;
            }
            return false;
        }

        // (9, 10) A counted share of the team (pinned, broken). 0..1; NaN below MinUnits.
        public static double Share(int count, int units) => units < MinUnits ? double.NaN : (double)count / units;

        // (7) Share of the team with an enemy (alive, on the field) within NearEnemyRange. 0..1; NaN below MinUnits.
        public static double EnemyWithin(IReadOnlyList<(double x, double z)> units, IReadOnlyList<(double x, double z)> enemies, double range = NearEnemyRange)
        {
            int n = units.Count;
            if (n < MinUnits) return double.NaN;
            int count = 0;
            foreach (var u in units) if (AnyWithin(u, enemies, range)) count++;
            return (double)count / n;
        }

        // (8) IdleOutside restricted to units with no enemy within IdleEnemyRange: idle, beyond radius + ring of every point
        // and nobody to fight. 0..1; NaN below MinUnits.
        public static double IdleNoEnemy(IReadOnlyList<(double x, double z)> units, IReadOnlyList<bool> idle, IReadOnlyList<(double x, double z)> enemies,
            IReadOnlyList<(double x, double z, double r)> points, double ring)
        {
            int n = units.Count;
            if (n < MinUnits) return double.NaN;
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                if (!idle[i] || AnyWithin(units[i], enemies, IdleEnemyRange)) continue;
                bool outside = true;
                foreach (var p in points)
                {
                    double dx = units[i].x - p.x, dz = units[i].z - p.z, zone = p.r + ring;
                    if (dx * dx + dz * dz <= zone * zone) { outside = false; break; }
                }
                if (outside) count++;
            }
            return (double)count / n;
        }

        // Running mean and peak of one per-frame series, skipping NaN frames.
        public sealed class Series
        {
            public double Sum, Peak = double.NaN; public int Count;
            public void Add(double v)
            {
                if (double.IsNaN(v)) return;
                Sum += v; Count++;
                if (double.IsNaN(Peak) || v > Peak) Peak = v;
            }
            public double Mean => Count == 0 ? double.NaN : Sum / Count;
        }
    }
}
