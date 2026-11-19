namespace WarCore.Sim
{
    public sealed partial class PlateField
    {
        // Walks the map cells the XZ projection visits, in t order. A crossing of the line x = X0 + L S at t = n / d (n >= 0):
        // moving +x the point on the line already belongs to column L; moving -x it still belongs to column L and is in L - 1
        // just after. A corner crossed with one axis moving + and the other - is visited at that single t by the cell the
        // corner belongs to. Every other visit is an interval of positive length, where open or closed ends change neither
        // whether 0 < f < thick has a solution nor its infimum (f is continuous, the band open), so intervals are taken closed.
        private void Walk(ref Query q)
        {
            long ox = OriginX.Raw, oz = OriginZ.Raw;
            long ix = FloorDiv(q.Fx - ox, S), iz = FloorDiv(q.Fz - oz, S);
            long tn = 0, td = 1;
            while (true)
            {
                if (Leaving(ix, q.Dx, Width) || Leaving(iz, q.Dz, Depth)) return;
                if (q.Found && Less(q.BestN, q.BestD, tn, td)) return;
                bool hasX = Crossing(ix, q.Dx, q.Fx, ox, Width, out long xn, out long xd, out long ixAfter);
                bool hasZ = Crossing(iz, q.Dz, q.Fz, oz, Depth, out long zn, out long zd, out long izAfter);
                long en, ed;
                if (hasX && (!hasZ || FixedUInt128.Multiply((ulong)xn, (ulong)zd).CompareTo(FixedUInt128.Multiply((ulong)zn, (ulong)xd)) <= 0)) { en = xn; ed = xd; }
                else if (hasZ) { en = zn; ed = zd; }
                else { en = 1; ed = 1; }
                if (en > ed) { en = 1; ed = 1; hasX = hasZ = false; }
                Cell(ref q, ix, iz, tn, td, en, ed);
                bool crossX = hasX && SameT(xn, xd, en, ed), crossZ = hasZ && SameT(zn, zd, en, ed);
                if (!crossX && !crossZ) return;
                bool positive = (crossX && q.Dx > 0) || (crossZ && q.Dz > 0), negative = (crossX && q.Dx < 0) || (crossZ && q.Dz < 0);
                if (positive && negative)
                {
                    if (q.Found && Less(q.BestN, q.BestD, en, ed)) return;
                    Cell(ref q, crossX && q.Dx > 0 ? ixAfter : ix, crossZ && q.Dz > 0 ? izAfter : iz, en, ed, en, ed);
                }
                if (crossX) ix = ixAfter;
                if (crossZ) iz = izAfter;
                if (en == ed)
                {
                    // A crossing at t = 1: the end point is the new cell's only when every crossing there moved +.
                    if (!negative && !(q.Found && Less(q.BestN, q.BestD, en, ed))) Cell(ref q, ix, iz, en, ed, en, ed);
                    return;
                }
                tn = en; td = ed;
            }
        }

        // The projection never comes back into the map along this axis.
        private static bool Leaving(long i, long d, int size) => d > 0 ? i >= size : d < 0 ? i < 0 : i < 0 || i >= size;

        // Next crossing on one axis from cell index i (outside the map: jump to the map's edge line). t = n / den, n >= 0.
        private static bool Crossing(long i, long d, long f, long origin, int size, out long n, out long den, out long after)
        {
            n = 0; den = 1; after = i;
            if (d == 0) return false;
            if (d > 0)
            {
                long line = i < 0 ? 0 : i + 1;
                n = origin + line * S - f; den = d; after = line;
            }
            else
            {
                long line = i >= size ? size : i;
                n = f - origin - line * S; den = -d; after = line - 1;
            }
            return true;
        }

        // The infimum, over the slabs solid in cell (ix, iz), of t in [ta, tb] with 0 < f(t) < thick; kept if it is earlier
        // than the best so far; a tie keeps the smaller id (slab index order is id order). Ties across cells happen after a
        // cell met at a single point.
        private void Cell(ref Query q, long ix, long iz, long an, long ad, long bn, long bd)
        {
            if (ix < 0 || iz < 0 || ix >= Width || iz >= Depth) return;
            int key = (int)(iz * Width + ix);
            long x0 = OriginX.Raw + ix * S, z0 = OriginZ.Raw + iz * S;
            for (int n = cellStart[key]; n < cellStart[key + 1]; n++)
            {
                int k = cellSlabs[n];
                PlateSlab p = slabs[k];
                int i = (int)ix - p.X, j = (int)iz - p.Z;
                long h00 = p.TopRaw(i, j), a = p.TopRaw(i + 1, j) - h00, b = p.TopRaw(i, j + 1) - h00;
                long c = S * h00 + a * (q.Fx - x0) + b * (q.Fz - z0) - S * q.Fy;
                long slope = a * q.Dx + b * q.Dz - S * q.Dy;
                long band = S * p.ThickRaw;
                long cn, cd;
                if (slope == 0)
                {
                    if (c <= 0 || c >= band) continue;
                    cn = an; cd = ad;
                }
                else
                {
                    long loN, hiN, den;
                    if (slope > 0) { loN = -c; hiN = band - c; den = slope; }
                    else { loN = c - band; hiN = c; den = -slope; }
                    if (!Less(loN, den, bn, bd) || !Less(an, ad, hiN, den)) continue;
                    if (Less(an, ad, loN, den)) { cn = loN; cd = den; }
                    else { cn = an; cd = ad; }
                }
                if (!q.Found || Less(cn, cd, q.BestN, q.BestD) || (k < q.BestSlab && !Less(q.BestN, q.BestD, cn, cd)))
                { q.Found = true; q.BestN = cn; q.BestD = cd; q.BestSlab = k; }
            }
        }

        // a / b < c / d for b, d > 0.
        private static bool Less(long a, long b, long c, long d) => (FixedInt128.Product(a, d) - FixedInt128.Product(c, b)).Sign < 0;

        private static bool SameT(long a, long b, long c, long d) => FixedUInt128.Multiply((ulong)a, (ulong)d).CompareTo(FixedUInt128.Multiply((ulong)c, (ulong)b)) == 0;

        private static long FloorDiv(long a, long b) { long q = a / b; if ((a % b != 0) && ((a < 0) != (b < 0))) q--; return q; }

        // floor(a * b / d) for 0 <= a, d > 0.
        private static long FloorMulDiv(long a, long b, long d)
        {
            FixedInt128 p = FixedInt128.Product(a, b);
            ulong q = p.Magnitude.DivideToUInt64((ulong)d);
            bool exact = FixedUInt128.Multiply(q, (ulong)d).CompareTo(p.Magnitude) == 0;
            long r = (long)q;
            return p.Negative ? (exact ? -r : -r - 1) : r;
        }
    }
}
