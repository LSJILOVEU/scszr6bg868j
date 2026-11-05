using System;
using System.Reflection;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    // Bound once: coarse phase timing uses the actual product methods with no arithmetic adapters.
    internal sealed class ProductGeometrySteps
    {
        private delegate bool AreaPredicate(in SpatialBody body, in SpatialRequest request);
        private delegate bool RayPredicate(in SpatialBody body, in SpatialRequest request, out SimFix distance);
        private readonly AreaPredicate sphere;
        private readonly AreaPredicate area;
        private readonly RayPredicate ray;
        private readonly QueryTrace trace;

        internal ProductGeometrySteps(QueryTrace trace)
        {
            this.trace = trace;
            Type type = typeof(SpatialGeometry);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            sphere = (AreaPredicate)type.GetMethod("Sphere", flags).CreateDelegate(typeof(AreaPredicate));
            area = (AreaPredicate)type.GetMethod("Area", flags).CreateDelegate(typeof(AreaPredicate));
            ray = (RayPredicate)type.GetMethod("Ray", flags).CreateDelegate(typeof(RayPredicate));
        }

        internal bool Intersects(in SpatialBody body, in SpatialRequest request, out SimFix distance, out bool precise)
        {
            distance = default;
            precise = true;
            bool box = body.Collider.ShapeKind != ShapeKind.VerticalCapsule;
            // Boxes are staged like capsules, so Precise counts equal the product's narrow-phase entries.
            if (request.Kind == SpatialQueryKind.Sphere || request.Kind == SpatialQueryKind.Cylinder || request.Kind == SpatialQueryKind.Sector)
            {
                using var scope = trace.Measure(QueryStage.Precise);
                if (box)
                    return SpatialBox.Intersects(in body, in request, out distance, out precise);
                return request.Kind == SpatialQueryKind.Sphere ? sphere(in body, in request) : area(in body, in request);
            }

            using (trace.Measure(QueryStage.Prefilter))
                precise = SpatialBounds.RayMayHit(in body, in request);
            if (!precise)
                return false;
            using var narrow = trace.Measure(QueryStage.Precise);
            // The box entry repeats the slab test it just passed; only its exact predicate is new work.
            return box ? SpatialBox.Intersects(in body, in request, out distance, out precise) : ray(in body, in request, out distance);
        }
    }
}
