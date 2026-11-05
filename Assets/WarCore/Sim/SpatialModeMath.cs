using System.Diagnostics;

namespace WarCore.Sim
{
    internal static class SpatialModeMath
    {
        // Reference points can be anywhere in the world, independently of the query bounds.
        internal static SimFix ReferenceDistance(in SpatialBody body, SimVec3 point)
            => body.Collider.ShapeKind == ShapeKind.VerticalCapsule
                ? ReferenceDistance(body.Position, body.Collider.Radius.Raw, body.Collider.Height.Raw, point)
                : SpatialBox.ReferenceDistance(in body, point);

        internal static SimFix ReferenceDistance(SimVec3 center, long radius, long height, SimVec3 point)
        {
            AssertWorldLimit();
            long spine = height / 2 - radius;
            if (spine < 0)
                spine = 0;
            long gap = point.Y.Raw - center.Y.Raw;
            if (gap < 0)
                gap = -gap;
            gap = gap > spine ? gap - spine : 0;
            FixedUInt128 squared = Square(point.X.Raw - center.X.Raw)
                + Square(gap) + Square(point.Z.Raw - center.Z.Raw);
            long distance = (long)squared.Sqrt() - radius;
            return SimFix.FromRaw(distance > 0 ? distance : 0);
        }

        private static FixedUInt128 Square(long value)
        {
            ulong magnitude = (ulong)(value < 0 ? -value : value);
            return FixedUInt128.Multiply(magnitude, magnitude);
        }

        internal static ulong DirectionSquared(SimVec3 direction)
            => Square(direction.X.Raw).Low + Square(direction.Y.Raw).Low + Square(direction.Z.Raw).Low;

        internal static bool BeyondBest(in RayCellEvent next, ulong directionSquared, long bestRaw, long rangeRaw)
        {
            AssertWorldLimit();
            // A terminal hit cannot be beaten by an event outside the closed query range.
            // Guard before +1; all other hits and live event numerators are bounded by range <= 2^30.
            if (bestRaw >= rangeRaw)
                return false;
            ulong nextBucket = (ulong)bestRaw + 1;
            FixedUInt128 lowerSquared = FixedUInt128.Multiply(next.Numerator * next.Numerator, directionSquared);
            FixedUInt128 threshold = FixedUInt128.Multiply(nextBucket * nextBucket, next.Denominator * next.Denominator);
            // floor(n*|D|/d) > bestRaw iff n^2*D^2 >= (bestRaw+1)^2*d^2.
            return lowerSquared.CompareTo(threshold) >= 0;
        }

        [Conditional("DEBUG")]
        private static void AssertWorldLimit()
        {
            SpatialWidth.AssertWorldLimit(SimVec3.WorldLimitMetres);
        }
    }
}
