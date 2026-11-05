namespace WarCore.Sim
{
    // Oriented-box narrow phase (query-contract section 12). Every predicate works in box-local
    // coordinates scaled by the yaw denominator c: X' = a*dx - b*dz and Z' = b*dx + a*dz with
    // a^2 + b^2 = c^2, so lengths scale exactly by c. Y is multiplied by c where an isotropic frame
    // is needed. Nothing is rounded before the final comparison or the reported distance.
    // Bounds (w = 2^30, c <= 2^24): world differences <= 2w, |X'|,|Z'| <= c*sqrt(2)*2w < 2^55.5,
    // scaled extents c*hx, c*hz, c*hy <= 2^54, sweep and direction components <= w.
    internal static class SpatialBox
    {
        // World AABB half widths: ceil((|a|hx + |b|hz) / c) <= hx + hz <= 2^30. Capsules use the radius.
        // The capsule test is inlined into grid rebuilds; the box arithmetic stays out of line.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static long ExtentX(in Collider collider)
            => collider.ShapeKind == ShapeKind.VerticalCapsule ? collider.Radius.Raw : BoxExtentX(in collider);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static long ExtentZ(in Collider collider)
            => collider.ShapeKind == ShapeKind.VerticalCapsule ? collider.Radius.Raw : BoxExtentZ(in collider);

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long BoxExtentX(in Collider collider)
        {
            BoxYaw yaw = collider.Yaw;
            return Ceiling(Abs(yaw.Cos) * collider.Radius.Raw + Abs(yaw.Sin) * collider.HalfDepth.Raw, yaw.Denominator);
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long BoxExtentZ(in Collider collider)
        {
            BoxYaw yaw = collider.Yaw;
            return Ceiling(Abs(yaw.Sin) * collider.Radius.Raw + Abs(yaw.Cos) * collider.HalfDepth.Raw, yaw.Denominator);
        }

        // Numerator <= 2^24 * 2^30; the result never exceeds 2^30.
        private static long Ceiling(long numerator, long denominator) => (numerator + denominator - 1) / denominator;
        private static long Abs(long value) => value < 0 ? -value : value;

        // Out of line so the capsule callers keep their pre-3a size and inlining.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static bool Intersects(in SpatialBody body, in SpatialRequest q, out SimFix distance, out bool enteredNarrowPhase)
        {
            SpatialWidth.AssertWorldLimit(SimVec3.WorldLimitMetres);
            distance = default;
            enteredNarrowPhase = true;
            switch (q.Kind)
            {
                case SpatialQueryKind.Sphere:
                    return Sphere(in body, in q);
                case SpatialQueryKind.Cylinder:
                    return Cylinder(in body, in q);
                case SpatialQueryKind.Sector:
                    return Sector(in body, in q);
            }
            enteredNarrowPhase = SpatialBounds.RayMayHit(in body, in q);
            if (!enteredNarrowPhase)
                return false;
            return q.Kind == SpatialQueryKind.Capsule ? Sweep(in body, in q) : Ray(in body, in q, out distance);
        }

        // Rotates a world XZ vector into scaled box-local coordinates.
        private static void Local(BoxYaw yaw, long dx, long dz, out long x, out long z)
        {
            long a = yaw.Cos;
            long b = yaw.Sin;
            x = a * dx - b * dz;
            z = b * dx + a * dz;
        }

        // Distance outside [-extent, extent]; |value| < 2^56 so negation is exact.
        private static long Excess(long value, long extent)
        {
            long magnitude = value < 0 ? -value : value;
            return magnitude > extent ? magnitude - extent : 0;
        }

        private static long Clamp(long value, long extent) => value < -extent ? -extent : value > extent ? extent : value;
        private static ulong Square(long value) => (ulong)(value < 0 ? -value : value) * (ulong)(value < 0 ? -value : value);
        private static FixedUInt128 Square128(long value) => FixedUInt128.Multiply(FixedInt128.Abs(value), FixedInt128.Abs(value));
        private static long HalfY(in Collider collider) => collider.Height.Raw / 2;

        // Sum of squared local excesses including c^2*ey^2: <= c^2 * (8w^2 + 4w^2) < 2^112.
        private static FixedUInt128 PointDistance2(in SpatialBody body, SimVec3 point)
        {
            BoxYaw yaw = body.Collider.Yaw;
            long c = yaw.Denominator;
            Local(yaw, point.X.Raw - body.Position.X.Raw, point.Z.Raw - body.Position.Z.Raw, out long x, out long z);
            long ex = Excess(x, c * body.Collider.Radius.Raw);
            long ez = Excess(z, c * body.Collider.HalfDepth.Raw);
            long ey = Excess(point.Y.Raw - body.Position.Y.Raw, HalfY(body.Collider));
            return Square128(ex) + Square128(ez) + FixedUInt128.Multiply(Square(ey), (ulong)(c * c));
        }

        private static bool Sphere(in SpatialBody body, in SpatialRequest q)
        {
            long c = body.Collider.Yaw.Denominator;
            return PointDistance2(in body, q.Origin).CompareTo(FixedUInt128.Multiply(Square(q.Radius.Raw), (ulong)(c * c))) <= 0;
        }

        private static bool OverlapsY(in SpatialBody body, in SpatialRequest q)
        {
            long gap = q.Origin.Y.Raw - body.Position.Y.Raw;
            return (gap < 0 ? -gap : gap) <= HalfY(body.Collider) + q.HalfHeight.Raw;
        }

        private static bool Cylinder(in SpatialBody body, in SpatialRequest q)
        {
            if (!OverlapsY(in body, in q))
                return false;
            BoxYaw yaw = body.Collider.Yaw;
            long c = yaw.Denominator;
            Local(yaw, q.Origin.X.Raw - body.Position.X.Raw, q.Origin.Z.Raw - body.Position.Z.Raw, out long x, out long z);
            long ex = Excess(x, c * body.Collider.Radius.Raw);
            long ez = Excess(z, c * body.Collider.HalfDepth.Raw);
            return (Square128(ex) + Square128(ez)).CompareTo(FixedUInt128.Multiply(Square(q.Radius.Raw), (ulong)(c * c))) <= 0;
        }

        // floor(true distance) = floor(isqrt(S) / c) because c is a positive integer.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        internal static SimFix ReferenceDistance(in SpatialBody body, SimVec3 point)
            => SimFix.FromRaw((long)(PointDistance2(in body, point).Sqrt() / (ulong)body.Collider.Yaw.Denominator));

        // Closed slab clip of t >= 0. Numerators < 2^56 and denominators < 2^55: products < 2^111.
        private static bool Slab(long origin, long direction, long extent, ref long enterN, ref long enterD, ref long exitN, ref long exitD)
        {
            if (direction == 0)
                return origin >= -extent && origin <= extent;
            long near = direction > 0 ? -extent - origin : origin - extent;
            long far = direction > 0 ? extent - origin : origin + extent;
            long denominator = direction > 0 ? direction : -direction;
            if (far < 0)
                return false;
            if (near > 0 && FixedUInt128.Multiply((ulong)near, (ulong)enterD).CompareTo(FixedUInt128.Multiply((ulong)enterN, (ulong)denominator)) > 0)
            {
                enterN = near;
                enterD = denominator;
            }
            if (exitD == 0 || FixedUInt128.Multiply((ulong)far, (ulong)exitD).CompareTo(FixedUInt128.Multiply((ulong)exitN, (ulong)denominator)) < 0)
            {
                exitN = far;
                exitD = denominator;
            }
            return FixedUInt128.Multiply((ulong)enterN, (ulong)exitD).CompareTo(FixedUInt128.Multiply((ulong)exitN, (ulong)enterD)) <= 0;
        }

        // First entry t = n/d >= 0; distance = floor(t * |D|) from n^2 * D^2 / d^2 (< 2^174 before division).
        private static bool Ray(in SpatialBody body, in SpatialRequest q, out SimFix distance)
        {
            distance = default;
            BoxYaw yaw = body.Collider.Yaw;
            long c = yaw.Denominator;
            Local(yaw, q.Origin.X.Raw - body.Position.X.Raw, q.Origin.Z.Raw - body.Position.Z.Raw, out long x, out long z);
            Local(yaw, q.Direction.X.Raw, q.Direction.Z.Raw, out long dx, out long dz);
            long enterN = 0;
            long enterD = 1;
            long exitN = 1;
            long exitD = 0;
            if (!Slab(x, dx, c * body.Collider.Radius.Raw, ref enterN, ref enterD, ref exitN, ref exitD)
                || !Slab(q.Origin.Y.Raw - body.Position.Y.Raw, q.Direction.Y.Raw, HalfY(body.Collider), ref enterN, ref enterD, ref exitN, ref exitD)
                || !Slab(z, dz, c * body.Collider.HalfDepth.Raw, ref enterN, ref enterD, ref exitN, ref exitD))
                return false;
            if (enterN == 0)
                return true;
            ulong direction2 = Square(q.Direction.X.Raw) + Square(q.Direction.Y.Raw) + Square(q.Direction.Z.Raw);
            FixedUInt128 enter2 = FixedUInt128.Multiply((ulong)enterN, (ulong)enterN);
            FixedUInt128 denominator2 = FixedUInt128.Multiply((ulong)enterD, (ulong)enterD);
            GeometryUInt192 travel2 = GeometryUInt192.Multiply(enter2, direction2);
            if (travel2.CompareTo(GeometryUInt192.Multiply(denominator2, Square(q.Range.Raw))) > 0)
                return false;
            // The range test bounds the quotient by R^2 <= 2^60.
            distance = SimFix.FromRaw((long)GeometryUInt192.Sqrt64(travel2.Divide(denominator2).Low));
            return true;
        }

        private static GeometryInt288 W(long value) => GeometryInt288.From(value);

        // Swept sphere: min over t in [0,1] of dist^2(o + t*s, box) <= R^2 in the isotropic scaled frame.
        // f is convex and C1 with a nondecreasing piecewise-linear derivative; breakpoints are where a
        // coordinate crosses a slab face. Values: o, o+s < 2^55.5; s, E, R' <= 2^54.
        private static bool Sweep(in SpatialBody body, in SpatialRequest q)
        {
            BoxYaw yaw = body.Collider.Yaw;
            long c = yaw.Denominator;
            Local(yaw, q.Origin.X.Raw - body.Position.X.Raw, q.Origin.Z.Raw - body.Position.Z.Raw, out long ox, out long oz);
            Local(yaw, q.Direction.X.Raw, q.Direction.Z.Raw, out long sx, out long sz);
            long oy = c * (q.Origin.Y.Raw - body.Position.Y.Raw);
            long sy = c * q.Direction.Y.Raw;
            long exX = c * body.Collider.Radius.Raw;
            long exY = c * HalfY(body.Collider);
            long exZ = c * body.Collider.HalfDepth.Raw;
            FixedUInt128 radius2 = FixedUInt128.Multiply(Square(q.Radius.Raw), (ulong)(c * c));
            if (Distance2(ox, oy, oz, exX, exY, exZ).CompareTo(radius2) <= 0
                || Distance2(ox + sx, oy + sy, oz + sz, exX, exY, exZ).CompareTo(radius2) <= 0)
                return true;
            // Reflect each axis so the sweep component is nonnegative; the box is symmetric.
            Reflect(ref ox, ref sx);
            Reflect(ref oy, ref sy);
            Reflect(ref oz, ref sz);
            // The minimum is interior only if the slope is negative at 0 and positive at 1.
            if ((Signed(ox, exX) * W(sx) + Signed(oy, exY) * W(sy) + Signed(oz, exZ) * W(sz)).Sign >= 0)
                return false;
            if ((Signed(ox + sx, exX) * W(sx) + Signed(oy + sy, exY) * W(sy) + Signed(oz + sz, exZ) * W(sz)).Sign <= 0)
                return false;
            // Up to six breakpoints strictly inside (0, 1), sorted by exact cross-multiplication.
            BreakpointList points = default;
            points.Add(-exX - ox, sx);
            points.Add(exX - ox, sx);
            points.Add(-exY - oy, sy);
            points.Add(exY - oy, sy);
            points.Add(-exZ - oz, sz);
            points.Add(exZ - oz, sz);
            points.Sort();
            long lowN = 0, lowD = 1, highN = 1, highD = 1;
            for (int i = 0; i < points.Count; i++)
            {
                points.Get(i, out long n, out long d);
                GeometryInt288 slope = Slope(ox, sx, exX, n, d) + Slope(oy, sy, exY, n, d) + Slope(oz, sz, exZ, n, d);
                if (slope.Sign >= 0)
                {
                    highN = n;
                    highD = d;
                    break;
                }
                lowN = n;
                lowD = d;
            }
            // On the bracketing piece the active faces are fixed; the quadratic minimum is
            // |u|^2 - (u.v)^2 / |v|^2 with u the offsets from the active faces and v the sweep.
            FixedUInt128 u2 = default;
            FixedUInt128 v2 = default;
            FixedInt128 uv = default;
            Active(ox, sx, exX, lowN, lowD, highN, highD, ref u2, ref v2, ref uv);
            Active(oy, sy, exY, lowN, lowD, highN, highD, ref u2, ref v2, ref uv);
            Active(oz, sz, exZ, lowN, lowD, highN, highD, ref u2, ref v2, ref uv);
            if (u2.CompareTo(radius2) <= 0)
                return true;
            if (v2.IsZero)
                return false;
            GeometryInt288 uvWide = GeometryInt288.From128(uv.Magnitude);
            return (GeometryInt288.From128(u2 - radius2) * GeometryInt288.From128(v2)).CompareTo(uvWide * uvWide) <= 0;
        }

        private static FixedUInt128 Distance2(long x, long y, long z, long exX, long exY, long exZ)
            => Square128(Excess(x, exX)) + Square128(Excess(y, exY)) + Square128(Excess(z, exZ));

        private static void Reflect(ref long origin, ref long sweep)
        {
            if (sweep >= 0)
                return;
            origin = -origin;
            sweep = -sweep;
        }

        // Signed excess beyond the nearest face, widened for slope products below 2^111.
        private static GeometryInt288 Signed(long value, long extent) => W(value - Clamp(value, extent));

        // Slope contribution at t = n/d, scaled by d: (x*d - clamp(x*d)) * s with x*d < 2^110.
        private static GeometryInt288 Slope(long origin, long sweep, long extent, long n, long d)
        {
            if (sweep == 0)
                return default;
            FixedInt128 position = FixedInt128.Product(origin, d) + FixedInt128.Product(n, sweep);
            FixedInt128 face = FixedInt128.Product(extent, d);
            FixedInt128 excess;
            if (position.Negative && position.Magnitude.CompareTo(face.Magnitude) > 0)
                excess = position + face;
            else if (!position.Negative && position.Magnitude.CompareTo(face.Magnitude) > 0)
                excess = position - face;
            else
                return default;
            return Wide(excess) * W(sweep);
        }

        private static GeometryInt288 Wide(FixedInt128 value)
        {
            GeometryInt288 magnitude = GeometryInt288.From128(value.Magnitude);
            return value.Negative ? -magnitude : magnitude;
        }

        // Adds one axis to the active-face sums for the open piece (low, high).
        private static void Active(long origin, long sweep, long extent, long lowN, long lowD, long highN, long highD,
            ref FixedUInt128 u2, ref FixedUInt128 v2, ref FixedInt128 uv)
        {
            long u;
            if (sweep == 0)
            {
                if (origin >= -extent && origin <= extent)
                    return;
                u = origin < 0 ? origin + extent : origin - extent;
            }
            else if (AtOrAfter(-extent - origin, sweep, highN, highD))
                u = origin + extent;
            else if (AtOrAfter(lowN, lowD, extent - origin, sweep))
                u = origin - extent;
            else
                return;
            u2 += Square128(u);
            v2 += FixedUInt128.Multiply((ulong)sweep, (ulong)sweep);
            uv += FixedInt128.Product(u, sweep);
        }

        // a/b >= c/d for positive denominators; products < 2^111.
        private static bool AtOrAfter(long a, long b, long c, long d)
        {
            FixedInt128 left = FixedInt128.Product(a, d);
            FixedInt128 right = FixedInt128.Product(c, b);
            return (left - right).Sign >= 0;
        }

        private struct BreakpointList
        {
            private long n0, n1, n2, n3, n4, n5;
            private long d0, d1, d2, d3, d4, d5;
            internal int Count;

            // Keeps n/d only when 0 < n/d < 1 (d > 0).
            internal void Add(long n, long d)
            {
                if (d <= 0 || n <= 0 || n >= d)
                    return;
                Set(Count++, n, d);
            }

            internal void Get(int index, out long n, out long d)
            {
                switch (index)
                {
                    case 0: n = n0; d = d0; return;
                    case 1: n = n1; d = d1; return;
                    case 2: n = n2; d = d2; return;
                    case 3: n = n3; d = d3; return;
                    case 4: n = n4; d = d4; return;
                    default: n = n5; d = d5; return;
                }
            }

            private void Set(int index, long n, long d)
            {
                switch (index)
                {
                    case 0: n0 = n; d0 = d; return;
                    case 1: n1 = n; d1 = d; return;
                    case 2: n2 = n; d2 = d; return;
                    case 3: n3 = n; d3 = d; return;
                    case 4: n4 = n; d4 = d; return;
                    default: n5 = n; d5 = d; return;
                }
            }

            // Insertion sort; both terms are positive and below 2^56, products below 2^111.
            internal void Sort()
            {
                for (int i = 1; i < Count; i++)
                    for (int j = i; j > 0; j--)
                    {
                        Get(j - 1, out long an, out long ad);
                        Get(j, out long bn, out long bd);
                        if (FixedUInt128.Multiply((ulong)an, (ulong)bd).CompareTo(FixedUInt128.Multiply((ulong)bn, (ulong)ad)) <= 0)
                            break;
                        Set(j - 1, bn, bd);
                        Set(j, an, ad);
                    }
            }
        }

        // Sector: Y overlap, closest rectangle point within R, then the capsule rule with that point in
        // place of the disk centre: inside the exact-cosine wedge, or a Q32 radial edge reaches the rectangle.
        private static bool Sector(in SpatialBody body, in SpatialRequest q)
        {
            if (!OverlapsY(in body, in q))
                return false;
            BoxYaw yaw = body.Collider.Yaw;
            long c = yaw.Denominator;
            long exX = c * body.Collider.Radius.Raw;
            long exZ = c * body.Collider.HalfDepth.Raw;
            Local(yaw, q.Origin.X.Raw - body.Position.X.Raw, q.Origin.Z.Raw - body.Position.Z.Raw, out long x, out long z);
            long vx = Clamp(x, exX) - x;
            long vz = Clamp(z, exZ) - z;
            FixedUInt128 near2 = Square128(vx) + Square128(vz);
            FixedUInt128 reach2 = FixedUInt128.Multiply(Square(q.Radius.Raw), (ulong)(c * c));
            if (near2.CompareTo(reach2) > 0)
                return false;
            if (near2.IsZero || q.Cosine == -1000)
                return true;
            Local(yaw, q.Direction.X.Raw, q.Direction.Z.Raw, out long dx, out long dz);
            // Rotation scales both vectors by c, so the cosine test is unchanged: dot < 2^111.
            GeometryInt288 dot = Wide(FixedInt128.Product(vx, dx) + FixedInt128.Product(vz, dz));
            GeometryInt288 threshold = GeometryInt288.From128(near2) * GeometryInt288.From128(Square128(dx) + Square128(dz))
                * W((long)q.Cosine * q.Cosine);
            int comparison = (dot * dot * W(1000000)).CompareTo(threshold);
            bool inside = q.Cosine >= 0 ? dot.Sign >= 0 && comparison >= 0 : dot.Sign >= 0 || comparison <= 0;
            if (inside)
                return true;
            GeometryInt288 sin = W(GeometrySinTable.Sin(q.Cosine));
            GeometryInt288 cos = W((long)q.Cosine * (1L << 32));
            GeometryInt288 wx = W(q.Direction.X.Raw);
            GeometryInt288 wz = W(q.Direction.Z.Raw);
            return EdgeHits(yaw, x, z, exX, exZ, wx * cos - wz * sin, wz * cos + wx * sin, q.Radius.Raw)
                || EdgeHits(yaw, x, z, exX, exZ, wx * cos + wz * sin, wz * cos - wx * sin, q.Radius.Raw);
        }

        // Radial edge apex + s*e, 0 <= s*|e| <= R, against the scaled rectangle. e < 2^72.5 in world
        // units and < 2^96.5 after rotation; the length test n^2*|e|^2 vs R^2*d^2 stays below 2^258.
        private static bool EdgeHits(BoxYaw yaw, long x, long z, long exX, long exZ, GeometryInt288 ex, GeometryInt288 ez, long range)
        {
            GeometryInt288 a = W(yaw.Cos);
            GeometryInt288 b = W(yaw.Sin);
            GeometryInt288 lx = a * ex - b * ez;
            GeometryInt288 lz = b * ex + a * ez;
            GeometryInt288 enterN = default;
            GeometryInt288 enterD = W(1);
            GeometryInt288 exitN = W(1);
            GeometryInt288 exitD = default;
            if (!WideSlab(W(x), lx, W(exX), ref enterN, ref enterD, ref exitN, ref exitD)
                || !WideSlab(W(z), lz, W(exZ), ref enterN, ref enterD, ref exitN, ref exitD))
                return false;
            return (enterN * enterN * (ex * ex + ez * ez)).CompareTo(W(range) * W(range) * enterD * enterD) <= 0;
        }

        private static bool WideSlab(GeometryInt288 origin, GeometryInt288 direction, GeometryInt288 extent,
            ref GeometryInt288 enterN, ref GeometryInt288 enterD, ref GeometryInt288 exitN, ref GeometryInt288 exitD)
        {
            if (direction.IsZero)
                return origin.CompareTo(-extent) >= 0 && origin.CompareTo(extent) <= 0;
            bool positive = direction.Sign > 0;
            GeometryInt288 near = positive ? -extent - origin : origin - extent;
            GeometryInt288 far = positive ? extent - origin : origin + extent;
            GeometryInt288 denominator = positive ? direction : -direction;
            if (far.Sign < 0)
                return false;
            if (near.Sign > 0 && (near * enterD).CompareTo(enterN * denominator) > 0)
            {
                enterN = near;
                enterD = denominator;
            }
            if (exitD.IsZero || (far * exitD).CompareTo(exitN * denominator) < 0)
            {
                exitN = far;
                exitD = denominator;
            }
            return (enterN * exitD).CompareTo(exitN * enterD) <= 0;
        }
    }
}
