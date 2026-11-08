using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.TerrainView
{
    // L8a (1)(2): the view's ground sampler answers what Sim's HeightField answers, bit for bit.
    public sealed class ViewGroundTests
    {
        private const long S = SimFix.Scale;

        private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        // Two points per cell, strictly inside triangle A ((u, v) = (5/8, 1/8)) and B ((1/8, 5/8)).
        private static (int Samples, int Diffs) Compare(WcMap map)
        {
            ViewGround ground = ViewGround.For(ViewMap.Parse(WcMapWriter.Write(map)));
            HeightField field = L8aFixtures.Field(map);
            int samples = 0, diffs = 0;
            for (int z = 0; z < (int)map.Depth; z++)
                for (int x = 0; x < (int)map.Width; x++)
                    foreach ((long u, long v) in new[] { (5 * S / 8, S / 8), (S / 8, 5 * S / 8) })
                    {
                        long xr = map.Min.X.Raw + x * S + u, zr = map.Min.Z.Raw + z * S + v;
                        Assert.That(field.TryHeightAt(SimFix.FromRaw(xr), SimFix.FromRaw(zr), out SimFix want), Is.True);
                        bool inside = ground.TryHeightRaw(xr, zr, out long got);
                        samples++;
                        if (!inside || got != want.Raw) diffs++;
                    }
            return (samples, diffs);
        }

        [Test]
        public void HandFixture_EverySample_MatchesHeightField()
        {
            (int samples, int diffs) = Compare(L8aFixtures.Hand());
            Assert.That(samples, Is.EqualTo(12));
            Assert.That(diffs, Is.EqualTo(0));
        }

        [Test]
        public void Checker64_EverySample_MatchesHeightField()
        {
            (int samples, int diffs) = Compare(TerrainCheckerMap.Build(64));
            Assert.That(samples, Is.EqualTo(8192));
            Assert.That(diffs, Is.EqualTo(0));
        }

        // Cell (2,0) of the hand fixture, point A: h00 = -3, h10 = 0, h11 = 0; exact value -3 + 3 * 5/8 = -1.125 raw,
        // floored toward negative infinity to -2 (truncation would give -1).
        [Test]
        public void HandFixture_NegativeFraction_FloorsToMinusTwo()
        {
            ViewGround ground = ViewGround.For(ViewMap.Parse(WcMapWriter.Write(L8aFixtures.Hand())));
            Assert.That(ground.TryHeightRaw(-2 * S + 2 * S + 5 * S / 8, 3 * S + S / 8, out long y), Is.True);
            Assert.That(y, Is.EqualTo(-2));
        }

        // Checker centres lie on the diagonal (u = v), half A: h = h00 + (h10 - h00) / 2 + (h11 - h10) / 2 = (h00 + h11) / 2
        // = h00, and h00 = 32768 * ((i + j) mod 2) - 16384: cell (0,0) -16384 raw = -0.25 m, cell (1,0) +16384 raw = 0.25 m.
        [Test]
        public void Checker_CellCentres_AreExactQuarterMetres()
        {
            ViewGround ground = ViewGround.For(ViewMap.Parse(TerrainCheckerMap.Bytes(8)));
            Assert.That(ground.TryHeightRaw(S / 2, S / 2, out long a), Is.True);
            Assert.That(a, Is.EqualTo(-16384));
            Assert.That(ground.TryHeightRaw(S + S / 2, S / 2, out long b), Is.True);
            Assert.That(b, Is.EqualTo(16384));
            Assert.That(Bits(ground.HeightAt(0.5f, 0.5f, 9f)), Is.EqualTo(Bits(-0.25f)));
            Assert.That(Bits(ground.HeightAt(1.5f, 0.5f, 9f)), Is.EqualTo(Bits(0.25f)));
        }

        // A point of the hand fixture away from the world origin (Min = (-2, 3)): HeightAt converts once, raw / 65536f.
        [Test]
        public void HandFixture_HeightAt_UsesTheMapOrigin()
        {
            ViewGround ground = ViewGround.For(ViewMap.Parse(WcMapWriter.Write(L8aFixtures.Hand())));
            // (-1.375, 3.125) is cell (0,0) at (u, v) = (5/8, 1/8): h00 = 0, h10 = -16384, h11 = 32768, A half:
            // 0 + (-16384) * 5/8 + (32768 + 16384) * 1/8 = -10240 + 6144 = -4096 raw.
            Assert.That(ground.TryHeightRaw((long)(-1.375 * S), (long)(3.125 * S), out long y), Is.True);
            Assert.That(y, Is.EqualTo(-4096));
            float h = ground.HeightAt(-1.375f, 3.125f, 9f);
            Assert.That(Bits(h), Is.EqualTo(Bits(-4096f / 65536f)));
        }

        [Test]
        public void OutsideTheField_IsFalse()
        {
            ViewGround ground = ViewGround.For(ViewMap.Parse(WcMapWriter.Write(L8aFixtures.Hand())));
            Assert.That(ground.TryHeightRaw(-2 * S - 1, 3 * S + S / 2, out _), Is.False);
            Assert.That(ground.TryHeightRaw(-2 * S + 3 * S, 3 * S + S / 2, out _), Is.False);
            Assert.That(ground.TryHeightRaw(-2 * S, 3 * S, out _), Is.True);
            Assert.That(ground.HeightAt(-7f, 3.5f, -7f), Is.EqualTo(-7f));
        }

        [Test]
        public void FlatMap_HasNoGround()
        {
            Assert.That(ViewGround.For(ViewMap.Parse(DemoRecipes.M7MapBytes())), Is.Null);
        }
    }
}
