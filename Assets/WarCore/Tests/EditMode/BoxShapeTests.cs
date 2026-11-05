using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    [Category("M0c3a")]
    public sealed class BoxShapeTests
    {
        private const int Unit = (int)SimFix.Scale;
        private static SimFix F(long raw) => SimFix.FromRaw(raw);
        private static SimVec3 V(long x, long y, long z) => BoxInputs.V(x, y, z);

        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void EveryKindMatchesTheIndependentRationalOracle(SpatialQueryKind kind)
        {
            ulong seed = 0x3A0B0000UL + (ulong)kind;
            FuzzCounts counts = Fuzz(kind, seed, 20000);
            // Positive controls: every group must produce both answers.
            for (int group = 0; group < BoxInputs.Groups; group++)
                Assert.That(counts.Hits[group], Is.GreaterThan(0), "No hits in group " + group);
            Assert.That(counts.TotalHits, Is.LessThan(counts.Count), "Every sample hit.");
            TestContext.WriteLine(counts.Describe(kind, seed));
        }

        // Large run for the delivery record: 400000 pairs per kind, 2000000 in total.
        [Category("Manual")]
        [Explicit("Large oracle run; minutes headless.")]
        [TestCase(SpatialQueryKind.Sphere)]
        [TestCase(SpatialQueryKind.Cylinder)]
        [TestCase(SpatialQueryKind.Sector)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Ray)]
        public void LargeRunMatchesTheIndependentRationalOracle(SpatialQueryKind kind)
        {
            ulong seed = 0x3A0B1000UL + (ulong)kind;
            TestContext.WriteLine(Fuzz(kind, seed, 400000).Describe(kind, seed));
        }

        private sealed class FuzzCounts
        {
            internal readonly int[] Hits = new int[BoxInputs.Groups];
            internal readonly int[] Cases = new int[BoxInputs.Groups];
            internal int TotalHits;
            internal int Count;
            internal int ReferenceChecks;

            internal string Describe(SpatialQueryKind kind, ulong seed)
            {
                var groups = new System.Text.StringBuilder();
                for (int i = 0; i < Hits.Length; i++)
                    groups.Append(i == 0 ? "" : ", ").Append(Names[i]).Append('=').Append(Hits[i]).Append('/').Append(Cases[i]);
                return string.Format("Box oracle: kind={0}; seed=0x{1:X}; pairs={2}; hits={3}; misses={4}; group hits/cases: {5}; reference distances={6}; mismatches=0.",
                    kind, seed, Count, TotalHits, Count - TotalHits, groups, ReferenceChecks);
            }
        }

        private static readonly string[] Names = { "local", "world", "world-edge", "contact+-1", "near" };

        private static FuzzCounts Fuzz(SpatialQueryKind kind, ulong seed, int count)
        {
            var inputs = new BoxInputs(seed);
            var counts = new FuzzCounts();
            for (int i = 0; i < count; i++)
            {
                BoxInputs.Sample sample = inputs.Random(kind, i);
                bool hit = Check(in sample, i);
                int group = i % BoxInputs.Groups;
                counts.Cases[group]++;
                counts.Count++;
                if (hit)
                {
                    counts.Hits[group]++;
                    counts.TotalHits++;
                }
                long product = SpatialModeMath.ReferenceDistance(in sample.Body, sample.Request.Origin).Raw;
                long oracle = OracleBox.ReferenceDistance(in sample.Body, sample.Request.Origin);
                if (product != oracle)
                    Assert.Fail("Reference distance mismatch at " + i + ", " + sample.Source + ": product=" + product + ", oracle=" + oracle);
                counts.ReferenceChecks++;
            }
            return counts;
        }

        private static bool Check(in BoxInputs.Sample sample, int index)
        {
            bool fast = SpatialGeometry.Intersects(in sample.Body, in sample.Request, out SimFix distance, false);
            bool slow = SpatialGeometry.Intersects(in sample.Body, in sample.Request, out SimFix slowDistance, true);
            bool oracle = OracleBox.Intersects(sample.Body, sample.Request, out SimFix oracleDistance);
            if (fast != oracle || slow != oracle || distance != oracleDistance || slowDistance != oracleDistance)
                Assert.Fail("Box mismatch at " + index + ", " + sample.Source + ", kind=" + sample.Request.Kind
                    + ", fast=" + fast + "/" + distance.Raw + ", slow=" + slow + "/" + slowDistance.Raw
                    + ", oracle=" + oracle + "/" + oracleDistance.Raw + Describe(in sample));
            // The world AABB prefilter must never reject an exact hit.
            if (oracle && (sample.Request.Kind == SpatialQueryKind.Ray || sample.Request.Kind == SpatialQueryKind.Capsule))
                Assert.That(SpatialBounds.RayMayHit(in sample.Body, in sample.Request), Is.True, sample.Source);
            return oracle;
        }

        private static string Describe(in BoxInputs.Sample sample)
        {
            Collider c = sample.Body.Collider;
            SpatialRequest q = sample.Request;
            return "; body p=" + sample.Body.Position + " hx=" + c.Radius.Raw + " h=" + c.Height.Raw + " hz=" + c.HalfDepth.Raw
                + " yaw=(" + c.Yaw.M + "," + c.Yaw.N + "); origin=" + q.Origin + " dir=" + q.Direction + " r=" + q.Radius.Raw
                + " range=" + q.Range.Raw + " hh=" + q.HalfHeight.Raw + " cos=" + q.Cosine;
        }

        [TestCase(0x3A0C01)]
        [TestCase(0x3A0C02)]
        public void MixedBoxAndCapsuleScenesMatchTheOracleInEveryMode(int seed)
        {
            var random = new Random(seed);
            var fixture = new QueryModeFixture();
            for (int i = 0; i < 72; i++)
                fixture.Add(Point(random, i), Shape(random, i, Layer(i)));
            int comparisons = 0;
            int boxes = 0;
            for (int step = 0; step < 200; step++)
            {
                int slot = step % fixture.Bodies.Count;
                EntityId entity = fixture.Bodies[slot].Entity;
                if (step % 17 == 0)
                {
                    fixture.Store.Destroy(entity);
                    fixture.Check(SpatialQueries.Sphere(fixture.Bodies[slot].Position, SimFix.FromInt(12)), default, 7, 3);
                    fixture.Store.RecycleDestroyed();
                    entity = fixture.Store.Create();
                    Assert.That(entity.Id, Is.EqualTo(slot));
                }
                fixture.Set(entity, Point(random, step), Shape(random, step, Layer(step)));
                // A yaw-only or depth-only change must still re-index the body.
                int turnSlot = (step + 29) % fixture.Bodies.Count;
                SpatialBody turned = fixture.Bodies[turnSlot];
                if (turned.Collider.ShapeKind == ShapeKind.OrientedBox && fixture.Store.IsAlive(turned.Entity))
                {
                    Collider c = turned.Collider;
                    Collider changed = step % 2 == 0
                        ? Collider.Box(c.Radius, c.Height, c.HalfDepth, Yaw(random, step + 1), c.Layer)
                        : Collider.Box(c.Radius, c.Height, F(Math.Min(Collider.BoxHalfExtentLimit, c.HalfDepth.Raw + 3 * SimFix.Scale)), c.Yaw, c.Layer);
                    fixture.Set(turned.Entity, turned.Position, changed);
                }
                int layerSlot = (step + 13) % fixture.Bodies.Count;
                EntityId layerEntity = fixture.Bodies[layerSlot].Entity;
                fixture.Store.Get(fixture.Colliders, layerEntity) = fixture.Store.Get(fixture.Colliders, layerEntity).WithLayer(Layer(step + 7));
                SimVec3 origin = Point(random, step);
                SimVec3 custom = step % 2 == 0 ? SimVec3.Metres(16384, -16384, 16384) : Point(random, step + 1);
                uint mask = step % 7 == 0 ? 0 : step % 3 == 0 ? uint.MaxValue : Layer(step + 1) | Layer(step + 2);
                for (int kind = 0; kind < 5; kind++)
                {
                    SpatialRequest request = Request(random, (SpatialQueryKind)kind, origin, step, mask);
                    fixture.Check(request, request.Origin, step % 19, step % 11);
                    fixture.Check(request, custom, step % 19, step % 11);
                    comparisons += 2;
                }
            }
            foreach (SpatialBody body in fixture.Bodies)
                if (body.Collider.ShapeKind == ShapeKind.OrientedBox)
                    boxes++;
            Assert.That(boxes, Is.GreaterThan(24));
            TestContext.WriteLine("Mixed box+capsule differential: seed=0x{0:X}; steps=200; bodies=72 (boxes at end={1}); shapes=5; mode sets={2}; yaw-only and depth-only re-index, layer-only changes, death/reuse, world corners; mismatches=0.", seed, boxes, comparisons);
        }

        private static uint Layer(int i) => i % 5 == 0 ? 0 : i % 5 == 1 ? 0x80000000u : i % 5 == 2 ? 1u : i % 5 == 3 ? 3u : uint.MaxValue;

        private static SimVec3 Point(Random random, int i)
        {
            long center = i % 3 == 0 ? -16374 : i % 3 == 1 ? 16374 : 0;
            long x = center * SimFix.Scale + random.Next(-3, 4) * 3L * SimFix.Scale + random.Next(-1, 2);
            long y = center * SimFix.Scale + random.Next(-3, 4) * 3L * SimFix.Scale + random.Next(-1, 2);
            long z = center * SimFix.Scale + random.Next(-3, 4) * 3L * SimFix.Scale + random.Next(-1, 2);
            return V(x, y, z);
        }

        private static BoxYaw Yaw(Random random, int i)
        {
            switch (i % 5)
            {
                case 0: return BoxYaw.Identity;
                case 1: return BoxYaw.QuarterTurns(random.Next(4));
                case 2: return BoxYaw.FromGenerator(4095, random.Next(2) == 0 ? 1 : -1);
                default:
                    while (true)
                    {
                        int m = random.Next(-4096, 4097);
                        int n = random.Next(-4096, 4097);
                        if ((m != 0 || n != 0) && (long)m * m + (long)n * n <= BoxYaw.MaxDenominator)
                            return BoxYaw.FromGenerator(m, n);
                    }
            }
        }

        // Alternates capsules and boxes; boxes cover zero extents, slabs, odd heights and every yaw class.
        private static Collider Shape(Random random, int i, uint layer)
        {
            if (i % 2 == 0)
            {
                long radius = i % 9 == 0 ? 4 * SimFix.Scale : i % 3 == 0 ? 0 : SimFix.Scale / 4;
                return new Collider(F(radius), F(2 * radius + (i % 4) * SimFix.Scale + 1), layer);
            }
            long hx = i % 7 == 1 ? 0 : random.Next(0, 4 * Unit + 1);
            long hz = i % 11 == 1 ? 0 : random.Next(0, 4 * Unit + 1);
            long height = i % 13 == 1 ? 0 : random.Next(0, 8 * Unit + 1) | 1;
            return Collider.Box(F(hx), F(height), F(hz), Yaw(random, i / 2), layer);
        }

        private static SpatialRequest Request(Random random, SpatialQueryKind kind, SimVec3 origin, int step, uint mask)
        {
            SimFix radius = SimFix.FromInt(step % 4 == 0 ? 0 : 16);
            SimVec3 direction = V((step % 2 == 0 ? 3 : -3) * SimFix.Scale + random.Next(-Unit, Unit), 0,
                (step % 3 - 1) * SimFix.Scale + random.Next(-Unit, Unit));
            switch (kind)
            {
                case SpatialQueryKind.Sphere:
                    return SpatialQueries.Sphere(origin, radius, mask);
                case SpatialQueryKind.Cylinder:
                    return SpatialQueries.Cylinder(origin, radius, SimFix.FromInt(step % 9), mask);
                case SpatialQueryKind.Sector:
                    return SpatialQueries.Sector(origin, radius, direction, step % 3 == 0 ? -1000 : step % 3 == 1 ? 600 : -300, SimFix.FromInt(step % 9), mask);
                case SpatialQueryKind.Capsule:
                    return SpatialQueries.Capsule(origin, Clamp(origin + direction), radius, mask);
                default:
                    return SpatialQueries.Ray(origin, new SimVec3(direction.X, SimFix.FromInt(step % 3 - 1), direction.Z), SimFix.FromInt(32), mask);
            }
        }

        private static SimVec3 Clamp(SimVec3 point)
        {
            const long limit = 1L << 30;
            return V(Math.Max(-limit, Math.Min(limit, point.X.Raw)), Math.Max(-limit, Math.Min(limit, point.Y.Raw)),
                Math.Max(-limit, Math.Min(limit, point.Z.Raw)));
        }

        [Test]
        public void YawUsesUnityEulerConventionAndExactQuarterTurns()
        {
            // Unity Euler(0, 90, 0) maps local +X to world -Z.
            var body = new SpatialBody(new EntityId(0, 1), default, Collider.Box(SimFix.FromInt(10), SimFix.FromInt(2), default, BoxYaw.QuarterTurns(1)));
            Assert.That(SpatialGeometry.Intersects(in body, SpatialQueries.Sphere(SimVec3.Metres(0, 0, -10), default), out _), Is.True);
            Assert.That(SpatialGeometry.Intersects(in body, SpatialQueries.Sphere(SimVec3.Metres(0, 0, 10), default), out _), Is.True);
            Assert.That(SpatialGeometry.Intersects(in body, SpatialQueries.Sphere(SimVec3.Metres(0, 0, -10) + V(0, 0, -1), default), out _), Is.False);
            Assert.That(SpatialGeometry.Intersects(in body, SpatialQueries.Sphere(V(1, 0, 0), default), out _), Is.False);
            int[,] expected = { { 1, 0, 1 }, { 0, 1, 1 }, { -1, 0, 1 }, { 0, -1, 1 } };
            for (int k = -4; k < 8; k++)
            {
                BoxYaw yaw = BoxYaw.QuarterTurns(k);
                int row = ((k % 4) + 4) % 4;
                Assert.That(yaw.Cos * expected[row, 2], Is.EqualTo(expected[row, 0] * yaw.Denominator), "cos k=" + k);
                Assert.That(yaw.Sin * expected[row, 2], Is.EqualTo(expected[row, 1] * yaw.Denominator), "sin k=" + k);
            }
            Assert.That(default(BoxYaw), Is.EqualTo(BoxYaw.Identity));
            Assert.That((BoxYaw.Identity.M, BoxYaw.Identity.N), Is.EqualTo((1, 0)));
        }

        [Test]
        public void YawGeneratorsAreCanonicalAndBounded()
        {
            Assert.That(BoxYaw.FromGenerator(2, 0), Is.EqualTo(BoxYaw.Identity));
            Assert.That(BoxYaw.FromGenerator(4096, 0), Is.EqualTo(BoxYaw.Identity));
            Assert.That(BoxYaw.FromGenerator(-3, -4), Is.EqualTo(BoxYaw.FromGenerator(3, 4)));
            Assert.That(BoxYaw.FromGenerator(6, 8), Is.EqualTo(BoxYaw.FromGenerator(3, 4)));
            Assert.That(BoxYaw.FromGenerator(0, -5), Is.EqualTo(BoxYaw.QuarterTurns(2)));
            Assert.That(BoxYaw.FromGenerator(-1, 1), Is.EqualTo(BoxYaw.FromGenerator(1, -1)));
            BoxYaw finest = BoxYaw.FromGenerator(4095, 1);
            Assert.That(finest.Denominator, Is.EqualTo(4095L * 4095 + 1));
            Assert.That(finest.Cos * finest.Cos + finest.Sin * finest.Sin, Is.EqualTo(finest.Denominator * finest.Denominator));
            Assert.Throws<ArgumentOutOfRangeException>(() => BoxYaw.FromGenerator(0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => BoxYaw.FromGenerator(4096, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => BoxYaw.FromGenerator(4097, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => BoxYaw.FromGenerator(0, -4097));
            Assert.Throws<ArgumentOutOfRangeException>(() => BoxYaw.FromGenerator(2897, 2897 + 1));
            Assert.That(BoxYaw.IsCanonical(1, 0) && BoxYaw.IsCanonical(0, 1) && BoxYaw.IsCanonical(4095, 1) && BoxYaw.IsCanonical(4095, -1), Is.True);
            Assert.That(BoxYaw.IsCanonical(2, 0) || BoxYaw.IsCanonical(0, -1) || BoxYaw.IsCanonical(-1, 0) || BoxYaw.IsCanonical(4096, 1)
                || BoxYaw.IsCanonical(2, 4) || BoxYaw.IsCanonical(0, 0) || BoxYaw.IsCanonical(0, 2), Is.False);
            // The finest step next to 0 is n/m = 1/4095: no canonical generator with n = 1 has m > 4095.
            for (int m = 1; m <= 4096; m++)
                Assert.That(BoxYaw.IsCanonical(m, 1), Is.EqualTo(m <= 4095), "m=" + m);
            // Every canonical rotation is exactly orthonormal; sample all generators on a coarse lattice.
            for (int m = 0; m <= 4096; m += 37)
                for (int n = -4096; n <= 4096; n += 41)
                    if ((m != 0 || n != 0) && (long)m * m + (long)n * n <= BoxYaw.MaxDenominator)
                    {
                        BoxYaw yaw = BoxYaw.FromGenerator(m, n);
                        Assert.That(BoxYaw.IsCanonical(yaw.M, yaw.N), Is.True);
                        Assert.That(yaw.Cos * yaw.Cos + yaw.Sin * yaw.Sin, Is.EqualTo(yaw.Denominator * yaw.Denominator));
                    }
        }

        [Test]
        public void WorldBoundsAreConservativeAndTightToOneRawUnit()
        {
            var inputs = new BoxInputs(0x3A0AABB);
            int checkedBoxes = 0;
            for (int i = 0; i < 20000; i++)
            {
                Collider box = i % 7 == 0
                    ? Collider.Box(F(Collider.BoxHalfExtentLimit - i % 3), F(32768L * SimFix.Scale), F(Collider.BoxHalfExtentLimit - i % 5), inputs.Yaw(i))
                    : inputs.Box(i % 2 == 0 ? 64 * SimFix.Scale : Collider.BoxHalfExtentLimit, i);
                BoxYaw yaw = box.Yaw;
                // Corners (+-hx, +-hz) map to x = +-hx*a/c +- hz*b/c and z = -+hx*b/c +- hz*a/c.
                BigInteger c = yaw.Denominator;
                BigInteger maxX = BigInteger.Abs(yaw.Cos) * box.Radius.Raw + BigInteger.Abs(yaw.Sin) * box.HalfDepth.Raw;
                BigInteger maxZ = BigInteger.Abs(yaw.Sin) * box.Radius.Raw + BigInteger.Abs(yaw.Cos) * box.HalfDepth.Raw;
                SimVec3 extents = box.WorldHalfExtents;
                Assert.That(extents.X.Raw * c >= maxX && (extents.X.Raw - 1) * c < maxX, Is.True, "x i=" + i);
                Assert.That(extents.Z.Raw * c >= maxZ && (extents.Z.Raw - 1) * c < maxZ, Is.True, "z i=" + i);
                Assert.That(extents.X.Raw <= box.Radius.Raw + box.HalfDepth.Raw && extents.Z.Raw <= box.Radius.Raw + box.HalfDepth.Raw, Is.True);
                Assert.That(extents.Y.Raw, Is.EqualTo(box.Height.Raw / 2));
                checkedBoxes++;
            }
            var capsule = new Collider(SimFix.FromInt(2), SimFix.FromInt(7));
            Assert.That(capsule.WorldHalfExtents, Is.EqualTo(new SimVec3(SimFix.FromInt(2), F(7 * SimFix.Scale / 2), SimFix.FromInt(2))));
            TestContext.WriteLine("Box AABB: {0} boxes incl. maximum extents and every yaw class; ceil((|a|hx+|b|hz)/c) conservative and within one raw unit; capsule extents unchanged.", checkedBoxes);
        }

        [Test]
        public void ColliderAndBodyKeepTheirSizesAndCapsuleDefaults()
        {
            Assert.That(MemoryMarshal.AsBytes(new Collider[1].AsSpan()).Length, Is.EqualTo(32));
            Assert.That(MemoryMarshal.AsBytes(new SpatialBody[1].AsSpan()).Length, Is.EqualTo(72));
            var capsule = new Collider(SimFix.FromInt(1), SimFix.FromInt(3), 5u, ShapeKind.VerticalCapsule, true);
            Assert.That(capsule.ShapeKind, Is.EqualTo(ShapeKind.VerticalCapsule));
            Assert.That(capsule.HalfDepth.Raw, Is.Zero);
            Assert.That(capsule.Yaw, Is.EqualTo(BoxYaw.Identity));
            Assert.That(default(Collider).ShapeKind, Is.EqualTo(ShapeKind.VerticalCapsule));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Collider(SimFix.FromInt(1), SimFix.FromInt(3), 1u, ShapeKind.OrientedBox));
            Assert.Throws<ArgumentOutOfRangeException>(() => Collider.Box(F(-1), default, default, BoxYaw.Identity));
            Assert.Throws<ArgumentOutOfRangeException>(() => Collider.Box(default, default, F(Collider.BoxHalfExtentLimit + 1), BoxYaw.Identity));
            Assert.Throws<ArgumentOutOfRangeException>(() => Collider.Box(default, F(32768L * SimFix.Scale + 1), default, BoxYaw.Identity));
            Assert.Throws<ArgumentOutOfRangeException>(() => Collider.Box(default, F(-1), default, BoxYaw.Identity));
            Collider box = Collider.Box(SimFix.FromInt(2), SimFix.FromInt(3), SimFix.FromInt(4), BoxYaw.FromGenerator(2, 1), 9u, true);
            foreach (Collider copy in new[] { box.WithLayer(9u), box.WithStatic(true), box.WithHeight(SimFix.FromInt(3)) })
                Assert.That(Fixtures.Hash(copy), Is.EqualTo(Fixtures.Hash(box)));
            Assert.That(box.WithHeight(SimFix.FromInt(1)).ShapeKind, Is.EqualTo(ShapeKind.OrientedBox));
            Assert.That(box.WithLayer(2u).Yaw, Is.EqualTo(box.Yaw));
            Assert.That(box.WithStatic(false).HalfDepth, Is.EqualTo(box.HalfDepth));
        }

        private static byte[] Bytes(Action<SimStateWriter> write)
        {
            var writer = new SimStateWriter();
            write(writer);
            return writer.ToArray();
        }

        private static byte[] Record(int shape, long radius, long height, ulong layer, bool isStatic, bool box = false, long depth = 0, int m = 1, int n = 0)
            => Bytes(w =>
            {
                w.Int32(shape);
                w.Int64(radius);
                w.Int64(height);
                w.UInt64(layer);
                w.Bool(isStatic);
                if (box)
                {
                    w.Int64(depth);
                    w.Int32(m);
                    w.Int32(n);
                }
            });

        [Test]
        public void CapsuleRecordsAndHashesKeepTheVersionThreeEncoding()
        {
            var serializer = new ColliderSerializer();
            Assert.That(serializer.Version, Is.EqualTo(3));
            Collider[] capsules =
            {
                default,
                new Collider(SimFix.FromInt(1), SimFix.FromInt(3)),
                new Collider(F(3), F(7), 0u, ShapeKind.VerticalCapsule, true),
                new Collider(F(1L << 30), F(32768L * SimFix.Scale), uint.MaxValue),
            };
            foreach (Collider capsule in capsules)
            {
                byte[] actual = Bytes(w => serializer.Write(w, capsule));
                Assert.That(actual, Is.EqualTo(Record(0, capsule.Radius.Raw, capsule.Height.Raw, capsule.Layer, capsule.IsStatic)));
                var expected = new StateHash();
                expected.Add(0);
                expected.Add(capsule.Radius.Raw);
                expected.Add(capsule.Height.Raw);
                expected.Add((ulong)capsule.Layer);
                expected.Add(capsule.IsStatic);
                Assert.That(Fixtures.Hash(capsule), Is.EqualTo(expected.Value));
                Collider read = serializer.Read(new SimStateReader(actual));
                Assert.That(Fixtures.Hash(read), Is.EqualTo(Fixtures.Hash(capsule)));
            }
        }

        [Test]
        public void BoxRecordsAppendDepthAndYawWithFrozenVectors()
        {
            var serializer = new ColliderSerializer();
            Collider box = Collider.Box(F(3 * SimFix.Scale + 1), F(5 * SimFix.Scale + 1), F(7 * SimFix.Scale), BoxYaw.FromGenerator(4095, -1), 0x80000001u, true);
            byte[] bytes = Bytes(w => serializer.Write(w, box));
            Assert.That(bytes, Is.EqualTo(Record(1, box.Radius.Raw, box.Height.Raw, box.Layer, true, true, box.HalfDepth.Raw, 4095, -1)));
            var expected = new StateHash();
            expected.Add(1);
            expected.Add(box.Radius.Raw);
            expected.Add(box.Height.Raw);
            expected.Add((ulong)box.Layer);
            expected.Add(true);
            expected.Add(box.HalfDepth.Raw);
            expected.Add(4095);
            expected.Add(-1);
            Assert.That(Fixtures.Hash(box), Is.EqualTo(expected.Value));
            Assert.That(Fixtures.Hash(box), Is.EqualTo(FrozenBoxHash), "Frozen box hash vector.");
            Assert.That(BitConverter.ToString(bytes).Replace("-", ""), Is.EqualTo(FrozenBoxRecord), "Frozen box record vector.");
            Collider read = serializer.Read(new SimStateReader(bytes));
            Assert.That((read.ShapeKind, read.Radius, read.Height, read.HalfDepth, read.Yaw, read.Layer, read.IsStatic),
                Is.EqualTo((box.ShapeKind, box.Radius, box.Height, box.HalfDepth, box.Yaw, box.Layer, box.IsStatic)));
            Assert.That(Bytes(w => serializer.Write(w, read)), Is.EqualTo(bytes));
            // Every box field reaches the hash.
            Collider[] variants =
            {
                Collider.Box(box.Radius, box.Height, box.HalfDepth, BoxYaw.FromGenerator(4095, 1), box.Layer, true),
                Collider.Box(box.Radius, box.Height, F(box.HalfDepth.Raw + 1), box.Yaw, box.Layer, true),
                Collider.Box(F(box.Radius.Raw + 1), box.Height, box.HalfDepth, box.Yaw, box.Layer, true),
                Collider.Box(box.Radius, F(box.Height.Raw - 1), box.HalfDepth, box.Yaw, box.Layer, true),
                Collider.Box(box.Radius, box.Height, default, BoxYaw.Identity, box.Layer, true),
            };
            var seen = new HashSet<ulong> { Fixtures.Hash(box) };
            foreach (Collider variant in variants)
                Assert.That(seen.Add(Fixtures.Hash(variant)), Is.True);
            // A box with zero depth and identity yaw still differs from the capsule with the same prefix.
            var capsule = new Collider(F(1), F(4), 1u);
            Assert.That(Fixtures.Hash(Collider.Box(F(1), F(4), default, BoxYaw.Identity)), Is.Not.EqualTo(Fixtures.Hash(capsule)));
        }

        private const ulong FrozenBoxHash = 12931211170299083287UL;
        // Little-endian: shape 1, hx, height, layer, static, depth, m = 4095, n = -1.
        private const string FrozenBoxRecord = "01000000" + "0100030000000000" + "0100050000000000" + "0100008000000000" + "01" + "0000070000000000" + "FF0F0000" + "FFFFFFFF";

        [Test]
        public void ReaderRejectsUnknownShapesNonCanonicalYawsAndOutOfRangeExtents()
        {
            var serializer = new ColliderSerializer();
            long r = SimFix.Scale;
            long h = 2 * SimFix.Scale;
            foreach (int shape in new[] { 2, 255, 256, 257, -1, int.MaxValue })
                Assert.Throws<SimSnapshotException>(() => serializer.Read(new SimStateReader(Record(shape, r, h, 1, false, true, r))), "shape=" + shape);
            foreach ((int m, int n) in new[] { (2, 0), (0, -1), (-1, 0), (0, 0), (2, 4), (4096, 1), (-3, 4), (5000, 0), (0, 2) })
                Assert.Throws<SimSnapshotException>(() => serializer.Read(new SimStateReader(Record(1, r, h, 1, false, true, r, m, n))), "yaw=" + m + "," + n);
            foreach (long depth in new[] { -1L, Collider.BoxHalfExtentLimit + 1, long.MinValue, long.MaxValue })
                Assert.Throws<SimSnapshotException>(() => serializer.Read(new SimStateReader(Record(1, r, h, 1, false, true, depth))), "depth=" + depth);
            // Width and height are checked before Collider.Box, so they fail as snapshot errors too.
            foreach (long width in new[] { -1L, Collider.BoxHalfExtentLimit + 1, long.MinValue, long.MaxValue })
                Assert.Throws<SimSnapshotException>(() => serializer.Read(new SimStateReader(Record(1, width, h, 1, false, true, r))), "width=" + width);
            foreach (long height in new[] { -1L, Collider.HeightLimit + 1, long.MinValue, long.MaxValue })
                Assert.Throws<SimSnapshotException>(() => serializer.Read(new SimStateReader(Record(1, r, height, 1, false, true, r))), "height=" + height);
            Collider edge = serializer.Read(new SimStateReader(Record(1, Collider.BoxHalfExtentLimit, Collider.HeightLimit, 1, false, true, 0)));
            Assert.That((edge.Radius.Raw, edge.Height.Raw, edge.HalfDepth.Raw), Is.EqualTo((Collider.BoxHalfExtentLimit, Collider.HeightLimit, 0L)));
            // A box record cannot be cut down to the capsule length.
            Assert.Throws<SimSnapshotException>(() => serializer.Read(new SimStateReader(Record(1, r, h, 1, false))));
            Assert.Throws<SimSnapshotException>(() => serializer.Read(new SimStateReader(Record(1, r, h, 1UL << 32, false, true, r))));
            Collider ok = serializer.Read(new SimStateReader(Record(1, r, h, 1, false, true, Collider.BoxHalfExtentLimit, 4095, 1)));
            Assert.That((ok.HalfDepth.Raw, ok.Yaw.M, ok.Yaw.N), Is.EqualTo((Collider.BoxHalfExtentLimit, 4095, 1)));
        }

        [Test]
        public void SnapshotRestoreKeepsBoxesHashesAndQueryAnswers()
        {
            var sim = new Simulation(31, config: new SimConfig(entityCapacity: 16));
            var positions = sim.Entities.Register<Position>("position", new PositionSerializer());
            var colliders = sim.Entities.Register<Collider>("collider", new ColliderSerializer());
            var grid = new GridSpatialQuery(sim.Entities, positions, colliders, SimFix.FromInt(4), 16, 1024);
            var random = new Random(0x3A05);
            var entities = new List<EntityId>();
            for (int i = 0; i < 12; i++)
            {
                EntityId id = sim.Entities.Create();
                entities.Add(id);
                sim.Entities.Set(positions, id, new Position(V(random.Next(-12, 13) * SimFix.Scale + random.Next(-9, 10), random.Next(-2, 3) * SimFix.Scale, random.Next(-12, 13) * SimFix.Scale)));
                sim.Entities.Set(colliders, id, i % 3 == 0 ? new Collider(SimFix.FromInt(1), SimFix.FromInt(3)) : Shape(random, 2 * i + 1, 1u));
            }
            grid.Rebuild();
            byte[] snapshot = sim.SaveSnapshot();
            ulong hash = sim.StateHash();
            string before = Answers(grid);
            Collider original = sim.Entities.Get(colliders, entities[1]);
            sim.Entities.Set(colliders, entities[1], Collider.Box(original.Radius, original.Height, original.HalfDepth, BoxYaw.FromGenerator(original.Yaw.M + 1, original.Yaw.N + 3), 1u));
            Assert.That(sim.StateHash(), Is.Not.EqualTo(hash), "A yaw-only change must reach the state hash.");
            sim.Entities.Destroy(entities[2]);
            sim.RestoreSnapshot(snapshot);
            grid.Rebuild();
            Assert.That(sim.StateHash(), Is.EqualTo(hash));
            Assert.That(sim.SaveSnapshot(), Is.EqualTo(snapshot));
            Assert.That(Answers(grid), Is.EqualTo(before));
            Assert.That(sim.Entities.Get(colliders, entities[1]).Yaw, Is.EqualTo(original.Yaw));
            TestContext.WriteLine("Box snapshot: 12 bodies (8 boxes); yaw-only change alters hash; restore gives equal hash, bytes and 5-kind AllCapped answers.");
        }

        private static string Answers(GridSpatialQuery grid)
        {
            var text = new System.Text.StringBuilder();
            var output = new SpatialHit[16];
            for (int kind = 0; kind < 5; kind++)
            {
                SpatialRequest request = QueryModeFixture.Request((SpatialQueryKind)kind);
                SpatialQueryResult result = grid.AllCapped(in request, SimVec3.Metres(3, 1, -2), 16, output);
                text.Append(kind).Append(':').Append(result.TotalHits);
                for (int i = 0; i < result.Written; i++)
                    text.Append(' ').Append(output[i].Entity).Append('@').Append(output[i].Distance.Raw);
                text.Append(';');
            }
            return text.ToString();
        }

        [Test]
        public void WarmBoxQueriesAllocateZeroBytes()
        {
            var windows = new AllocationWindow();
            do
            {
                var fixture = new QueryModeFixture();
                var random = new Random(0x3A0A);
                for (int i = 0; i < 32; i++)
                    fixture.Add(SimVec3.Metres(i % 15 + 1, 0, i % 5 - 2), i % 4 == 0 ? new Collider(F(SimFix.Scale / 2), F(2 * SimFix.Scale), 0x80000001u) : Shape(random, 2 * i + 1, 0x80000001u));
                var requests = new SpatialRequest[5];
                for (int i = 0; i < 5; i++)
                    requests[i] = QueryModeFixture.Request((SpatialQueryKind)i, 0x80000000u);
                var output = new SpatialHit[17];
                var meter = new AllocationMeter();
                long checksum = 0;
                for (int i = 0; i < 64; i++)
                    checksum += RunWarm(fixture.Grid, requests, output);
                windows.Begin(meter);
                for (int i = 0; i < 256; i++)
                    checksum += RunWarm(fixture.Grid, requests, output);
                AllocationSample allocated = windows.End();
                Assert.That(checksum, Is.GreaterThan(0));
                if (!windows.NeedsRetry)
                    AllocationAssertions.Zero(allocated);
                TestContext.WriteLine("Box warm queries: 256 rounds * 5 kinds * 4 mode paths over 24 boxes + 8 capsules; {0}; checksum={1}.", allocated.Describe(), checksum);
            }
            while (windows.NeedsRetry);
        }

        private static long RunWarm(ISpatialQueryModes query, SpatialRequest[] requests, SpatialHit[] output)
        {
            long sum = 0;
            var reference = SimVec3.Metres(16, 4, -8);
            foreach (SpatialRequest request in requests)
            {
                if (query.Any(in request))
                    sum++;
                if (query.Nearest(in request, out SpatialHit nearest))
                    sum += nearest.Distance.Raw;
                sum += query.AllCapped(in request, reference, 7, output).Written;
                sum += query.AllCapped(in request, 512, output).TotalHits;
            }
            return sum;
        }
    }
}
