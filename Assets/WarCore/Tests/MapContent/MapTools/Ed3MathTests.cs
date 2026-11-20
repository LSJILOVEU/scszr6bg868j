using System;
using System.Numerics;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed3Fixtures;

namespace WarCore.Tests
{
    // ED-3a acceptance 3: the integer pieces worked out by hand (falloffs, polyline distance, mesa, noise, patch).
    public sealed class Ed3MathTests
    {
        [Test]
        public void DiscFalloffRadiusThreeTable()
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
                    Assert.That(MapTerrainMath.Weight(3, di * di + dj * dj), Is.EqualTo(table[dj + 3, di + 3]), di + "," + dj);
        }

        [Test]
        public void DiscFalloffEqualsTheBrushUpToRadiusSixtyFour()
        {
            var random = new Random(31);
            for (int n = 0; n < 200; n++)
            {
                int r = random.Next(1, 65), di = random.Next(-r - 2, r + 3), dj = random.Next(-r - 2, r + 3);
                Assert.That(MapTerrainMath.Weight(r, (long)di * di + (long)dj * dj), Is.EqualTo(MapBrush.Weight(r, di, dj)), r + " " + di + " " + dj);
            }
        }

        [Test]
        public void DiscFalloffRadiusTwoHundred()
        {
            Assert.That(MapTerrainMath.Weight(200, 0), Is.EqualTo(1024));
            Assert.That(MapTerrainMath.Weight(200, 100 * 100), Is.EqualTo(576));
            Assert.That(MapTerrainMath.Weight(200, 190 * 190), Is.EqualTo(9));
            Assert.That(MapTerrainMath.Weight(200, 199 * 199), Is.EqualTo(0));
            Assert.That(MapTerrainMath.Weight(200, 200 * 200), Is.EqualTo(0));
            Assert.That(MapTerrainMath.Weight(256, 255 * 255), Is.EqualTo(0));
            Assert.That(MapTerrainMath.Weight(256, 200 * 200), Is.EqualTo(1024L * 25536 * 25536 / (65536L * 65536)));
        }

        private static readonly MapXZ[] Segment = { new MapXZ(0, 0), new MapXZ(10, 0) };
        private static readonly MapXZ[] Corner = { new MapXZ(0, 0), new MapXZ(10, 0), new MapXZ(10, 10) };

        private static long Wl(MapXZ[] line, int x, int z, int w) => MapTerrainMath.LineWeight(MapTerrainMath.Near(line, 1, x, z), w);

        [Test]
        public void BandFalloffByHand()
        {
            Assert.That(Wl(Segment, 5, 0, 4), Is.EqualTo(1024));
            Assert.That(Wl(Segment, 5, 2, 4), Is.EqualTo(576));
            Assert.That(Wl(Segment, 5, -2, 4), Is.EqualTo(576));
            Assert.That(Wl(Segment, 5, 4, 4), Is.EqualTo(0));
            Assert.That(Wl(Segment, -2, 0, 4), Is.EqualTo(576));
            Assert.That(Wl(Segment, 12, 1, 4), Is.EqualTo(484));
            // Two steps: (1024 * 1599 / 1600) * 1599 / 1600 = 1023 * 1599 / 1600 = 1022; one step would give 1022.7 -> 1022 too, so
            // also a point where they differ: q = 7, B = 16 at (-3, 0): (1024 * 7 / 16) * 7 / 16 = 448 * 7 / 16 = 196 (one step 196).
            Assert.That(Wl(Segment, -3, 0, 4), Is.EqualTo(196));
            // (5, 1), w = 3: B = 900, q = 800: 1024 * 800 / 900 = 910, 910 * 800 / 900 = 808 (one step: 809).
            Assert.That(Wl(Segment, 5, 1, 3), Is.EqualTo(808));
        }

        [Test]
        public void CornerTakesTheNearerSegmentAndTheEarlierOnATie()
        {
            MapLineNear a = MapTerrainMath.Near(Corner, 1, 8, 3);
            Assert.That(a.Seg, Is.EqualTo(1));
            Assert.That(a.Num, Is.EqualTo(400)); Assert.That(a.Den, Is.EqualTo(100)); Assert.That(a.T, Is.EqualTo(30));
            MapLineNear tie = MapTerrainMath.Near(Corner, 1, 8, 2);
            Assert.That(tie.Seg, Is.EqualTo(0));
            MapLineNear outside = MapTerrainMath.Near(Corner, 1, 12, 0);
            Assert.That(outside.Seg, Is.EqualTo(0));
            Assert.That(outside.Num, Is.EqualTo(4)); Assert.That(outside.Den, Is.EqualTo(1)); Assert.That(outside.T, Is.EqualTo(120));
            MapLineNear past = MapTerrainMath.Near(Corner, 1, 12, 13);
            Assert.That(past.Seg, Is.EqualTo(1));
            Assert.That(past.Num, Is.EqualTo(13)); Assert.That(past.Den, Is.EqualTo(1));
            MapLineNear doubled = MapTerrainMath.Near(Segment, 2, 11, 3);
            Assert.That(doubled.Num, Is.EqualTo(3600)); Assert.That(doubled.Den, Is.EqualTo(400)); Assert.That(doubled.T, Is.EqualTo(220));
        }

        [Test]
        public void CloserComparesFractionsExactlyWithoutOverflow()
        {
            var random = new Random(7);
            for (int n = 0; n < 2000; n++)
            {
                long da = random.Next(1, 1 << 26), db = random.Next(1, 1 << 26);
                long na = (long)(random.NextDouble() * (1L << 52)), nb = n % 3 == 0 ? na / da * db + random.Next(0, (int)Math.Min(db, int.MaxValue)) : (long)(random.NextDouble() * (1L << 52));
                var a = new MapLineNear { Num = na, Den = da };
                var b = new MapLineNear { Num = nb, Den = db };
                bool expected = new BigInteger(na) * db < new BigInteger(nb) * da;
                Assert.That(MapTerrainMath.Closer(a, b), Is.EqualTo(expected), na + "/" + da + " " + nb + "/" + db);
            }
            Assert.That(MapTerrainMath.Closer(new MapLineNear { Num = 250, Den = 100 }, new MapLineNear { Num = 7, Den = 3 }), Is.False);
            Assert.That(MapTerrainMath.Closer(new MapLineNear { Num = 7, Den = 3 }, new MapLineNear { Num = 250, Den = 100 }), Is.True);
            Assert.That(MapTerrainMath.Closer(new MapLineNear { Num = 4, Den = 1 }, new MapLineNear { Num = 400, Den = 100 }), Is.False);
        }

        [Test]
        public void BandMembership()
        {
            Assert.That(MapTerrainMath.InBand(MapTerrainMath.Near(Segment, 1, 5, 4), 4), Is.True);
            Assert.That(MapTerrainMath.InBand(MapTerrainMath.Near(Segment, 1, 5, 5), 4), Is.False);
            Assert.That(MapTerrainMath.InBand(MapTerrainMath.Near(Segment, 1, -4, 0), 4), Is.True);
            Assert.That(MapTerrainMath.InBand(MapTerrainMath.Near(Segment, 1, -3, 3), 4), Is.False);
        }

        [Test]
        public void MesaTopIsTheCentreBeforeItPlusHeightAndOutsideIsUnchanged()
        {
            MapDesc d = Map(40, 40);
            MapDesc hill = Run(d, Gen(40, 40, Hill(20, 20, 40, 10000)));
            MapDesc both = Run(d, Gen(40, 40, Join(Hill(20, 20, 40, 10000), Mesa(22, 20, 5, 100))));
            long top = H(hill, 22, 20) + Raw(100);
            Assert.That(top, Is.EqualTo(651520 + 6553));
            for (int j = 0; j <= 40; j++)
                for (int i = 0; i <= 40; i++)
                {
                    long d2 = (i - 22L) * (i - 22) + (j - 20L) * (j - 20);
                    Assert.That(H(both, i, j), Is.EqualTo(d2 <= 25 ? top : H(hill, i, j)), i + "," + j);
                }
            // A centre off the map reads the nearest on-map point.
            MapDesc edge = Run(d, Gen(40, 40, Join(Hill(0, 20, 40, 10000), Mesa(-3, 20, 4, 1000))));
            Assert.That(H(edge, 0, 20), Is.EqualTo(Raw(10000) + Raw(1000)));
        }

        [Test]
        public void HashVectorsFromAnIndependentScript()
        {
            Assert.That(MapNoise.Hash(1, 0, 0, 0), Is.EqualTo(6238072747940578789UL));
            Assert.That(MapNoise.Hash(7, 3, -5, 2), Is.EqualTo(2626826968296484108UL));
            Assert.That(MapNoise.Hash(18446744073709551615UL, -1024, 1536, 513), Is.EqualTo(1095270648660591333UL));
        }

        [Test]
        public void PickReachesBothEnds()
        {
            bool low = false, high = false;
            for (int n = 0; n < 200; n++)
            {
                long v = MapNoise.Pick(9, 2, n, 0, -3, 3);
                Assert.That(v, Is.InRange(-3, 3));
                low |= v == -3; high |= v == 3;
                Assert.That(MapNoise.Pick(9, 2, n, 0, 5, 5), Is.EqualTo(5));
            }
            Assert.That(low && high, Is.True);
            Assert.That(MapNoise.Pick(7, 3, -5, 2, 10, 19), Is.EqualTo(10 + (long)(2626826968296484108UL % 10)));
        }

        private static int Count(MapDesc d, string name)
        {
            int n = 0;
            for (int z = 0; z < d.Size.Z; z++)
                for (int x = 0; x < d.Size.X; x++)
                    if (Surface(d, x, z) == name) n++;
            return n;
        }

        [Test]
        public void SmoothPatchIsTheDoubledCircle()
        {
            MapDesc d = Map(20, 20);
            // r = 1 around (5, 5): centres (5.5 +- 1) within 1 m: only the 4 cells touching the point.
            MapDesc one = Run(d, Gen(20, 20, Patch(5, 5, 1, "mud", 0)));
            Assert.That(Count(one, "mud"), Is.EqualTo(4));
            Assert.That(Surface(one, 4, 4), Is.EqualTo("mud")); Assert.That(Surface(one, 5, 5), Is.EqualTo("mud"));
            Assert.That(Surface(one, 6, 5), Is.EqualTo("grass"));
            // r = 3: centres with (2x + 1 - 10)^2 + (2z + 1 - 10)^2 <= 36: 32 cells.
            MapDesc three = Run(d, Gen(20, 20, Patch(5, 5, 3, "mud", 0)));
            Assert.That(Count(three, "mud"), Is.EqualTo(32));
            for (int z = 0; z < 20; z++)
                for (int x = 0; x < 20; x++)
                {
                    long ax = 2 * x + 1 - 10, az = 2 * z + 1 - 10;
                    Assert.That(Surface(three, x, z), Is.EqualTo(ax * ax + az * az <= 36 ? "mud" : "grass"), x + "," + z);
                }
        }

        [Test]
        public void RoughPatchFollowsTheSeed()
        {
            MapDesc d = Map(40, 40);
            string a = Gen(40, 40, Patch(20, 20, 10, "mud", 512), seed: 11), b = Gen(40, 40, Patch(20, 20, 10, "mud", 512), seed: 12);
            MapDesc ra = Run(d, a), rb = Run(d, b);
            Assert.That(MapDescJson.Write(Run(d, a)), Is.EqualTo(MapDescJson.Write(ra)));
            // Counts from the independent script (log, rough.py): 320 and 325.
            Assert.That(Count(ra, "mud"), Is.EqualTo(320));
            Assert.That(Count(rb, "mud"), Is.EqualTo(325));
            Assert.That(RoughCount(11), Is.EqualTo(320));
            Assert.That(MapDescJson.Write(ra), Is.Not.EqualTo(MapDescJson.Write(rb)));
            Assert.That(Count(ra, "mud"), Is.Not.EqualTo(Count(Run(d, Gen(40, 40, Patch(20, 20, 10, "mud", 0))), "mud")));
        }

        // The rule written out: ((2x + 1 - 40)^2 + (2z + 1 - 40)^2) 1024 <= 400 (1024 + j), j = Pick(seed, 0, x, z, -512, 512).
        private static int RoughCount(ulong seed)
        {
            int n = 0;
            for (int z = 0; z < 40; z++)
                for (int x = 0; x < 40; x++)
                {
                    long ax = 2 * x + 1 - 40, az = 2 * z + 1 - 40;
                    long j = MapNoise.Pick(seed, 0, x, z, -512, 512);
                    if ((ax * ax + az * az) * 1024 <= 400 * (1024 + j)) n++;
                }
            return n;
        }
    }
}
