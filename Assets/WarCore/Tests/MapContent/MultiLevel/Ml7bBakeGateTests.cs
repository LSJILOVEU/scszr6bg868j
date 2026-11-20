using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.Ml7bMaps;

namespace WarCore.Tests
{
    // ML-7b acceptance 3 (bake), 2 (PLATE_ANCHOR), 2c (gate by plate) and the bake and gate sides of 2b (cell edges).
    public sealed class Ml7bBakeGateTests
    {
        private static MapProblem Footprint(string id, int x, int z, long xMm, long zMm) => new MapProblem("GATE_SPAWN_FOOTPRINT", id, (x, z), new MapMm(xMm, zMm));

        // Plates listed 9 then 5: layers follow ascending id, so 5 (the stair) is layer 1 and 9 (the first floor) layer 2.
        [Test]
        public void PlateIdsBecomeLayersAndAnchorsStandOnTheTop()
        {
            MapSetup setup = Setup(Ml7bDescTests.OnPlates());
            Assert.That((setup.ControlPoints[0].SpawnLayer, setup.ControlPoints[0].Spawn.Y.Raw), Is.EqualTo((2, 3 * M)), "spawn on the first floor");
            Assert.That((setup.ControlPoints[1].SpawnLayer, setup.ControlPoints[1].Spawn.Y.Raw), Is.EqualTo((0, 0L)));
            Assert.That((setup.Bases[0].Layer, setup.Bases[0].Point.Y.Raw, setup.Bases[0].Point.X.Raw), Is.EqualTo((2, 3 * M, 11500 * M / 1000)));
            Assert.That((setup.Bases[1].Layer, setup.Bases[1].Point.Y.Raw), Is.EqualTo((0, 0L)));
            Assert.That((setup.Soldiers[0].Layer, setup.Soldiers[1].Layer), Is.EqualTo((1, 0)), "unit 1 on the stair (id 5)");
            Assert.That(setup.ControlPoints[0].Center.Y.Raw, Is.EqualTo(0L), "the point's centre stays on the ground (ML-9)");

            // On the stair: 0 -> 3 m over x 10..14, so x = 12.25 m stands at 2.25 * 0.75 m.
            MapSetup slope = Setup(House(new[] { Point(1, 5000, 13000, new MapMm(12250, 9500), 5), Point(2, 19000, 13000, new MapMm(19000, 13000)) }));
            Assert.That((slope.ControlPoints[0].SpawnLayer, slope.ControlPoints[0].Spawn.Y.Raw), Is.EqualTo((1, 9 * M / 4 * 3 / 4)));
        }

        [Test]
        public void AnAnchorOffASolidCellIsRefused()
        {
            Assert.That(BakeError(House(bases: new[] { Base(1, 1, 11500, 9500, 9), Base(2, 2, 22000, 8000) })),
                Is.EqualTo("MAPDESC_PLATE_ANCHOR base 1"), "the first floor's cutout over the stair");
            Assert.That(BakeError(House(units: new[] { Unit(1, 1500, 1500), Unit(2, 3500, 3500, 9) })), Is.EqualTo("MAPDESC_PLATE_ANCHOR unit 2"), "off the plate");
            Assert.That(BakeError(House(new[] { Point(1, 5000, 13000, new MapMm(16500, 7500), 9), Point(2, 19000, 13000, new MapMm(19000, 13000)) })),
                Is.EqualTo("MAPDESC_PLATE_ANCHOR spawn 1"), "east of the plate");
            Assert.That(Setup(House(units: new[] { Unit(1, 14500, 6500, 9) })).Soldiers[0].Layer, Is.EqualTo(2), "a solid cell under a wall bakes: walkability is not the bake's");
        }

        // 2c: anchors on walkable plate cells pass, though the ground under the stair is closed by its headroom.
        [Test]
        public void TheGateReadsThePlateLayer()
        {
            Assert.That(Gate(Ml7bDescTests.OnPlates()), Is.Empty);
            Assert.That(Gate(House(units: new[] { Unit(1, 12500, 9500) })), Is.EqualTo(new[] { Footprint("unit 1", 12, 9, 12500, 9500) }),
                "the same point on the ground is closed");
            Assert.That(Gate(House(units: new[] { Unit(3, 14500, 6500, 9) })), Is.EqualTo(new[] { Footprint("unit 3 layer 2", 14, 6, 14500, 6500) }),
                "a plate cell under a wall");
            Assert.That(Gate(House(bases: new[] { Base(1, 1, 14500, 6500, 9), Base(2, 2, 22000, 8000) })),
                Is.EqualTo(new[] { Footprint("base 1 layer 2", 14, 6, 14500, 6500) }));
            Assert.That(Gate(House(new[] { Point(1, 5000, 13000, new MapMm(14500, 6500), 9), Point(2, 19000, 13000, new MapMm(19000, 13000)) })),
                Is.EqualTo(new[] { Footprint("spawn 1 layer 2", 14, 6, 14500, 6500) }));
            Assert.That(Gate(House()), Is.Empty, "no plate ids: the ground rules, as before");
        }

        // 2c: a base on the first floor reaches both points down the stair; without the stair it reaches neither.
        [Test]
        public void AFloorBaseReachesThePointsDownTheStair()
        {
            MapBase[] bases = { Base(1, 1, 11500, 7500, 9), Base(2, 2, 22000, 8000) };
            Assert.That(Gate(House(bases: bases)), Is.Empty);
            Assert.That(Gate(House(bases: bases, stair: false)), Is.EqualTo(new[]
            {
                new MapProblem("GATE_UNREACHABLE", "base 1 point 1", (11, 7), new MapMm(11500, 7500)),
                new MapProblem("GATE_UNREACHABLE", "base 1 point 2", (11, 7), new MapMm(11500, 7500)),
            }));
        }

        // 2b, bake and gate: the cutout's minimum x edge (10 m) belongs to the cutout cell (10, 9); 1 mm less is floor cell (9, 9).
        [Test]
        public void TheCellEdgeDecidesBakeAndGate()
        {
            MapDesc Edge(long xMm) => House(units: new[] { Unit(1, xMm, 9500, 9) });
            Assert.That(BakeError(Edge(10000)), Is.EqualTo("MAPDESC_PLATE_ANCHOR unit 1"));
            Assert.That(Setup(Edge(9999)).Soldiers[0].Layer, Is.EqualTo(2));
            Assert.That(Gate(Edge(10000)), Is.EqualTo(new[] { Footprint("unit 1 layer 2", 10, 9, 10000, 9500) }));
            Assert.That(Gate(Edge(9999)), Is.Empty);
        }
    }
}
