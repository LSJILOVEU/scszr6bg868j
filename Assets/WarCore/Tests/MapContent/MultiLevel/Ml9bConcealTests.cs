using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml9World;

namespace WarCore.Tests
{
    // ML-9b acceptance 7: a unit on a floor plate has no ground cover under it (factor 1000); a unit on the ground keeps it.
    public sealed class Ml9bConcealTests
    {
        // The layer of a unit by its foot height: above the ground means on the plate (layer 1).
        private sealed class ByHeight : IUnitLayers
        {
            internal Ml9World World;
            public int LayerOf(EntityId unit) => World.Combat.Point(unit).Y.Raw > 0 ? 1 : 0;
        }

        // Shooter (team 1) at (8.5, 15.5), target A 4 m away at (8.5, 11.5) over a ground cell with cover 100, target B 6 m away
        // at (14.5, 15.5) in the open; range 30 m, so A as cover sees it only within 3 m. All three on the floor (3 m) or all on
        // the ground (no plates).
        private static int Choose(bool plates, bool layers)
        {
            long y = plates ? Top : 0;
            var by = layers ? new ByHeight() : null;
            var selector = new ConcealedSight(new NearestVisibleEnemy(), A2.Grid(32, 32), A2.Cover(32, 32, (x, z) => (short)(x == 8 && z == 11 ? 100 : 1000)), by);
            var w = new Ml9World(Pack(Ml3World.Rwr, selector: selector), new[] { U(8500, y, 15500, 1), U(8500, y, 11500, 2), U(14500, y, 15500, 2) },
                plates ? Floor() : null);
            if (by != null) by.World = w;
            Assert.That(w.Combat.Visible(w.Unit(0), w.Unit(1)) && w.Combat.Visible(w.Unit(0), w.Unit(2)), Is.True, "both in sight");
            if (!selector.TrySelect(w.Combat, w.Unit(0), SimFix.FromInt(30), out EntityId target)) return -1;
            return target.Equals(w.Unit(1)) ? 1 : target.Equals(w.Unit(2)) ? 2 : -2;
        }

        [Test]
        public void TheFloorHasNoGroundCover()
        {
            Assert.That(Choose(plates: true, layers: true), Is.EqualTo(1), "on the floor the grass below does not hide A");
            Assert.That(Choose(plates: true, layers: false), Is.EqualTo(2), "without layers the cell is read as before");
            Assert.That(Choose(plates: false, layers: true), Is.EqualTo(2), "on the ground the grass hides A");
            Assert.That(Choose(plates: false, layers: false), Is.EqualTo(2));
        }

        [Test]
        public void TheKeyDoesNotChangeWithLayers()
        {
            short[] cover = A2.Cover(32, 32, (x, z) => 500);
            Assert.That(new ConcealedSight(new NearestVisibleEnemy(), A2.Grid(32, 32), cover, new ByHeight()).PolicyKey,
                Is.EqualTo(new ConcealedSight(new NearestVisibleEnemy(), A2.Grid(32, 32), cover).PolicyKey));
        }

        // The bridge hands the plate layers to the cover it builds on a terrain map with plates (and none without plates): unit 30
        // stands on the floor, the others on the ground.
        [TestCase(true)]
        [TestCase(false)]
        public void TheBridgeHandsTheLayersToCover(bool plates)
        {
            string[] rows = Enumerable.Range(0, 40).Select(z => new string('A', 20) + new string('B', 4) + new string('A', 56)).ToArray();
            MapDesc desc = Ml4Maps.Make(0, 80, 40, null, plates ? new[] { Ml4Maps.Flat(1, 40, 30, 4, 4, 3 * M) } : new MapPlate[0],
                surfaces: new MapSurfaces(new[] { "dirt", "grass" }, rows));
            var units = RespawnWorld.Units(Ml9bMatchTests.L(30, 41500, 32500, plates ? 1 : 0));
            WorldRecipe recipe = RespawnWorld.Recipe(units, RespawnWorld.Pack(RespawnWorld.Points(), RespawnWorld.Bases(), 0), false)
                .WithTerrainRules(A2.Pack(grass: ", \"concealMilli\": 500"));
            var w = new M6World(plates ? recipe.WithPlatesPreview() : recipe, Ml7Worlds.Bytes(desc));
            Assert.That(w.Combat.Pack.Selector, Is.InstanceOf<ConcealedSight>());
            IUnitLayers layers = Ml7Worlds.Field<IUnitLayers>(w.Combat.Pack.Selector, "layers");
            Assert.That(layers, plates ? Is.Not.Null : Is.Null);
            if (!plates) return;
            Assert.That((layers.LayerOf(w.Unit(30)), layers.LayerOf(w.Unit(RespawnWorld.Ally))), Is.EqualTo((1, 0)));
        }
    }
}
