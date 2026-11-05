using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("PartitionPerf")]
    public sealed class PartitionBoundsTests
    {
        [Test]
        public void ScalarRootAndWideFallbackKeepExactFloorAcrossTheGuard()
        {
            // Read the loaded product constant so a separately compiled mutation cannot be hidden by inlining.
            BigInteger limit = (ulong)typeof(FixedUInt128).GetField(nameof(FixedUInt128.ScalarRootLimit),
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetRawConstantValue();
            Assert.That(limit <= (BigInteger.One << 62), Is.True);
            Assert.That(5 * (limit >> 4) < (BigInteger.One << 61), Is.True);
            var random = new A1MathInputs(0x51005A7UL);
            BigInteger[] edges = { 0, 1, 2, 3, limit - 1, limit, limit + 1,
                (BigInteger.One << 64) - 1, BigInteger.One << 64, (BigInteger.One << 64) + 1 };
            foreach (BigInteger value in edges)
                Root(value);
            for (int i = 0; i < 20000; i++)
            {
                ulong seed = random.Next();
                BigInteger root = seed & uint.MaxValue;
                BigInteger value = i % 2 == 0 ? (BigInteger)seed : root * root + i % 3 - 1;
                if (value.Sign >= 0)
                    Root(value);
            }
            TestContext.WriteLine("Partition root reference: seed=0x51005A7; 20000 random/square-boundary values plus 10 fixed edges; exact floor inequalities; mismatches=0.");
        }

        private static void Root(BigInteger value)
        {
            var number = new FixedUInt128((ulong)(value & ulong.MaxValue), (ulong)(value >> 64));
            BigInteger root = number.Sqrt();
            Assert.That(root * root <= value && (root + 1) * (root + 1) > value, Is.True, "Integer floor square root of " + value);
        }

        [Test]
        public void ScalarSlabGuardLeavesThreeSpareBitsAndRejectsEveryOversizedOperand()
        {
            long limit = (long)(ulong)typeof(SpatialBounds).GetField(nameof(SpatialBounds.ScalarSlabLimit),
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetRawConstantValue();
            BigInteger maximumProduct = 2 * (BigInteger)limit * limit;
            Assert.That(maximumProduct < (BigInteger.One << 60), Is.True, "Signed long must retain three spare magnitude bits.");
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var values = new long[] { limit * sign, limit * sign, limit * sign, limit, limit, limit * sign, limit * sign, limit * sign };
                Assert.That(Eligible(values), Is.True);
                for (int i = 0; i < values.Length; i++)
                {
                    long old = values[i];
                    values[i] = old + (old < 0 ? -1 : 1);
                    Assert.That(Eligible(values), Is.False, "Oversized operand " + i);
                    values[i] = old;
                }
            }
        }

        private static bool Eligible(long[] v)
        {
            var direction = V(v[5], v[6], v[7]);
            return SpatialBounds.CanUseScalarSlab(v[0], v[1], v[2], v[3], v[4], in direction);
        }

        [Test]
        public void LocalAndWideSlabsMatchBigIntegerAtBranchEdgesAndClosedContacts()
        {
            const long w = 1L << 30;
            const long l = 1L << 29;
            long[] edges = { -w, -l - 1, -l, -l + 1, -1, 0, 1, l - 1, l, l + 1, w };
            long[] radii = { 0, 1, l - 1, l, l + 1, w };
            var random = new A1MathInputs(0x51AB2026UL);
            int admitted = 0;
            int rejected = 0;
            int scalar = 0;
            int wide = 0;
            for (int i = 0; i < 12000; i++)
            {
                var center = V(Pick(edges, ref random), Pick(edges, ref random), Pick(edges, ref random));
                var origin = V(Pick(edges, ref random), Pick(edges, ref random), Pick(edges, ref random));
                long radius = radii[random.Next() % (ulong)radii.Length];
                var direction = V(i % 2 == 0 ? l : -l, Pick(edges, ref random), Pick(edges, ref random));
                if (i % 3 == 0)
                {
                    center = SimVec3.Metres(i % 400, i % 5, i % 11);
                    origin = SimVec3.Metres(i % 400 - 4, i % 3, i % 7);
                    radius = i % 8 * SimFix.Scale;
                    direction = SimVec3.Metres(1, 0, i % 7 - 3);
                }
                var body = new SpatialBody(new EntityId(0, 1), center, new Collider(SimFix.FromRaw(radius), SimFix.FromRaw(2 * radius)));
                SpatialRequest request = SpatialQueries.Ray(origin, direction, SimFix.FromRaw(w));
                bool expected = Reference(in body, in request);
                bool actual = SpatialBounds.RayMayHit(in body, in request);
                Assert.That(actual, Is.EqualTo(expected), "Boundary slab case " + i);
                if (actual)
                    admitted++;
                else
                    rejected++;
                if (SpatialBounds.CanUseScalarSlab(center.X.Raw - origin.X.Raw, center.Y.Raw - origin.Y.Raw,
                    center.Z.Raw - origin.Z.Raw, radius, radius, in direction))
                    scalar++;
                else
                    wide++;
            }
            var point = new SpatialBody(new EntityId(0, 1), SimVec3.Metres(2, 0, 2), default);
            var corner = SpatialQueries.Ray(default, SimVec3.Metres(1, 0, 1), SimFix.FromInt(4));
            Assert.That(SpatialBounds.RayMayHit(in point, in corner), Is.True, "A closed zero-width contact cannot be discarded.");
            Assert.That(admitted, Is.GreaterThan(0));
            Assert.That(rejected, Is.GreaterThan(0));
            Assert.That(scalar, Is.GreaterThanOrEqualTo(4000));
            Assert.That(wide, Is.GreaterThan(4000));
            TestContext.WriteLine("Partition slab reference: seed=0x51AB2026; cases=12000; admitted={0}; rejected={1}; scalarEligible={2}; wide={3}; mismatches=0.", admitted, rejected, scalar, wide);
        }

        private static long Pick(long[] values, ref A1MathInputs random) => values[random.Next() % (ulong)values.Length];
        private static SimVec3 V(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));

        private static bool Reference(in SpatialBody body, in SpatialRequest q)
        {
            long r = body.Collider.Radius.Raw;
            long h = body.Collider.Height.Raw / 2;
            long x = body.Position.X.Raw;
            long y = body.Position.Y.Raw;
            long z = body.Position.Z.Raw;
            if (x - r > q.Maximum.X.Raw || x + r < q.Minimum.X.Raw || y - h > q.Maximum.Y.Raw || y + h < q.Minimum.Y.Raw
                || z - r > q.Maximum.Z.Raw || z + r < q.Minimum.Z.Raw)
                return false;
            BigInteger enterN = 0;
            BigInteger enterD = 1;
            BigInteger exitN = 1;
            BigInteger exitD = 0;
            return Axis(x - r - q.Origin.X.Raw, x + r - q.Origin.X.Raw, q.Direction.X.Raw, ref enterN, ref enterD, ref exitN, ref exitD)
                && Axis(y - h - q.Origin.Y.Raw, y + h - q.Origin.Y.Raw, q.Direction.Y.Raw, ref enterN, ref enterD, ref exitN, ref exitD)
                && Axis(z - r - q.Origin.Z.Raw, z + r - q.Origin.Z.Raw, q.Direction.Z.Raw, ref enterN, ref enterD, ref exitN, ref exitD);
        }

        private static bool Axis(BigInteger low, BigInteger high, BigInteger direction,
            ref BigInteger enterN, ref BigInteger enterD, ref BigInteger exitN, ref BigInteger exitD)
        {
            if (direction.IsZero)
                return low <= 0 && high >= 0;
            BigInteger near = direction.Sign > 0 ? low : -high;
            BigInteger far = direction.Sign > 0 ? high : -low;
            BigInteger denominator = BigInteger.Abs(direction);
            if (far.Sign < 0)
                return false;
            if (near * enterD > enterN * denominator)
            {
                enterN = near;
                enterD = denominator;
            }
            if (exitD.IsZero || far * exitD < exitN * denominator)
            {
                exitN = far;
                exitD = denominator;
            }
            return enterN * exitD <= exitN * enterD;
        }
    }
}
