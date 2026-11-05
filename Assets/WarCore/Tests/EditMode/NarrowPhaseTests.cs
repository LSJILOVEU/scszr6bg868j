using System;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;
using WarCore.Tests.Oracle;

namespace WarCore.Tests
{
    [Category("NarrowPhase")]
    public sealed class NarrowPhaseTests
    {
        [TestCase(SpatialQueryKind.Ray)]
        [TestCase(SpatialQueryKind.Capsule)]
        [TestCase(SpatialQueryKind.Sector)]
        public void ForcedSlowAndIndependentOracleMatchBoundaryBiasedInputs(SpatialQueryKind kind)
        {
            ulong seed = 0x4E415200UL + (ulong)kind;
            var inputs = new A2SpatialInputs(seed);
            int hits = 0;
            int fast = 0;
            for (int i = 0; i < 36000; i++)
            {
                A2SpatialInputs.Sample sample = inputs.Random(kind, i);
                Check(in sample, i);
                if (SpatialGeometry.Intersects(in sample.Body, in sample.Request, out _))
                    hits++;
                if (kind == SpatialQueryKind.Sector && SpatialSector.TryIntersects(in sample.Body, in sample.Request, out _))
                    fast++;
            }
            TestContext.WriteLine("Narrow differential: kind={0}; seed=0x{1:X}; cases=36000; hits={2}; sector_fast_decisions={3}; frozen oracle and forced slow; mismatches=0.", kind, seed, hits, fast);
        }

        private static void Check(in A2SpatialInputs.Sample sample, int index)
        {
            bool expected = SpatialGeometry.Intersects(in sample.Body, in sample.Request, out SimFix slowDistance, true);
            bool actual = SpatialGeometry.Intersects(in sample.Body, in sample.Request, out SimFix distance, false);
            bool oracle = OracleSpatialGeometry.Intersects(sample.Body, sample.Request, out SimFix oracleDistance);
            if (actual != expected || actual != oracle || distance != slowDistance || distance != oracleDistance)
                Assert.Fail("Narrow mismatch at " + index + ", group=" + sample.Source + ", kind=" + sample.Request.Kind
                    + ", fast=" + actual + "/" + distance.Raw + ", slow=" + expected + "/" + slowDistance.Raw
                    + ", oracle=" + oracle + "/" + oracleDistance.Raw);
        }

        [Test]
        public void SectorLocalAnglesEdgesAndGuardNeighboursExerciseBothPaths()
        {
            const ulong seed = 0x53454354;
            var random = new A1MathInputs(seed);
            var inputs = new A2SpatialInputs(seed);
            int fast = 0;
            int fallback = 0;
            int hits = 0;
            int[] angles = { -1000, -999, -800, -600, -1, 0, 1, 599, 600, 601, 800, 999, 1000 };
            for (int i = 0; i < 24000; i++)
            {
                long scale = i % 4 == 0 ? (1L << 20) + i % 3 - 1 : 1L << (i % 20);
                long radius = 1 + (long)(random.Next() % 65536);
                long x = (long)(random.Next() % (ulong)(2 * scale + 1)) - scale;
                long z = (long)(random.Next() % (ulong)(2 * scale + 1)) - scale;
                long direction = i % 7 == 0 ? 1L << 30 : 65536;
                var sample = inputs.Make(SpatialQueryKind.Sector, A2SpatialInputs.V(x, i % 5 - 2, z), radius,
                    2 * radius + (i & 1), default, A2SpatialInputs.V(direction, 0, i % 3 == 0 ? -direction : direction),
                    scale, 0, i % 9, angles[i % angles.Length], "local-guard-angle");
                Check(in sample, i);
                if (SpatialSector.TryIntersects(in sample.Body, in sample.Request, out bool hit))
                {
                    fast++;
                    if (hit)
                        hits++;
                }
                else
                    fallback++;
            }
            Assert.That(fast, Is.GreaterThan(10000));
            Assert.That(fallback, Is.GreaterThan(100));
            Assert.That(hits, Is.GreaterThan(1000));
            TestContext.WriteLine("Sector guarded differential: seed=0x{0:X}; cases=24000; fast={1}; fallback={2}; fast_hits={3}; mismatches=0.", seed, fast, fallback, hits);
        }

        [Test]
        public void ThirtySixThousandExactHitsSurviveTheProductPrefilter()
        {
            const ulong seed = 0x4E504630;
            var random = new A1MathInputs(seed);
            int accepted = 0;
            int[] groups = new int[12];
            for (int i = 0; i < 12000; i++)
            {
                DdaCoverageTests.Build(ref random, i, out SpatialBody body, out SpatialRequest ray, out long size);
                for (int transform = 0; transform < 3; transform++)
                {
                    SimVec3 offset = transform == 2 ? A2SpatialInputs.V(size, size, size) : default;
                    var b = new SpatialBody(body.Entity, DdaCoverageTests.Transform(body.Position, transform, offset), body.Collider);
                    SimVec3 direction = transform == 1 ? A2SpatialInputs.V(-ray.Direction.X.Raw, -ray.Direction.Y.Raw, -ray.Direction.Z.Raw) : ray.Direction;
                    var q = new SpatialRequest(SpatialQueryKind.Ray, DdaCoverageTests.Transform(ray.Origin, transform, offset), default, direction, ray.Range, default, 0);
                    Assert.That(ExactRayCapsule.Hits(in b, in q), Is.True, "Exact reference must confirm each generated hit.");
                    Assert.That(SpatialRay.MayHit(in b, in q), Is.True, "False prefilter rejection at " + i + "/" + transform);
                    var sample = new A2SpatialInputs.Sample(b, q, "exact-hit");
                    Check(in sample, i);
                    accepted++;
                    groups[i % 12]++;
                }
            }
            Assert.That(accepted, Is.EqualTo(36000));
            foreach (int count in groups)
                Assert.That(count, Is.EqualTo(3000));
            TestContext.WriteLine("Product prefilter: seed=0x{0:X}; independent exact hits={1}; 12 boundary groups x 3000; tangent/endpoint/zero axes/non-axis/world/odd heights; false_rejections=0.", seed, accepted);
        }

        [Test]
        public void AxisNearSquareBandPreservesScaledRootQuantization()
        {
            var inputs = new A2SpatialInputs(0xA815);
            foreach (long u in new long[] { 32766, 32768, 32770, 40000, 46340 })
                foreach (long speed in new long[] { 1, 2, 65536, 1L << 30 })
                    foreach (long sign in new long[] { -1, 1 })
                    {
                        long radius = u * u / 2 + 1;
                        var sample = inputs.Make(SpatialQueryKind.Ray, A2SpatialInputs.V(sign * 100000, 0, radius - 1),
                            radius, radius * 2, default, A2SpatialInputs.V(sign * speed, 0, 0),
                            0, 100000, 0, 0, "axis-near-square");
                        Check(in sample, (int)u);
                        Assert.That(SpatialGeometry.Intersects(in sample.Body, in sample.Request, out SimFix distance), Is.True);
                        bool fraction = speed * SimFix.Scale >= 2 * u + 1;
                        Assert.That(distance.Raw, Is.EqualTo(100000 - u - (fraction ? 1 : 0)));
                    }
            TestContext.WriteLine("Axis near-square band: 40 cases; section squared=u^2+1; +/- directions; speeds 1/2/65536/2^30; original scaled-floor distances preserved.");
        }

        [Test]
        public void SectorTangenciesAndOneRawNeighboursUseTheFastEdges()
        {
            var inputs = new A2SpatialInputs(0xED6E);
            int hits = 0;
            for (int i = 1; i <= 1000; i++)
                for (int margin = -1; margin <= 1; margin++)
                {
                    long a = i;
                    long k = 1 + i % 127;
                    var sample = inputs.Make(SpatialQueryKind.Sector, A2SpatialInputs.V(3 * a - 4 * k, 0, 4 * a + 3 * k),
                        5 * k + margin, 2 * (5 * k + margin), default, A2SpatialInputs.V(65536, 0, 0),
                        20 * a, 0, 0, 600, "edge-tangent+-1");
                    Check(in sample, i * 3 + margin);
                    Assert.That(SpatialSector.TryIntersects(in sample.Body, in sample.Request, out bool hit), Is.True);
                    if (hit)
                        hits++;
                    Assert.That(hit, Is.EqualTo(margin >= 0));
                }
            Assert.That(hits, Is.EqualTo(2000));
            TestContext.WriteLine("Sector edge tangency: 3000 fast cases; 1000 exact tangents and +/-1 raw; independent and forced-slow comparison; mismatches=0.");
        }

        [Test]
        public void WarmFastAndSlowPredicatesAllocateZeroWithPositiveControl()
        {
            var inputs = new A2SpatialInputs(0xA110C192);
            var samples = new A2SpatialInputs.Sample[600];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = inputs.Random(i % 2 == 0 ? SpatialQueryKind.Ray : SpatialQueryKind.Sector, i);
            long checksum = RunAllocationBatch(samples);
            var meter = new AllocationMeter();
            var windows = new AllocationWindow();
            do
            {
                windows.Begin(meter);
                checksum = unchecked(checksum + RunAllocationBatch(samples));
                windows.End();
            }
            while (windows.NeedsRetry);
            AllocationAssertions.Zero(windows.Result);
            TestContext.WriteLine("Narrow fast+forced-slow allocation: calls=12000; {0}; checksum={1}; meter positive control required.", windows.Result.Describe(), checksum);
        }

        private static long RunAllocationBatch(A2SpatialInputs.Sample[] samples)
        {
            long checksum = 0;
            for (int repeat = 0; repeat < 10; repeat++)
                for (int i = 0; i < samples.Length; i++)
                {
                    ref readonly A2SpatialInputs.Sample sample = ref samples[i];
                    bool fast = SpatialGeometry.Intersects(in sample.Body, in sample.Request, out SimFix a, false);
                    bool slow = SpatialGeometry.Intersects(in sample.Body, in sample.Request, out SimFix b, true);
                    checksum = unchecked(checksum + a.Raw + b.Raw + (fast ? 1 : 0) + (slow ? 1 : 0));
                }
            return checksum;
        }

        [Test]
        public void WidthBoundsHaveTwoSpareBitsAndContractLimitsStayFixed()
        {
            Assert.That(SimVec3.WorldLimitMetres, Is.EqualTo(16384));
            BigInteger w = BigInteger.One << 30;
            Assert.That(51 * BigInteger.Pow(w, 4) < (BigInteger.One << 126), Is.True);
            Assert.That(12 * BigInteger.Pow(w, 4) * (BigInteger.One << 32) < (BigInteger.One << 156), Is.True);
            BigInteger local = BigInteger.One << 20;
            Assert.That(18 * BigInteger.Pow(local, 6) < (BigInteger.One << 125), Is.True);
            Assert.That(338 * BigInteger.Pow(w, 6) < (BigInteger.One << 189), Is.True);
            Assert.That(26 * BigInteger.Pow(w, 4) < (BigInteger.One << 125), Is.True);
            Assert.That(64 * BigInteger.Pow(w, 6) < (BigInteger.One << 187), Is.True);
            TestContext.WriteLine("Narrow width proofs: determinant <126 bits, positive scaled radicand <156, local edge endpoint <125, horizontal range product <189; reassociated 128-bit factor <125, wide factor-of-four <187; margins >=2; world limit tied to guards.");
        }
    }
}
