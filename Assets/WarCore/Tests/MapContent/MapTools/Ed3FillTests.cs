using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed3Fixtures;

namespace WarCore.Tests
{
    // ED-3a acceptance 4: the slope fill and the fixed-point settle against brute force, and the step bound L.
    public sealed class Ed3FillTests
    {
        private const int N = 17;

        private static long[] Field(Random random, long spread)
        {
            var t = new long[N * N];
            for (int p = 0; p < t.Length; p++) t[p] = (long)(random.NextDouble() * spread) - spread / 2;
            return t;
        }

        private static int D1(int p, int q) => Math.Abs(p % N - q % N) + Math.Abs(p / N - q / N);

        // max over q of (t(q) - L |p - q|_1), O(N^2).
        private static long[] Brute(long[] t)
        {
            var r = new long[t.Length];
            for (int p = 0; p < t.Length; p++)
            {
                long best = long.MinValue;
                for (int q = 0; q < t.Length; q++) best = Math.Max(best, t[q] - L * D1(p, q));
                r[p] = best;
            }
            return r;
        }

        [Test]
        public void StepBoundIsTheLargestLegalOne()
        {
            Assert.That(MapTerrainMath.L, Is.EqualTo(92681));
            Assert.That(MapTerrain.MaxStep, Is.EqualTo(92681));
            Assert.That(2 * L * L, Is.LessThanOrEqualTo(4 * S * S));
            Assert.That(2 * (L + 1) * (L + 1), Is.GreaterThan(4 * S * S));
            Assert.That(MapBrush.CellWithin(0, L, L, 2 * L, 2, 1), Is.True);
            Assert.That(MapBrush.CellWithin(0, L + 1, L + 1, 2 * L + 2, 2, 1), Is.False);
        }

        [Test]
        public void TwoPassFillEqualsBruteForce()
        {
            var random = new Random(20);
            for (int round = 0; round < 20; round++)
            {
                long[] t = Field(random, round % 2 == 0 ? 2_000_000 : 400_000);
                long[] start = (long[])t.Clone();
                MapTerrainMath.Fill(t, N, N);
                Assert.That(t, Is.EqualTo(Brute(start)), "round " + round);
                long max = long.MinValue;
                foreach (long v in start) max = Math.Max(max, v);
                for (int p = 0; p < t.Length; p++)
                {
                    Assert.That(t[p], Is.GreaterThanOrEqualTo(start[p]));
                    Assert.That(t[p], Is.LessThanOrEqualTo(max));
                    if (p % N + 1 < N) Assert.That(Math.Abs(t[p + 1] - t[p]), Is.LessThanOrEqualTo(L));
                    if (p + N < t.Length) Assert.That(Math.Abs(t[p + N] - t[p]), Is.LessThanOrEqualTo(L));
                }
                long[] again = (long[])t.Clone();
                MapTerrainMath.Fill(again, N, N);
                Assert.That(again, Is.EqualTo(t));
            }
        }

        [Test]
        public void FillUsesBothDirections()
        {
            // A peak in the middle: points before it in scan order only rise in the backward pass.
            var t = new long[N * N];
            t[8 * N + 8] = 1_000_000;
            MapTerrainMath.Fill(t, N, N);
            Assert.That(t[0], Is.EqualTo(0));
            Assert.That(t[8 * N + 7], Is.EqualTo(1_000_000 - L));
            Assert.That(t[7 * N + 8], Is.EqualTo(1_000_000 - L));
            Assert.That(t[8 * N + 9], Is.EqualTo(1_000_000 - L));
            Assert.That(t[6 * N + 7], Is.EqualTo(1_000_000 - 3 * L));
        }

        [Test]
        public void StepOfExactlyLStaysAndOneMoreIsRaised()
        {
            var t = new long[] { 0, L, 2 * L, 0, 0, 0 };
            MapTerrainMath.Fill(t, 3, 2);
            Assert.That(t, Is.EqualTo(new long[] { 0, L, 2 * L, 0, 0, L }));
            var u = new long[] { 0, L + 1, 0, 0 };
            MapTerrainMath.Fill(u, 2, 2);
            Assert.That(u, Is.EqualTo(new long[] { 1, L + 1, 0, 1 }));
        }

        // Lo, Up by brute force; clamp the other points into [Lo, Up]; then their max envelope over every point.
        private static long[] BruteSettle(long[] t, bool[] fixedPoint)
        {
            var c = (long[])t.Clone();
            for (int p = 0; p < t.Length; p++)
            {
                if (fixedPoint[p]) continue;
                long lo = long.MinValue, up = long.MaxValue;
                for (int f = 0; f < t.Length; f++)
                {
                    if (!fixedPoint[f]) continue;
                    lo = Math.Max(lo, t[f] - L * D1(p, f));
                    up = Math.Min(up, t[f] + L * D1(p, f));
                }
                c[p] = Math.Min(Math.Max(t[p], lo), up);
            }
            long[] filled = Brute(c);
            for (int p = 0; p < t.Length; p++)
                if (fixedPoint[p]) filled[p] = t[p];
            return filled;
        }

        [Test]
        public void SettleEqualsBruteForceAndKeepsFixedPoints()
        {
            var random = new Random(44);
            for (int round = 0; round < 20; round++)
            {
                long[] smooth = Field(random, 3_000_000);
                MapTerrainMath.Fill(smooth, N, N);
                long[] t = Field(random, 3_000_000);
                var fixedBy = new int[t.Length];
                var fixedPoint = new bool[t.Length];
                for (int p = 0; p < t.Length; p++)
                {
                    fixedBy[p] = -1;
                    if (random.Next(5) == 0) { fixedPoint[p] = true; fixedBy[p] = random.Next(3); t[p] = smooth[p]; }
                }
                long[] expected = BruteSettle(t, fixedPoint);
                MapTerrain.Settle(t, fixedBy, N, N);
                Assert.That(t, Is.EqualTo(expected), "round " + round);
                for (int p = 0; p < t.Length; p++)
                {
                    if (fixedPoint[p]) Assert.That(t[p], Is.EqualTo(smooth[p]));
                    if (p % N + 1 < N) Assert.That(Math.Abs(t[p + 1] - t[p]), Is.LessThanOrEqualTo(L));
                    if (p + N < t.Length) Assert.That(Math.Abs(t[p + N] - t[p]), Is.LessThanOrEqualTo(L));
                }
            }
        }

        [Test]
        public void SettleClampsDownToTheUpperEnvelope()
        {
            // One fixed point at 0 in a field at 10 L: the others come down to L times their distance.
            var t = new long[9];
            var fixedBy = new int[9];
            for (int p = 0; p < 9; p++) { t[p] = 10 * L; fixedBy[p] = -1; }
            t[0] = 0; fixedBy[0] = 4;
            MapTerrain.Settle(t, fixedBy, 3, 3);
            Assert.That(t, Is.EqualTo(new long[] { 0, L, 2 * L, L, 2 * L, 3 * L, 2 * L, 3 * L, 4 * L }));
        }

        [Test]
        public void SettleLiftsUpToTheLowerEnvelope()
        {
            var t = new long[] { 0, 0, 0, 0, 0, 0, 0, 0, 5 * L };
            var fixedBy = new int[] { -1, -1, -1, -1, -1, -1, -1, -1, 2 };
            MapTerrain.Settle(t, fixedBy, 3, 3);
            Assert.That(t, Is.EqualTo(new long[] { L, 2 * L, 3 * L, 2 * L, 3 * L, 4 * L, 3 * L, 4 * L, 5 * L }));
        }

        [Test]
        public void IncompatibleFixedPointsNameBothStamps()
        {
            var t = new long[] { 0, 0, 0, 0, 0, 3 * L + 1 };
            var fixedBy = new int[] { 2, -1, -1, -1, -1, 7 };
            ArgumentException e = Assert.Throws<ArgumentException>(() => MapTerrain.Settle(t, fixedBy, 3, 2));
            Assert.That(e.Message, Does.Contain("2 and 7"));
            var same = new long[] { 0, 0, 3 * L };
            ArgumentException self = Assert.Throws<ArgumentException>(() => MapTerrain.Settle(same, new[] { 5, -1, 5 }, 3, 1));
            Assert.That(self.Message, Does.Contain("road stamp 5 does not fit with itself"));
            // Exactly L per metre apart is compatible.
            var ok = new long[] { 0, 0, 0, 0, 0, 3 * L };
            Assert.DoesNotThrow(() => MapTerrain.Settle(ok, new[] { 1, -1, -1, -1, -1, 2 }, 3, 2));
            Assert.That(ok, Is.EqualTo(new long[] { 0, L, 2 * L, L, 2 * L, 3 * L }));
        }
    }
}
