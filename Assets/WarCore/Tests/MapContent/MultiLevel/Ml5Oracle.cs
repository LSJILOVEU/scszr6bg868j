using System.Collections.Generic;
using System.Numerics;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-5 acceptance 4: an independent answer with BigInteger rationals. Each solid cell of each plate is a prism; on it the
    // segment's feasible t is the intersection of [0, 1] with six linear constraints (x >= left, x < right, z the same,
    // 0 < f < thick), closed or open as written. The answer is the smallest infimum over all prisms, ties to the smaller id.
    // Nothing here calls into PlateField beyond its public slab data.
    internal static class Ml5Oracle
    {
        internal struct Answer
        {
            internal bool Found;
            internal ulong Id;
            internal BigInteger Num, Den; // t*, Den > 0
            internal long X, Y, Z;
            internal bool Tie; // another plate has the same t*
        }

        private struct Bound { internal BigInteger N, D; internal bool Closed; } // N / D, D > 0

        internal static Answer Solve(long ox, long oz, IReadOnlyList<PlateSlab> plates, long fx, long fy, long fz, long tx, long ty, long tz)
        {
            const long S = SimFix.Scale;
            long dx = tx - fx, dy = ty - fy, dz = tz - fz;
            long minX = System.Math.Min(fx, tx), maxX = System.Math.Max(fx, tx), minZ = System.Math.Min(fz, tz), maxZ = System.Math.Max(fz, tz);
            var best = new Answer();
            foreach (PlateSlab p in plates)
                for (int j = 0; j < p.Depth; j++)
                    for (int i = 0; i < p.Width; i++)
                    {
                        if (!p.IsSolid(i, j)) continue;
                        long x0 = ox + (p.X + i) * S, z0 = oz + (p.Z + j) * S;
                        if (x0 + S <= minX || x0 > maxX || z0 + S <= minZ || z0 > maxZ) continue; // only ever widens
                        BigInteger h00 = p.TopRaw(i, j), a = p.TopRaw(i + 1, j) - h00, b = p.TopRaw(i, j + 1) - h00;
                        // S f(t) = c + k t
                        BigInteger c = S * h00 + a * (fx - x0) + b * (fz - z0) - (BigInteger)S * fy;
                        BigInteger k = a * dx + b * dz - (BigInteger)S * dy;
                        BigInteger band = (BigInteger)S * p.ThickRaw;
                        var lo = new Bound { N = 0, D = 1, Closed = true };
                        var hi = new Bound { N = 1, D = 1, Closed = true };
                        bool ok = Apply(ref lo, ref hi, fx - x0, dx, true)          // x >= x0
                            && Apply(ref lo, ref hi, x0 + S - fx, -dx, false)          // x < x0 + S
                            && Apply(ref lo, ref hi, fz - z0, dz, true)
                            && Apply(ref lo, ref hi, z0 + S - fz, -dz, false)
                            && Apply(ref lo, ref hi, c, k, false)                       // f > 0
                            && Apply(ref lo, ref hi, band - c, -k, false);              // f < thick
                        if (!ok) continue;
                        int cmp = (lo.N * hi.D).CompareTo(hi.N * lo.D);
                        if (cmp > 0 || (cmp == 0 && !(lo.Closed && hi.Closed))) continue;
                        bool tie = false;
                        if (best.Found)
                        {
                            int against = (lo.N * best.Den).CompareTo(best.Num * lo.D);
                            if (against == 0 && p.Id != best.Id) { best.Tie = true; tie = true; }
                            if (against > 0 || (against == 0 && p.Id >= best.Id)) continue;
                        }
                        best.Found = true; best.Id = p.Id; best.Num = lo.N; best.Den = lo.D; best.Tie = tie;
                    }
            if (best.Found)
            {
                best.X = fx + (long)Floor(best.Num * dx, best.Den);
                best.Y = fy + (long)Floor(best.Num * dy, best.Den);
                best.Z = fz + (long)Floor(best.Num * dz, best.Den);
            }
            return best;
        }

        // alpha + beta t >= 0 (closed) or > 0 (open), folded into [lo, hi]. False when it can never hold.
        private static bool Apply(ref Bound lo, ref Bound hi, BigInteger alpha, BigInteger beta, bool closed)
        {
            if (beta.IsZero) return closed ? alpha.Sign >= 0 : alpha.Sign > 0;
            if (beta.Sign > 0)
            {
                var b = new Bound { N = -alpha, D = beta, Closed = closed }; // t >= -alpha / beta
                int cmp = (b.N * lo.D).CompareTo(lo.N * b.D);
                if (cmp > 0 || (cmp == 0 && !closed)) lo = b;
            }
            else
            {
                var b = new Bound { N = alpha, D = -beta, Closed = closed }; // t <= alpha / -beta
                int cmp = (b.N * hi.D).CompareTo(hi.N * b.D);
                if (cmp < 0 || (cmp == 0 && !closed)) hi = b;
            }
            return true;
        }

        private static BigInteger Floor(BigInteger a, BigInteger d)
        {
            BigInteger q = BigInteger.DivRem(a, d, out BigInteger r);
            return r.Sign < 0 ? q - 1 : q;
        }
    }
}
