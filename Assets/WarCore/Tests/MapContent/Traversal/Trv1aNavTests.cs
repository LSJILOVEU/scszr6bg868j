using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Sim;
using static WarCore.Tests.Trv1aMaps;

namespace WarCore.Tests
{
    // TRV-1a: a stamped ramp in the ML-6 graph (T1-08, T1-09).
    public sealed class Trv1aNavTests
    {
        // A floor 0.9 m up on cells x 2..5, z 2..3 (solid down to the ground); the doorway is its east edge (x = 6).
        private static MapDesc Floor() => Flat(16, 8, Ml4Maps.Flat(1, 2, 2, 4, 2, 58982, 58982));

        private static bool Seam(LayeredNav nav, int a, int b) => Enumerable.Range(0, nav.SeamCount(a)).Any(k => nav.Seam(a, k) == b);

        // T1-08: from the ground at (9, 2) to the floor at (3, 2): (8, 2), the ramp's (7, 2) and (6, 2), the floor's (5, 2),
        // (4, 2), (3, 2): six orthogonal steps of cost 1, 6000. Without the ramp the floor is out of reach.
        [Test]
        public void TheRampJoinsTheGroundToTheFloor()
        {
            LayeredNav before = Nav(Floor());
            Assert.That(Cost(before, Node(before, 0, 9, 2), Node(before, 1, 3, 2)), Is.EqualTo(-1));

            MapDesc d = Stamp(Floor(), Ramp(Floor(), 6, 2, 2, MapSide.PlusX, 58982));
            Assert.That(Ml4Maps.Gate(d), Is.Empty);
            LayeredNav nav = Nav(d);
            Assert.That(nav.LayerCount, Is.EqualTo(3), "ground, floor, ramp");
            Assert.That(Cost(nav, Node(nav, 0, 9, 2), Node(nav, 1, 3, 2)), Is.EqualTo(6000));
            Assert.That(Seam(nav, Node(nav, 2, 7, 2), Node(nav, 0, 8, 2)) && Seam(nav, Node(nav, 2, 6, 3), Node(nav, 1, 5, 3)), "foot and top seams");
            Assert.That(nav.IsPassable(Node(nav, 0, 6, 2)) || nav.IsPassable(Node(nav, 0, 7, 3)), Is.False, "the ground under the ramp is closed");
        }

        // T1-09: at 1/4 the 0.9 m ramp takes 4 cells, points 58982, 44236, 29491, 14745, 0. A side edge seams only where both
        // end points are within 0.3 m of the ground: the last cell (14745, 0) on both sides; the others (>= 29491) nowhere.
        [Test]
        public void ASideSeamsOnlyWhereBothEndsMeetTheGround()
        {
            MapStampPlan plan = Ramp(Floor(), 6, 2, 2, MapSide.PlusX, 58982, 1, 4);
            Assert.That(Points(Plate(plan)), Is.EqualTo(new long[] { 58982, 44236, 29491, 14745, 0 }));
            LayeredNav nav = Nav(Stamp(Floor(), plan));
            for (int x = 6; x <= 9; x++)
            {
                bool expected = x == 9;
                Assert.That(Seam(nav, Node(nav, 2, x, 2), Node(nav, 0, x, 1)), Is.EqualTo(expected), "south side at x " + x);
                Assert.That(Seam(nav, Node(nav, 2, x, 3), Node(nav, 0, x, 4)), Is.EqualTo(expected), "north side at x " + x);
            }
        }
    }
}
