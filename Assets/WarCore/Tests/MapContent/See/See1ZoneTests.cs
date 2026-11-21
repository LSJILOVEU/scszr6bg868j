using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.See1Field;

namespace WarCore.Tests
{
    // SEE-1 rule 1 (the zone round the player): ground T has building A on x 10..30, z 10..14 (6 m high). Default camera: the
    // line back from a point at height h' above the feet start (x - 0.7071 h', z - 0.7071 h'), so from the feet (0.1 m up) it
    // can meet A up to 5.9 m, which is 0.7071 * 5.9 = 4.17 m behind A's back face (z 14): a unit stands in A's shadow when
    // z <= 18.17. From the head (1 m up) the shadow is 0.7071 * 5 = 3.54 m deep (z <= 17.54).
    public sealed class See1ZoneTests
    {
        private const long M = SimFix.Scale;

        private static LayerCutResult Run(ViewMap map, float x, float z, float radius, (float X, float Y, float Z)? camera = null) =>
            LayerCut.Run(map.Plates, map, At(x, z), camera ?? Forward, radius);

        private static CutState Of(LayerCutResult cut, int wall) => cut.Wall(wall);

        // A1: radius 0 is the old cut. (20, 17) is in A's shadow (17 <= 18.17), (20, 20) is not.
        [Test]
        public void RadiusZeroIsTheOldCut()
        {
            ViewMap t = T();
            Assert.That(Of(Run(t, 20, 17, 0), 0), Is.EqualTo(CutState.Faded));
            Assert.That(Of(Run(t, 20, 20, 0), 0), Is.EqualTo(CutState.Shown));
            Assert.That(Of(Run(t, 20, 23.1f, 0), 0), Is.EqualTo(CutState.Shown), "the ring is off at radius 0");
            Assert.That(LayerCut.Run(t.Plates, t, At(20, 23.1f), Forward).Wall(0), Is.EqualTo(CutState.Shown), "the default is off");
        }

        // A2, radius 5 (rings at 2.5 and 5 m). (20, 20): the sample 2.5 m towards -z is (20, 17.5), in the shadow (17.5 <= 18.17).
        // (20, 23.1): only the outer sample towards -z, (20, 18.1), is (18.1 <= 18.17); (20, 23.3): its sample (20, 18.3) is not.
        [Test]
        public void TheRingFadesWhatShadowsASample()
        {
            ViewMap t = T();
            Assert.That(Of(Run(t, 20, 20, 5), 0), Is.EqualTo(CutState.Faded));
            Assert.That(Of(Run(t, 20, 23.1f, 5), 0), Is.EqualTo(CutState.Faded));
            Assert.That(Of(Run(t, 20, 23.3f, 5), 0), Is.EqualTo(CutState.Shown));
        }

        // A3: the pillar (x 40..41, z 10..11, 6 m) and unit (43.5, 16.5). Towards -z: sample (43.5, 14) at 2.5 m: its line meets
        // the pillar's x 40..41 at h' 3.54..4.95 and its z 10..11 at h' 4.24..5.66, together 4.24..4.95, under 5.9: faded. The
        // sample at 5 m, (43.5, 11.5): x at 3.54..4.95, z at 0.71..2.12: misses. So only the inner ring counts.
        // Unit (34.5, 19) beside A: towards 225 degrees (-x, -z) the samples (32.73, 17.23) and (30.96, 15.46) reach A (x <= 30,
        // z <= 14 at h' 3.86 and 4.57 (2.07 for the outer one), under 5.9); towards 180 degrees (32, 19): z 14.83 at 5.9: misses;
        // towards 270 degrees (34.5, 16.5) and (34.5, 14): x 30.33 at 5.9: misses. So only the 225 degrees direction counts.
        [Test]
        public void BothRingsAndAllEightDirectionsAreUsed()
        {
            ViewMap t = T();
            LayerCutResult a = Run(t, 43.5f, 16.5f, 5);
            Assert.That((Of(a, 0), Of(a, 1)), Is.EqualTo((CutState.Shown, CutState.Faded)), "the pillar, by the inner sample only");
            Assert.That(Of(Run(t, 43.5f, 16.5f, 0), 1), Is.EqualTo(CutState.Shown), "not by the unit itself");
            LayerCutResult b = Run(t, 34.5f, 19, 5);
            Assert.That(Of(b, 0), Is.EqualTo(CutState.Faded), "A, by the 225 degrees samples");
            Assert.That(Of(Run(t, 34.5f, 19, 0), 0), Is.EqualTo(CutState.Shown));
        }

        // A4: no peeking into the building: (20, 8), 2 m in front of A's near face (z 10). The samples inside A and beyond it are
        // not seen from the unit (the line from the eye at 1 m crosses A), so A stays; the samples to the sides see no A.
        [Test]
        public void WhatTheUnitCannotSeeIsNotSampled()
        {
            ViewMap t = T();
            Assert.That(Of(Run(t, 20, 8, 5), 0), Is.EqualTo(CutState.Shown));
        }

        // A5: a low wall on x 10..30, z 40..41 and a unit at (20, 43), 2 m behind it. Its own lines pass over it (the feet line
        // reaches z 41 at 2.83 m up, over a 1.6 m wall); the samples behind it are not seen; the samples beside it, such as
        // (21.77, 41.23), are seen and their lines cross it. It fades when its top is more than 1.5 m over the feet: 1.2 m no,
        // exactly 1.5 m no, 1.6 m yes.
        [Test]
        public void LowWallsDoNotFlicker()
        {
            Assert.That(Of(Run(Map((10, 40, 20, 1, 1.2)), 20, 43, 5), 0), Is.EqualTo(CutState.Shown));
            Assert.That(Of(Run(Map((10, 40, 20, 1, 1.5)), 20, 43, 5), 0), Is.EqualTo(CutState.Shown), "1.5 m is not more than 1.5 m");
            Assert.That(Of(Run(Map((10, 40, 20, 1, 1.6)), 20, 43, 5), 0), Is.EqualTo(CutState.Faded));
        }

        // The height is measured from the unit's feet: on the same ground a unit standing 0.2 m up sees the 1.6 m wall as 1.4 m.
        [Test]
        public void TheHeightIsOverTheFeet()
        {
            ViewMap map = Map((10, 40, 20, 1, 1.6));
            var up = new ViewEntity(1, 0, 20, 0.2f, 43, team: 1);
            Assert.That(LayerCut.Run(map.Plates, map, up, Forward, 5).Wall(0), Is.EqualTo(CutState.Shown));
        }

        // A9: yaw 135, pitch 60: the view direction is (0.3536, -0.8660, -0.3536); the line back goes -x, +z by 0.4082 m a metre
        // up. A hides what is at z < 10 up to 0.4082 * 5.9 = 2.41 m short of its near face (z 10): z >= 7.59 is in its shadow.
        // (20, 9) is, (20, 7) is not; with radius 5 the sample (20, 9.5) of (20, 7) is.
        [Test]
        public void ARotatedAndTiltedCamera()
        {
            ViewMap t = T();
            var camera = new IsoCamera(12f, 0f, 135f, 60f).Forward;
            Assert.That(Of(Run(t, 20, 9, 0, camera), 0), Is.EqualTo(CutState.Faded));
            Assert.That(Of(Run(t, 20, 7, 0, camera), 0), Is.EqualTo(CutState.Shown));
            Assert.That(Of(Run(t, 20, 7, 5, camera), 0), Is.EqualTo(CutState.Faded));
            Assert.That(Of(Run(t, 20, 2, 5, camera), 0), Is.EqualTo(CutState.Shown), "its samples reach z 7 at most, short of 7.59");
        }

        // A6: the demo house (ML-10: 3 m walls round x 8..16, z 5..11 with a doorway on the west side at z 7..9, a first floor on
        // x 9..15, z 6..10 at 2.5..3 m, a stair on x 10..14, z 9..10) and the player on the ground under the floor at (11.5, 7.5).
        // The floor is Hidden, whatever the ring. Radius 0: the south wall fades (its own lines). Radius 5: the inner sample
        // towards -x, (9, 7.5), is seen through the doorway and its line (x - 0.7071 h', z - 0.7071 h') crosses the west wall's
        // southern piece (x 8..9, z 6..7) at h' 0.71..1.41; the northern piece (z 9..10) is not crossed by any sample (its lines
        // go towards -z, away from it); the north and east walls are behind the unit. The stair is not in the way.
        [Test]
        public void TheDemoHouseFadesTheSouthAndTheWestPiece()
        {
            byte[] bytes = WarCore.Presentation.PlateDemo.MapBytes();
            ViewPlates plates = ViewPlates.From(WarCore.Content.WcMapReader.Read(bytes));
            ViewMap map = ViewMap.Parse(bytes);
            int Wall(float x, float z) => Enumerable.Range(0, map.Walls.Length).Single(w => map.Walls[w].CenterX == x && map.Walls[w].CenterZ == z);
            int south = Wall(12f, 5.5f), north = Wall(12f, 10.5f), westSouth = Wall(8.5f, 6.5f), westNorth = Wall(8.5f, 9.5f), east = Wall(15.5f, 8f);
            ViewEntity me = At(11.5f, 7.5f);

            LayerCutResult zero = LayerCut.Run(plates, map, me, Forward);
            Assert.That((zero.Plate(0), zero.Plate(1)), Is.EqualTo((CutState.Shown, CutState.Hidden)));
            Assert.That(new[] { south, north, westSouth, westNorth, east }.Select(zero.Wall),
                Is.EqualTo(new[] { CutState.Faded, CutState.Shown, CutState.Shown, CutState.Shown, CutState.Shown }));

            LayerCutResult ring = LayerCut.Run(plates, map, me, Forward, 5);
            Assert.That((ring.Plate(0), ring.Plate(1)), Is.EqualTo((CutState.Shown, CutState.Hidden)));
            Assert.That(new[] { south, north, westSouth, westNorth, east }.Select(ring.Wall),
                Is.EqualTo(new[] { CutState.Faded, CutState.Shown, CutState.Faded, CutState.Shown, CutState.Shown }));
        }

        // The plate under the unit never fades by the ring: on the stair's foot (10.5, 0.375) the sample 2.5 m towards +x, (13, 9.5),
        // lies under the stair (top 2.25 m); its line (from 0.475 m up) crosses cell (12, 9) between 1.0 and 2.25 m and
        // would fade the stair. For the same point as a ground unit's it does.
        [Test]
        public void ThePlateUnderTheUnitStays()
        {
            byte[] bytes = WarCore.Presentation.PlateDemo.MapBytes();
            ViewPlates plates = ViewPlates.From(WarCore.Content.WcMapReader.Read(bytes));
            ViewMap map = ViewMap.Parse(bytes);
            var onTheStair = new ViewEntity(1, 0, 10.5f, 0.375f, 9.5f, team: 1, layer: 1);
            var onTheGround = new ViewEntity(1, 0, 10.5f, 0.375f, 9.5f, team: 1);
            Assert.That(LayerCut.Run(plates, map, onTheStair, Forward, 5).Plate(0), Is.EqualTo(CutState.Shown));
            Assert.That(LayerCut.Run(plates, map, onTheGround, Forward, 5).Plate(0), Is.EqualTo(CutState.Faded));
        }

        // The height rule holds for plates too: a platform on x 3..6, z 6..10 west of the house, 0.5 m thick, seen from the unit
        // at (11.5, 7.5) through the doorway: the outer west sample (6.5, 7.5) has its line (from 0.1 m up) through the platform
        // at h' 0.7..0.9 (a 1 m top, slab 0.5..1) or 1.4..1.9 (a 2 m top, slab 1.5..2). It fades when its top is over 1.5 m.
        [Test]
        public void LowPlatformsStayAndHighOnesFade()
        {
            MapPlate Platform(long top) => new MapPlate(10, new MapXZ(3, 6), new MapXZ(3, 4), top, null, M / 2);
            ViewEntity me = At(11.5f, 7.5f);
            var (lowPlates, lowMap) = House(plates: new[] { Platform(M) });
            var (highPlates, highMap) = House(plates: new[] { Platform(2 * M) });
            Assert.That(LayerCut.Run(lowPlates, lowMap, me, Forward, 5).Plate(2), Is.EqualTo(CutState.Shown));
            Assert.That(LayerCut.Run(highPlates, highMap, me, Forward, 5).Plate(2), Is.EqualTo(CutState.Faded));
            Assert.That(LayerCut.Run(highPlates, highMap, me, Forward).Plate(2), Is.EqualTo(CutState.Shown), "not by the unit's own lines");
        }

        // What is hidden stays hidden: a 2 m wall standing on the first floor at cell (10, 6) (bottom 3 m) is hidden under the
        // floor; the line from the sample (13.27, 9.27) passes through it at 3.64 m up, and the wall stays Hidden.
        [Test]
        public void HiddenStaysHidden()
        {
            var (plates, map) = House(new[] { new MapWall(19, "wall", "walls", new MapXZ(10, 6), new MapXZ(1, 1), 2 * M, null, null, 3 * M) });
            int upper = Enumerable.Range(0, map.Walls.Length).Single(w => map.Walls[w].CenterX == 10.5f && map.Walls[w].CenterZ == 6.5f);
            Assert.That(LayerCut.Run(plates, map, At(11.5f, 7.5f), Forward, 5).Wall(upper), Is.EqualTo(CutState.Hidden));
        }

        // A sample is seen from the eye (1 m up), not from the feet: a wall raised from 0.5 m to 3.5 m on x 10..30, z 30..31 lets
        // the feet line of (20, 28) pass under it to the samples behind it, the eye does not: they are not seen, the wall stays.
        [Test]
        public void TheEyeNotTheFeetSeesTheSample()
        {
            ViewMap map = Raised(10, 30, 20, 1, 0.5, 3);
            Assert.That(Of(Run(map, 20, 28, 5), 0), Is.EqualTo(CutState.Shown));
        }

        // No player, a dead one: nothing fades whatever the radius.
        [Test]
        public void NoPlayerNothingFades()
        {
            ViewMap t = T();
            foreach (ViewEntity? player in new ViewEntity?[] { null, At(20, 17, alive: false) })
            {
                LayerCutResult cut = LayerCut.Run(t.Plates, t, player, Forward, 5);
                Assert.That(Enumerable.Range(0, cut.WallCount).Select(cut.Wall), Is.All.EqualTo(CutState.Shown));
            }
        }
    }
}
