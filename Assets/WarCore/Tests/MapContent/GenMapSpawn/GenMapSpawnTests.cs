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
                    + "\"speed\":4200,\"block\":\"shield\",\"archetype\":7}]}"
            }, FileContentSource.Rwr()), "map-spawn");

        private static MapBakeResult Map() => Bake(Desc("map-spawn", null, null, OnePoint, TwoBases,
            Unit(17, "guard", 1, 3, 3500, 2500, false) + "," + Unit(29, "guard", 2, 8, 6500, 4500, true),
            41, "none", 2, 3, 30));

        [Test]
        public void OptInUsesContentSpeedAndBlockInWorld()
        {
            MapBakeResult map = Map();
            WorldRecipe recipe = MapRecipe.From(map, Pack(), useUnitTypes: true);
            Assert.That(recipe.Units.Length, Is.EqualTo(2));
            UnitSpawn active = recipe.Units[0], reserve = recipe.Units[1];
            Assert.That((active.Key, active.Squad, active.Team, active.XMm, active.ZMm, active.Reserve),
                Is.EqualTo((17UL, 3, 1, 3500L, 2500L, false)));
            Assert.That((reserve.Key, reserve.Squad, reserve.Team, reserve.XMm, reserve.ZMm, reserve.Reserve),
                Is.EqualTo((29UL, 8, 2, 6500L, 4500L, true)));
            var world = new M6World(recipe, map.WcMapBytes);
            Assert.That(world.Read<MoveIntent>(17).SpeedPerTick, Is.EqualTo(SimFix.Ratio(4200, 30000)));
            Assert.That(world.Has<Position>(29), Is.False);
            foreach (UnitSpawn unit in recipe.Units)
            {
                Assert.That((unit.Archetype, unit.Weapon, unit.Melee, unit.Block, unit.SpeedMmPerSec),
                    Is.EqualTo((7, 1, 0, 1, 4200L)));
                Assert.That(world.Read<BlockState>(unit.Key).Spec, Is.EqualTo(1));
                BlockSpec block = world.Combat.BlockSpecOf(world.Unit(unit.Key));
                Assert.That((block.CosHalfAnglePermille, block.ReductionPermille, block.AiRaisePermille),
                    Is.EqualTo((500, 750, 1000)));
            }
        }

        [Test]
        public void DefaultAndDisabledKeepLegacySpawns()
        {
            MapBakeResult map = Map();
            ContentPack pack = Pack();
            foreach (WorldRecipe recipe in new[] { MapRecipe.From(map, pack), MapRecipe.From(map, pack, useUnitTypes: false) })
            {
                var world = new M6World(recipe, map.WcMapBytes);
                Assert.That(world.Read<MoveIntent>(17).SpeedPerTick, Is.EqualTo(SimFix.Ratio(6000, 30000)));
                foreach (UnitSpawn unit in recipe.Units)
                {
                    Assert.That((unit.Archetype, unit.Weapon, unit.Melee, unit.Block, unit.SpeedMmPerSec),
                        Is.EqualTo((0, 1, 0, -1, 6000L)));
                    Assert.That(world.Combat.CanBlock(world.Unit(unit.Key)), Is.False);
                }
            }
        }
    }
}
