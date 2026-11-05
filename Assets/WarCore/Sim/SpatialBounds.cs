namespace WarCore.Sim
{
    internal static class SpatialBounds
    {
        internal const ulong ScalarSlabLimit = 1UL << 29;

        internal static bool RayMayHit(in SpatialBody body, in SpatialRequest request)
            => body.Collider.ShapeKind == ShapeKind.VerticalCapsule ? CapsuleMayHit(in body, in request) : BoxMayHit(in body, in request);

        // The capsule narrow phase calls this directly after its own shape dispatch.
        internal static bool CapsuleMayHit(in SpatialBody body, in SpatialRequest request)
        {
            SpatialGeometry.AssertCapsule(in body);
            long radius = body.Collider.Radius.Raw;
            long half = body.Collider.Height.Raw / 2;
            long x = body.Position.X.Raw;
            long y = body.Position.Y.Raw;
            long z = body.Position.Z.Raw;
            if (x - radius > request.Maximum.X.Raw || x + radius < request.Minimum.X.Raw ||
                y - half > request.Maximum.Y.Raw || y + half < request.Minimum.Y.Raw ||
                z - radius > request.Maximum.Z.Raw || z + radius < request.Minimum.Z.Raw)
                return false;
            radius += request.Radius.Raw;
            half += request.Radius.Raw;
            long dx = x - request.Origin.X.Raw;
            long dy = y - request.Origin.Y.Raw;
            long dz = z - request.Origin.Z.Raw;
            bool scalar = CanUseScalarSlab(dx, dy, dz, radius, half, request.Direction);
            long enterN = 0;
            long enterD = 1;
            long exitN = 1;
            long exitD = request.Kind == SpatialQueryKind.Capsule ? 1 : 0;
            return Slab(dx - radius, dx + radius, request.Direction.X.Raw, ref enterN, ref enterD, ref exitN, ref exitD, scalar) &&
                Slab(dy - half, dy + half, request.Direction.Y.Raw, ref enterN, ref enterD, ref exitN, ref exitD, scalar) &&
                Slab(dz - radius, dz + radius, request.Direction.Z.Raw, ref enterN, ref enterD, ref exitN, ref exitD, scalar);
        }

        // The box's outward-rounded world AABB, expanded by the sweep radius, is a superset of the
        // exact shape. Half widths <= 2^30 keep every slab bound of the capsule proof.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static bool BoxMayHit(in SpatialBody body, in SpatialRequest request)
        {
            long rx = SpatialBox.ExtentX(body.Collider);
            long rz = SpatialBox.ExtentZ(body.Collider);
            long half = body.Collider.Height.Raw / 2;
            long x = body.Position.X.Raw;
            long y = body.Position.Y.Raw;
            long z = body.Position.Z.Raw;
            if (x - rx > request.Maximum.X.Raw || x + rx < request.Minimum.X.Raw ||
                y - half > request.Maximum.Y.Raw || y + half < request.Minimum.Y.Raw ||
                z - rz > request.Maximum.Z.Raw || z + rz < request.Minimum.Z.Raw)
                return false;
            rx += request.Radius.Raw;
            rz += request.Radius.Raw;
            half += request.Radius.Raw;
            long dx = x - request.Origin.X.Raw;
            long dy = y - request.Origin.Y.Raw;
            long dz = z - request.Origin.Z.Raw;
            // The scalar guard proof assumes one horizontal half width; the larger of the two box
            // widths bounds both X and Z slabs, so each numerator stays <= 2L and each product <= 2L^2.
            bool scalar = CanUseScalarSlab(dx, dy, dz, rx > rz ? rx : rz, half, request.Direction);
            long enterN = 0;
            long enterD = 1;
            long exitN = 1;
            long exitD = request.Kind == SpatialQueryKind.Capsule ? 1 : 0;
            return Slab(dx - rx, dx + rx, request.Direction.X.Raw, ref enterN, ref enterD, ref exitN, ref exitD, scalar) &&
                Slab(dy - half, dy + half, request.Direction.Y.Raw, ref enterN, ref enterD, ref exitN, ref exitD, scalar) &&
                Slab(dz - rz, dz + rz, request.Direction.Z.Raw, ref enterN, ref enterD, ref exitN, ref exitD, scalar);
        }

        // Called only with validated world deltas and nonnegative expanded extents.
        // Each numerator is at most 2L and each denominator at most L: products <=2L^2,
        // with L=2^29, use 60 magnitude bits and leave three spare bits in signed long.
        internal static bool CanUseScalarSlab(long x, long y, long z, long radius, long half, in SimVec3 direction)
            => (Magnitude(x) | Magnitude(y) | Magnitude(z) | (ulong)radius | (ulong)half
                | Magnitude(direction.X.Raw) | Magnitude(direction.Y.Raw) | Magnitude(direction.Z.Raw)) <= ScalarSlabLimit;

        private static ulong Magnitude(long value) => (ulong)(value < 0 ? -value : value);

        private static FixedUInt128 Product(long a, long b) => FixedUInt128.Multiply((ulong)a, (ulong)b);

        private static bool Slab(long low, long high, long direction, ref long enterN, ref long enterD, ref long exitN, ref long exitD, bool scalar)
        {
            if (direction == 0)
                return low <= 0 && high >= 0;
            long near = direction > 0 ? low : -high;
            long far = direction > 0 ? high : -low;
            long denominator = direction > 0 ? direction : -direction;
            if (far < 0)
                return false;
            if (scalar)
            {
                if (near * enterD > enterN * denominator)
                {
                    enterN = near;
                    enterD = denominator;
                }
                if (exitD == 0 || far * exitD < exitN * denominator)
                {
                    exitN = far;
                    exitD = denominator;
                }
                return enterN * exitD <= exitN * enterD;
            }
            // Public limits bound a slab numerator by 4*2^30 and a denominator by 2^30.
            // Full-width products leave ample margin without rounding away a grazing contact.
            if (near > 0 && Product(near, enterD).CompareTo(Product(enterN, denominator)) > 0)
            {
                enterN = near;
                enterD = denominator;
            }

            if (exitD == 0 || Product(far, exitD).CompareTo(Product(exitN, denominator)) < 0)
            {
                exitN = far;
                exitD = denominator;
            }

            return Product(enterN, exitD).CompareTo(Product(exitN, enterD)) <= 0;
        }
    }
}
