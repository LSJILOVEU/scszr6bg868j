using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed2Fixtures;

namespace WarCore.Tests
{
    // ED-2 acceptance 2 and 3: falloff and the height brushes worked out by hand, and their symmetry.
    public sealed class Ed2BrushTests
    {
        // 100 i + 10 j on a 4 x 4 map (5 x 5 points).
        private static MapDesc Tilted() => Map(4, 4, (i, j) => 100 * i + 10 * j);

        [Test]
        public void TheRadiusThreeFalloffTable()
        {
            int[,] table =
            {
                { 0, 0, 0, 0, 0, 0, 0 },
                { 0, 12, 202, 316, 202, 12, 0 },
                { 0, 202, 619, 809, 619, 202, 0 },
                { 0, 316, 809, 1024, 809, 316, 0 },
                { 0, 202, 619, 809, 619, 202, 0 },
                { 0, 12, 202, 316, 202, 12, 0 },
                { 0, 0, 0, 0, 0, 0, 0 },
            };
            for (int dj = -3; dj <= 3; dj++)
                for (int di = -3; di <= 3; di++)
                    Assert.That(MapBrush.Weight(3, di, dj), Is.EqualTo(table[dj + 3, di + 3]), di + "," + dj);
            Assert.That(MapBrush.Weight(1, 0, 0), Is.EqualTo(1024));
            Assert.That(MapBrush.Weight(1, 1, 0), Is.EqualTo(0));
            Assert.That(MapBrush.Weight(64, 64, 0), Is.EqualTo(0));
            Assert.That(MapBrush.Weight(64, 63, 0), Is.EqualTo(1024 * 127L * 127 / (4096L * 4096)));
        }

        [Test]
        public void RaiseByHand()
        {
            // Radius 2: weights 1024 (centre), 576 (d^2 = 1), 256 (d^2 = 2); amount -333 gives -333, -187, -83 (toward zero).
            Same(W(1, 1, 110 - 83, 2, 1, 210 - 187, 3, 1, 310 - 83,
                   1, 2, 120 - 187, 2, 2, 220 - 333, 3, 2, 320 - 187,
                   1, 3, 130 - 83, 2, 3, 230 - 187, 3, 3, 330 - 83), MapBrush.Raise(Tilted(), 2, 2, 2, -333));
            Same(W(1, 1, 110 + 250, 2, 1, 210 + 562, 3, 1, 310 + 250,
                   1, 2, 120 + 562, 2, 2, 220 + 1000, 3, 2, 320 + 562,
                   1, 3, 130 + 250, 2, 3, 230 + 562, 3, 3, 330 + 250), MapBrush.Raise(Tilted(), 2, 2, 2, 1000));
        }

        [Test]
        public void FlattenByHand()
        {
            // Target 200: h + (200 - h) w / 1024 toward zero.
            Same(W(1, 1, 132, 2, 1, 205, 3, 1, 283,
                   1, 2, 165, 2, 2, 200, 3, 2, 253,
                   1, 3, 147, 2, 3, 214, 3, 3, 298), MapBrush.Flatten(Tilted(), 2, 2, 2, 200));
        }

        [Test]
        public void SmoothByHand()
        {
            // A bump of 1000 at (2, 2) on the tilted map: means 376, 276, 476, 366, 386; the corners' means equal their heights.
            MapDesc d = Map(4, 4, (i, j) => i == 2 && j == 2 ? 1000 : 100 * i + 10 * j);
            Same(W(2, 1, 297, 1, 2, 207, 2, 2, 376, 3, 2, 407, 2, 3, 317), MapBrush.Smooth(d, 2, 2, 2));
        }

        [Test]
        public void SmoothTakesTheFloorOfTheMeanAndOnlyOnMapNeighbours()
        {
            // Corner (0, 0): (0 - 1 - 1) / 3 floors to -1; toward zero would be 0.
            MapDesc d = Map(2, 2, (i, j) => (i == 1 && j == 0) || (i == 0 && j == 1) ? -1 : 0);
            Same(W(0, 0, -1), MapBrush.Smooth(d, 0, 0, 1));
            // Edge (1, 0): (5 + 0 + 0 + 0) / 4 = 1.
            MapDesc e = Map(2, 2, (i, j) => i == 1 && j == 0 ? 5 : 0);
            Same(W(1, 0, 1), MapBrush.Smooth(e, 1, 0, 1));
        }

        [Test]
        public void RampByHand()
        {
            // (1, 2) at 0 to (5, 2) at 3001, half width 1: t = 4 (i - 1), L^2 = 16, ramp(t) = 0, 750, 1500, 2250, 3001 on rows
            // 1..3. The largest change is 3001, so the skirt is 1 point: rows 0 and 4 (c^2 = 64 <= 2^2 L^2) want ramp(t), the
            // points within 2 of an end beyond it want the end's height. Points already at their wanted height are not written.
            var up = new List<int>();
            for (int j = 0; j <= 4; j++)
            {
                up.AddRange(new[] { 2, j, 750, 3, j, 1500, 4, j, 2250, 5, j, 3001 });
                if (j >= 1 && j <= 3) up.AddRange(new[] { 6, j, 3001 });
                if (j == 2) up.AddRange(new[] { 7, j, 3001 });
            }
            Same(W(up.ToArray()), MapBrush.Ramp(Flat(8, 4), 1, 2, 0, 5, 2, 3001, 1, 3, 4));
            // Downhill rounds toward negative infinity: 3001 + floor(-750.25) = 2250.
            var down = new List<int>();
            for (int j = 0; j <= 4; j++)
            {
                if (j >= 1 && j <= 3) down.AddRange(new[] { 0, j, 3001 });
                down.AddRange(new[] { 1, j, 3001, 2, j, 2250, 3, j, 1500, 4, j, 750 });
            }
            Same(W(down.ToArray()), MapBrush.Ramp(Flat(8, 4), 1, 2, 3001, 5, 2, 0, 1, 3, 4));
            // Half width 0: the points on the segment, and a one-point skirt.
            Same(W(1, 1, 1000, 2, 1, 2000, 1, 2, 1000, 2, 2, 2000, 3, 2, 2000, 1, 3, 1000, 2, 3, 2000),
                MapBrush.Ramp(Flat(4, 4), 0, 2, 0, 2, 2, 2000, 0, 3, 4));
        }

        [Test]
        public void ADiagonalRampsBand()
        {
            // (0, 0) to (2, 2) at 800, half width 1: on the ramp when |i - j| <= 1 and i + j <= 4, wanted 200 (i + j);
            // (2, 0) is in the skirt (c^2 = 16 <= 2^2 L^2) and wants ramp(4) = 400.
            var at = new Dictionary<(int, int), int>();
            foreach (MapHeightWrite w in MapBrush.Ramp(Flat(4, 4), 0, 0, 0, 2, 2, 800, 1, 3, 4)) at[(w.I, w.J)] = w.Raw;
            foreach ((int i, int j) in new[] { (1, 0), (0, 1), (1, 1), (2, 1), (1, 2), (2, 2) })
                Assert.That(at[(i, j)], Is.EqualTo(200 * (i + j)), i + "," + j);
            Assert.That(at[(2, 0)], Is.EqualTo(400));
            Assert.That(at.ContainsKey((0, 0)), Is.False);
        }

        [Test]
        public void BankByHand()
        {
            // (1, 3) to (5, 3), half width 3: c = -4 (j - 3), B = 144; row 2 (c^2 = 16) weight 910, 500 * 910 / 1024 = 444;
            // row 1 (c^2 = 64) weight 568, 277; row 0 (c^2 = 144) weight 0.
            var expected = new List<int>();
            for (int i = 1; i <= 5; i++) expected.AddRange(new[] { i, 1, 277 });
            for (int i = 1; i <= 5; i++) expected.AddRange(new[] { i, 2, 444 });
            Same(W(expected.ToArray()), MapBrush.Bank(Flat(8, 6), 1, 3, 5, 3, 3, 500));
            // Reversed, the band is the other side: rows 4 and 5, toward zero (-444.3 is -444).
            expected.Clear();
            for (int i = 1; i <= 5; i++) expected.AddRange(new[] { i, 4, -444 });
            for (int i = 1; i <= 5; i++) expected.AddRange(new[] { i, 5, -277 });
            Same(W(expected.ToArray()), MapBrush.Bank(Flat(8, 6), 5, 3, 1, 3, 3, -500));
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(17)]
        public void BrushesOnAFlatMapAreSymmetric(int radius)
        {
            const int c = 20;
            MapDesc flat = Map(2 * c, 2 * c, (i, j) => 5000);
            var brushes = new Func<MapHeightWrite[]>[]
            {
                () => MapBrush.Raise(flat, c, c, radius, 777),
                () => MapBrush.Raise(flat, c, c, radius, -777),
                () => MapBrush.Flatten(flat, c, c, radius, 1234),
                () => MapBrush.Smooth(Map(2 * c, 2 * c, (i, j) => i == c && j == c ? 9000 : 5000), c, c, radius),
            };
            foreach (Func<MapHeightWrite[]> brush in brushes)
            {
                var at = new Dictionary<(int, int), int>();
                foreach (MapHeightWrite w in brush()) at[(w.I, w.J)] = w.Raw;
                Assert.That(at.Count, Is.GreaterThan(0));
                foreach (KeyValuePair<(int, int), int> p in at)
                {
                    int di = p.Key.Item1 - c, dj = p.Key.Item2 - c;
                    Assert.That(at[(c - di, c + dj)], Is.EqualTo(p.Value), "x mirror");
                    Assert.That(at[(c + di, c - dj)], Is.EqualTo(p.Value), "z mirror");
                    Assert.That(at[(c + dj, c + di)], Is.EqualTo(p.Value), "transpose");
                }
            }
            MapHeightWrite[] up = MapBrush.Raise(flat, c, c, radius, 777), down = MapBrush.Raise(flat, c, c, radius, -777);
            Assert.That(down.Length, Is.EqualTo(up.Length));
            for (int n = 0; n < up.Length; n++)
            {
                Assert.That((down[n].I, down[n].J), Is.EqualTo((up[n].I, up[n].J)));
                Assert.That(down[n].Raw - 5000, Is.EqualTo(-(up[n].Raw - 5000)));
            }
        }

        [Test]
        public void OnlyChangedPointsAreWritten()
        {
            // Flattening to the height the map already has writes nothing; a tiny raise skips points whose step rounds to 0.
            Assert.That(MapBrush.Flatten(Flat(8, 8), 4, 4, 3, 0), Is.Empty);
            MapHeightWrite[] tiny = MapBrush.Raise(Flat(8, 8), 4, 4, 3, 1);
            Same(W(4, 4, 1), tiny);
        }

        [Test]
        public void CentresOffTheMapWriteOnlyOnMapPoints()
        {
            MapHeightWrite[] w = MapBrush.Raise(Flat(4, 4), -2, -2, 4, 1000);
            Assert.That(w.Length, Is.GreaterThan(0));
            foreach (MapHeightWrite p in w) Assert.That(p.I >= 0 && p.J >= 0 && p.I <= 4 && p.J <= 4, Is.True);
            Assert.That(MapBrush.Raise(Flat(4, 4), -100, 2, 4, 1000), Is.Empty);
            // Rows 1 and 3 are the one-point skirt.
            MapHeightWrite[] ramp = MapBrush.Ramp(Flat(4, 4), -10, 2, 0, 10, 2, 2000, 0, 3, 4);
            var expected = new List<int>();
            for (int j = 1; j <= 3; j++)
                for (int i = 0; i <= 4; i++) expected.AddRange(new[] { i, j, 1000 + 100 * i });
            Same(W(expected.ToArray()), ramp);
            Assert.That(MapBrush.SurfaceSquare(Flat(4, 4), 10, 10, 6, "mud").Length, Is.EqualTo(0));
        }

        [Test]
        public void ArgumentsOutOfRangeAreRefused()
        {
            MapDesc d = Flat(8, 8);
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Weight(0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Weight(65, 0, 0));
            Assert.DoesNotThrow(() => MapBrush.Raise(d, 4, 4, 64, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Raise(d, 4, 4, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Raise(d, 4, 4, 65, 1));
            Assert.DoesNotThrow(() => MapBrush.Raise(d, 4, 4, 1, (1 << 29) - 1));
            Assert.DoesNotThrow(() => MapBrush.Raise(d, 4, 4, 1, -(1 << 29) + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Raise(d, 4, 4, 1, 1 << 29));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Raise(d, 4, 4, 1, -(1 << 29)));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Flatten(d, 4, 4, 1, 1 << 29));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Smooth(d, 4, 4, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Bank(d, 0, 0, 4, 0, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Bank(d, 0, 0, 4, 0, 1, 1 << 29));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Ramp(d, 0, 0, 0, 4, 0, 0, 65, 3, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Ramp(d, 0, 0, 0, 4, 0, 0, -1, 3, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Ramp(d, 0, 0, 0, 4, 0, 300000, 1, 3, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Ramp(d, 0, 0, -300000, 4, 0, 0, 1, 3, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Ramp(d, 0, 0, 0, 4, 0, 0, 1, 3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Ramp(d, 0, 0, 0, 4, 0, 0, 1, 9, 4));
            Assert.DoesNotThrow(() => MapBrush.Ramp(d, -1024, 0, 0, 8 + 1024, 8 + 1024, 0, 1, 3, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Ramp(d, -1025, 0, 0, 4, 0, 0, 1, 3, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Ramp(d, 0, 0, 0, 8 + 1025, 0, 0, 1, 3, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Bank(d, 0, -1025, 4, 0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.Bank(d, 0, 0, 4, 8 + 1025, 1, 1));
            Assert.Throws<ArgumentException>(() => MapBrush.Ramp(d, 2, 2, 0, 2, 2, 0, 1, 3, 4));
            Assert.Throws<ArgumentException>(() => MapBrush.Bank(d, 2, 2, 2, 2, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.SurfaceCircle(d, 0, 0, -1, "mud"));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapBrush.SurfaceSquare(d, 0, 0, 65, "mud"));
            Assert.Throws<ArgumentNullException>(() => MapBrush.Raise(null, 0, 0, 1, 1));
        }

        [Test]
        public void HeightBrushesNeedHeights()
        {
            MapDesc none = MapNew.Desc(new MapNewSpec { Id = "x", Pack = Pack, Width = 4, Depth = 4, CeilingRaw = 1000, Surface = "grass" });
            Assert.Throws<ArgumentException>(() => MapBrush.Raise(none, 1, 1, 1, 1));
            Assert.Throws<ArgumentException>(() => MapBrush.Flatten(none, 1, 1, 1, 1));
            Assert.Throws<ArgumentException>(() => MapBrush.Smooth(none, 1, 1, 1));
            Assert.Throws<ArgumentException>(() => MapBrush.Ramp(none, 0, 0, 0, 2, 0, 0, 1, 3, 4));
            Assert.Throws<ArgumentException>(() => MapBrush.Bank(none, 0, 0, 2, 0, 1, 1));
        }
    }
}
