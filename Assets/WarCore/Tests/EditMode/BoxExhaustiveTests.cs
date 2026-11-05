using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    // Exhaustive small-domain differential: every box from a small extent set and a yaw set whose
    // denominators are 1, 2, 5 and 13, against every query on an integer lattice around it. With
    // extents that are multiples of c the faces, edges and corners land on lattice points, so exact
    // tangencies, grazing rays and corner contacts are enumerated rather than sampled. The default
    // run checks a prime-strided subset; the explicit run checks the whole domain.
    [Category("M0c3a")]
    public sealed class BoxExhaustiveTests
    {
        private const int DefaultStride = 263;
        private static readonly long[] HalfX = { 0, 3, 5, 10 };
        private static readonly long[] HalfZ = { 0, 4, 5 };
        private static readonly long[] Heights = { 0, 3, 6 };
        private static readonly (int m, int n)[] Yaws = { (1, 0), (1, 1), (0, 1), (1, -1), (2, 1), (2, -1), (3, 2), (4095, 1) };
        private static readonly SimVec3 Centre = BoxInputs.V(1, -1, 2);

        private static SimFix F(long raw) => SimFix.FromRaw(raw);

        private static List<SpatialBody> Boxes(bool small)
        {
            var result = new List<SpatialBody>();
            int id = 0;
            foreach ((int m, int n) in Yaws)
                foreach (long hx in HalfX)
                    foreach (long hz in HalfZ)
                        foreach (long h in Heights)
                        {
                            if (small && ((hx != 0 && hx != 5) || hz == 4 || h == 3))
                                continue;
                            Collider box = Collider.Box(F(hx), F(h), F(hz), BoxYaw.FromGenerator(m, n));
                            result.Add(new SpatialBody(new EntityId(id++, 1), Centre, box));
                        }
            return result;
        }

        private static IEnumerable<SpatialRequest> Requests(SpatialQueryKind kind)
        {
            switch (kind)
            {
                case SpatialQueryKind.Sphere:
                    for (long x = -12; x <= 12; x++)
                        for (long z = -12; z <= 12; z++)
                            for (long y = -5; y <= 5; y++)
                                foreach (long r in new long[] { 0, 1, 2, 4 })
                                    yield return SpatialQueries.Sphere(BoxInputs.V(x, y, z), F(r));
                    break;
                case SpatialQueryKind.Cylinder:
                    for (long x = -12; x <= 12; x++)
                        for (long z = -12; z <= 12; z++)
                            for (long y = -5; y <= 5; y++)
                                foreach (long r in new long[] { 0, 2, 4 })
                                    foreach (long hh in new long[] { 0, 2 })
                                        yield return SpatialQueries.Cylinder(BoxInputs.V(x, y, z), F(r), F(hh));
                    break;
                case SpatialQueryKind.Sector:
                    var directions = new[] { (1L, 0L), (1L, 1L), (0L, 1L), (-1L, 1L), (-1L, 0L), (-1L, -1L), (0L, -1L), (1L, -1L), (2L, 1L), (-1L, 3L) };
                    for (long x = -12; x <= 12; x += 3)
                        for (long z = -12; z <= 12; z += 3)
                            for (long y = -4; y <= 4; y += 4)
                                foreach ((long dx, long dz) in directions)
                                    foreach (int cosine in new[] { -1000, -800, -600, 0, 500, 600, 800, 1000 })
                                        foreach (long r in new long[] { 5, 15 })
                                            foreach (long hh in new long[] { 0, 3 })
                                                yield return SpatialQueries.Sector(BoxInputs.V(x, y, z), F(r), BoxInputs.V(dx, 0, dz), cosine, F(hh));
                    break;
                case SpatialQueryKind.Capsule:
                    for (long x = -12; x <= 12; x += 4)
                        for (long z = -12; z <= 12; z += 4)
                            for (long y = -4; y <= 4; y += 4)
                                for (long sx = -8; sx <= 8; sx += 4)
                                    for (long sz = -8; sz <= 8; sz += 4)
                                        for (long sy = -4; sy <= 4; sy += 4)
                                            foreach (long r in new long[] { 0, 2 })
                                                yield return SpatialQueries.Capsule(BoxInputs.V(x, y, z), BoxInputs.V(x + sx, y + sy, z + sz), F(r));
                    break;
                default:
                    for (long x = -12; x <= 12; x += 3)
                        for (long z = -12; z <= 12; z += 3)
                            for (long y = -4; y <= 4; y += 4)
                                for (long dx = -2; dx <= 2; dx++)
                                    for (long dy = -1; dy <= 1; dy++)
                                        for (long dz = -2; dz <= 2; dz++)
                                        {
                                            if (dx == 0 && dy == 0 && dz == 0)
                                                continue;
                                            foreach (long range in new long[] { 0, 7, 40 })
                                                yield return SpatialQueries.Ray(BoxInputs.V(x, y, z), BoxInputs.V(dx, dy, dz), F(range));
                                        }
                    break;
            }
        }

        private static (long checks, long hits, long rejectedByBounds) Run(SpatialQueryKind kind, int stride)
        {
            var requests = new List<SpatialRequest>(Requests(kind));
            List<SpatialBody> boxes = Boxes(kind == SpatialQueryKind.Capsule);
            long index = 0, checks = 0, hits = 0, rejected = 0;
            foreach (SpatialBody body in boxes)
                foreach (SpatialRequest request in requests)
                {
                    if (index++ % stride != 0)
                        continue;
                    checks++;
                    bool fast = SpatialGeometry.Intersects(in body, in request, out SimFix distance, false);
                    bool slow = SpatialGeometry.Intersects(in body, in request, out SimFix slowDistance, true);
                    bool oracle = OracleBox.Intersects(body, request, out SimFix oracleDistance);
                    if (fast != oracle || slow != oracle || distance != oracleDistance || slowDistance != oracleDistance)
                        Assert.Fail("Exhaustive mismatch at " + (index - 1) + ": fast=" + fast + "/" + distance.Raw + ", slow=" + slow + "/" + slowDistance.Raw
                            + ", oracle=" + oracle + "/" + oracleDistance.Raw + Describe(in body, in request));
                    if (request.Kind == SpatialQueryKind.Ray || request.Kind == SpatialQueryKind.Capsule)
                    {
                        bool may = SpatialBounds.RayMayHit(in body, in request);
                        if (oracle && !may)
                            Assert.Fail("Bounds rejected an exact hit" + Describe(in body, in request));
                        rejected += may ? 0 : 1;
                    }
                    if (request.Kind == SpatialQueryKind.Sphere && SpatialModeMath.ReferenceDistance(in body, request.Origin).Raw != OracleBox.ReferenceDistance(in body, request.Origin))
                        Assert.Fail("Reference distance mismatch" + Describe(in body, in request));
                    hits += oracle ? 1 : 0;
                }
            return (checks, hits, rejected);
        }

        private static string Describe(in SpatialBody body, in SpatialRequest q)
        {
            Collider c = body.Collider;
            return "; hx=" + c.Radius.Raw + " h=" + c.Height.Raw + " hz=" + c.HalfDepth.Raw + " yaw=(" + c.Yaw.M + "," + c.Yaw.N + "); origin=" + q.Origin
                + " dir=" + q.Direction + " r=" + q.Radius.Raw + " range=" + q.Range.Raw + " hh=" + q.HalfHeight.Raw + " cos=" + q.Cosine;
        }

        private static void Report(SpatialQueryKind kind, int stride)
        {
            (long checks, long hits, long rejected) = Run(kind, stride);
            Assert.That(hits, Is.GreaterThan(0));
            Assert.That(hits, Is.LessThan(checks));
            TestContext.WriteLine("box-exhaustive {0}: stride={1}; checks={2}; hits={3}; bounds_rejections={4}; mismatches=0", kind, stride, checks, hits, rejected);
        }

        // A Q32 radial edge is inexact for non-Pythagorean cosines, yet it can still be exactly parallel
        // to a box face: for cos = -681 the content of (57+i)^2 (C+iS) reduces the edge to the integer
        // direction (39591176, -39497323), and under yaw (57, 1) its local X component is exactly 0.
        // The apex sits on the face line local x = -hx, one raw unit below the face z = -hz, so the edge
        // grazes the face closed. The approximate edge lies 1.8e-12 degrees outside the exact wedge,
        // so only the parallel-slab branch of the edge test decides this hit.
        [Test]
        public void ReachableAxisParallelQ32EdgeGrazesAFaceClosed()
        {
            var body = new SpatialBody(new EntityId(0, 1), default,
                Collider.Box(F(114000), F(2), F(1999), BoxYaw.FromGenerator(57, 1)));
            SimVec3 direction = BoxInputs.V(39591176, 0, -39497323);
            // expected: 1 hit, 0 miss, -1 only product/oracle agreement (apex moved off the face line).
            foreach ((long dx, long radius, int expected) in new[] { (-114000L, 2L, 1), (-114000L, 1L, 1), (-114000L, 0L, 0), (-114001L, 2L, -1), (-113999L, 2L, -1) })
            {
                SpatialRequest sector = SpatialQueries.Sector(BoxInputs.V(dx, 0, 2000), F(radius), direction, -681, default);
                bool fast = SpatialGeometry.Intersects(in body, in sector, out _, false);
                bool slow = SpatialGeometry.Intersects(in body, in sector, out _, true);
                bool oracle = OracleBox.Intersects(body, sector, out _);
                Assert.That((fast, slow), Is.EqualTo((oracle, oracle)), "dx=" + dx + ", radius=" + radius);
                if (expected >= 0)
                    Assert.That(oracle, Is.EqualTo(expected == 1), "dx=" + dx + ", radius=" + radius);
            }
        }

        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void StridedLatticeMatchesTheOracle(SpatialQueryKind kind) => Report(kind, DefaultStride);

        [Category("Manual")]
        [Explicit("Whole lattice; tens of minutes headless.")]
        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void WholeLatticeMatchesTheOracle(SpatialQueryKind kind) => Report(kind, 1);
    }
}
