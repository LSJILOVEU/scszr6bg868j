using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Sim;
using static WarCore.Tests.Ml6Nav;

namespace WarCore.Tests
{
    // ML-6 acceptance 6 and the bridge half of 2l: graphs built from baked .wcmap files by WcMapLayeredNav.
    public sealed class Ml6BridgeTests
    {
        private const long M = SimFix.Scale;

        private static MapDesc Desc(int w, int d, MapXZ origin, long floor, int[][] heights, MapSurfaces surfaces, params MapPlate[] plates) =>
            new MapDesc("ml6", "none", "rwr-m6", new MapXZ(w, d), origin, floor, floor + 10 * M,
                new[] { new MapLayer("walls", 1) }, heights, surfaces, plates, new MapCutout[0], new MapWall[0],
                new MapPoint[0], new MapBase[0], new MapUnit[0], new MapRun(41, "none"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(1, 1, 1, 1, plates.Max(p => p.Id) + 1, 1));

        private static MapSurfaces Surfaces(int w, int d, params string[] names) =>
            new MapSurfaces(names, Enumerable.Repeat(new string('A', w), d).ToArray());

        // 6a: the ML-4 demo house (pack rwr-m6 has no terrain section: no rules, slope 2/1). Ground 24 x 16 = nodes 0..383;
        // stair 384..387 (x 9..12, z 9); first floor 388..407 (z 6: 388..393, z 7: 394..399, z 8: 400..405, z 9: x 13, 14 =
        // 406, 407; the cutout over the stair has no nodes); roof 408..431. The stair's foot at x = 9 faces wall 4 on cell
        // (8, 9) and its sides rise 0.75 m a cell, so the only seam is the stair's top 387 <-> first floor (13, 9) 406.
        [Test]
        public void DemoHouse()
        {
            WcMap map = Ml4Maps.Bake(Ml4Maps.Demo());
            LayeredNav nav = WcMapLayeredNav.Build(map, null, "rwr-m6");
            Assert.That(WcMapLayeredNav.Build(map, RwrPack.Load("rwr-m6")).Digest, Is.EqualTo(nav.Digest), "the pack overload is the same graph");
            Assert.That((nav.GroundCount, nav.LayerCount, nav.Count), Is.EqualTo((384, 4, 432)));
            for (int x = 9; x <= 12; x++) Assert.That(Node(nav, 1, x, 9), Is.EqualTo(384 + x - 9), "stair");
            for (int z = 6; z <= 8; z++)
                for (int x = 9; x <= 14; x++) Assert.That(Node(nav, 2, x, z), Is.EqualTo(388 + (z - 6) * 6 + x - 9), "first floor");
            Assert.That((Node(nav, 2, 13, 9), Node(nav, 2, 14, 9)), Is.EqualTo((406, 407)));
            for (int x = 9; x <= 12; x++) Assert.That(nav.TryNode(2, x, 9, out _), Is.False, "cutout over the stair");
            Assert.That((Node(nav, 3, 9, 6), Node(nav, 3, 14, 9)), Is.EqualTo((408, 431)), "roof");
            Assert.That((nav.IsPassable(Node(nav, 2, 11, 6)), nav.IsPassable(Node(nav, 2, 11, 8)), nav.IsPassable(Node(nav, 2, 11, 7))),
                Is.EqualTo((false, false, true)), "partition walls 6 and 7, lintel 8 over the doorway");
            Assert.That(AllSeams(nav), Is.EqualTo(new[] { (387, 406), (406, 387) }));

            var f = new LayeredPathfinder(nav);
            Assert.That(f.Find(7 * 24 + 5, Node(nav, 2, 13, 7)), Is.EqualTo(GridPathStatus.Unreachable), "from outside the stair cannot be climbed");
            Found(f, 406, 388, 5828, 406, 404, 397, 396, 395, 388);
            Found(f, 384, 393, 7414, 384, 385, 386, 387, 406, 405, 399, 393);
            Assert.That(f.Expanded, Is.EqualTo(8));
        }

        // 6b: a two-storey block on a map with a negative origin, a height field and a surface table, whose stair foot is open:
        // ground 10 x 4 at height 0; stair (plate 1) on cells [2,1] size [4,1] rising along x 0 -> 3 m (3/4); first floor
        // (plate 2) on [6,0] size [3,3] at 3 m. Nodes: ground 0..39, stair 40..43, floor 44..52 (row-major). From ground
        // (0,1) = 10: 10 -> 11 -> 40 -> 41 -> 42 -> 43 -> 47 -> 48 -> 49 (floor (8,1)), eight orthogonal steps of cost 1.
        [Test]
        public void UpAnOpenStair()
        {
            MapDesc desc = Desc(10, 4, new MapXZ(-4, -2), 0, Ml4Maps.Ground(10, 4, 0), Surfaces(10, 4, "dirt"),
                Ml4Maps.Slope(1, 2, 1, 4, 1, "x", 0, 3 * M), Ml4Maps.Flat(2, 6, 0, 3, 3, 3 * M));
            WcMap map = Ml4Maps.Bake(desc);
            Assert.That((map.Min.X.Raw, map.Min.Z.Raw, map.HasHeightField, map.HasSurfaceTable), Is.EqualTo((-4 * M, -2 * M, true, true)));
            var rules = new TerrainRules("ml6", 3, 4, new[] { new SurfaceDef("dirt", true, 1000, 1) });
            LayeredNav nav = WcMapLayeredNav.Build(map, rules, "ml6");
            Assert.That(nav.Count, Is.EqualTo(53));
            Assert.That(Seams(nav, 11), Is.EqualTo(new[] { 40 }), "the stair's foot");
            Found(new LayeredPathfinder(nav), 10, 49, 8000, 10, 11, 40, 41, 42, 43, 47, 48, 49);
        }

        // 6c: a plate's surface gives its cost, speed and passability; without one it is cost 1, speed 1000. Plates 1..3 on
        // cells (0,0), (1,0), (2,0) of a 4 x 2 flat map, with surfaces mud, none and lava (nodes 8, 9, 10). The ground is
        // lava, so a plate without a surface does not take the ground's.
        [Test]
        public void PlateSurfaces()
        {
            MapDesc desc = Desc(4, 2, new MapXZ(0, 0), 0, null, new MapSurfaces(new[] { "dirt", "lava", "mud" }, new[] { "BBBB", "BBBB" }),
                Ml4Maps.Flat(1, 0, 0, 1, 1, 0, surface: "mud"), Ml4Maps.Flat(2, 1, 0, 1, 1, 0), Ml4Maps.Flat(3, 2, 0, 1, 1, 0, surface: "lava"));
            WcMap map = Ml4Maps.Bake(desc);
            SurfaceDef dirt = new SurfaceDef("dirt", true, 1000, 1), mud = new SurfaceDef("mud", true, 500, 4), lava = new SurfaceDef("lava", false, 0, 0);
            LayeredNav nav = WcMapLayeredNav.Build(map, new TerrainRules("ml6", 3, 4, new[] { dirt, lava, mud }), "ml6");
            Assert.That((nav.CostAt(8), nav.SpeedMilliAt(8)), Is.EqualTo(((byte)4, (ushort)500)), "mud");
            Assert.That((nav.CostAt(9), nav.SpeedMilliAt(9)), Is.EqualTo(((byte)1, (ushort)1000)), "no surface");
            Assert.That((nav.CostAt(10), nav.SpeedMilliAt(10), nav.IsPassable(10)), Is.EqualTo(((byte)0, (ushort)0, false)), "lava");

            foreach (TerrainRules missing in new[] { null, new TerrainRules("ml6", 3, 4, new[] { dirt, lava }) })
            {
                string want = Assert.Throws<ContentException>(() => TerrainDerivation.Derive(map, missing, "ml6")).Message;
                Assert.That(Assert.Throws<ContentException>(() => WcMapLayeredNav.Build(map, missing, "ml6")).Message, Is.EqualTo(want));
                StringAssert.Contains(missing == null ? "has no terrain section" : "has no entry 'mud'", want);
            }
        }

        // 6d: the pack's walkable slope decides a stair cell: at 3/4 a 49152 raw rise is walkable, 49153 is not.
        [TestCase(49152, true)]
        [TestCase(49153, false)]
        public void PackSlope(long rise, bool walkable)
        {
            WcMap map = Ml4Maps.Bake(Desc(3, 1, new MapXZ(0, 0), 0, null, null, Ml4Maps.Slope(1, 1, 0, 1, 1, "x", 0, rise)));
            LayeredNav nav = WcMapLayeredNav.Build(map, new TerrainRules("ml6", 3, 4, new SurfaceDef[0]), "ml6");
            Assert.That(nav.IsPassable(3), Is.EqualTo(walkable));
            Assert.That(WcMapLayeredNav.Build(map, null, "ml6").IsPassable(3), Is.True, "no rules: 2/1");
        }

        // 2l through the bridge: a flat map's ground is Min.Y (floor 2 m here), so a plate 19660 raw over it has a seam.
        [TestCase(19660, true)]
        [TestCase(19661, false)]
        public void FlatGroundIsMinY(long over, bool seam)
        {
            WcMap map = Ml4Maps.Bake(Desc(2, 1, new MapXZ(0, 0), 2 * M, null, null, Ml4Maps.Flat(1, 1, 0, 1, 1, 2 * M + over)));
            Assert.That(map.Min.Y.Raw, Is.EqualTo(2 * M));
            LayeredNav nav = WcMapLayeredNav.Build(map, null, "ml6");
            Assert.That(Seams(nav, 0), Is.EqualTo(seam ? new[] { 2 } : new int[0]));
        }

        // 6e: no plates, no graph; the ground is TerrainDerivation's; the limits are ML-4's and ML-5's.
        [Test]
        public void NoPlatesGroundAndLimits()
        {
            Assert.That(WcMapLayeredNav.Build(Ml4Maps.Bake(Ml4Maps.Make(8, 4)), null, "rwr-m6"), Is.Null);
            WcMap map = Ml4Maps.Bake(Ml4Maps.Demo());
            Assert.That(WcMapLayeredNav.Build(map, null, "rwr-m6").Ground.Digest, Is.EqualTo(TerrainDerivation.Derive(map, null, "rwr-m6").Digest));
            Assert.That((LayeredNav.MaxPlates, LayeredNav.MaxPerCell), Is.EqualTo((PlateSpan.MaxPlates, PlateSpan.MaxPerCell)));
            Assert.That((LayeredNav.MaxPlates, LayeredNav.MaxPerCell), Is.EqualTo((PlateField.MaxPlates, PlateField.MaxPerCell)));
            Assert.Throws<ArgumentNullException>(() => WcMapLayeredNav.Build(null, null, "x"));
            Assert.Throws<ArgumentNullException>(() => WcMapLayeredNav.Build(map, (ContentPack)null));
        }
    }
}
