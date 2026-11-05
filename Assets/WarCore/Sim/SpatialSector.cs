using System.Diagnostics;

namespace WarCore.Sim
{
    // Exact reductions remove powers of two only; uncertain large determinants retain the wide path.
    internal static class SpatialSector
    {
        private const long LocalLimit = 1L << 20;

        internal static bool TryIntersects(in SpatialBody body, in SpatialRequest q, out bool hit)
        {
            SpatialWidth.AssertWorldLimit(SimVec3.WorldLimitMetres);
            SpatialGeometry.AssertCapsule(in body);
            hit = false;
            long radius = body.Collider.Radius.Raw;
            long spine = body.Collider.Height.Raw / 2 - radius;
            long y = body.Position.Y.Raw - q.Origin.Y.Raw;
            y = y < 0 ? -y : y;
            y -= spine + q.HalfHeight.Raw;
            if (y < 0)
                y = 0;
            long x = body.Position.X.Raw - q.Origin.X.Raw;
            long z = body.Position.Z.Raw - q.Origin.Z.Raw;
            long reach = radius + q.Radius.Raw;
            if (y > radius || x < -reach || x > reach || z < -reach || z > reach)
                return true;
            // The guarded disk arithmetic has at least 20 spare bits even before the 128-bit comparisons.
            if (reach > LocalLimit)
                return false;
            ulong distance2 = Square(x) + Square(z);
            ulong disk2 = Square(radius) - Square(y);
            ulong length2 = Square(q.Radius.Raw);
            if (distance2 > length2 + disk2)
            {
                ulong excess = distance2 - length2 - disk2;
                if (FixedUInt128.Multiply(excess, excess).CompareTo(FixedUInt128.Multiply(4 * length2, disk2)) > 0)
                    return true;
            }
            if (q.Cosine == -1000 || distance2 == 0)
            {
                hit = true;
                return true;
            }
            long dx = q.Direction.X.Raw;
            long dz = q.Direction.Z.Raw;
            Reduce(ref dx, ref dz);
            if (Abs(dx) > LocalLimit || Abs(dz) > LocalLimit)
                return false;
            long dot = x * dx + z * dz;
            ulong direction2 = Square(dx) + Square(dz);
            // A convex forward cone lies in this half-plane; a separated disk cannot touch either edge.
            if (q.Cosine >= 0 && dot < 0 && FixedUInt128.Multiply(Abs(dot), Abs(dot))
                .CompareTo(FixedUInt128.Multiply(disk2, direction2)) > 0)
                return true;
            FixedUInt128 lhs = Scale(FixedUInt128.Multiply(Abs(dot), Abs(dot)), 1000000);
            FixedUInt128 rhs = Scale(FixedUInt128.Multiply(distance2, direction2), (ulong)((long)q.Cosine * q.Cosine));
            int comparison = lhs.CompareTo(rhs);
            bool inside = q.Cosine >= 0 ? dot >= 0 && comparison >= 0 : dot >= 0 || comparison <= 0;
            if (inside)
            {
                hit = true;
                return true;
            }
            long cos = (long)q.Cosine * (1L << 32);
            long sin = GeometrySinTable.Sin(q.Cosine);
            Reduce(ref cos, ref sin);
            // Scaling the edge exactly keeps the frozen table's last bit, including irrational angles.
            if (Abs(dx) > (1UL << 17) || Abs(dz) > (1UL << 17))
                return false;
            long ax = dx * cos - dz * sin;
            long az = dz * cos + dx * sin;
            long bx = dx * cos + dz * sin;
            long bz = dz * cos - dx * sin;
            Reduce(ref ax, ref az);
            Reduce(ref bx, ref bz);
            if (Abs(ax) > LocalLimit || Abs(az) > LocalLimit || Abs(bx) > LocalLimit || Abs(bz) > LocalLimit)
                return false;
            hit = NearEdge(x, z, ax, az, distance2, length2, disk2)
                || NearEdge(x, z, bx, bz, distance2, length2, disk2);
            return true;
        }

        private static bool NearEdge(long x, long z, long dx, long dz, ulong distance2, ulong length2, ulong disk2)
        {
            long dot = x * dx + z * dz;
            if (dot <= 0)
                return distance2 <= disk2;
            ulong d2 = Square(dx) + Square(dz);
            FixedUInt128 dot2 = FixedUInt128.Multiply((ulong)dot, (ulong)dot);
            if (dot2.CompareTo(FixedUInt128.Multiply(length2, d2)) > 0)
            {
                if (distance2 + length2 <= disk2)
                    return true;
                ulong excess = distance2 + length2 - disk2;
                // Each factor is bounded by the 2^20 guard; the largest product is below 2^125.
                return Scale(FixedUInt128.Multiply(excess, excess), d2)
                    .CompareTo(Scale(dot2, 4 * length2)) <= 0;
            }
            long cross = x * dz - z * dx;
            return FixedUInt128.Multiply(Abs(cross), Abs(cross)).CompareTo(FixedUInt128.Multiply(disk2, d2)) <= 0;
        }

        private static ulong Abs(long value) => (ulong)(value < 0 ? -value : value);
        private static ulong Square(long value) => Abs(value) * Abs(value);

        private static FixedUInt128 Scale(FixedUInt128 value, ulong scale)
        {
            FixedUInt128 low = FixedUInt128.Multiply(value.Low, scale);
            return new FixedUInt128(low.Low, low.High + value.High * scale);
        }

        private static void Reduce(ref long x, ref long z)
        {
            ulong bits = Abs(x) | Abs(z);
            if (bits == 0)
                return;
            int shift = 0;
            if ((bits & 0xffffffffUL) == 0)
            {
                bits >>= 32;
                shift += 32;
            }
            if ((bits & 0xffffUL) == 0)
            {
                bits >>= 16;
                shift += 16;
            }
            if ((bits & 0xffUL) == 0)
            {
                bits >>= 8;
                shift += 8;
            }
            while ((bits & 1) == 0)
            {
                bits >>= 1;
                shift++;
            }
            x = SimInteger.ShiftRight(x, shift);
            z = SimInteger.ShiftRight(z, shift);
        }
    }
}
