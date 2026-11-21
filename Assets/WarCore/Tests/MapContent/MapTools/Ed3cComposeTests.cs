using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed3Fixtures;
using static WarCore.Tests.Ed3cFixtures;

namespace WarCore.Tests
{
    // ED-3c-1 acceptance A5 to A9: the base layer (bilinear, two floors), its parameters, the window, the support matrix,
    // and the later steps (clamp, fill, stamps) working on top of it. Every composition that should succeed runs inside
    // Assert.DoesNotThrow (RunB), so a mutation that throws is a failed assertion.
    public sealed class Ed3cComposeTests
    {
        private static void At(MapDesc d, int i, int j, long expected) => Assert.That(H(d, i, j), Is.EqualTo(expected), i + "," + j);

        // Every point equals the reference (no clamp or fill reached in these cases).
        private static void MatchesReference(MapDesc d, int[][] h, int baseMm = 0, long x0 = 0, long z0 = 0, long scale = 10000,
            long exag = 1000, long datum = 0)
        {
            for (int j = 0; j <= d.Size.Z; j++)
                for (int i = 0; i <= d.Size.X; i++)
                    Assert.That(H(d, i, j), Is.EqualTo(Reference(h, 30000, i, j, baseMm, x0, z0, scale, exag, datum)), i + "," + j);
        }

        [Test]
        public void BilinearPointsOnASixBySixMap()
        {
            MapDesc d = RunB(Map(6, 6), GenB(6, 6), Tile);
            At(d, 0, 0, 0); At(d, 1, 0, 65536); At(d, 3, 0, 196608); At(d, 6, 0, 294912); At(d, 0, 1, 19660);
            At(d, 1, 1, 74271); At(d, 3, 3, 157286); At(d, 4, 5, 288358); At(d, 5, 4, 321126); At(d, 6, 6, 353894);
            MatchesReference(d, T);
        }

        [Test]
        public void ExaggerationScalesHeights()
        {
            MapDesc half = RunB(Map(6, 6), GenB(6, 6, exag: 500), Tile);
            At(half, 1, 0, 32768); At(half, 1, 1, 37135);
            MatchesReference(half, T, exag: 500);
            MapDesc more = RunB(Map(6, 6), GenB(6, 6, exag: 1100), Tile);
            At(more, 1, 0, 72089); At(more, 1, 1, 81699);
            MatchesReference(more, T, exag: 1100);
        }

        [Test]
        public void DatumAndBaseMmShiftHeights()
        {
            MapDesc datum = RunB(Map(6, 6), GenB(6, 6, datum: 10000), Tile);
            At(datum, 0, 0, -65536); At(datum, 0, 1, -45876); At(datum, 6, 6, 288358);
            MapDesc up = RunB(Map(6, 6), GenB(6, 6, baseMm: "500"), Tile);
            At(up, 0, 0, 32768); At(up, 1, 1, 107039);
            MapDesc down = RunB(Map(6, 6), GenB(6, 6, baseMm: "-500"), Tile);
            At(down, 0, 0, -32768); At(down, 0, 1, -13108);
            MatchesReference(down, T, baseMm: -500);
            MapDesc both = RunB(Map(6, 6), GenB(6, 6, baseMm: "500", exag: 500), Tile);
            At(both, 0, 0, 32768); At(both, 1, 1, 69903);
            MatchesReference(both, T, baseMm: 500, exag: 500);
        }

        [Test]
        public void OffsetsMoveTheWindow()
        {
            MapDesc x = RunB(Map(4, 6), GenB(4, 6, x0: 15000), Tile);
            At(x, 0, 0, 98304); At(x, 4, 0, 278528);
            MatchesReference(x, T, x0: 15000);
            MapDesc z = RunB(Map(6, 4), GenB(6, 4, z0: 15000), Tile);
            At(z, 0, 0, 29491); At(z, 0, 4, 173670);
            MatchesReference(z, T, z0: 15000);
        }

        [Test]
        public void ScaleChangesSpacingAndHeight()
        {
            MapDesc fine = RunB(Map(12, 12), GenB(12, 12, scale: 5000), Tile);
            At(fine, 1, 1, 79731); At(fine, 12, 12, 707788);
            MatchesReference(fine, T, scale: 5000);
            MapDesc coarse = RunB(Map(3, 3), GenB(3, 3, scale: 20000), Tile);
            At(coarse, 1, 1, 63350); At(coarse, 3, 3, 176947);
            MatchesReference(coarse, T, scale: 20000);
        }

        [Test]
        public void MixedParametersAndNegativeTile()
        {
            MapDesc mixed = RunB(Map(8, 8), GenB(8, 8, scale: 7000, exag: 1100, datum: 1234), Tile);
            At(mixed, 0, 0, -12709); At(mixed, 3, 2, 196351); At(mixed, 8, 8, 534348);
            MatchesReference(mixed, T, scale: 7000, exag: 1100, datum: 1234);
            int[][] low = Shift(T, -60000);
            MapDesc neg = RunB(Map(6, 6), GenB(6, 6, sha: FpMinus60000), Dem(low));
            At(neg, 1, 1, -318945); At(neg, 2, 1, -264333);
            MatchesReference(neg, low);
        }

        [Test]
        public void WindowMustStayInsideTheTile()
        {
            MapDesc edge = RunB(Map(6, 6), GenB(6, 6), Tile);
            At(edge, 6, 6, 353894);
            RefusesB(Map(7, 6), GenB(7, 6), Tile, "x");
            RefusesB(Map(6, 7), GenB(6, 7), Tile, "z");
            ArgumentException x = Assert.Throws<ArgumentException>(() => ComposeB(Map(6, 7), GenB(6, 7), Tile));
            Assert.That(x.Message, Does.Not.Contain("along x"));
            RefusesB(Map(6, 6), GenB(6, 6, x0: 1), Tile, "x");
            RefusesB(Map(6, 6), GenB(6, 6, z0: 1), Tile, "z");
            RunB(Map(5, 6), GenB(5, 6, x0: 10000), Tile);
            RunB(Map(6, 5), GenB(6, 5, z0: 10000), Tile);
            RefusesB(Map(5, 6), GenB(5, 6, x0: 10001), Tile, "x");
            RefusesB(Map(6, 5), GenB(6, 5, z0: 10001), Tile, "z");
        }

        // A 4-column, 3-row tile: the x window uses the columns and the z window the rows.
        [Test]
        public void NonSquareTile()
        {
            int[][] w = { new[] { 0, 30000, 45000, 15000 }, new[] { 9000, 24000, 60000, 33000 }, new[] { 30000, 45000, 54000, 21000 } };
            const string sha = "6b884253a0d4890f8b43863765a5a1da6d108d58e209dd33daf04bc33e69256b";
            MapDesc d = RunB(Map(9, 6), GenB(9, 6, sha: sha), Dem(w));
            At(d, 9, 0, 98304); At(d, 9, 6, 137625); At(d, 8, 3, 275251);
            MatchesReference(d, w);
            RefusesB(Map(10, 6), GenB(10, 6, sha: sha), Dem(w), "x");
            RefusesB(Map(9, 7), GenB(9, 7, sha: sha), Dem(w), "z");
        }

        [Test]
        public void SupportMatrix()
        {
            MapDesc plain = Map(6, 6);
            MapStampPlan old = Compose(plain, Gen(6, 6, Hill(3, 3, 2, 800)));
            MapStampPlan same = Ok(() => ComposeB(plain, Gen(6, 6, Hill(3, 3, 2, 800)), null));
            Assert.That(same.Heights.Count, Is.EqualTo(old.Heights.Count));
            for (int p = 0; p < old.Heights.Count; p++) Assert.That(same.Heights[p].Raw, Is.EqualTo(old.Heights[p].Raw), "point " + p);
            RefusesB(plain, Gen(6, 6, ""), Tile, "dem");
            RefusesB(plain, GenB(6, 6), null, "dem");
            RefusesB(plain, GenB(6, 6, sha: Fp24001), Tile, "sha256");
            RefusesB(plain, GenB(6, 6), Dem(T).Replace("24000", "24001"), "sha256");
            RefusesB(Map(7, 6), GenB(7, 6), Tile, "x");
            RunB(plain, GenB(6, 6), Tile);
            MapDesc flat = Map(6, 6, heights: false);
            RefusesB(flat, GenB(6, 6), Tile, "heights");
            Assert.Throws<ArgumentException>(() => MapGenSpec.Read(WithKey(Gen(6, 6, "", baseMm: null), BaseKey())));
        }

        [Test]
        public void ClampStillApplies()
        {
            MapDesc d = RunB(Map(6, 6, ceiling: 131072), GenB(6, 6), Tile);
            At(d, 6, 6, 131072); At(d, 0, 0, 0); At(d, 1, 0, 65536);
            for (int j = 0; j <= 6; j++)
                for (int i = 0; i <= 6; i++) Assert.That(H(d, i, j), Is.LessThanOrEqualTo(131072), i + "," + j);
        }

        [Test]
        public void FillStillApplies()
        {
            MapDesc d = RunB(Map(6, 6), GenB(6, 6, exag: 2000), Tile);
            AllLegal(d);
            At(d, 6, 3, 786432); At(d, 6, 6, 707788);
            long top = long.MinValue;
            bool raised = false;
            for (int j = 0; j <= 6; j++)
                for (int i = 0; i <= 6; i++)
                {
                    long before = Reference(T, 30000, i, j, exag: 2000);
                    Assert.That(H(d, i, j), Is.GreaterThanOrEqualTo(before), i + "," + j);
                    raised |= H(d, i, j) > before;
                    top = Math.Max(top, H(d, i, j));
                }
            Assert.That(top, Is.EqualTo(786432));
            Assert.That(raised, Is.True, "the fill raised no point, so this case does not test it");
        }

        [Test]
        public void StampsAddOnTop()
        {
            MapDesc baseOnly = RunB(Map(6, 6), GenB(6, 6, exag: 500), Tile);
            MapDesc hillOnly = Run(Map(6, 6), Gen(6, 6, Hill(3, 3, 3, 500)));
            MapDesc both = RunB(Map(6, 6), GenB(6, 6, Hill(3, 3, 3, 500), exag: 500), Tile);
            Assert.That(H(hillOnly, 3, 3), Is.EqualTo(32768));
            for (int j = 0; j <= 6; j++)
                for (int i = 0; i <= 6; i++)
                    Assert.That(H(both, i, j), Is.EqualTo(H(baseOnly, i, j) + H(hillOnly, i, j)), i + "," + j);
        }
    }
}
