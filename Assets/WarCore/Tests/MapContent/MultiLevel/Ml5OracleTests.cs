using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.Ml5Plates;

namespace WarCore.Tests
{
    // ML-5 acceptance 4: 20000 seeded segments over random 12 x 8 maps (1-6 flat or sloped plates up to 2/1, holes,
    // overlapping bodies, origins on both sides of zero and near the world's edge) against Ml5Oracle: blocked or not, plate,
    // t* and the floored point all equal.
    public sealed class Ml5OracleTests
    {
        private const int W = 12, D = 8, PerSeed = 5000, PerMap = 50;
        private static readonly int[] Seeds = { 5101, 5102, 5103, 5104 };

        private sealed class Counts { internal int Total, Blocked, TZero, Corner, Tie, Vertical, Zero, Big, BigThrough; }

        [Test]
        public void MatchesTheOracle()
        {
            var counts = new Counts();
            var watch = Stopwatch.StartNew();
            foreach (int seed in Seeds) RunSeed(seed, counts);
            watch.Stop();
            TestContext.WriteLine("segments " + counts.Total + ", blocked " + counts.Blocked + ", t*=0 " + counts.TZero + ", corner " + counts.Corner
                + ", tie " + counts.Tie + ", vertical " + counts.Vertical + ", zero " + counts.Zero + ", big " + counts.Big + " (through " + counts.BigThrough
                + "), " + watch.ElapsedMilliseconds + " ms");
            Assert.That(counts.Total, Is.EqualTo(Seeds.Length * PerSeed));
            Assert.That(counts.Blocked * 4, Is.GreaterThanOrEqualTo(counts.Total), "at least a quarter blocked");
            Assert.That(new[] { counts.TZero, counts.Corner, counts.Tie, counts.Vertical, counts.Zero }, Has.All.GreaterThanOrEqualTo(50));
            Assert.That(counts.Big * 20, Is.GreaterThanOrEqualTo(counts.Total), "at least 5% with ends at 2^31..2^32");
            Assert.That(counts.BigThrough * 2, Is.GreaterThanOrEqualTo(counts.Big), "half of them cross the map");
            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(60000));
        }

        private static void RunSeed(int seed, Counts counts)
        {
            var rnd = new Random(seed);
            for (int m = 0; m < PerSeed / PerMap; m++)
            {
                bool snapped = m % 2 == 1, far = m % 10 == 3;
                long ox = Origin(rnd, far, W), oz = Origin(rnd, far, D);
                List<PlateSlab> plates = Plates(rnd, snapped, out long low, out long high);
                var field = new PlateField(W, D, SimFix.FromRaw(ox), SimFix.FromRaw(oz), plates);
                for (int n = 0; n < PerMap; n++)
                {
                    Segment(rnd, snapped, ox, oz, low, high, plates[0], counts, out long[] a, out long[] b);
                    Compare(field, ox, oz, plates, a, b, counts);
                }
            }
        }

        private static long Origin(Random rnd, bool far, int size)
        {
            long limit = SimVec3.WorldLimitMetres;
            if (!far) return rnd.Next(-20, 21) * S;
            return rnd.Next(2) == 0 ? (-limit + rnd.Next(0, 4)) * S : (limit - size - rnd.Next(0, 4)) * S;
        }

        private static long Pick(Random rnd, bool snapped, long lo, long hi)
        {
            if (snapped) return (S / 2) * (lo / (S / 2) + rnd.Next(0, (int)((hi - lo) / (S / 2)) + 1));
            return lo + (long)(rnd.NextDouble() * (hi - lo + 1));
        }

        private static List<PlateSlab> Plates(Random rnd, bool snapped, out long low, out long high)
        {
            var plates = new List<PlateSlab>();
            low = long.MaxValue; high = long.MinValue;
            int count = rnd.Next(1, 7);
            ulong id = 0;
            int x = 0, z = 0, w = 1, d = 1, kind = 0;
            long top = 0, step = 0;
            for (int k = 0; k < count; k++)
            {
                id += (ulong)rnd.Next(1, 4);
                long thick = Pick(rnd, snapped, S / 2, 2 * S);
                // On snapped maps a plate may repeat the previous one's footprint and top (a different thickness): the two
                // bodies overlap and meet a segment at the same t.
                if (k == 0 || !snapped || rnd.Next(3) != 0)
                {
                    x = rnd.Next(0, W - 1); z = rnd.Next(0, D - 1); w = rnd.Next(2, Math.Min(8, W - x) + 1); d = rnd.Next(2, Math.Min(6, D - z) + 1);
                    top = Pick(rnd, snapped, -2 * S, 6 * S);
                    kind = rnd.Next(3);
                    step = kind == 0 ? 0 : Pick(rnd, snapped, -2 * S, 2 * S);
                }
                var holes = new List<(int, int)>();
                for (int j = 0; j < d; j++)
                    for (int i = 0; i < w; i++)
                        if (rnd.Next(100) < 15) holes.Add((i, j));
                long[] along = new long[(kind == 2 ? d : w) + 1];
                for (int i = 0; i < along.Length; i++) along[i] = top + step * i;
                PlateSlab p = kind == 0 ? Flat(id, x, z, w, d, top, thick, holes.ToArray())
                    : kind == 1 ? AlongX(id, x, z, w, d, along, thick, holes.ToArray()) : AlongZ(id, x, z, w, d, along, thick, holes.ToArray());
                plates.Add(p);
                foreach (long h in along) { low = Math.Min(low, h - thick); high = Math.Max(high, h); }
            }
            return plates;
        }

        private static void Segment(Random rnd, bool snapped, long ox, long oz, long low, long high, PlateSlab focus, Counts counts, out long[] a, out long[] b)
        {
            int kind = rnd.Next(100);
            long X() => Pick(rnd, snapped, ox - 2 * S, ox + W * S + 2 * S);
            long Z() => Pick(rnd, snapped, oz - 2 * S, oz + D * S + 2 * S);
            long Y() => Pick(rnd, snapped, low - S, high + S);
            if (kind < 3)
            {
                long x = X(), z = Z();
                a = new[] { x, Y(), z }; b = new[] { x, Y(), z };
                counts.Vertical++;
            }
            else if (kind < 4)
            {
                a = new[] { X(), Y(), Z() }; b = (long[])a.Clone();
                counts.Zero++;
            }
            else if (kind < 10)
            {
                // Through a grid corner with one axis rising and the other falling: the corner cell is met at t = 1/2.
                long cx = ox + rnd.Next(1, W) * S, cz = oz + rnd.Next(1, D) * S;
                long vx = Pick(rnd, snapped, S / 2, 3 * S), vz = Pick(rnd, snapped, S / 2, 3 * S), y = Y();
                if (rnd.Next(2) == 0) vx = -vx; else vz = -vz;
                a = new[] { cx - vx, y, cz - vz }; b = new[] { cx + vx, Y(), cz + vz };
                counts.Corner++;
            }
            else if (kind < 16)
            {
                counts.Big++;
                long Far() => (rnd.Next(2) == 0 ? -1 : 1) * ((1L << 31) + (long)(rnd.NextDouble() * ((1L << 31) - (1L << 30))));
                if (rnd.Next(3) != 0)
                {
                    // Both ends far out on opposite sides of a point of the map: the segment crosses it, and the end on
                    // the point's side is at 2^31..2^32 on every axis.
                    long[] p = { X(), Y(), Z() };
                    a = new long[3]; b = new long[3];
                    for (int i = 0; i < 3; i++)
                    {
                        long sign = p[i] < 0 ? -1 : 1, v = (1L << 31) + (long)(rnd.NextDouble() * ((1L << 32) - Math.Abs(p[i]) - (1L << 31)));
                        a[i] = p[i] + sign * v; b[i] = p[i] - sign * v;
                    }
                    counts.BigThrough++;
                }
                else { a = new[] { Far(), Far(), Far() }; b = new[] { Far(), Far(), Far() }; }
            }
            else
            {
                // The end lands inside the first plate's footprint half of the time, so that enough segments are blocked.
                a = new[] { X(), Y(), Z() };
                b = rnd.Next(2) == 0 ? new[] { X(), Y(), Z() }
                    : new[] { Pick(rnd, snapped, ox + focus.X * S, ox + (focus.X + focus.Width) * S - 1), Y(), Pick(rnd, snapped, oz + focus.Z * S, oz + (focus.Z + focus.Depth) * S - 1) };
            }
            counts.Total++;
        }

        private static void Compare(PlateField field, long ox, long oz, List<PlateSlab> plates, long[] a, long[] b, Counts counts)
        {
            Ml5Oracle.Answer want = Ml5Oracle.Solve(ox, oz, plates, a[0], a[1], a[2], b[0], b[1], b[2]);
            bool got = field.TryFirstThrough(V(a[0], a[1], a[2]), V(b[0], b[1], b[2]), out PlateHit hit);
            string at = "segment " + string.Join(",", a) + " -> " + string.Join(",", b) + " origin " + ox + "," + oz;
            Assert.That(got, Is.EqualTo(want.Found), at);
            if (!want.Found) return;
            counts.Blocked++;
            if (want.Num.IsZero) counts.TZero++;
            if (want.Tie) counts.Tie++;
            Assert.That(hit.PlateId, Is.EqualTo(want.Id), at);
            Assert.That(new BigInteger(hit.TNum) * want.Den, Is.EqualTo(want.Num * hit.TDen), at);
            Assert.That(hit.At, Is.EqualTo(V(want.X, want.Y, want.Z)), at);
        }
    }
}
