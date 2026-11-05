using System;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("NarrowPhase")]
    public sealed class Geometry192Tests
    {
        private static BigInteger Value(GeometryUInt192 value)
            => value.Low + ((BigInteger)value.Middle << 64) + ((BigInteger)value.High << 128);
        private static BigInteger Value(FixedUInt128 value) => value.Low + ((BigInteger)value.High << 64);
        private static GeometryUInt192 From(BigInteger value)
            => new GeometryUInt192((ulong)(value & ulong.MaxValue), (ulong)((value >> 64) & ulong.MaxValue), (ulong)(value >> 128));

        private static void CheckRoot(BigInteger n)
        {
            BigInteger r = Value(From(n).Sqrt());
            Assert.That(r * r <= n && (r + 1) * (r + 1) > n, Is.True, "192-bit floor root: " + n);
        }

        [Test]
        public void SquareRootDivisionAndCarriesMatchIndependentIntegerInequalities()
        {
            const ulong seed = 0x1925A001;
            var random = new A1MathInputs(seed);
            for (int bit = 0; bit < 192; bit++)
                for (int offset = -1; offset <= 1; offset++)
                    CheckRoot((BigInteger.One << bit) + offset);
            CheckRoot((BigInteger.One << 192) - 1);
            for (int i = 0; i < 20000; i++)
            {
                var n = new GeometryUInt192(random.Next(), random.Next(), random.Next());
                CheckRoot(Value(n));
                var d = new FixedUInt128(random.Next() | 1, i % 3 == 0 ? 0 : random.Next());
                Assert.That(Value(n.Divide(d)), Is.EqualTo(Value(n) / Value(d)));
                var r = new FixedUInt128(random.Next(), random.Next() & uint.MaxValue);
                Assert.That(Value(GeometryUInt192.Square(r)), Is.EqualTo(Value(r) * Value(r)));
                var s = new FixedUInt128(random.Next(), random.Next() & uint.MaxValue);
                Assert.That(Value(GeometryUInt192.Multiply(r, s)), Is.EqualTo(Value(r) * Value(s)));
                Assert.That(Value(GeometryUInt192.Scale32(d)), Is.EqualTo(Value(d) << 32));
                ulong factor = random.Next();
                Assert.That(Value(GeometryUInt192.Multiply(d, factor)), Is.EqualTo(Value(d) * factor));
                BigInteger scalable = Value(n) >> 2;
                Assert.That(Value(From(scalable).TimesFour()), Is.EqualTo(scalable * 4));
            }
            Assert.Throws<SimBoundaryException>(() => new GeometryUInt192(1).Divide(default));
            Assert.Throws<SimBoundaryException>(() => GeometryUInt192.Square(new FixedUInt128(0, 1UL << 32)));
            Assert.Throws<SimBoundaryException>(() => GeometryUInt192.Multiply(new FixedUInt128(0, 1UL << 32), new FixedUInt128(1)));
            Assert.Throws<SimBoundaryException>(() => new GeometryUInt192(0, 0, 1UL << 62).TimesFour());
            Assert.Throws<SimBoundaryException>(() => (GeometryInt288.From(long.MaxValue) * GeometryInt288.From(long.MaxValue)
                * GeometryInt288.From(8)).Magnitude128());
            TestContext.WriteLine("Geometry192: seed=0x{0:X}; 20000 full-width random roots/divisions/squares/shifts/products/scales + 577 power neighbours; independent BigInteger inequalities; zero mismatches; explicit boundary failures checked.", seed);
        }
    }
}
