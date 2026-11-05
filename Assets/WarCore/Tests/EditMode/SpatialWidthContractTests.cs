using System;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    [Category("R24")]
    public sealed class SpatialWidthContractTests
    {
        [Test]
        public void WidthProofRejectsAChangedWorldLimit()
        {
            Type proof = typeof(SimFix).Assembly.GetType("WarCore.Sim.SpatialWidth");
            Assert.That(proof, Is.Not.Null, "A throwing Debug proof guard must bind every public spatial entry to the audited world size.");
            MethodInfo guard = proof.GetMethod("AssertWorldLimit", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(guard, Is.Not.Null);
            guard.Invoke(null, new object[] { 16384 });
            var error = Assert.Throws<TargetInvocationException>(() => guard.Invoke(null, new object[] { 16385 }));
            Assert.That(error.InnerException, Is.TypeOf<SimBoundaryException>());
        }

        [TestCase("position")]
        [TestCase("body")]
        [TestCase("radius")]
        [TestCase("height")]
        [TestCase("range")]
        [TestCase("direction")]
        [TestCase("capsule-start")]
        [TestCase("capsule-end")]
        [TestCase("reference")]
        public void PublicInputsRejectBrokenWidthContractsBeforeArithmetic(string entry)
        {
            var outside = new SimVec3(SimFix.FromRaw(long.MinValue), default, default);
            var unit = SimVec3.Metres(1, 0, 0);
            var grid = SpatialQueryTests.Grid(new SpatialBody[0]);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                switch (entry)
                {
                    case "position":
                        _ = new Position(outside);
                        break;
                    case "body":
                        _ = new SpatialBody(new EntityId(1, 1), outside, default);
                        break;
                    case "radius":
                        _ = new Collider(SimFix.FromRaw((1L << 30) + 1), SimFix.FromInt(32768));
                        break;
                    case "height":
                        _ = new Collider(default, SimFix.FromRaw((1L << 31) + 1));
                        break;
                    case "range":
                        _ = SpatialQueries.Ray(default, unit, SimFix.FromRaw((1L << 30) + 1));
                        break;
                    case "direction":
                        _ = SpatialQueries.Ray(default, outside, SimFix.FromInt(1));
                        break;
                    case "capsule-start":
                        grid.Capsule(outside, unit, default, new EntityId[1]);
                        break;
                    case "capsule-end":
                        _ = SpatialQueries.Capsule(default, outside, default);
                        break;
                    default:
                        var request = SpatialQueries.Sphere(default, default);
                        grid.Nearest(in request, outside, out _);
                        break;
                }
            });
        }

        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void MaximumWorldGeometryMatchesIndependent384BitOracle(SpatialQueryKind kind)
        {
            const long w = 1L << 30;
            var random = new A1MathInputs(0xB0240000UL + (ulong)kind);
            int hits = 0;
            for (int i = 0; i < 2000; i++)
            {
                long radius = i % 4 == 0 ? 0 : w - (long)(random.Next() & 65535);
                long height = i % 4 == 0 ? 2 * w - (i & 1) : 2 * w;
                SimVec3 center = V(Edge(ref random), Edge(ref random), Edge(ref random));
                var body = new SpatialBody(new EntityId(1, 1), center, new Collider(SimFix.FromRaw(radius), SimFix.FromRaw(height)));
                SimVec3 origin = V(Edge(ref random), Edge(ref random), Edge(ref random));
                SimVec3 direction = V(w, kind == SpatialQueryKind.Sector || i % 2 == 0 ? 0 : w, i % 3 == 0 ? 0 : w);
                SimFix reach = SimFix.FromRaw(w - (i % 3));
                SpatialRequest request;
                switch (kind)
                {
                    case SpatialQueryKind.Sphere:
                        request = SpatialQueries.Sphere(origin, reach);
                        break;
                    case SpatialQueryKind.Cylinder:
                        request = SpatialQueries.Cylinder(origin, reach, reach);
                        break;
                    case SpatialQueryKind.Sector:
                        request = SpatialQueries.Sector(origin, reach, direction, i % 2001 - 1000, reach);
                        break;
                    case SpatialQueryKind.Capsule:
                        request = SpatialQueries.Capsule(origin, V(0, origin.Y.Raw, origin.Z.Raw), reach);
                        break;
                    default:
                        request = SpatialQueries.Ray(origin, direction, reach);
                        break;
                }
                bool expected = OracleSpatialGeometry.Intersects(body, request, out SimFix reference);
                bool actual = SpatialGeometry.Intersects(in body, in request, out SimFix distance);
                Assert.That(actual, Is.EqualTo(expected), "hit at " + i);
                Assert.That(distance.Raw, Is.EqualTo(reference.Raw), "distance at " + i);
                if (actual)
                    hits++;
            }
            Assert.That(hits, Is.GreaterThan(0));
            TestContext.WriteLine("R2-4 maximum geometry: kind={0}; samples=2000; hits={1}; mismatches=0; world=2^30 raw; height<=2^31 raw", kind, hits);
        }

        [Test]
        public void AllCappedDistanceAndNearestLowerBoundRetainHighSquareBits()
        {
            const long w = 1L << 30;
            SimVec3 a = V(-w, -w, -w);
            SimVec3 b = V(w, w, w);
            SimFix distance = SpatialModeMath.ReferenceDistance(a, 0, 0, b);
            BigInteger squared = 12 * (BigInteger)w * w;
            BigInteger root = distance.Raw;
            Assert.That(root * root <= squared && (root + 1) * (root + 1) > squared, Is.True);
            var next = new RayCellEvent(0, 0, 0, (ulong)w, (ulong)w);
            Assert.That(SpatialModeMath.BeyondBest(in next, 3UL << 60, w - 1, w), Is.True);
            Assert.That(SpatialModeMath.BeyondBest(in next, 3UL << 60, w, w), Is.False);
        }

        private static SimVec3 V(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        private static long Edge(ref A1MathInputs random)
        {
            long value = (1L << 30) - (long)(random.Next() & 65535);
            return (random.Next() & 1) == 0 ? value : -value;
        }
    }
}
