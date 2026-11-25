using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // RC1-K4: a stage of the showcase is a map file and one content layer. Every stage in the list and the house as it was
    // before AIM-2 are held to that (a stage added to the list is held to it without a new case here), and the map files
    // to what the map editor takes.
    public sealed class StageFileTests
    {
        private static IEnumerable<string> StageIds() => Showcase.Stages.Select(s => s.Id).Append("legacy house");

        private static ShowcaseStage Stage(string id)
        {
            if (id == "legacy house") return Showcase.LegacyHouse;
            foreach (ShowcaseStage stage in Showcase.Stages)
                if (stage.Id == id) return stage;
            throw new ArgumentException(id);
        }

        private static byte[] MapFile(ShowcaseStage stage) =>
            File.ReadAllBytes(Path.Combine(Directory.GetParent(ArchitectureTests.AssetRoot()).FullName, stage.Map));

        private static string Fields(UnitSpawn u) => string.Join(",", u.Key, u.Archetype, u.Squad, u.Team, u.Weapon, u.Reserve, u.Melee,
            u.XMm, u.ZMm, u.SpeedMmPerSec, u.Block, u.DashDistanceMm, u.DashTicks, u.DashCooldownTicks, u.Layer);

        // A stage has no world of its own. Its map is the file's bake. Its recipe has the file's seed and points and the
        // file's soldiers: none added and none left out, each placed as the file places it and made as the stage's layer
        // makes a unit of its type (weapon, melee, speed, dash, block). The player starts in team 1's first deployed
        // soldier of the file. Which layer the file names does not count.
        [TestCaseSource(nameof(StageIds))]
        public void AStageIsItsMapFileUnderItsLayer(string id)
        {
            ShowcaseStage stage = Stage(id);
            Assert.That(stage.Map, Does.StartWith("Assets/WarCore/Maps/").And.EndsWith(".map.json"));
            MapBakeResult baked = MapBake.Run(MapFile(stage));
            MapSetup setup = baked.Setup;
            ContentPack pack = ContentCatalog.Find().Load(stage.Layer);
            WorldRecipe recipe = stage.Recipe();
            Assert.That(stage.MapBytes(), Is.EqualTo(baked.WcMapBytes), "the map");
            Assert.That(recipe.Seed, Is.EqualTo(setup.Seed), "the seed");
            Assert.That(recipe.Match.Points.Length, Is.EqualTo(setup.ControlPoints.Length), "the points");
            Assert.That(recipe.Units.Length, Is.EqualTo(setup.Soldiers.Count), "the soldiers: none added in code, none left out");
            ulong first = 0;
            for (int i = 0; i < setup.Soldiers.Count; i++)
            {
                MapSoldier s = setup.Soldiers[i];
                Assert.That(Fields(recipe.Units[i]),
                    Is.EqualTo(Fields(UnitSpawn.OfType(s.Key, pack, s.Type, s.Squad, s.Team, s.XMm, s.ZMm, s.Reserve, s.Layer))), "soldier " + s.Key);
                if (first == 0 && s.Team == 1 && !s.Reserve) first = s.Key;
            }
            Assert.That(recipe.PlayerKey, Is.EqualTo(first), "the player");
        }

        // The map file of a stage is one the map editor takes: written the way the editor writes it, and without a problem
        // at the editor's gate under the layer the file names. (Two stages play one map; it is read once.)
        [Test]
        public void TheStageMapsAreAsTheEditorWritesThemAndPassItsGate()
        {
            var seen = new HashSet<string>();
            foreach (ShowcaseStage stage in Showcase.Stages)
            {
                if (!seen.Add(stage.Map)) continue;
                byte[] file = MapFile(stage);
                MapDesc desc = MapDescJson.Read(file);
                Assert.That(file, Is.EqualTo(MapDescJson.Write(desc)), stage.Map + ": the bytes the editor writes");
                Assert.That(MapGate.Check(file, ContentCatalog.Find().Load(desc.Pack)), Is.Empty, stage.Map + ": problems at the gate");
            }
            Assert.That(seen, Is.Not.Empty);
        }
    }
}
