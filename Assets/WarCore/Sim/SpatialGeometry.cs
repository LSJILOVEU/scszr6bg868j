using System.Diagnostics;

namespace WarCore.Sim
{
    internal static class SpatialGeometry
    {
        public static bool Intersects(in SpatialBody body, in SpatialRequest q, out SimFix distance)
            => Intersects(in body, in q, out distance, out _);

        // A per-call switch avoids mutable global state and lets tests force the original arithmetic.
        internal static bool Intersects(in SpatialBody body, in SpatialRequest q, out SimFix distance, bool forceSlow)
            => forceSlow ? SpatialGeometrySlow.Intersects(in body, in q, out distance)
                : Intersects(in body, in q, out distance, out _);

        // The shape dispatch made this too large for the default inliner; keeping it inlined into the
        // public entry holds the all-capsule path to its pre-3a call depth.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static bool Intersects(in SpatialBody body, in SpatialRequest q, out SimFix distance, out bool enteredNarrowPhase)
        {
            if (body.Collider.ShapeKind != ShapeKind.VerticalCapsule)
            {
                // Passing enteredNarrowPhase by reference into the out-of-line box call makes the JIT keep the
                // caller's discard local on the stack for the whole method, including the capsule path.
                bool boxHit = SpatialBox.Intersects(in body, in q, out distance, out bool boxEntered);
                enteredNarrowPhase = boxEntered;
                return boxHit;
            }
            distance = default;
            enteredNarrowPhase = true;
            if (q.Kind == SpatialQueryKind.Sphere)
                return Sphere(in body, in q);
            if (q.Kind == SpatialQueryKind.Cylinder || q.Kind == SpatialQueryKind.Sector)
                return Area(in body, in q);
            enteredNarrowPhase = SpatialBounds.CapsuleMayHit(in body, in q);
            return enteredNarrowPhase && Ray(in body, in q, out distance);
        }

        // Capsule kernels below the entry dispatch never see another shape.
        [Conditional("DEBUG")]
        internal static void AssertCapsule(in SpatialBody body)
        {
            if (body.Collider.ShapeKind != ShapeKind.VerticalCapsule)
                throw new SimBoundaryException("M0C_SHAPE_KERNEL_MISMATCH");
        }

        private static bool Sphere(in SpatialBody body, in SpatialRequest q) => SpatialGeometrySlow.Sphere(in body, in q);
        private static bool Area(in SpatialBody body, in SpatialRequest q)
        {
            if (q.Kind == SpatialQueryKind.Sector && SpatialSector.TryIntersects(in body, in q, out bool hit))
                return hit;
            return SpatialGeometrySlow.Area(in body, in q);
        }

        private static bool Ray(in SpatialBody body, in SpatialRequest q, out SimFix distance)
            => SpatialRay.Intersects(in body, in q, out distance);
    }
}
