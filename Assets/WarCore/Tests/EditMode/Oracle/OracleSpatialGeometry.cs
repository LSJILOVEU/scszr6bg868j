// Frozen baseline formulas use only the test-owned WideInt for arithmetic.
// Sharing public request/result carriers preserves API coverage without sharing the predicate.
using WarCore.Sim;

namespace WarCore.Tests.Oracle
{
    internal static class OracleSpatialGeometry
    {
        private static WideInt W(long value) => WideInt.From(value);
        private static WideInt Square(long value) => W(value) * W(value);
        private static WideInt Sum(long x, long y, long z) => Square(x) + Square(y) + Square(z);
        private static long Gap(long aMin, long aMax, long bMin, long bMax) => aMax < bMin ? bMin - aMax : bMax < aMin ? aMin - bMax : 0;
        public static bool Intersects(SpatialBody body, SpatialRequest q, out SimFix distance)
        {
            if (body.Collider.ShapeKind == ShapeKind.OrientedBox)
                return OracleBox.Intersects(body, q, out distance);
            distance = default;
            long radius = body.Collider.Radius.Raw;
            long spine = body.Collider.Height.Raw / 2 - radius;
            long low = body.Position.Y.Raw - spine;
            long high = body.Position.Y.Raw + spine;
            long x = body.Position.X.Raw - q.Origin.X.Raw;
            long z = body.Position.Z.Raw - q.Origin.Z.Raw;
            if (q.Kind == SpatialQueryKind.Sphere)
            {
                long y = Gap(low, high, q.Origin.Y.Raw, q.Origin.Y.Raw);
                return Sum(x, y, z).CompareTo(Square(radius + q.Radius.Raw)) <= 0;
            }

            if (q.Kind == SpatialQueryKind.Cylinder || q.Kind == SpatialQueryKind.Sector)
            {
                long y = Gap(low, high, q.Origin.Y.Raw - q.HalfHeight.Raw, q.Origin.Y.Raw + q.HalfHeight.Raw);
                if (y > radius)
                    return false;
                WideInt diskRadius2 = Square(radius) - Square(y);
                if (!DisksOverlap(Square(x) + Square(z), q.Radius.Raw, diskRadius2))
                    return false;
                return q.Kind == SpatialQueryKind.Cylinder || Sector(x, z, diskRadius2, q);
            }

            if (RayCapsule(q.Origin, q.Direction, body.Position.X.Raw, body.Position.Z.Raw, low, high, radius + q.Radius.Raw, out WideInt numerator, out WideInt denominator))
            {
                if (q.Kind == SpatialQueryKind.Capsule)
                    return numerator.CompareTo(denominator) <= 0;
                // Compare squared ranges before rounding the reported distance.
                WideInt direction2 = Sum(q.Direction.X.Raw, q.Direction.Y.Raw, q.Direction.Z.Raw);
                if ((numerator * numerator * direction2).CompareTo(Square(q.Range.Raw) * denominator * denominator) > 0)
                    return false;
                distance = SimFix.FromRaw((numerator * numerator * direction2 / (denominator * denominator)).Sqrt().ToLong());
                return true;
            }

            return false;
        }

        // Keeping the cross-section squared avoids losing sub-unit contacts at capsule caps.
        private static bool DisksOverlap(WideInt distance2, long radius, WideInt otherRadius2)
        {
            WideInt excess = distance2 - Square(radius) - otherRadius2;
            return excess.Sign <= 0 || (excess * excess).CompareTo(W(4) * Square(radius) * otherRadius2) <= 0;
        }

        private static bool Sector(long x, long z, WideInt diskRadius2, SpatialRequest q)
        {
            if (q.Cosine == -1000 || (x == 0 && z == 0))
                return true;
            long dx = q.Direction.X.Raw;
            long dz = q.Direction.Z.Raw;
            WideInt dot = W(x) * W(dx) + W(z) * W(dz);
            WideInt threshold = (Square(x) + Square(z)) * (Square(dx) + Square(dz)) * Square(q.Cosine);
            int comparison = (dot * dot * W(1000000)).CompareTo(threshold);
            bool inside = q.Cosine >= 0 ? dot.Sign >= 0 && comparison >= 0 : dot.Sign >= 0 || comparison <= 0;
            if (inside)
                return true;
            // Extra fractional bits keep radial-edge rounding below one coordinate unit across the world.
            WideInt sin = (W(1000000 - (long)q.Cosine * q.Cosine) * Square(1L << 32)).Sqrt();
            WideInt cos = W(q.Cosine) * W(1L << 32);
            return NearRadialSegment(x, z, W(dx) * cos - W(dz) * sin, W(dz) * cos + W(dx) * sin, q.Radius.Raw, diskRadius2) || NearRadialSegment(x, z, W(dx) * cos + W(dz) * sin, W(dz) * cos - W(dx) * sin, q.Radius.Raw, diskRadius2);
        }

        private static bool NearRadialSegment(long x, long z, WideInt dx, WideInt dz, long length, WideInt radius2)
        {
            WideInt d2 = dx * dx + dz * dz;
            WideInt dot = W(x) * dx + W(z) * dz;
            if (dot.Sign <= 0)
                return (Square(x) + Square(z)).CompareTo(radius2) <= 0;
            if ((dot * dot).CompareTo(Square(length) * d2) > 0)
            {
                WideInt excess = Square(x) + Square(z) + Square(length) - radius2;
                return excess.Sign <= 0 || (excess * excess * d2).CompareTo(W(4) * Square(length) * dot * dot) <= 0;
            }

            WideInt cross = W(x) * dz - W(z) * dx;
            return (cross * cross).CompareTo(radius2 * d2) <= 0;
        }

        private static bool Root(WideInt a, WideInt b, WideInt c, out WideInt numerator, out WideInt denominator)
        {
            numerator = default;
            denominator = a * W(65536);
            if (a.IsZero)
                return false;
            WideInt discriminant = b * b - a * c;
            if (discriminant.Sign < 0)
                return false;
            numerator = -b * W(65536) - (discriminant * Square(65536)).Sqrt();
            return numerator.Sign >= 0;
        }

        private static void Choose(WideInt n, WideInt d, ref bool found, ref WideInt bestN, ref WideInt bestD)
        {
            if (!found || (n * bestD).CompareTo(bestN * d) < 0)
            {
                found = true;
                bestN = n;
                bestD = d;
            }
        }

        private static bool RayCapsule(SimVec3 origin, SimVec3 direction, long cx, long cz, long low, long high, long radius, out WideInt bestN, out WideInt bestD)
        {
            bestN = default;
            bestD = W(1);
            long x = origin.X.Raw - cx;
            long z = origin.Z.Raw - cz;
            long gap = Gap(low, high, origin.Y.Raw, origin.Y.Raw);
            if (Sum(x, gap, z).CompareTo(Square(radius)) <= 0)
                return true;
            long dx = direction.X.Raw;
            long dy = direction.Y.Raw;
            long dz = direction.Z.Raw;
            WideInt a = Square(dx) + Square(dz);
            WideInt b = W(x) * W(dx) + W(z) * W(dz);
            WideInt c = Square(x) + Square(z) - Square(radius);
            bool found = false;
            if (Root(a, b, c, out WideInt n, out WideInt denominator))
            {
                WideInt y = W(origin.Y.Raw) * denominator + W(dy) * n;
                if (y.CompareTo(W(low) * denominator) >= 0 && y.CompareTo(W(high) * denominator) <= 0)
                    Choose(n, denominator, ref found, ref bestN, ref bestD);
            }

            a += Square(dy);
            for (int cap = 0; cap < 2; cap++)
            {
                long y = origin.Y.Raw - (cap == 0 ? low : high);
                WideInt capB = b + W(y) * W(dy);
                WideInt capC = c + Square(y);
                if (Root(a, capB, capC, out n, out denominator))
                    Choose(n, denominator, ref found, ref bestN, ref bestD);
            }

            return found;
        }
    }
}
