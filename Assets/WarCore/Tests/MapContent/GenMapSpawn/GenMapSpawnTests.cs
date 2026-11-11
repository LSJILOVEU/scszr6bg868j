using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.E1aSamples;
using static WarCore.Tests.E1bSamples;

namespace WarCore.Tests
{
    public sealed class GenMapSpawnTests
    {
        private static ContentPack Pack() => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["map-spawn"] = "{\"id\":\"map-spawn\",\"extends\":\"rwr-melee\",\"block\":["
                + "{\"id\":\"unused\",\"cosHalfAnglePermille\":0,\"reductionPermille\":0,\"aiRaisePermille\":0},"
                + "{\"id\":\"shield\",\"cosHalfAnglePermille\":500,\"reductionPermille\":750,\"aiRaisePermille\":1000}],"
                + "\"unitTypes\":[{\"id\":\"guard\",\"weapon\":\"rwr.weapon-2\",\"melee\":\"rwr.sword-1\","
                + "\"speed\":4200,\"archetype\":7,\"block\":\"shield\"}]}"
        }, FileContentSource.Rwr()), "map-spawn");

        private static MapBakeResult Map(bool reserve) => Bake(Desc("map-spawn", null, null, "", "",
            Unit(17, "guard", 2, 5, 3500, 4500, reserve), 77, "none", 1, 1, 18));

        [TestCase(false)]
        [TestCase(true)]
        public void EnabledUsesUnitTypeInTheWorld(bool reserve)
        {
            MapBakeResult map = Map(reserve);
            WorldRecipe recipe = MapRecipe.From(map, Pack(), useUnitTypes: true);
            UnitSpawn unit = recipe.Units[0];
            Assert.That((unit.Key, unit.Squad, unit.Team, unit.XMm, unit.ZMm, unit.Reserve),
                Is.EqualTo((17UL, 5, 2, 3500L, 4500L, reserve)));
            Assert.That((unit.Archetype, unit.Weapon, unit.Melee, unit.Block, unit.SpeedMmPerSec),
                Is.EqualTo((7, 1, 0, 1, 4200L)));
            var world = new M6World(recipe, map.WcMapBytes);
            Assert.That(world.Read<MoveIntent>(17).SpeedPerTick, Is.EqualTo(SimFix.Ratio(4200, 1000 * SimClock.TickRate)));
            Assert.That(world.Combat.CanBlock(world.Unit(17)), Is.True);
            BlockSpec block = world.Combat.BlockSpecOf(world.Unit(17));
            Assert.That((block.CosHalfAnglePermille, block.ReductionPermille, block.AiRaisePermille), Is.EqualTo((500, 750, 1000)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DefaultAndDisabledKeepLegacySpawnValues(bool omitSwitch)
        {
            MapBakeResult map = Map(false);
            ContentPack pack = Pack();
            WorldRecipe recipe = omitSwitch ? MapRecipe.From(map, pack) : MapRecipe.From(map, pack, useUnitTypes: false);
            UnitSpawn unit = recipe.Units[0];
            Assert.That((unit.Archetype, unit.Weapon, unit.Melee, unit.Block, unit.SpeedMmPerSec),
                Is.EqualTo((0, 1, 0, -1, 6000L)));
            var world = new M6World(recipe, map.WcMapBytes);
            Assert.That(world.Read<MoveIntent>(17).SpeedPerTick, Is.EqualTo(SimFix.Ratio(6000, 1000 * SimClock.TickRate)));
            Assert.That(world.Combat.CanBlock(world.Unit(17)), Is.False);
        }
    }
}
