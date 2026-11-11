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
        private static ContentPack Pack() =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
            {
                ["map-spawn"] = "{\"id\":\"map-spawn\",\"extends\":\"rwr-melee\",\"block\":["
                    + "{\"id\":\"unused\",\"cosHalfAnglePermille\":0,\"reductionPermille\":0,\"aiRaisePermille\":0},"
                    + "{\"id\":\"shield\",\"cosHalfAnglePermille\":500,\"reductionPermille\":750,\"aiRaisePermille\":1000}],"
                    + "\"unitTypes\":[{\"id\":\"guard\",\"weapon\":\"rwr.weapon-2\",\"melee\":\"rwr.sword-1\","
                    + "\"speed\":4200,\"archetype\":7,\"block\":\"shield\"}]}"
            }, FileContentSource.Rwr()), "map-spawn");

        private static MapBakeResult Map() => Bake(Desc("map-spawn", null, null, "", "",
            Unit(17, "guard", 2, 3, 3500, 4500, false) + ", " + Unit(91, "guard", 1, 8, 92500, 4500, true),
            41, "none", 1, 1, 92));

        [Test]
        public void EnabledSpawnsUseContentInTheWorld()
        {
            MapBakeResult map = Map();
            WorldRecipe recipe = MapRecipe.From(map, Pack(), useUnitTypes: true);
            Assert.That(recipe.Units.Length, Is.EqualTo(2));
            UnitSpawn first = recipe.Units[0], reserve = recipe.Units[1];
            Assert.That((first.Key, first.Squad, first.Team, first.XMm, first.ZMm, first.Reserve),
                Is.EqualTo((17UL, 3, 2, 3500L, 4500L, false)));
            Assert.That((reserve.Key, reserve.Squad, reserve.Team, reserve.XMm, reserve.ZMm, reserve.Reserve),
                Is.EqualTo((91UL, 8, 1, 92500L, 4500L, true)));
            var world = new M6World(recipe, map.WcMapBytes);
            foreach (UnitSpawn spawn in recipe.Units)
            {
                Assert.That((spawn.Archetype, spawn.SpeedMmPerSec, spawn.Weapon, spawn.Melee, spawn.Block),
                    Is.EqualTo((7, 4200L, 1, 0, 1)));
                Assert.That(world.Read<MoveIntent>(spawn.Key).SpeedPerTick, Is.EqualTo(SimFix.Ratio(4200, 1000 * SimClock.TickRate)));
                Assert.That(world.Combat.CanBlock(world.Unit(spawn.Key)), Is.True);
                BlockSpec block = world.Combat.BlockSpecOf(world.Unit(spawn.Key));
                Assert.That((block.CosHalfAnglePermille, block.ReductionPermille, block.AiRaisePermille),
                    Is.EqualTo((500, 750, 1000)));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void DisabledSpawnsKeepLegacyValues(bool omitSwitch)
        {
            MapBakeResult map = Map();
            ContentPack pack = Pack();
            WorldRecipe recipe = omitSwitch ? MapRecipe.From(map, pack) : MapRecipe.From(map, pack, useUnitTypes: false);
            var world = new M6World(recipe, map.WcMapBytes);
            foreach (UnitSpawn spawn in recipe.Units)
            {
                Assert.That((spawn.Archetype, spawn.SpeedMmPerSec, spawn.Weapon, spawn.Melee, spawn.Block),
                    Is.EqualTo((0, 6000L, 1, 0, -1)));
                Assert.That(world.Read<MoveIntent>(spawn.Key).SpeedPerTick, Is.EqualTo(SimFix.Ratio(6000, 1000 * SimClock.TickRate)));
                Assert.That(world.Combat.CanBlock(world.Unit(spawn.Key)), Is.False);
            }
        }
    }
}
