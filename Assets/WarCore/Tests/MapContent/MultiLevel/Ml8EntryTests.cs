using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml7Worlds;
using static WarCore.Tests.Ml8Worlds;

namespace WarCore.Tests
{
    // ML-8 acceptance 2 (the entry function) and 3 (footprints).
    public sealed class Ml8EntryTests
    {
        private const long S = SimFix.Scale;

        // Hand-built graph, 8 x 4 cells, flat ground at 0. Ground blocked on (2, 1), (2, 2), (3, 1), (5, 2), (4, 3).
        //   Plate 1 (layer 1): one cell (2, 1) at 0.25 m: seams with the ground cells round it that are open, (1, 1) and (2, 0).
        //   Plate 2 (layer 2): one cell (6, 1) at 0.25 m over ground that stays walkable: a seam from ground (5, 1) lands on a
        //     cell the ground itself still walks.
        private static InterimLocomotion Fixture(out SimSession s)
        {
            s = Ml8Worlds.Walk(Bytes(Ml4Maps.Make(8, 4, Ml4Maps.Flat(1, 6, 2, 2, 2, 3 * M))), new[] { Unit(1, 1, 500, 500) });
            PlateSlab p1 = Ml5Plates.Flat(1, 2, 1, 1, 1, S / 4, S / 8), p2 = Ml5Plates.Flat(2, 6, 1, 1, 1, S / 4, S / 8);
            LayeredNav nav = Ml6Nav.Nav(Ml6Nav.Open(8, 4, (2, 1), (2, 2), (3, 1), (5, 2), (4, 3)), p1, p2);
            Rebind(s, nav, p1, p2);
            return M3TestWorld.Locomotion(s);
        }

        [Test]
        public void TheEntryFunctionKeepsOrChangesTheLayer()
        {
            InterimLocomotion l = Fixture(out _);
            (bool, int) E(int layer, (int, int) a, (int, int) b) => Enter(l, 8, layer, a, b);
            Assert.That(E(0, (0, 0), (0, 0)), Is.EqualTo((true, 0)), "same cell");
            Assert.That(E(1, (2, 1), (2, 1)), Is.EqualTo((true, 1)), "same cell on a plate");
            Assert.That(E(0, (0, 0), (1, 0)), Is.EqualTo((true, 0)), "walkable on this layer");
            Assert.That(E(0, (1, 1), (2, 1)), Is.EqualTo((true, 1)), "across the seam onto the plate");
            Assert.That(E(1, (2, 1), (1, 1)), Is.EqualTo((true, 0)), "and back");
            Assert.That(E(0, (2, 0), (2, 1)), Is.EqualTo((true, 1)), "the seam along z");
            Assert.That(E(1, (2, 1), (2, 2)), Is.EqualTo((false, 1)), "no seam lands on that cell (the seams land on (1, 1) and (2, 0))");
            Assert.That(E(1, (2, 1), (3, 1)), Is.EqualTo((false, 1)), "nor on (3, 1)");
            Assert.That(E(0, (1, 2), (2, 2)), Is.EqualTo((false, 0)), "blocked, no seam");
            Assert.That(E(0, (5, 1), (6, 1)), Is.EqualTo((true, 0)), "a seam lands there, but the ground walks the cell: stay");
            Assert.That(E(2, (6, 1), (5, 1)), Is.EqualTo((true, 0)), "plate 2 back onto the ground");
            Assert.That(E(0, (0, 0), (2, 0)), Is.EqualTo((false, 0)), "two cells apart");
            Assert.That(E(0, (0, 0), (0, 2)), Is.EqualTo((false, 0)), "two cells apart along z");
        }

        [Test]
        public void ADiagonalStepIsTwoOrthogonalOnes()
        {
            InterimLocomotion l = Fixture(out _);
            (bool, int) E(int layer, (int, int) a, (int, int) b) => Enter(l, 8, layer, a, b);
            Assert.That(E(0, (0, 0), (1, 1)), Is.EqualTo((true, 0)), "walkable diagonal");
            Assert.That(E(0, (3, 0), (2, 1)), Is.EqualTo((true, 1)), "x first: (2, 0) then the seam; z first would cross blocked (3, 1)");
            Assert.That(E(0, (1, 2), (2, 1)), Is.EqualTo((true, 1)), "x first crosses blocked (2, 2); z first: (1, 1) then the seam");
            Assert.That(E(0, (3, 2), (2, 1)), Is.EqualTo((false, 0)), "both ways blocked: (2, 2) and (3, 1)");
            Assert.That(E(0, (4, 2), (5, 3)), Is.EqualTo((true, 0)), "walkable on this layer: entered though both splits, (5, 2) and (4, 3), are blocked");
            Assert.That(E(1, (2, 1), (1, 0)), Is.EqualTo((true, 0)), "off the plate diagonally");
            Assert.That(E(1, (2, 1), (3, 2)), Is.EqualTo((false, 1)), "off the plate where no seam is");
        }

        // The cell of a point is floor((x - origin) / 1 m): exactly on the edge is the next cell, one raw short is this one.
        [Test]
        public void TheCellEdgeDecidesTheLayer()
        {
            InterimLocomotion l = Fixture(out _);
            SimVec3 from = P(1500, 0, 1500);
            SimFix z = SimFix.FromRaw(3 * S / 2);
            (bool ok, SimVec3 next) edge = PathClear(l, 0, from, new SimVec3(SimFix.FromRaw(2 * S), default, z));
            int edgeLayer = Field<int>(l, "candidateLayer");
            (bool ok, SimVec3 next) shy = PathClear(l, 0, from, new SimVec3(SimFix.FromRaw(2 * S - 1), default, z));
            int shyLayer = Field<int>(l, "candidateLayer");
            Assert.That((edge.ok, edgeLayer, edge.next.Y.Raw), Is.EqualTo((true, 1, S / 4)), "on the edge: the plate's cell, its top");
            Assert.That((shy.ok, shyLayer, shy.next.Y.Raw), Is.EqualTo((true, 0, 0L)), "one raw short: still the ground");
        }

        // 3: footprints on the demo house (open cells = this layer's walkable cells and the seam targets of the own cell).
        [Test]
        public void FootprintsStandOnOpenCells()
        {
            SimSession s = Ml8Worlds.Walk(Demo, new[] { Unit(1, 1, 1500, 2000) });
            InterimLocomotion l = M3TestWorld.Locomotion(s);
            (bool, int, long) Step(int layer, SimVec3 from, SimVec3 to)
            {
                (bool ok, SimVec3 next) r = PathClear(l, layer, from, to);
                return (r.ok, Field<int>(l, "candidateLayer"), r.ok ? r.next.Y.Raw : 0);
            }
            long top138 = StairTop(13800 * S / 1000), top102 = StairTop(10200 * S / 1000);
            Assert.That(Step(0, P(9800, 0, 9500), P(10200, 0, 9500)), Is.EqualTo((true, 1, top102)), "from the cell before the stair's foot onto it");
            Assert.That(Step(1, P(10200, top102, 9500), P(9800, 0, 9500)), Is.EqualTo((true, 0, 0L)), "and back down");
            Assert.That(Step(1, P(13800, top138, 9500), P(14200, 0, 9500)), Is.EqualTo((true, 2, 3 * M)), "from the stair's top onto the floor cell it faces");
            Assert.That(Step(1, P(13900, StairTop(13900 * S / 1000), 9300), P(14150, 0, 9200)), Is.EqualTo((true, 2, 3 * M)),
                "the way there opens both ends' cells: the end footprint covers floor cells (13, 8) and (14, 8), closed for the stair");
            Assert.That((Open(l, 24, 1, (13, 9), 14, 9), Open(l, 24, 1, (13, 9), 14, 8), Open(l, 24, 1, (13, 9), 13, 8)), Is.EqualTo((true, false, false)),
                "the stair's top: the faced floor cell is open, its diagonal and its side are not");
            Assert.That((Open(l, 24, 2, (14, 9), 14, 8), Open(l, 24, 2, (14, 9), 13, 9), Open(l, 24, 2, (14, 9), 12, 9)), Is.EqualTo((true, true, false)),
                "the floor cell: its floor neighbour and the stair's top (a seam target) are open, the cutout beyond is not");
            Assert.That(Step(2, P(14240, 3 * M, 9110), P(13990, 0, 9240)).Item1, Is.False,
                "a step onto the stair that would stop with the footprint over floor cell (13, 8), which the stair's rule closes");
            Assert.That(Step(2, P(14240, 3 * M, 9500), P(13990, 0, 9500)).Item1, Is.True, "the same step along the stair's middle");
            Assert.That(Step(2, P(9500, 3 * M, 7500), P(9200, 0, 7500)).Item1, Is.False, "the floor's west edge has no seam: the footprint may not hang over it");
            Assert.That(Step(2, P(9500, 3 * M, 7500), P(9250, 0, 7500)), Is.EqualTo((true, 2, 3 * M)), "touching the edge is allowed");
            Assert.That(Step(0, P(10500, 0, 7500), P(10800, 0, 7500)), Is.EqualTo((true, 0, 0L)), "the ground under the floor (2.5 m clear) walks");
        }

        // 3: an L of two plates that meet with a seam on each of two rows. From (4, 1) only (5, 1) is a seam target, so a
        // footprint that cuts the corner over (5, 2) is refused; opening the whole other plate would let it through.
        [Test]
        public void AnLShapedCornerIsNotCut()
        {
            SimSession s = Ml8Worlds.Walk(Bytes(Ml4Maps.Make(8, 4, Ml4Maps.Flat(1, 6, 2, 2, 2, 3 * M))), new[] { Unit(1, 1, 500, 500) });
            PlateSlab p = Ml5Plates.Flat(1, 2, 1, 3, 2, S / 4, S / 8), q = Ml5Plates.Flat(2, 5, 1, 1, 2, S / 4, S / 8);
            LayeredNav nav = Ml6Nav.Nav(Ml6Nav.Open(8, 4, (2, 1), (3, 1), (4, 1), (2, 2), (3, 2), (4, 2), (5, 1), (5, 2)), p, q);
            Rebind(s, nav, p, q);
            InterimLocomotion l = M3TestWorld.Locomotion(s);
            Assert.That((Open(l, 8, 1, (4, 1), 5, 1), Open(l, 8, 1, (4, 1), 5, 2), Open(l, 8, 2, (5, 2), 4, 2)), Is.EqualTo((true, false, true)));
            SimVec3 from = new SimVec3(SimFix.FromRaw(9 * S / 2), SimFix.FromRaw(S / 4), SimFix.FromRaw(3 * S / 2));
            Assert.That(PathClear(l, 1, from, new SimVec3(SimFix.FromRaw(4900 * S / 1000), default, SimFix.FromRaw(1900 * S / 1000))).ok, Is.False, "corner over (5, 2)");
            Assert.That(PathClear(l, 1, from, new SimVec3(SimFix.FromRaw(4900 * S / 1000), default, SimFix.FromRaw(1500 * S / 1000))).ok, Is.True, "over the seam target (5, 1)");
        }
    }
}
