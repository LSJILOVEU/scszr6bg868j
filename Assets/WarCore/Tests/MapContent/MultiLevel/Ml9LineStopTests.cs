using System;
using NUnit.Framework;
using WarCore.Sim;
using static WarCore.Tests.Ml5Plates;

namespace WarCore.Tests
{
    // ML-9 acceptance 2: LineStop.TryFirst, the first of the ground (L9) and the floor plates (ML-5) on one segment.
    public sealed class Ml9LineStopTests
    {
        // Ground 32 x 8 m: flat at 0 up to x = 20 m, then rising 1 m per metre.
        private static HeightField Ramp()
        {
            var points = new int[33 * 9];
            for (int j = 0; j <= 8; j++)
                for (int i = 0; i <= 32; i++) points[j * 33 + i] = i <= 20 ? 0 : (int)((i - 20) * S);
            return new HeightField(32, 8, default, default, points);
        }

        // Plate 1 over x in [8, 12) m and plate 2 over x in [25, 28) m, both with the body between 0.5 and 1 m.
        private static PlateField Two() => Field(32, 8, 0, 0, Flat(1, 8, 0, 4, 8, S, S / 2), Flat(2, 25, 0, 3, 8, S, S / 2));

        private static SimVec3 Mm(long x, long y, long z) => V(x * S / 1000, y * S / 1000, z * S / 1000);

        [Test]
        public void ThePlateStopsFirst()
        {
            SimVec3 from = Mm(2000, 750, 4500), to = Mm(30000, 750, 4500);
            Assert.That(LineStop.TryFirst(Ramp(), Two(), from, to, out SimVec3 at), Is.True);
            Assert.That(at, Is.EqualTo(Mm(8000, 750, 4500)), "plate 1's side at x = 8 m, before the ground's rise at x = 21 m");
            Assert.That(Two().TryFirstThrough(from, to, out PlateHit hit) && hit.At.Equals(at), Is.True, "the plate's own point");
            Assert.That(LineStop.TryFirst(Ramp(), null, from, to, out SimVec3 ground), Is.True);
            Assert.That(ground, Is.EqualTo(Mm(21000, 750, 4500)), "without plates: the ground's point");
        }

        [Test]
        public void TheGroundStopsFirst()
        {
            SimVec3 from = Mm(14000, 750, 4500), to = Mm(30000, 750, 4500);
            Assert.That(LineStop.TryFirst(Ramp(), Two(), from, to, out SimVec3 at), Is.True);
            Assert.That(at, Is.EqualTo(Mm(21000, 750, 4500)), "the ground at x = 21 m, before plate 2 at x = 25 m");
            Assert.That(Ramp().TryFirstBelow(from, to, out SimVec3 ground) && ground.Equals(at), Is.True, "the ground's own point");
            Assert.That(LineStop.TryFirst(null, Two(), from, to, out SimVec3 plate), Is.True);
            Assert.That(plate, Is.EqualTo(Mm(25000, 750, 4500)), "without ground: the plate's point");
        }

        // A tie: both report floor(P(t)) per coordinate, so the winner's point is the same either way; the result is that point.
        [Test]
        public void ATieGivesTheCommonPoint()
        {
            // Ground rising from 0 at x = 20 m to 1 m at x = 21 m; plate 2 moved to start at x = 21 m.
            PlateField plates = Field(32, 8, 0, 0, Flat(2, 21, 0, 3, 8, S, S / 2));
            SimVec3 from = Mm(14000, 750, 4500), to = Mm(30000, 750, 4500);
            Assert.That(Ramp().TryFirstBelow(from, to, out SimVec3 ground), Is.True);
            Assert.That(plates.TryFirstThrough(from, to, out PlateHit hit), Is.True);
            Assert.That((ground, hit.At), Is.EqualTo((Mm(21000, 750, 4500), Mm(21000, 750, 4500))), "the same t, the same point");
            Assert.That(LineStop.TryFirst(Ramp(), plates, from, to, out SimVec3 at) && at.Equals(ground), Is.True);
        }

        [Test]
        public void NothingStops()
        {
            SimVec3 from = Mm(1000, 5000, 1000), to = Mm(20000, 5000, 7000);
            Assert.That(LineStop.TryFirst(Ramp(), Two(), from, to, out SimVec3 at), Is.False);
            Assert.That(at, Is.EqualTo(default(SimVec3)));
            Assert.That(LineStop.TryFirst(null, null, Mm(0, -5000, 0), Mm(9000, -5000, 0), out _), Is.False, "no ground, no plates");
        }

        // Invariant C-f 2: with one side missing the result and the point are the other side's, bit for bit, on 2000 random
        // segments (fixed seed) inside and around the map, vertical and zero-length ones included.
        [Test]
        public void OneSideAloneIsThatSide()
        {
            HeightField ground = Ramp();
            PlateField plates = Field(32, 8, 0, 0, Flat(1, 8, 0, 4, 8, S, S / 2, (1, 3)), AlongX(2, 20, 2, 6, 4, new[] { 0L, S / 2, S, 3 * S / 2, 2 * S, 5 * S / 2, 3 * S }, S / 2));
            var random = new Random(9);
            int groundHits = 0, plateHits = 0;
            for (int n = 0; n < 2000; n++)
            {
                SimVec3 from = Random(random), to = (n % 10) switch
                {
                    0 => from,
                    1 => new SimVec3(from.X, Random(random).Y, from.Z),
                    _ => Random(random),
                };
                bool g = ground.TryFirstBelow(from, to, out SimVec3 gAt);
                Assert.That(LineStop.TryFirst(ground, null, from, to, out SimVec3 at), Is.EqualTo(g), "ground " + n);
                Assert.That(at, Is.EqualTo(gAt), "ground point " + n);
                bool p = plates.TryFirstThrough(from, to, out PlateHit hit);
                Assert.That(LineStop.TryFirst(null, plates, from, to, out at), Is.EqualTo(p), "plates " + n);
                Assert.That(at, Is.EqualTo(hit.At), "plate point " + n);
                if (g) groundHits++;
                if (p) plateHits++;
            }
            Assert.That(groundHits > 200 && plateHits > 200, Is.True, "both sides are hit often: " + groundHits + ", " + plateHits);
        }

        // x in [-4, 36] m, y in [-1, 4] m, z in [-2, 10] m, in raw units.
        private static SimVec3 Random(Random r) =>
            V(r.Next(-4 * (int)S, 36 * (int)S), r.Next(-(int)S, 4 * (int)S), r.Next(-2 * (int)S, 10 * (int)S));
    }
}
