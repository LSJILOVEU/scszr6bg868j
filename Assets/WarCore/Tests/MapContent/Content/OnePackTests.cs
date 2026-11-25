using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml7Worlds;

namespace WarCore.Tests
{
    // RC1-K2: a world reads one content pack. A recipe takes it once, and every feature reads that pack on every map.
    public sealed class OnePackTests
    {
        private static readonly UnitSpawn[] One = { Unit(1, 1, 500, 500) };

        // The pack is given once. The same pack again changes nothing; another pack is refused, whether it is another layer or
        // the same layer loaded a second time; so is no pack. WithPack leaves the recipe it is called on as it was.
        [Test]
        public void ARecipeTakesItsPackOnce()
        {
            ContentPack pack = RwrPack.Load(RwrPack.M6Layer);
            WorldRecipe bare = Recipe(One), given = bare.WithPack(pack);
            Assert.That(bare.Pack, Is.Null);
            Assert.That(given.Pack, Is.SameAs(pack));
            WorldRecipe again = null;
            Assert.DoesNotThrow(() => again = given.WithPack(pack), "the same pack again");
            Assert.That(again.Pack, Is.SameAs(pack));
            Assert.That(given.WithFacing().Pack, Is.SameAs(pack), "a later option keeps the pack");
            foreach (ContentPack other in new[] { RwrPack.Load(DemoRecipes.PlaytestLayer), RwrPack.Load(RwrPack.M6Layer) })
            {
                ArgumentException refused = Assert.Throws<ArgumentException>(() => given.WithPack(other));
                Assert.That(refused.Message, Does.StartWith("The recipe already reads content pack 'rwr-m6'. A world reads one pack: "), other.Id);
                Assert.That(refused.ParamName, Is.EqualTo("pack"), other.Id);
            }
            Assert.That(given.Pack, Is.SameAs(pack), "a refused pack changes nothing");
            Assert.That(Assert.Throws<ArgumentNullException>(() => bare.WithPack(null)).ParamName, Is.EqualTo("pack"));
        }

        private static ulong Key(SimSession s, string name) => M3TestWorld.Sim(s).StableContent.Key(name);

        // A plate world derives its layered graph and its terrain tables under the terrain rules of its pack. Without a pack,
        // and under a pack that has no terrain rules, it takes the default rules (the graph a map has on its own).
        [Test]
        public void APlateWorldReadsItsPacksTerrainRules()
        {
            WcMap map = WcMapReader.Read(Plates);
            ContentPack rules = RwrPack.Load(DemoRecipes.TerrainMatchLayer), none = RwrPack.Load(RwrPack.M6Layer);
            Assert.That(rules.Terrain, Is.Not.Null);
            Assert.That(none.Terrain, Is.Null);
            ulong nav = WcMapLayeredNav.Build(map, rules).Digest, tables = TerrainDerivation.Derive(map, rules).Digest;
            ulong ownNav = WcMapLayeredNav.Build(map, null, map.ContentId).Digest, ownTables = TerrainDerivation.Derive(map, null, map.ContentId).Digest;
            Assert.That(nav, Is.Not.EqualTo(ownNav), "the rules of the fixture's pack change the graph");
            Assert.That(tables, Is.Not.EqualTo(ownTables), "and the tables");

            SimSession under = SimSession.Create(Recipe(One).WithPack(rules), Plates);
            Assert.That((Key(under, "plates.nav"), Key(under, "terrain.tables")), Is.EqualTo((nav, tables)));
            SimSession without = SimSession.Create(Recipe(One).WithPack(none), Plates), bare = SimSession.Create(Recipe(One), Plates);
            Assert.That((Key(without, "plates.nav"), Key(without, "terrain.tables")), Is.EqualTo((ownNav, ownTables)));
            Assert.That(M3TestWorld.Sim(without).StableContent.Digest, Is.EqualTo(M3TestWorld.Sim(bare).StableContent.Digest));
        }

        // The ruler's table of played stages (tools/ruler/played.tsv) names the layer each stage runs on: the stages of the
        // showcase and the multiplayer playtest, no more and no fewer.
        [Test]
        public void ThePlayedTableNamesTheLayerOfEveryStage()
        {
            var stages = new List<string> { "mp " + DemoRecipes.PlaytestLayer };
            foreach (ShowcaseStage stage in Showcase.Stages) stages.Add(stage.Id + " " + stage.Layer);
            var rows = new List<string>();
            foreach (string line in File.ReadAllLines(Path.Combine(Directory.GetParent(ArchitectureTests.AssetRoot()).FullName, "tools/ruler/played.tsv")))
            {
                string[] cells = line.TrimEnd('\r').Split('\t');
                if (cells[0].Length > 0 && cells[0][0] != '#') rows.Add(cells[0] + " " + cells[2]);
            }
            stages.Sort(StringComparer.Ordinal);
            rows.Sort(StringComparer.Ordinal);
            Assert.That(string.Join("\n", rows), Is.EqualTo(string.Join("\n", stages)));
        }
    }
}
