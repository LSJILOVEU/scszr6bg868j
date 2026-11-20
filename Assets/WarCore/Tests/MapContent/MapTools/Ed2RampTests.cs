using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Ed2Fixtures;

namespace WarCore.Tests
{
    // ED-2 acceptance 6: ramps are walkable, or refused.
    public sealed class Ed2RampTests
    {
        // Every cell with all four corners on the ramp, at the heights after the writes, within num / den.
        private static void RampWalkable(MapDesc d, int i0, int j0, int i1, int j1, int halfWidth, long num, long den)
        {
            long dx = i1 - i0, dz = j1 - j0, l2 = dx * dx + dz * dz, band = (long)halfWidth * halfWidth * l2;
            Func<int, int, bool> on = (i, j) =>
            {
                long t = (i - i0) * dx + (j - j0) * dz, c = (i - i0) * dz - (j - j0) * dx;
                return t >= 0 && t <= l2 && c * c <= band;
            };
            int cells = 0;
            for (int cz = 0; cz < d.Size.Z; cz++)
                for (int cx = 0; cx < d.Size.X; cx++)
                {
                    if (!on(cx, cz) || !on(cx + 1, cz) || !on(cx, cz + 1) || !on(cx + 1, cz + 1)) continue;
                    cells++;
                    Assert.That(MapBrush.CellWithin(d.Heights[cz][cx], d.Heights[cz][cx + 1], d.Heights[cz + 1][cx], d.Heights[cz + 1][cx + 1], num, den),
                        Is.True, "cell " + cx + "," + cz);
                }
            Assert.That(cells, Is.GreaterThan(0));
        }

        [Test]
        public void ATenMetreRampOnFlatGroundFitsAndIsWalkable()
        {
            // 0 to 10 m over 20 m, half width 2: the skirt (10 points) gives the sides room to slope down.
            MapDesc d = Map(30, 30, (i, j) => 0, 0, 20 * S);
            MapDesc after = Apply(d, MapBrush.Ramp(d, 2, 15, 0, 22, 15, (int)(10 * S), 2, 3, 4));
            AllLegal(after);
            RampWalkable(after, 2, 15, 22, 15, 2, 3, 4);
            for (int i = 2; i <= 22; i++) Assert.That(after.Heights[15][i], Is.EqualTo((10 * S * (i - 2)) / 20), "i " + i);
            Assert.DoesNotThrow(() => MapBake.Run(MapDescJson.Write(after)));
        }

        [Test]
        public void AHundredMetreRampFitsWithinThePassLimit()
        {
            // 0 to 100 m over 220 m: the skirt is capped at 64 points, enough for 2/1 sides, and Legalize finishes.
            MapDesc d = Map(240, 160, (i, j) => 0, 0, 120 * S);
            MapDesc after = Apply(d, MapBrush.Ramp(d, 10, 80, 0, 230, 80, (int)(100 * S), 1, 3, 4));
            Assert.That(MapBrush.LastPasses, Is.LessThan(MapBrush.MaxPasses));
            Assert.That(after.Heights[80][230], Is.EqualTo(100 * S));
            RampWalkable(after, 10, 80, 230, 80, 1, 3, 4);
        }

        [Test]
        public void ADiagonalRampCutIntoAHillsideIsWalkable()
        {
            // A hillside rising 1/2 along j; a ramp across it from (4, 4) to (20, 16).
            MapDesc d = Map(24, 24, (i, j) => (int)(S / 2 * j), 0, 20 * S);
            int h0 = d.Heights[4][4], h1 = d.Heights[16][20] + (int)(2 * S);
            MapDesc after = Apply(d, MapBrush.Ramp(d, 4, 4, h0, 20, 16, h1, 1, 3, 4));
            AllLegal(after);
            RampWalkable(after, 4, 4, 20, 16, 1, 3, 4);
        }

        [Test]
        public void ARampExactlyAtTheWalkableSlopeIsAllowedAndOneRawMoreIsNot()
        {
            // Along i over 4 m: 3/4 m per metre is 49152 raw, at the limit; 196612 / 4 per metre floors to 49153 at t = 4.
            MapDesc d = Map(10, 12, (i, j) => 0, 0, 4 * S);
            MapDesc after = Apply(d, MapBrush.Ramp(d, 1, 6, 0, 5, 6, 196608, 1, 3, 4));
            RampWalkable(after, 1, 6, 5, 6, 1, 3, 4);
            Assert.That(after.Heights[6][5], Is.EqualTo(196608));
            Assert.Throws<ArgumentException>(() => MapBrush.Ramp(d, 1, 6, 0, 5, 6, 196612, 1, 3, 4));
            // 196609: floor gives steps 49152, 49152, 49152, 49153; only the last cell is too steep.
            Assert.Throws<ArgumentException>(() => MapBrush.Ramp(d, 1, 6, 0, 5, 6, 196609, 1, 3, 4));
            Assert.DoesNotThrow(() => MapBrush.Ramp(d, 1, 6, 0, 5, 6, 196612, 1, 4, 4));
        }

        [Test]
        public void ARampWithoutRoomIsRefused()
        {
            // A spike at (5, 4) makes its cells illegal, so the point is frozen and the ramp through it cannot reach its
            // wanted height: nothing is written.
            MapDesc d = Map(8, 8, (i, j) => i == 5 && j == 4 ? 300000 : 0, 0, 10 * S);
            var e = Assert.Throws<ArgumentException>(() => MapBrush.Ramp(d, 2, 4, (int)(3 * S), 6, 4, (int)(3 * S), 1, 3, 4));
            StringAssert.Contains("does not fit", e.Message);
            Assert.DoesNotThrow(() => MapBrush.Ramp(d, 2, 1, (int)(3 * S), 6, 1, (int)(3 * S), 0, 3, 4));
        }

        [Test]
        public void TheDocumentUsesItsLayersWalkableSlope()
        {
            // rwr-terrain-demo: 3/4. At 3/4 the ramp is accepted, at 1/1 it is refused; the document stays as it was.
            MapDocument doc = MapDocument.Open(MapDescJson.Write(Map(10, 12, (i, j) => 0, 0, 4 * S)));
            byte[] before = doc.Save();
            MapDocument ok = doc.WithRamp(1, 6, 0, 5, 6, 196608, 1);
            Assert.That(ok.Save(), Is.EqualTo(MapDescJson.Write(Apply(MapDescJson.Read(before), MapBrush.Ramp(MapDescJson.Read(before), 1, 6, 0, 5, 6, 196608, 1, 3, 4)))));
            Assert.Throws<ArgumentException>(() => doc.WithRamp(1, 6, 0, 5, 6, 262144, 1));
            Assert.That(doc.Save(), Is.EqualTo(before));
        }

        [Test]
        public void ALayerWithoutTerrainRulesHasNoRamp()
        {
            MapDesc d = MapNew.Desc(new MapNewSpec { Id = "x", Content = "rwr", Pack = "rwr", Width = 4, Depth = 4, CeilingRaw = 1000, HeightRaw = 0 });
            Assert.Throws<InvalidOperationException>(() => MapDocument.Open(MapDescJson.Write(d)).WithRamp(0, 0, 0, 2, 0, 0, 1));
        }
    }
}
