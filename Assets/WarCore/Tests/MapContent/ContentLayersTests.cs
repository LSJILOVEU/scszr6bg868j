using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class ContentLayersTests
    {
        private string previousDirectory, temporaryDirectory, dataDirectory;

        [SetUp]
        public void UseIsolatedContentDirectory()
        {
            previousDirectory = Directory.GetCurrentDirectory();
            var source = FileContentSource.Rwr();
            temporaryDirectory = Path.Combine(Path.GetTempPath(), "warcore-content-" + Guid.NewGuid().ToString("N"));
            dataDirectory = Path.Combine(temporaryDirectory, FileContentSource.RwrDirectory);
            Directory.CreateDirectory(dataDirectory);
            foreach (string id in new[] { "rwr", "rwr-m6" })
            {
                Assert.That(source.TryRead(id, out string json, out _), Is.True);
                File.WriteAllText(Path.Combine(dataDirectory, id + ".json"), json);
            }
            // Exercise the same upward directory lookup used by tools launched below the project root.
            string workingDirectory = Path.Combine(temporaryDirectory, "tools", "nested");
            Directory.CreateDirectory(workingDirectory);
            Directory.SetCurrentDirectory(workingDirectory);
        }

        [TearDown]
        public void RemoveIsolatedContentDirectory()
        {
            Directory.SetCurrentDirectory(previousDirectory);
            if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory, true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RecipesUseSelectedLayerForCombatUnitsAndMatch(bool m7)
        {
            File.WriteAllText(Path.Combine(dataDirectory, "selected.json"),
                "{\"id\":\"selected\",\"extends\":\"rwr-m6\",\"weapons\":[{\"id\":\"rwr.weapon-1\",\"fireIntervalMilli\":6780}]," +
                "\"unitTypes\":[{\"id\":\"rwr.soldier-1\",\"weapon\":\"rwr.weapon-2\"}],\"match\":{\"maxTicks\":4321}}");
            var pack = ContentLoader.Load(FileContentSource.Rwr(), "selected");
            WorldRecipe actual = m7 ? ContentLayers.M7("selected") : ContentLayers.M6("selected");
            WorldRecipe expected = m7 ? DemoRecipes.M7(pack) : DemoRecipes.M6(pack);
            WorldRecipe basis = m7 ? ContentLayers.M7("rwr-m6") : ContentLayers.M6("rwr-m6");
            Assert.That(actual.Units.Length, Is.EqualTo(m7 ? 300 : 80));
            Assert.That(actual.Units.ToArray(), Is.EqualTo(expected.Units.ToArray()));
            Assert.That(actual.Units[0].Weapon, Is.Not.EqualTo(basis.Units[0].Weapon));
            Assert.That(actual.Seed, Is.EqualTo(expected.Seed));
            Assert.That(actual.Mover, Is.EqualTo(expected.Mover));
            Assert.That(actual.Combat.PolicyDigest, Is.EqualTo(expected.Combat.PolicyDigest));
            Assert.That(actual.Combat.PolicyDigest, Is.Not.EqualTo(basis.Combat.PolicyDigest));
            Assert.That(actual.Match.PolicyDigest, Is.EqualTo(expected.Match.PolicyDigest));
            Assert.That(actual.Match.PolicyDigest, Is.Not.EqualTo(basis.Match.PolicyDigest));
            Assert.Throws<ContentException>(() => { if (m7) ContentLayers.M7("missing"); else ContentLayers.M6("missing"); });
        }

        [Test]
        public void WeaponsExposeLoadedValuesAndUnitNames()
        {
            var pack = ContentLoader.Load(FileContentSource.Rwr(), "rwr-m6");
            var rows = ContentLayers.Weapons("rwr-m6");
            Assert.That(rows.Count, Is.EqualTo(pack.WeaponCount));
            for (int i = 0; i < rows.Count; i++)
            {
                var weapon = pack.BuildCombat().Weapons[i];
                var units = Enumerable.Range(0, pack.UnitTypeCount).Select(pack.UnitTypeId)
                    .Where(id => pack.WeaponOfUnitType(id) == i);
                Assert.That(rows[i].Id, Is.EqualTo(pack.WeaponId(i)));
                Assert.That(rows[i].UnitTypes, Is.EqualTo(string.Join(", ", units)));
                Assert.That(rows[i].FireIntervalMilli, Is.EqualTo(weapon.FireIntervalMilli));
                Assert.That(rows[i].Magazine, Is.EqualTo(weapon.Magazine));
                Assert.That(rows[i].ReloadMilli, Is.EqualTo(weapon.ReloadMilli));
            }
            Assert.Throws<ContentException>(() => ContentLayers.Weapons("missing"));
        }

        [Test]
        public void SaveOverrideRoundTripsAndWritesOnlyChangedFields()
        {
            string basePath = Path.Combine(dataDirectory, "rwr-m6.json");
            string baseJson = File.ReadAllText(basePath);
            var rows = ContentLayers.Weapons("rwr-m6").ToArray();
            var original = rows[0];
            rows[0] = new ContentWeaponRow(original.Id, original.UnitTypes, 6780, original.Magazine, original.ReloadMilli);
            string path = ContentLayers.SaveOverride("rwr-m6", "saved", rows);
            // macOS temp paths may be /var or /private/var for the same file; compare the file itself.
            Assert.That(new FileInfo(path).FullName.EndsWith("saved.json"), Is.True);
            Assert.That(File.Exists(Path.Combine(dataDirectory, "saved.json")), Is.True);
            var json = ContentJson.Parse(File.ReadAllText(path), "saved");
            Assert.That(json.Find("id").Text, Is.EqualTo("saved"));
            Assert.That(json.Find("extends").Text, Is.EqualTo("rwr-m6"));
            Assert.That(json.Find("weapons").Items.Count, Is.EqualTo(1));
            Assert.That(json.Find("weapons").Items[0].Members.Count, Is.EqualTo(2));
            Assert.That(json.Find("weapons").Items[0].Find("fireIntervalMilli"), Is.Not.Null);
            Assert.That(ContentLayers.Weapons("saved"), Is.EqualTo(rows));
            Assert.That(File.ReadAllText(basePath), Is.EqualTo(baseJson));

            rows[0] = new ContentWeaponRow(original.Id, original.UnitTypes, 6780, 17, 90000);
            Assert.That(ContentLayers.SaveOverride("rwr-m6", "saved", rows), Is.EqualTo(path));
            Assert.That(ContentLayers.Weapons("saved"), Is.EqualTo(rows));
            foreach (WorldRecipe recipe in new[] { ContentLayers.M6("saved"), ContentLayers.M7("saved") })
                Assert.That(recipe.Combat.Weapons[0].FireIntervalMilli, Is.EqualTo(6780));

            ContentLayers.SaveOverride("rwr-m6", "saved", ContentLayers.Weapons("rwr-m6"));
            Assert.That(ContentJson.Parse(File.ReadAllText(path), "saved").Find("weapons").Items, Is.Empty);
        }

        [Test]
        public void InvalidSaveLeavesExistingFileIntact()
        {
            var rows = ContentLayers.Weapons("rwr-m6").ToArray();
            string path = ContentLayers.SaveOverride("rwr-m6", "saved", rows);
            string original = File.ReadAllText(path);
            var row = rows[0];
            rows[0] = new ContentWeaponRow(row.Id, row.UnitTypes, 999, row.Magazine, row.ReloadMilli);
            Assert.Throws<ContentException>(() => ContentLayers.SaveOverride("rwr-m6", "saved", rows));
            Assert.That(File.ReadAllText(path), Is.EqualTo(original));
            Assert.Throws<ArgumentException>(() => ContentLayers.SaveOverride("rwr-m6", "../escape", rows));
        }

        [Test]
        public void RateWrapperClampsToLoadableIntervalAndSavedLayerLoads()
        {
            foreach (decimal rate in new[] { 30m, 30.01m, 12000m, decimal.MaxValue })
                Assert.That(ContentLayers.IntervalFromShotsPerSecond(rate), Is.EqualTo(1000));
            foreach (long interval in new[] { 1000L, 3390L, 3690L, 1000000000000L })
            {
                Assert.That(ContentLayers.ShotsPerSecond(interval), Is.EqualTo(30000m / interval));
                Assert.That(ContentLayers.IntervalFromShotsPerSecond(ContentLayers.ShotsPerSecond(interval)), Is.EqualTo(interval));
            }
            Assert.That(ContentLayers.IntervalFromShotsPerSecond(19.2m), Is.EqualTo(1563)); // 1562.5 milli rounds up.
            Assert.That(ContentLayers.IntervalFromShotsPerSecond(16m), Is.EqualTo(1875));
            Assert.That(ContentLayers.IntervalFromShotsPerSecond(0.0000000000000000000000000001m), Is.EqualTo(1000000000000L));
            Assert.Throws<ContentException>(() => ContentLayers.IntervalFromShotsPerSecond(0));
            Assert.Throws<ContentException>(() => ContentLayers.IntervalFromShotsPerSecond(-1));
            Assert.Throws<ContentException>(() => ContentLayers.ShotsPerSecond(0));
            var row = ContentLayers.Weapons("rwr-m6")[0];
            ContentLayers.SaveOverride("rwr-m6", "fast", new[] {
                new ContentWeaponRow(row.Id, row.UnitTypes, ContentLayers.IntervalFromShotsPerSecond(100m), row.Magazine, row.ReloadMilli)
            });
            Assert.That(ContentLayers.Weapons("fast")[0].FireIntervalMilli, Is.EqualTo(1000));
        }
    }
}
