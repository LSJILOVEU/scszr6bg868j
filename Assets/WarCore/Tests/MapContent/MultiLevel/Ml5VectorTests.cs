using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.Ml5Plates;

namespace WarCore.Tests
{
    // ML-5 acceptance 2: hand vectors. Fixture M (Ml5Plates.M): map 8 x 4 at the origin, plate 1 flat on [2,0] size [4,4],
    // top 196608, thick 32768 (bottom 163840).
    public sealed class Ml5VectorTests
    {
        // 2a: a level line at 2.75 m enters through the plate's sides; t* is the side, at is floored (inside the plate going
        // +x, on the far face going -x).
        [Test]
        public void LevelLineEntersThroughTheSide()
        {
            PlateField f = M();
            PlateHit h = Hit(f, V(32768, 180224, 98304), V(491520, 180224, 98304));
            T(h, 3, 14);
            Assert.That((h.At, h.PlateId), Is.EqualTo((V(131072, 180224, 98304), 1UL)));
            PlateHit back = Hit(f, V(491520, 180224, 98304), V(32768, 180224, 98304));
            T(back, 3, 14);
            Assert.That((back.At, back.PlateId), Is.EqualTo((V(393216, 180224, 98304), 1UL)));
        }

        // 2b: touching the top or the bottom face is clear; one raw inside is blocked.
        [TestCase(196608, false)]
        [TestCase(163840, false)]
        [TestCase(196607, true)]
        [TestCase(163841, true)]
        public void FacesAreClearInsideIsBlocked(long y, bool blocked)
        {
            PlateField f = M();
            Assert.That(f.Blocks(V(32768, y, 98304), V(491520, y, 98304)), Is.EqualTo(blocked));
            if (blocked) T(Hit(f, V(32768, y, 98304), V(491520, y, 98304)), 3, 14);
        }

        // 2c: straight down meets the top face (not attained, the infimum); straight up meets the bottom face; stopping on
        // the top face is clear; a zero-length segment inside is blocked at t = 0.
        [Test]
        public void VerticalAndZeroLength()
        {
            PlateField f = M();
            PlateHit down = Hit(f, V(229376, 262144, 98304), V(229376, 131072, 98304));
            T(down, 1, 2);
            Assert.That((down.At, down.PlateId), Is.EqualTo((V(229376, 196608, 98304), 1UL)));
            PlateHit up = Hit(f, V(229376, 131072, 98304), V(229376, 262144, 98304));
            T(up, 1, 4);
            Assert.That(up.At, Is.EqualTo(V(229376, 163840, 98304)));
            Clear(f, V(229376, 262144, 98304), V(229376, 196608, 98304), "ends on the top face");
            Clear(f, V(229376, 196608, 98304), V(229376, 262144, 98304), "starts on the top face going up");
            Clear(f, V(229376, 163840, 98304), V(229376, 131072, 98304), "starts on the bottom face going down");
            PlateHit point = Hit(f, V(229376, 180224, 98304), V(229376, 180224, 98304));
            T(point, 0, 1);
            Assert.That(point.At, Is.EqualTo(V(229376, 180224, 98304)));
        }

        // 2d: half-open cells. Along x = 2 m (the plate's minimum edge) blocked, along 6 m (its maximum edge) clear, one raw
        // less blocked; the same on z with fixture Mz (plate on z [1, 3)).
        [Test]
        public void HalfOpenEdges()
        {
            PlateField f = M();
            Assert.That(f.Blocks(V(131072, 180224, 32768), V(131072, 180224, 229376)), Is.True, "minimum x edge");
            Assert.That(f.Blocks(V(393216, 180224, 32768), V(393216, 180224, 229376)), Is.False, "maximum x edge");
            Assert.That(f.Blocks(V(393215, 180224, 32768), V(393215, 180224, 229376)), Is.True);
            PlateField mz = Field(8, 4, 0, 0, Flat(1, 2, 1, 4, 2));
            Assert.That(mz.Blocks(V(163840, 180224, 65536), V(360448, 180224, 65536)), Is.True, "minimum z edge");
            Assert.That(mz.Blocks(V(163840, 180224, 196608), V(360448, 180224, 196608)), Is.False, "maximum z edge");
            Assert.That(mz.Blocks(V(163840, 180224, 196607), V(360448, 180224, 196607)), Is.True);
        }

        // 2e: fixture Mh = M with cell [3,1] dug out; the hole's minimum edge belongs to the hole.
        [Test]
        public void CutoutCellsAreOpen()
        {
            PlateField f = M((1, 1));
            Assert.That(f.Blocks(V(229376, 262144, 98304), V(229376, 131072, 98304)), Is.False, "in the hole");
            Assert.That(f.Blocks(V(196608, 262144, 98304), V(196608, 131072, 98304)), Is.False, "the hole's minimum edge");
            Assert.That(f.Blocks(V(196607, 262144, 98304), V(196607, 131072, 98304)), Is.True, "cell [2,1]");
        }

        // 2f: fixture Ms: map 4 x 1, plate 2 on [0,0] size [2,1] rising along x 0 / 65536 / 131072. The level line at 0.75 m
        // enters through the sloped top at x = 0.75 m; x = 2 m is the next column's, where there is no plate.
        [Test]
        public void SlopedPlate()
        {
            PlateField f = Field(4, 1, 0, 0, AlongX(2, 0, 0, 2, 1, new long[] { 0, 65536, 131072 }));
            PlateHit h = Hit(f, V(0, 49152, 32768), V(131072, 49152, 32768));
            T(h, 3, 8);
            Assert.That((h.At, h.PlateId), Is.EqualTo((V(49152, 49152, 32768), 2UL)));
            Clear(f, V(131072, 147456, 32768), V(131072, 81920, 32768), "x = 2 m is column 2");
        }

        // 2g: cells met at a single point. A plate solid only in cell (cx, cz) of an 8 x 4 map, line at 2.75 m.
        private static PlateField One(int cx, int cz) => Field(8, 4, 0, 0, Flat(1, cx, cz, 1, 1));

        [Test]
        public void DiagonalThroughACorner()
        {
            // (2.5, 2.5) -> (3.5, 1.5) m passes the corner (3, 2) at t = 1/2; the corner is cell (3,2)'s.
            PlateHit h = Hit(One(3, 2), V(163840, 180224, 163840), V(229376, 180224, 98304));
            T(h, 1, 2);
            Assert.That((h.At, h.PlateId), Is.EqualTo((V(196608, 180224, 131072), 1UL)));
            Clear(One(2, 1), V(163840, 180224, 163840), V(229376, 180224, 98304), "cell (2,1) only touches the corner's closure");
        }

        [Test]
        public void StartOnAMinimumEdge()
        {
            // Starting on x = 3 m going -x: the start is column 3's, then column 2.
            PlateHit h = Hit(One(3, 1), V(196608, 180224, 98304), V(131072, 180224, 98304));
            T(h, 0, 1);
            Assert.That(h.At, Is.EqualTo(V(196608, 180224, 98304)));
            Clear(One(2, 1), V(196608, 180224, 98304), V(229376, 180224, 98304), "going +x from x = 3 m never meets column 2");
        }

        [Test]
        public void EndOnAMinimumEdge()
        {
            // Ending on x = 3 m from -x: the end is column 3's.
            PlateHit h = Hit(One(3, 1), V(163840, 180224, 98304), V(196608, 180224, 98304));
            T(h, 1, 1);
            Assert.That(h.At, Is.EqualTo(V(196608, 180224, 98304)));
            Clear(One(2, 1), V(229376, 180224, 98304), V(196608, 180224, 98304), "ending on x = 3 m from +x stays in column 3");
        }

        // 2h: ties go to the smaller id: two overlapping plates (the field does not check the stack rule); and the corner
        // cell's plate 3 against plate 1 in the next cell, both at t = 1/2.
        [Test]
        public void TiesGoToTheSmallerId()
        {
            PlateField both = Field(8, 4, 0, 0, Flat(1, 2, 0, 4, 4), Flat(3, 2, 0, 4, 4));
            Assert.That(Hit(both, V(32768, 180224, 98304), V(491520, 180224, 98304)).PlateId, Is.EqualTo(1UL));
            PlateField corner = Field(8, 4, 0, 0, Flat(1, 3, 1, 1, 1), Flat(3, 3, 2, 1, 1));
            PlateHit h = Hit(corner, V(163840, 180224, 163840), V(229376, 180224, 98304));
            T(h, 1, 2);
            Assert.That(h.PlateId, Is.EqualTo(1UL), "plate 3 is met first in the walk, plate 1 ties it");
        }

        // 2i: a negative origin; at is floored toward negative infinity.
        [Test]
        public void NegativeOriginFloorsDown()
        {
            PlateField f = Field(8, 4, -262144, -131072, Flat(1, 2, 0, 4, 4));
            PlateHit h = Hit(f, V(-229376, 180224, -32768), V(229383, 180227, -32773));
            T(h, 98304, 458759);
            Assert.That((h.At, h.PlateId), Is.EqualTo((V(-131072, 180224, -32770), 1UL)), "z = -32769.07 floors to -32770");
            PlateHit down = Hit(f, V(-32768, 262144, -32768), V(-32768, 131072, -32768));
            T(down, 1, 2);
            Assert.That(down.At, Is.EqualTo(V(-32768, 196608, -32768)));
        }

        // 2j: parts off the map hold no plate; a segment crossing the map is still blocked.
        [Test]
        public void OffTheMap()
        {
            PlateField f = M();
            PlateHit h = Hit(f, V(-131072, 180224, 98304), V(720896, 180224, 98304));
            Assert.That(h.At, Is.EqualTo(V(131072, 180224, 98304)));
            T(h, 262144, 851968);
            Assert.That(f.Blocks(V(720896, 180224, 98304), V(-131072, 180224, 98304)), Is.True, "both ends off the map");
            Clear(f, V(-131072, 180224, -65536), V(720896, 180224, -65536), "beside the map");
            Clear(f, V(-131072, 180224, 98304), V(-65536, 180224, 98304), "before the map");
        }

        // 2k: a start inside the plate body is blocked at t = 0.
        [Test]
        public void StartInsideTheBody()
        {
            PlateHit h = Hit(M(), V(229376, 180224, 98304), V(229376, 180224, 360448));
            T(h, 0, 1);
            Assert.That(h.At, Is.EqualTo(V(229376, 180224, 98304)));
        }

        // 2l: fixture Ms' (tops 0 / 65537 / 131074): at x = 1.5 m the top is 98305.5, the floored standing height 98305 is
        // inside the plate, so a level line from it is blocked at once (K11; ML-9 keeps ends 1 raw above).
        [Test]
        public void FlooredStandingHeightIsInsideASlope()
        {
            PlateField f = Field(4, 1, 0, 0, AlongX(2, 0, 0, 2, 1, new long[] { 0, 65537, 131074 }));
            PlateHit h = Hit(f, V(98304, 98305, 32768), V(98304, 98305, 0));
            T(h, 0, 1);
            Assert.That((h.At, h.PlateId), Is.EqualTo((V(98304, 98305, 32768), 2UL)));
            Clear(f, V(98304, 98306, 32768), V(98304, 98306, 0), "one raw above the floored top");
        }
    }
}
