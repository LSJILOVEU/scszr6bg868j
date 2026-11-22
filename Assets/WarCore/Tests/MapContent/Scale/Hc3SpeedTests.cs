using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.E1aSamples;
using static WarCore.Tests.E1bSamples;

namespace WarCore.Tests
{
    // HC-3: a unit's speed has one default (ContentPack.DefaultUnitSpeedMmPerSec); a unit type's own speed wins where unit types
    // are read; a recipe for a map with terrain or floor plates refuses a unit faster than the one-step cap when it is built.
    public sealed class Hc3SpeedTests
    {
        private const string AncientData = "Assets/WarCore/Content/Ancient/Data";

        // A layer of its own over a shipped pack, with unit types written in it.
        private static ContentPack Layer(string id, string extends, string unitTypes, string match = null) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
            {
                [id] = "{\"id\":\"" + id + "\",\"extends\":\"" + extends + "\",\"unitTypes\":[" + unitTypes + "]"
                    + (match == null ? "" : ",\"match\":{" + match + "}") + "}"
            }, FileContentSource.Rwr()), id);

        private static string Speed(string type, long mm) => "{\"id\":\"" + type + "\",\"speed\":" + mm + "}";
        private static string BothSpeeds(long first, long second) =>
            Speed(RwrPack.SoldierTeam1, first) + "," + Speed(RwrPack.SoldierTeam2, second);

        // S2: the reader gives a type's own speed, else the default.
        [Test]
        public void S2_SpeedOfUnitTypeReadsTheTypeElseTheDefault()
        {
            ContentPack layer = Layer("hc3-read", RwrPack.M6Layer, Speed(RwrPack.SoldierTeam1, 4500));
            Assert.That(layer.SpeedOfUnitType(RwrPack.SoldierTeam1), Is.EqualTo(4500L));
            Assert.That(layer.SpeedOfUnitType(RwrPack.SoldierTeam2), Is.EqualTo(ContentPack.DefaultUnitSpeedMmPerSec));
            Assert.That(RwrPack.Load(RwrPack.M6Layer).SpeedOfUnitType(RwrPack.SoldierTeam1), Is.EqualTo(ContentPack.DefaultUnitSpeedMmPerSec));
            ContentPack ancient = ContentLoader.Load(new FileContentSource(AncientData), "ancient");
            Assert.That(ancient.SpeedOfUnitType("ancient.swordsman"), Is.EqualTo(3000L));
        }

        // S4: an unknown unit type is refused with the pack and the id; SpawnOfUnitType keeps its own message.
        [Test]
        public void S4_UnknownUnitTypeIsRefused()
        {
            ContentPack pack = RwrPack.Load(RwrPack.M6Layer);
            ContentException e = Assert.Throws<ContentException>(() => pack.SpeedOfUnitType("no.such"));
            Assert.That(e.Message, Does.Contain("'rwr-m6'").And.Contain("'no.such'"));
            ContentException missing = Assert.Throws<ContentException>(() => pack.SpawnOfUnitType(RwrPack.SoldierTeam1));
            Assert.That(missing.Message, Does.Contain("is missing field 'speed'"));
        }

        // S3: the map file's default route (switch off) gives every unit the default speed, even when the type names one.
        [Test]
        public void S3_DefaultRouteGivesTheDefaultSpeed()
        {
            WorldRecipe demo = MapRecipe.From(MapBake.Run(PlateDemoMap.DescBytes()), RwrPack.Load(RwrPack.M6Layer));
            Assert.That(demo.Units.Length, Is.GreaterThan(0));
            foreach (UnitSpawn u in demo.Units) Assert.That(u.SpeedMmPerSec, Is.EqualTo(ContentPack.DefaultUnitSpeedMmPerSec));
            ContentPack layer = Layer("hc3-read", RwrPack.M6Layer, Speed(RwrPack.SoldierTeam1, 4500));
            WorldRecipe own = MapRecipe.From(Bake(Desc("hc3-read", null, null, OnePoint, TwoBases, TwoSoldiers, 41, "none", 2, 3, 3)), layer);
            Assert.That(own.Units.Length, Is.EqualTo(2));
            foreach (UnitSpawn u in own.Units) Assert.That(u.SpeedMmPerSec, Is.EqualTo(ContentPack.DefaultUnitSpeedMmPerSec));
        }

        // The default speed, as a step, is within the one-step cap of a world with terrain or plates.
        [Test]
        public void S3_DefaultSpeedFitsTheTerrainStepCap()
        {
            Assert.That(SimFix.Ratio(ContentPack.DefaultUnitSpeedMmPerSec, 1000 * SimClock.TickRate).Raw,
                Is.LessThanOrEqualTo(WarCore.Interim.InterimLocomotion.MaxTerrainStepRaw));
        }

        // Terrain (a height field, no plates), plates (the plate demo house, no height field) and flat (neither), each with
        // soldier-1 (key 1, team 1) first and soldier-2 (key 2, team 2) second, under a layer of their own.
        private const string TerrainLayer = "hc3-terrain", PlatesLayer = "hc3-plates", FlatLayer = "hc3-flat";

        private static MapBakeResult TerrainMap() => Bake(Desc(TerrainLayer, Heights(), null, OnePoint, TwoBases, TwoSoldiers, 41, "none", 2, 3, 3));

        private static MapBakeResult FlatMap() => Bake(Desc(FlatLayer, null, null, OnePoint, TwoBases, TwoSoldiers, 41, "none", 2, 3, 3));

        private static MapBakeResult PlatesMap()
        {
            MapDesc d = PlateDemoMap.Desc();
            Assert.That(d.Heights, Is.Null);
            Assert.That(d.Plates.Count, Is.GreaterThan(0));
            var units = new[]
            {
                new MapUnit(1, RwrPack.SoldierTeam1, 1, 1, new MapMm(1500, 2000), false),
                new MapUnit(2, RwrPack.SoldierTeam2, 2, 2, new MapMm(21500, 2000), false),
            };
            var desc = new MapDesc(d.Id, d.Content, PlatesLayer, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, d.Layers.ToArray(), null, null,
                d.Plates.ToArray(), d.Cutouts.ToArray(), d.Walls.ToArray(), d.Points.ToArray(), d.Bases.ToArray(), units, d.Run,
                d.Owners.ToArray(), null, null, new MapStamp[0], new MapSrc[0], d.Next);
            return Bake(MapDescJson.Write(desc));
        }

        private static void Walks(WorldRecipe recipe, MapBakeResult map)
        {
            SimSession session = SimSession.Create(recipe.WithPlayer(1), map.WcMapBytes);
            session.SetPlayerInput(1, new PlayerInput(1000, 0));
            Assert.DoesNotThrow(() => { for (int t = 0; t < 30; t++) session.StepOnce(); });
        }

        private static void Refused(System.Func<WorldRecipe> build, string layer, string type, long mm)
        {
            ContentException e = Assert.Throws<ContentException>(() => build());
            Assert.That(e.Message, Does.Contain("'" + layer + "'").And.Contain("'" + type + "'").And.Contain(mm + " mm/s"));
        }

        // S5: 7530 mm/s on the second unit type is refused on terrain and on plates when the recipe is built; 7500 (exactly the
        // cap) builds and walks; 7501 is one raw step above the cap.
        [TestCase(7530)]
        [TestCase(7501)]
        public void S5_TooFastOnTerrainIsRefusedAtTheRecipe(long mm)
        {
            ContentPack pack = Layer(TerrainLayer, E1bSamples.TerrainPack, BothSpeeds(6000, mm));
            Refused(() => MapRecipe.From(TerrainMap(), pack, useUnitTypes: true), TerrainLayer, RwrPack.SoldierTeam2, mm);
        }

        [TestCase(7530)]
        [TestCase(7501)]
        public void S5_TooFastOnPlatesIsRefusedAtTheRecipe(long mm)
        {
            ContentPack pack = Layer(PlatesLayer, RwrPack.M6Layer, BothSpeeds(6000, mm));
            Refused(() => MapRecipe.From(PlatesMap(), pack, useUnitTypes: true), PlatesLayer, RwrPack.SoldierTeam2, mm);
        }

        [Test]
        public void S5_TheCapItselfBuildsAndWalks()
        {
            MapBakeResult terrain = TerrainMap(), plates = PlatesMap();
            WorldRecipe onTerrain = null, onPlates = null;
            Assert.DoesNotThrow(() => onTerrain = MapRecipe.From(terrain, Layer(TerrainLayer, E1bSamples.TerrainPack, BothSpeeds(7500, 7500)), useUnitTypes: true));
            Assert.DoesNotThrow(() => onPlates = MapRecipe.From(plates, Layer(PlatesLayer, RwrPack.M6Layer, BothSpeeds(7500, 7500)), useUnitTypes: true));
            Walks(onTerrain, terrain);
            Walks(onPlates, plates);
        }

        [Test]
        public void S5_FlatMapsAreNotCapped()
        {
            ContentPack pack = Layer(FlatLayer, RwrPack.M6Layer, BothSpeeds(6000, 7530));
            WorldRecipe recipe = null;
            Assert.DoesNotThrow(() => recipe = MapRecipe.From(FlatMap(), pack, useUnitTypes: true));
            Assert.That(recipe.Units[1].SpeedMmPerSec, Is.EqualTo(7530L));
        }

        // Only the pack's extra reserves are too fast: the map's own units are at 6000.
        [Test]
        public void S5_TooFastReservesAreRefused()
        {
            string types = BothSpeeds(6000, 6000) + ",{\"id\":\"hc3.runner\",\"weapon\":\"rwr.weapon-1\",\"speed\":7530}";
            ContentPack pack = Layer(TerrainLayer, E1bSamples.TerrainPack, types, "\"reserveCount\":1,\"reserveType\":\"hc3.runner\"");
            foreach (bool useUnitTypes in new[] { true, false })
            {
                if (useUnitTypes) Refused(() => MapRecipe.From(TerrainMap(), pack, useUnitTypes: true), TerrainLayer, "hc3.runner", 7530);
                else Assert.DoesNotThrow(() => MapRecipe.From(TerrainMap(), pack), "the default route gives reserves the default speed");
            }
            ContentPack calm = Layer(TerrainLayer, E1bSamples.TerrainPack, BothSpeeds(6000, 6000) + ",{\"id\":\"hc3.runner\",\"weapon\":\"rwr.weapon-1\",\"speed\":7500}",
                "\"reserveCount\":1,\"reserveType\":\"hc3.runner\"");
            WorldRecipe recipe = null;
            Assert.DoesNotThrow(() => recipe = MapRecipe.From(TerrainMap(), calm, useUnitTypes: true));
            Assert.That(recipe.Units.Length, Is.EqualTo(4), "two map units and one reserve a team");
        }

        // S6: the plate and street demos walk at half the soldier's speed.
        [Test]
        public void S6_HalfSpeedDemosFollowTheSoldier()
        {
            long soldier = RwrPack.Load(RwrPack.M6Layer).SpeedOfUnitType(RwrPack.SoldierTeam1);
            foreach (WorldRecipe recipe in new[] { PlateDemo.Recipe(), StreetDemo.Recipe() })
            {
                Assert.That(recipe.Units.Length, Is.GreaterThan(0));
                foreach (UnitSpawn u in recipe.Units)
                {
                    Assert.That(u.SpeedMmPerSec, Is.EqualTo(soldier / 2));
                    Assert.That(u.SpeedMmPerSec, Is.EqualTo(3000L));
                }
            }
        }
    }
}
