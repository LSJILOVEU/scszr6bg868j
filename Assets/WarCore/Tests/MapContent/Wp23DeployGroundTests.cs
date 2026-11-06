using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L2: deployment reads the ground. In a terrain world a deployed unit stands only where its footprint is clear (the exact
    // query of the navigation grid) and at the height of the height field; a flat world keeps its footprint-corner test.
    public sealed class Wp23DeployGroundTests
    {
        private const long S = Wp23.S;
        private static readonly (int, int, int, int)[] Box = { (10, 2, 1, 3) };
        private static readonly SimFix Radius = SimFix.Ratio(1, 4);

        private static WcMap TerrainMap() => Wp23.Make(16, 8, 0, 6 * S, Box, 4 * S, (i, j) => (int)(i * (S / 16)));
        private static WcMap FlatMap() => Wp23.Make(16, 8, 0, 6 * S, Box, 4 * S);

        private static DeployGround Terrain(WcMap map, out TerrainTables tables)
        {
            tables = TerrainDerivation.Derive(map, Wp23.Pack());
            return new DeployGround(tables.Nav, tables, map.FlatHeight.Raw);
        }

        private static DeployGround Flat(WcMap map) => new DeployGround(WcMapNavigation.Create(map), null, map.FlatHeight.Raw);

        private static SimVec3 P(long xMm, long zMm) => new SimVec3(Wp23.Mm(xMm), default, Wp23.Mm(zMm));

        // The standing test of the terrain world is the exact footprint query: the same answers at the edges and at walls.
        [Test]
        public void TerrainWorld_StandsWhereTheFootprintIsClear()
        {
            DeployGround ground = Terrain(TerrainMap(), out _);
            Assert.That(ground.CanStand(P(250, 4000)), Is.True, "west edge");
            Assert.That(ground.CanStand(P(249, 4000)), Is.False, "outside the west edge");
            Assert.That(ground.CanStand(P(15750, 4000)), Is.True, "east edge");
            Assert.That(ground.CanStand(P(15751, 4000)), Is.False, "outside the east edge");
            Assert.That(ground.CanStand(P(9750, 3500)), Is.True, "touching the wall");
            Assert.That(ground.CanStand(P(9751, 3500)), Is.False, "overlapping the wall");
            Assert.That(ground.CanStand(P(10500, 3500)), Is.False, "inside the wall");
            // The old footprint-corner test would call this corner overlap clear: the circle is exact now.
            Assert.That(ground.CanStand(P(9850, 1850)), Is.False, "inside the round corner");
        }

        [Test]
        public void TerrainWorld_StandsOnTheHeightField()
        {
            WcMap map = TerrainMap();
            DeployGround ground = Terrain(map, out TerrainTables tables);
            foreach (long xMm in new long[] { 500, 3333, 8000, 15500 })
            {
                SimVec3 at = P(xMm, 1500);
                Assert.That(tables.Ground.TryHeightAt(at.X, at.Z, out SimFix expected), Is.True);
                Assert.That(ground.TryGround(at, out SimVec3 on), Is.True);
                Assert.That(on.Y.Raw, Is.EqualTo(expected.Raw), xMm.ToString());
                Assert.That(on.X, Is.EqualTo(at.X)); Assert.That(on.Z, Is.EqualTo(at.Z));
            }
            Assert.That(tables.Ground.TryHeightAt(P(15500, 1500).X, P(15500, 1500).Z, out SimFix east), Is.True);
            Assert.That(east.Raw, Is.GreaterThan(tables.Ground.TryHeightAt(P(500, 1500).X, P(500, 1500).Z, out SimFix west) ? west.Raw : 0), "the slope rises");
            // Outside the field there is no ground to stand on.
            Assert.That(ground.TryGround(P(-100, 1500), out _), Is.False);
            Assert.That(ground.TryGround(P(16100, 1500), out _), Is.False);
        }

        // A terrain world with surfaces but no height field keeps the flat floor.
        [Test]
        public void SurfaceOnlyWorld_KeepsTheFlatFloor()
        {
            WcMap map = Wp23.Make(16, 8, 3 * S, 9 * S, null, 4 * S, null, Wp23.Names, (x, z) => (byte)(x < 8 ? 0 : 1));
            DeployGround ground = Terrain(map, out TerrainTables tables);
            Assert.That(tables.Ground, Is.Null);
            Assert.That(ground.TryGround(P(5000, 4000), out SimVec3 on), Is.True);
            Assert.That(on.Y.Raw, Is.EqualTo(3 * S));
        }

        // A flat world is unchanged: the footprint-corner test and the flat floor, checked against a copy of the old rule.
        [Test]
        public void FlatWorld_MatchesTheOldRule()
        {
            WcMap map = FlatMap();
            DeployGround ground = Flat(map);
            NavGrid grid = WcMapNavigation.Create(map);
            int checkedPoints = 0, blocked = 0;
            for (long z = -250; z <= 8250; z += 125)
                for (long x = -250; x <= 16250; x += 125)
                {
                    SimVec3 at = P(x, z);
                    Assert.That(ground.CanStand(at), Is.EqualTo(OldRule(grid, at)), $"{x},{z}");
                    Assert.That(ground.TryGround(at, out SimVec3 on), Is.True);
                    Assert.That(on.Y.Raw, Is.EqualTo(map.FlatHeight.Raw));
                    checkedPoints++; if (!OldRule(grid, at)) blocked++;
                }
            Assert.That(checkedPoints, Is.GreaterThan(5000));
            Assert.That(blocked, Is.GreaterThan(200));
        }

        private static bool OldRule(NavGrid grid, SimVec3 at)
        {
            if (!grid.TryCell(at, out int cell) || !grid.IsPassable(cell % grid.Width, cell / grid.Width)) return false;
            for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                {
                    var corner = at + new SimVec3(Radius * SimFix.FromInt(x), default, Radius * SimFix.FromInt(z));
                    if (!grid.TryCell(corner, out int c) || !grid.IsPassable(c % grid.Width, c / grid.Width)) return false;
                }
            return true;
        }

        // The digest that feeds the deployer's policy key is the grid's own.
        [Test]
        public void Digest_IsTheGridsDigest()
        {
            WcMap map = FlatMap();
            NavGrid grid = WcMapNavigation.Create(map);
            Assert.That(new DeployGround(grid, null, 0).Digest, Is.EqualTo(grid.Digest));
        }
    }
}
