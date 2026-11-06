using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L1: in a terrain world every unit that gets a Position must stand with its whole footprint (0.25 m) on walkable
    // ground inside the map; otherwise the world is refused instead of placing it silently.
    public sealed class Wp23SpawnTests
    {
        private const long S = Wp23.S;
        // 16 x 8 cells, a wall box over x 10..11, z 2..5, a gentle slope so that the map has a height field.
        private static readonly (int, int, int, int)[] Box = { (10, 2, 1, 3) };

        private static WcMap Map() => Wp23.Make(16, 8, 0, 6 * S, Box, 4 * S, (i, j) => (int)(i * (S / 16)));

        private static SimSession Create(MoverKind mover, WcMap map, params UnitSpawn[] units) =>
            Wp23.Session(map, mover, units, null, Wp23.Pack());

        private static void Accepts(long xMm, long zMm, string why)
        {
            foreach (MoverKind mover in new[] { MoverKind.InterimPath, MoverKind.InterimCrowd })
                Assert.DoesNotThrow(() => Create(mover, Map(), Wp23.Unit(1, 1, xMm, zMm)), why + " " + mover);
        }

        private static void Refuses(long xMm, long zMm, string why)
        {
            foreach (MoverKind mover in new[] { MoverKind.InterimPath, MoverKind.InterimCrowd })
                Assert.That(Assert.Throws<ArgumentException>(() => Create(mover, Map(), Wp23.Unit(7, 1, xMm, zMm)), why + " " + mover).Message,
                    Does.Contain("7"), why);
        }

        // The map edge is a closed interval [min + 0.25 m, max - 0.25 m]: exactly on it stands, 1 mm outside does not.
        [Test]
        public void MapEdges_ClosedAtTheFootprint()
        {
            Accepts(250, 4000, "west edge"); Refuses(249, 4000, "west edge");
            Accepts(15750, 4000, "east edge"); Refuses(15751, 4000, "east edge");
            Accepts(5000, 250, "north edge"); Refuses(5000, 249, "north edge");
            Accepts(5000, 7750, "south edge"); Refuses(5000, 7751, "south edge");
            Accepts(250, 250, "corner"); Refuses(250, 249, "corner");
        }

        // A start that touches a wall (distance exactly 0.25 m) is clear; 1 mm closer is not.
        [Test]
        public void TouchingAWall_IsClear_ButOverlappingIsNot()
        {
            Accepts(9750, 3500, "west face"); Refuses(9751, 3500, "west face");
            Accepts(11250, 3500, "east face"); Refuses(11249, 3500, "east face");
            Accepts(10500, 1750, "north face"); Refuses(10500, 1751, "north face");
            Accepts(10500, 5250, "south face"); Refuses(10500, 5249, "south face");
        }

        // Round corners: at the corner point itself 0.25 m away is clear, and a diagonal start inside the circle is not.
        [Test]
        public void WallCorner_IsRoundedByTheFootprint()
        {
            Accepts(9750, 2000, "beside the corner"); Accepts(10000, 1750, "above the corner");
            Refuses(9850, 1850, "diagonal inside the corner circle");
            Accepts(9823, 1823, "diagonal just outside the corner circle");
        }

        [Test]
        public void InsideAWallOrABlockedSurface_IsRefused()
        {
            Refuses(10500, 3500, "inside the wall");
            WcMap closed = Wp23Fx.Map((x, z) => x == 5 && z == 5);
            foreach (MoverKind mover in new[] { MoverKind.InterimPath, MoverKind.InterimCrowd })
            {
                Assert.Throws<ArgumentException>(() => Wp23Fx.World(mover, closed, Wp23.Unit(1, 1, 5500, 5500)), "blocked surface " + mover);
                Assert.DoesNotThrow(() => Wp23Fx.World(mover, closed, Wp23.Unit(1, 1, 4500, 5500)), "free neighbour " + mover);
                Assert.Throws<ArgumentException>(() => Wp23Fx.World(mover, closed, Wp23.Unit(1, 1, 5000, 5000)), "on a blocked corner " + mover);
            }
        }

        // Without a Match a Reserve unit still gets a Position, so it is checked like the others.
        [Test]
        public void ReserveUnitWithoutAMatch_IsChecked()
        {
            var bad = new UnitSpawn(3, 0, 1, 10500, 3500, 4000, reserve: true);
            var good = new UnitSpawn(3, 0, 1, 2500, 3500, 4000, reserve: true);
            Assert.Throws<ArgumentException>(() => Create(MoverKind.InterimCrowd, Map(), bad));
            Assert.DoesNotThrow(() => Create(MoverKind.InterimCrowd, Map(), good));
        }

        // After creation every unit really is clear: a grid of starts, all walkable ones accepted, the rest refused.
        [Test]
        public void EveryAcceptedUnit_StandsClear()
        {
            WcMap map = Map();
            NavGrid nav = TerrainDerivation.Derive(map, Wp23.Pack()).Nav;
            int accepted = 0, refused = 0;
            ulong key = 1;
            for (long z = 250; z <= 7750; z += 500)
                for (long x = 250; x <= 15750; x += 500)
                {
                    var unit = Wp23.Unit(key++, 1, x, z);
                    bool clear = nav.SegmentClear(Wp23.V(x, 0, z), Wp23.V(x, 0, z), InterimLocomotion.UnitFootprint);
                    if (clear)
                    {
                        SimSession s = Create(MoverKind.InterimCrowd, map, unit);
                        Assert.That(nav.SegmentClear(Wp23.At(s, unit.Key), Wp23.At(s, unit.Key), InterimLocomotion.UnitFootprint), Is.True);
                        accepted++;
                    }
                    else
                    {
                        Assert.Throws<ArgumentException>(() => Create(MoverKind.InterimCrowd, map, unit), $"{x},{z}");
                        refused++;
                    }
                }
            Assert.That(accepted, Is.GreaterThan(100));
            Assert.That(refused, Is.GreaterThan(5));
        }

        // A world without terrain keeps its old behaviour: the same start inside a wall box is still accepted.
        [Test]
        public void FlatWorld_IsUnchanged()
        {
            WcMap flat = Wp23.Make(16, 8, 0, 6 * S, Box, 4 * S);
            foreach (MoverKind mover in new[] { MoverKind.InterimPath, MoverKind.InterimCrowd })
                Assert.DoesNotThrow(() => Wp23.Session(flat, mover, new[] { Wp23.Unit(1, 1, 10500, 3500) }), mover.ToString());
        }
    }
}
