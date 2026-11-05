using System.Numerics;
using WarCore.Sim;

namespace WarCore.Tests.Oracle
{
    // This reference minimizes quadratics over exact intervals. It never rounds an intersection root
    // and does not call the product bounds, DDA, square root or frozen geometric implementation.
    internal static class ExactRayCapsule
    {
        internal static bool Hits(in SpatialBody body, in SpatialRequest ray)
        {
            // Capsule-only reference; box rays are checked against OracleBox.
            if (body.Collider.ShapeKind != ShapeKind.VerticalCapsule)
                throw new System.ArgumentException("ExactRayCapsule accepts vertical capsules only.");
            BigInteger dx = ray.Direction.X.Raw;
            BigInteger dy = ray.Direction.Y.Raw;
            BigInteger dz = ray.Direction.Z.Raw;
            BigInteger x = ray.Origin.X.Raw - body.Position.X.Raw;
            BigInteger y = ray.Origin.Y.Raw - body.Position.Y.Raw;
            BigInteger z = ray.Origin.Z.Raw - body.Position.Z.Raw;
            BigInteger radius = body.Collider.Radius.Raw;
            BigInteger spine = body.Collider.Height.Raw / 2 - body.Collider.Radius.Raw;
            BigInteger d2 = dx * dx + dy * dy + dz * dz;
            BigInteger range = ray.Range.Raw;
            if (Sphere(x, y - spine, z, dx, dy, dz, radius, d2, range)
                || Sphere(x, y + spine, z, dx, dy, dz, radius, d2, range))
                return true;
            BigInteger loN = 0;
            BigInteger loD = 1;
            BigInteger hiN = 0;
            BigInteger hiD = 0;
            if (dy.IsZero)
            {
                if (y < -spine || y > spine)
                    return false;
            }
            else
            {
                loN = dy.Sign > 0 ? -spine - y : y - spine;
                loD = BigInteger.Abs(dy);
                hiN = dy.Sign > 0 ? spine - y : y + spine;
                hiD = loD;
                if (loN.Sign < 0)
                    loN = 0;
                if (hiN < loN)
                    return false;
            }
            return MinimumIsNonpositive(dx * dx + dz * dz, x * dx + z * dz, x * x + z * z - radius * radius,
                loN, loD, hiN, hiD, d2, range);
        }

        private static bool Sphere(BigInteger x, BigInteger y, BigInteger z, BigInteger dx, BigInteger dy,
            BigInteger dz, BigInteger radius, BigInteger d2, BigInteger range)
        {
            return MinimumIsNonpositive(d2, x * dx + y * dy + z * dz, x * x + y * y + z * z - radius * radius,
                0, 1, 0, 0, d2, range);
        }

        private static bool InRange(BigInteger n, BigInteger d, BigInteger d2, BigInteger range)
            => n * n * d2 <= range * range * d * d;

        private static bool AtFraction(BigInteger a, BigInteger b, BigInteger c, BigInteger n, BigInteger d)
            => a * n * n + 2 * b * n * d + c * d * d <= 0;

        private static bool MinimumIsNonpositive(BigInteger a, BigInteger b, BigInteger c,
            BigInteger loN, BigInteger loD, BigInteger hiN, BigInteger hiD, BigInteger d2, BigInteger range)
        {
            if (!InRange(loN, loD, d2, range))
                return false;
            if (AtFraction(a, b, c, loN, loD))
                return true;
            // A nonnegative derivative at the lower endpoint cannot turn down for a convex quadratic.
            if (a.IsZero || -b * loD <= loN * a)
                return false;
            if ((hiD.IsZero || -b * hiD <= hiN * a) && InRange(-b, a, d2, range))
                return b * b >= a * c;
            if (!hiD.IsZero && InRange(hiN, hiD, d2, range))
                return AtFraction(a, b, c, hiN, hiD);
            // At the physical endpoint, q(R/|D|)*D^2 = X + Y*sqrt(D^2).
            // Compare signs before squaring, so the irrational endpoint remains exact.
            BigInteger x = a * range * range + c * d2;
            BigInteger y = 2 * b * range;
            if (y.IsZero)
                return x.Sign <= 0;
            if (y.Sign < 0)
                return x.Sign <= 0 || x * x <= y * y * d2;
            return x.Sign < 0 && x * x >= y * y * d2;
        }
    }
}
