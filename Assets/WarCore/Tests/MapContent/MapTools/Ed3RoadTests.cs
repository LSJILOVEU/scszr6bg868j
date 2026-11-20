using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ed3Fixtures;

namespace WarCore.Tests
{
    // ED-3a acceptance 5: roads (walkable along, level across, vertex limits, reading earlier stamps, crossings).
    public sealed class Ed3RoadTests
    {
        // g = floor(3/4 S) - 4 for rwr-terrain-demo.
        private const long G = 49148;

        [Test]
        public void RoadOverAHillIsWalkableAndLevelAcross()
        {
            MapDesc d = Map(100, 100);
            MapDesc r = Run(d, Gen(100, 100, Join(Hill(50, 50, 40, 30000), Road("[[10, 50], [50, 50], [90, 50]]", 3))));
            AllLegal(r);
            // The top vertex is limited to g per metre over 40 m from the foot (1 966 080 wanted).
            Assert.That(H(r, 50, 50), Is.EqualTo(40 * G));
            Assert.That(H(r, 10, 50), Is.EqualTo(0));
            Assert.That(H(r, 30, 50), Is.EqualTo(40 * G * 20 / 40));
            int walkable = 0;
            for (int i = 0; i <= 100; i++)
                for (int j = 47; j <= 53; j++)
                {
                    long dz = j - 50;
                    if (i < 10 && (10 - i) * (10 - i) + dz * dz > 9) continue;
                    if (i > 90 && (i - 90) * (i - 90) + dz * dz > 9) continue;
                    Assert.That(H(r, i, j), Is.EqualTo(H(r, i, 50)), "level across at " + i + "," + j);
                }
            for (int cx = 10; cx < 90; cx++)
                for (int cz = 47; cz < 53; cz++)
                {
                    Assert.That(MapBrush.CellWithin(H(r, cx, cz), H(r, cx + 1, cz), H(r, cx, cz + 1), H(r, cx + 1, cz + 1), 3, 4), Is.True,
                        "cell " + cx + "," + cz);
                    walkable++;
                }
            Assert.That(walkable, Is.EqualTo(80 * 6));
            Assert.That(Surface(r, 50, 50), Is.EqualTo("road"));
            Assert.That(Surface(r, 50, 52), Is.EqualTo("road"));
            Assert.That(Surface(r, 50, 47), Is.EqualTo("road"));
            Assert.That(Surface(r, 50, 53), Is.EqualTo("grass"), "centre 3.5 m off the line");
            Assert.That(Surface(r, 50, 46), Is.EqualTo("grass"));
        }

        // A plateau at the ceiling around (45, 10) and ground 0 at (5, 10): the vertex difference is the ceiling.
        private static MapDesc Climb(long ceiling) =>
            Run(Map(50, 20, ceiling: ceiling), Gen(50, 20, Join(Mesa(45, 10, 3, 1000000), Road("[[5, 10], [45, 10]]", 1))));

        [Test]
        public void ExactlyGPerMetreIsKeptAndOneMoreIsLimited()
        {
            MapDesc keep = Climb(40 * G);
            Assert.That(H(keep, 45, 10), Is.EqualTo(40 * G));
            Assert.That(H(keep, 5, 10), Is.EqualTo(0));
            Assert.That(H(keep, 25, 10), Is.EqualTo(20 * G));
            AllLegal(keep);
            MapDesc limited = Climb(40 * (G + 1));
            Assert.That(H(limited, 45, 10), Is.EqualTo(40 * G));
            Assert.That(H(limited, 5, 10), Is.EqualTo(0));
            AllLegal(limited);
        }

        [Test]
        public void PlateauAtTheStartRaisesTheEnd()
        {
            // The forward pass limits each vertex against the one before it, so the end is lifted to start - g * len.
            MapDesc r = Run(Map(50, 20, ceiling: 40 * (G + 1)), Gen(50, 20, Join(Mesa(5, 10, 3, 1000000), Road("[[5, 10], [45, 10]]", 1))));
            Assert.That(H(r, 5, 10), Is.EqualTo(40 * (G + 1)));
            Assert.That(H(r, 45, 10), Is.EqualTo(40));
            AllLegal(r);
        }

        [Test]
        public void RoadReadsTheHillBeforeIt()
        {
            MapDesc r = Run(Map(40, 40), Gen(40, 40, Join(Hill(20, 20, 40, 10000), Road("[[20, 20], [20, 30]]", 2))));
            Assert.That(H(r, 20, 20), Is.EqualTo(Raw(10000)));
            Assert.That(H(r, 20, 30), Is.EqualTo(576000));
            Assert.That(H(r, 20, 25), Is.EqualTo(655360 + (576000 - 655360) * 50 / 100));
            Assert.That(H(r, 22, 25), Is.EqualTo(H(r, 20, 25)));
        }

        [Test]
        public void MismatchedCrossingIsRefusedWithBothNumbers()
        {
            MapDesc d = Map(40, 40);
            byte[] before = MapDescJson.Write(d);
            string gen = Gen(40, 40, Join(Mesa(20, 40, 3, 10000), Road("[[0, 20], [40, 20]]", 3), Road("[[20, 0], [20, 40]]", 3)));
            ArgumentException e = Assert.Throws<ArgumentException>(() => Compose(d, gen));
            Assert.That(e.Message, Does.Contain("road stamps 1 and 2"));
            Assert.That(MapDescJson.Write(d), Is.EqualTo(before));
        }

        [Test]
        public void CrossingThatMeetsTheEarlierRoadIsAccepted()
        {
            MapDesc d = Map(40, 40);
            string gen = Gen(40, 40, Join(Mesa(20, 40, 3, 5000), Road("[[0, 20], [40, 20]]", 3), Road("[[20, 0], [20, 20], [20, 40]]", 3)));
            MapDesc r = Run(d, gen);
            AllLegal(r);
            Assert.That(H(r, 20, 20), Is.EqualTo(0));
            Assert.That(H(r, 30, 20), Is.EqualTo(0));
            Assert.That(H(r, 20, 40), Is.EqualTo(Raw(5000)));
            Assert.That(H(r, 20, 30), Is.EqualTo(Raw(5000) / 2));
        }

        [Test]
        public void VertexOffTheMapReadsTheNearestPoint()
        {
            MapDesc r = Run(Map(30, 10), Gen(30, 10, Join(Mesa(0, 5, 4, 5000), Road("[[-10, 5], [20, 5]]", 2))));
            Assert.That(H(r, 20, 5), Is.EqualTo(0));
            Assert.That(H(r, 0, 5), Is.EqualTo(218454));
            Assert.That(H(r, 10, 5), Is.EqualTo(327680 - 327680 * 600 / 900));
        }

        [Test]
        public void SteepRoadCellIsRefused()
        {
            // A hairpin: two legs 3 m apart climbing in opposite directions cannot both be met.
            MapDesc d = Map(60, 20, ceiling: 40 * G);
            string gen = Gen(60, 20, Join(Mesa(50, 5, 2, 1000000), Road("[[10, 5], [50, 5], [50, 8], [10, 8]]", 1)));
            ArgumentException e = Assert.Throws<ArgumentException>(() => Compose(d, gen));
            Assert.That(e.Message, Does.Contain("road stamp 1"));
        }
    }
}
