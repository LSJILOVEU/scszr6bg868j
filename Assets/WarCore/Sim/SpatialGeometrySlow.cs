using System.Runtime.CompilerServices;

namespace WarCore.Sim
{
    internal static class SpatialGeometrySlow
    {
        private static GeometryInt288 W(long value) => GeometryInt288.From(value);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Square64(long value)
        {
            ulong magnitude = (ulong)(value < 0 ? -value : value);
            return magnitude * magnitude;
        }

        private static FixedUInt128 Square128(long value) => FixedUInt128.Multiply(FixedInt128.Abs(value), FixedInt128.Abs(value));
        private static GeometryInt288 Square(long value) => GeometryInt288.From128(Square128(value));
        private static GeometryInt288 Sum(long x, long y, long z) => Square(x) + Square(y) + Square(z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long Gap(long aMin, long aMax, long bMin, long bMax) => aMax < bMin ? bMin - aMax : bMax < aMin ? aMin - bMax : 0;

        public static bool Intersects(in SpatialBody body, in SpatialRequest q, out SimFix distance)
            => Intersects(in body, in q, out distance, out _);

        internal static bool Intersects(in SpatialBody body, in SpatialRequest q, out SimFix distance, out bool enteredNarrowPhase)
        {
            // Boxes have a single exact implementation; the forced-slow switch covers capsule arithmetic only.
            if (body.Collider.ShapeKind != ShapeKind.VerticalCapsule)
                return SpatialBox.Intersects(in body, in q, out distance, out enteredNarrowPhase);
            distance = default;
            enteredNarrowPhase = true;
            // Separate stack frames keep the common sphere path free of wide-integer temporaries.
            if (q.Kind == SpatialQueryKind.Sphere)
                return Sphere(in body, in q);
            if (q.Kind == SpatialQueryKind.Cylinder || q.Kind == SpatialQueryKind.Sector)
                return Area(in body, in q);
            // Diagnostics count actual entries past the existing slab prefilter, without a second bounds test.
            enteredNarrowPhase = SpatialBounds.RayMayHit(body, q);
            return enteredNarrowPhase && Ray(in body, in q, out distance);
        }

        internal static bool Sphere(in SpatialBody body, in SpatialRequest q)
        {
            SpatialGeometry.AssertCapsule(in body);
            long radius = body.Collider.Radius.Raw;
            long spine = body.Collider.Height.Raw / 2 - radius;
            long low = body.Position.Y.Raw - spine;
            long high = body.Position.Y.Raw + spine;
            long x = body.Position.X.Raw - q.Origin.X.Raw;
            long z = body.Position.Z.Raw - q.Origin.Z.Raw;
            long y = Gap(low, high, q.Origin.Y.Raw, q.Origin.Y.Raw);
            long reach = radius + q.Radius.Raw;
            if (x < -reach || x > reach || z < -reach || z > reach || y > reach)
                return false;
            return (Square128(x) + Square128(y) + Square128(z)).CompareTo(Square128(reach)) <= 0;
        }

        internal static bool Area(in SpatialBody body, in SpatialRequest q)
        {
            SpatialGeometry.AssertCapsule(in body);
            long radius = body.Collider.Radius.Raw;
            long spine = body.Collider.Height.Raw / 2 - radius;
            long low = body.Position.Y.Raw - spine;
            long high = body.Position.Y.Raw + spine;
            long x = body.Position.X.Raw - q.Origin.X.Raw;
            long z = body.Position.Z.Raw - q.Origin.Z.Raw;
            long y = Gap(low, high, q.Origin.Y.Raw - q.HalfHeight.Raw, q.Origin.Y.Raw + q.HalfHeight.Raw);
            long reach = radius + q.Radius.Raw;
            if (y > radius || x < -reach || x > reach || z < -reach || z > reach)
                return false;
            ulong diskRadius2 = Square64(radius) - Square64(y);
            if (!DisksOverlap(Square128(x) + Square128(z), q.Radius.Raw, diskRadius2))
                return false;
            return q.Kind == SpatialQueryKind.Cylinder || Sector(x, z, GeometryInt288.FromUnsigned(diskRadius2), q);
        }

        internal static bool Ray(in SpatialBody body, in SpatialRequest q, out SimFix distance)
        {
            SpatialGeometry.AssertCapsule(in body);
            distance = default;
            long radius = body.Collider.Radius.Raw;
            long spine = body.Collider.Height.Raw / 2 - radius;
            long low = body.Position.Y.Raw - spine;
            long high = body.Position.Y.Raw + spine;
            if (RayCapsule(q.Origin, q.Direction, body.Position.X.Raw, body.Position.Z.Raw, low, high, radius + q.Radius.Raw, out GeometryInt288 numerator, out GeometryInt288 denominator))
            {
                if (numerator.IsZero)
                    return true;
                if (q.Kind == SpatialQueryKind.Capsule)
                    return numerator.CompareTo(denominator) <= 0;
                // Compare squared ranges before rounding the reported distance.
                GeometryInt288 direction2 = Sum(q.Direction.X.Raw, q.Direction.Y.Raw, q.Direction.Z.Raw);
                GeometryInt288 squaredTravel = numerator * numerator * direction2;
                GeometryInt288 squaredDenominator = denominator * denominator;
                if (squaredTravel.CompareTo(Square(q.Range.Raw) * squaredDenominator) > 0)
                    return false;
                distance = SimFix.FromRaw((squaredTravel / squaredDenominator).Sqrt().ToLong());
                return true;
            }

            return false;
        }

        // Keeping the cross-section squared avoids losing sub-unit contacts at capsule caps.
        private static bool DisksOverlap(FixedUInt128 distance2, long radius, ulong otherRadius2)
        {
            ulong radius2 = Square64(radius);
            ulong sum = radius2 + otherRadius2;
            if (distance2.CompareTo(new FixedUInt128(sum)) <= 0)
                return true;
            FixedUInt128 excess = distance2 - new FixedUInt128(sum);
            // At a cap tip diskRadius2 can be zero; the reach precheck bounds excess by 7*WorldRaw^2.
            return FixedUInt128.Multiply(excess.Low, excess.Low).CompareTo(FixedUInt128.Multiply(radius2, otherRadius2).TimesFour()) <= 0;
        }

        private static bool Sector(long x, long z, GeometryInt288 diskRadius2, in SpatialRequest q)
        {
            if (q.Cosine == -1000 || (x == 0 && z == 0))
                return true;
            long dx = q.Direction.X.Raw;
            long dz = q.Direction.Z.Raw;
            GeometryInt288 dot = (W(x) * W(dx) + W(z) * W(dz));
            GeometryInt288 threshold = (Square(x) + Square(z)) * (Square(dx) + Square(dz)) * Square(q.Cosine);
            int comparison = (dot * dot * W(1000000)).CompareTo(threshold);
            bool inside = q.Cosine >= 0 ? dot.Sign >= 0 && comparison >= 0 : dot.Sign >= 0 || comparison <= 0;
            if (inside)
                return true;
            // Extra fractional bits keep radial-edge rounding below one coordinate unit across the world.
            GeometryInt288 sin = W(GeometrySinTable.Sin(q.Cosine));
            GeometryInt288 cos = W((long)q.Cosine * (1L << 32));
            return NearRadialSegment(x, z, W(dx) * cos - W(dz) * sin, W(dz) * cos + W(dx) * sin, q.Radius.Raw, diskRadius2) || NearRadialSegment(x, z, W(dx) * cos + W(dz) * sin, W(dz) * cos - W(dx) * sin, q.Radius.Raw, diskRadius2);
        }

        private static bool NearRadialSegment(long x, long z, GeometryInt288 dx, GeometryInt288 dz, long length, GeometryInt288 radius2)
        {
            GeometryInt288 d2 = dx * dx + dz * dz;
            GeometryInt288 dot = W(x) * dx + W(z) * dz;
            if (dot.Sign <= 0)
                return (Square(x) + Square(z)).CompareTo(radius2) <= 0;
            if ((dot * dot).CompareTo(Square(length) * d2) > 0)
            {
                GeometryInt288 excess = Square(x) + Square(z) + Square(length) - radius2;
                return excess.Sign <= 0 || (excess * excess * d2).CompareTo(W(4) * Square(length) * dot * dot) <= 0;
            }

            GeometryInt288 cross = W(x) * dz - W(z) * dx;
            return (cross * cross).CompareTo(radius2 * d2) <= 0;
        }

        private static bool Root(GeometryInt288 a, GeometryInt288 b, GeometryInt288 c, out GeometryInt288 numerator, out GeometryInt288 denominator)
        {
            numerator = default;
            denominator = a * W(SimFix.Scale);
            if (a.IsZero || b.Sign > 0)
                return false;
            GeometryInt288 discriminant = b * b - a * c;
            if (discriminant.Sign < 0)
                return false;
            numerator = -b * W(SimFix.Scale) - (discriminant * Square(SimFix.Scale)).Sqrt();
            return numerator.Sign >= 0;
        }

        private static void Choose(GeometryInt288 n, GeometryInt288 d, ref bool found, ref GeometryInt288 bestN, ref GeometryInt288 bestD)
        {
            if (!found || (n * bestD).CompareTo(bestN * d) < 0)
            {
                found = true;
                bestN = n;
                bestD = d;
            }
        }

        private static bool RayCapsule(in SimVec3 origin, in SimVec3 direction, long cx, long cz, long low, long high, long radius, out GeometryInt288 bestN, out GeometryInt288 bestD)
        {
            bestN = default;
            bestD = W(1);
            long x = origin.X.Raw - cx;
            long z = origin.Z.Raw - cz;
            long gap = Gap(low, high, origin.Y.Raw, origin.Y.Raw);
            if ((Square128(x) + Square128(gap) + Square128(z)).CompareTo(Square128(radius)) <= 0)
                return true;
            long dx = direction.X.Raw;
            long dy = direction.Y.Raw;
            long dz = direction.Z.Raw;
            GeometryInt288 a = GeometryInt288.FromUnsigned(Square64(dx) + Square64(dz));
            GeometryInt288 b = (W(x) * W(dx) + W(z) * W(dz));
            GeometryInt288 c = Square(x) + Square(z) - Square(radius);
            // A horizontal ray has the same denominator for every cap and side root.
            // The nearest cross-section therefore also has the earliest rounded numerator.
            if (dy == 0)
                return Root(a, b, c + Square(gap), out bestN, out bestD);
            bool found = false;
            if (Root(a, b, c, out GeometryInt288 n, out GeometryInt288 denominator))
            {
                GeometryInt288 y = W(origin.Y.Raw) * denominator + W(dy) * n;
                if (y.CompareTo(W(low) * denominator) >= 0 && y.CompareTo(W(high) * denominator) <= 0)
                    Choose(n, denominator, ref found, ref bestN, ref bestD);
            }

            a += Square(dy);
            int capCount = low == high ? 1 : 2;
            for (int cap = 0; cap < capCount; cap++)
            {
                long y = origin.Y.Raw - (cap == 0 ? low : high);
                GeometryInt288 capB = b + W(y) * W(dy);
                GeometryInt288 capC = c + Square(y);
                if (Root(a, capB, capC, out n, out denominator))
                    Choose(n, denominator, ref found, ref bestN, ref bestD);
            }

            return found;
        }
    }
}
