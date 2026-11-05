using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class ContentWeaponEditTests
    {
        private const string Weapon = "rwr.weapon-1", Local = "edit-test";

        private static Dictionary<string, ContentWeaponChange> Change(ContentWeaponChange change) =>
            new Dictionary<string, ContentWeaponChange> { [Weapon] = change };

        private static ContentPack Load(string json, string id = Local) => ContentLoader.Load(
            new MemoryContentSource(new Dictionary<string, string> { [id] = json }, FileContentSource.Rwr()), id);

        private static string Generate(IReadOnlyDictionary<string, ContentWeaponChange> changes, string basis = "rwr-m6") =>
            ContentWeaponEdit.Generate(FileContentSource.Rwr(), basis, Local, changes);

        private static void SameWeapon(WeaponDef actual, WeaponDef expected)
        {
            Assert.That(actual.FireIntervalMilli, Is.EqualTo(expected.FireIntervalMilli));
            Assert.That(actual.Magazine, Is.EqualTo(expected.Magazine));
            Assert.That(actual.ReloadMilli, Is.EqualTo(expected.ReloadMilli));
            Assert.That(actual.FalloffStart, Is.EqualTo(expected.FalloffStart));
            Assert.That(actual.FalloffEnd, Is.EqualTo(expected.FalloffEnd));
            Assert.That(actual.EffectRaw, Is.EqualTo(expected.EffectRaw));
        }

        [Test]
        public void GeneratedLayerLoadsAndOnlyChangesRequestedWeaponFields()
        {
            ContentPack basis = ContentLoader.Load(FileContentSource.Rwr(), "rwr-m6");
            string json = Generate(Change(new ContentWeaponChange(6780, 17, 90000)));
            JsonNode root = ContentJson.Parse(json, Local);
            Assert.That(root.Find("id").Text, Is.EqualTo(Local));
            Assert.That(root.Find("extends").Text, Is.EqualTo("rwr-m6"));
            Assert.That(root.Find("weapons").Items.Count, Is.EqualTo(1));
            ContentPack edited = Load(json);
            Assert.That(edited.WeaponCount, Is.EqualTo(basis.WeaponCount));
            var before = basis.BuildCombat();
            var after = edited.BuildCombat();
            for (int i = 0; i < basis.WeaponCount; i++)
            {
                Assert.That(edited.WeaponId(i), Is.EqualTo(basis.WeaponId(i)));
                WeaponDef expected = before.Weapons[i];
                if (basis.WeaponId(i) == Weapon)
                    expected = new WeaponDef(6780, 17, 90000, expected.FalloffStart, expected.FalloffEnd, expected.EffectRaw);
                SameWeapon(after.Weapons[i], expected);
            }
            Assert.That(after.PolicyDigest, Is.Not.EqualTo(before.PolicyDigest));
            Assert.That(edited.Match, Is.EqualTo(basis.Match));
            Assert.That(edited.UnitTypeCount, Is.EqualTo(basis.UnitTypeCount));
            for (int i = 0; i < basis.UnitTypeCount; i++)
            {
                Assert.That(edited.UnitTypeId(i), Is.EqualTo(basis.UnitTypeId(i)));
                Assert.That(edited.WeaponOfUnitType(edited.UnitTypeId(i)), Is.EqualTo(basis.WeaponOfUnitType(basis.UnitTypeId(i))));
            }
        }

        [TestCase("fireIntervalMilli")]
        [TestCase("magazine")]
        [TestCase("reloadMilli")]
        public void OptionalFieldsInheritAndOnlyTheChangedFieldIsWritten(string field)
        {
            var change = new ContentWeaponChange(field == "fireIntervalMilli" ? 6780 : (long?)null,
                field == "magazine" ? 17 : (long?)null, field == "reloadMilli" ? 90000 : (long?)null);
            string json = Generate(Change(change));
            JsonNode entry = ContentJson.Parse(json, Local).Find("weapons").Items[0];
            Assert.That(entry.Members.Count, Is.EqualTo(2), "id plus the one changed field");
            Assert.That(entry.Find(field), Is.Not.Null);
            WeaponDef before = ContentLoader.Load(FileContentSource.Rwr(), "rwr-m6").BuildCombat().Weapons[0];
            SameWeapon(Load(json).BuildCombat().Weapons[0], new WeaponDef(
                change.FireIntervalMilli ?? before.FireIntervalMilli, (int)(change.Magazine ?? before.Magazine),
                change.ReloadMilli ?? before.ReloadMilli, before.FalloffStart, before.FalloffEnd, before.EffectRaw));
        }

        [Test]
        public void EmptyUnspecifiedAndUnchangedEditsHaveNoEntriesAndKeepPolicyDigest()
        {
            var basis = ContentLoader.Load(FileContentSource.Rwr(), "rwr-m6").BuildCombat();
            WeaponDef w = basis.Weapons[0];
            foreach (var changes in new[] { new Dictionary<string, ContentWeaponChange>(), Change(default),
                Change(new ContentWeaponChange(w.FireIntervalMilli, w.Magazine, w.ReloadMilli)) })
            {
                string json = Generate(changes);
                Assert.That(ContentJson.Parse(json, Local).Find("weapons").Items, Is.Empty);
                var result = Load(json).BuildCombat();
                Assert.That(result.PolicyDigest, Is.EqualTo(basis.PolicyDigest));
                for (int i = 0; i < basis.Weapons.Length; i++) SameWeapon(result.Weapons[i], basis.Weapons[i]);
            }
        }

        [Test]
        public void UnknownWeaponIsRejectedEvenWhenItsEditIsEmpty()
        {
            string message = Assert.Throws<ContentException>(() => Generate(
                new Dictionary<string, ContentWeaponChange> { ["missing-weapon"] = default })).Message;
            Assert.That(message, Does.Contain("missing-weapon").And.Contain("rwr-m6"));
        }

        [TestCase("fireIntervalMilli", 0L)]
        [TestCase("fireIntervalMilli", 999L)]
        [TestCase("fireIntervalMilli", 1000000000001L)]
        [TestCase("reloadMilli", -1L)]
        [TestCase("reloadMilli", 999L)]
        [TestCase("reloadMilli", 1000000000001L)]
        [TestCase("magazine", 0L)]
        [TestCase("magazine", 2147483648L)]
        public void IllegalValuesNameLayerWeaponAndField(string field, long value)
        {
            var change = new ContentWeaponChange(field == "fireIntervalMilli" ? value : (long?)null,
                field == "magazine" ? value : (long?)null, field == "reloadMilli" ? value : (long?)null);
            string message = Assert.Throws<ContentException>(() => Generate(Change(change))).Message;
            Assert.That(message, Does.Contain(Local).And.Contain(Weapon).And.Contain(field).And.Contain("must be in"));
        }

        [TestCase(1000L, 1L)]
        [TestCase(1000000000000L, 2147483647L)]
        public void ActualLoaderBoundsAreAccepted(long milli, long magazine)
        {
            var w = Load(Generate(Change(new ContentWeaponChange(milli, magazine, milli)))).BuildCombat().Weapons[0];
            Assert.That(w.FireIntervalMilli, Is.EqualTo(milli));
            Assert.That(w.ReloadMilli, Is.EqualTo(milli));
            Assert.That(w.Magazine, Is.EqualTo(magazine));
        }

        [TestCase(1L)] [TestCase(1000L)] [TestCase(3390L)] [TestCase(3690L)]
        [TestCase(45000L)] [TestCase(999999999999L)] [TestCase(1000000000000L)]
        public void DisplayRateRoundTripsToTheSameInteger(long interval)
        {
            decimal rate = ContentWeaponEdit.ShotsPerSecond(interval);
            Assert.That(rate, Is.EqualTo(30000m / interval));
            Assert.That(ContentWeaponEdit.IntervalFromShotsPerSecond(rate), Is.EqualTo(interval));
        }

        [Test]
        public void RateConversionRoundsHalfUpClampsAndRejectsNonPositiveInput()
        {
            Assert.That(ContentWeaponEdit.IntervalFromShotsPerSecond(12000m), Is.EqualTo(3)); // 2.5 milli
            Assert.That(ContentWeaponEdit.IntervalFromShotsPerSecond(decimal.MaxValue), Is.EqualTo(1));
            Assert.That(ContentWeaponEdit.IntervalFromShotsPerSecond(0.0000000000000000000000000001m), Is.EqualTo(ContentWeaponEdit.MaxMilli));
            Assert.Throws<ContentException>(() => ContentWeaponEdit.IntervalFromShotsPerSecond(0));
            Assert.Throws<ContentException>(() => ContentWeaponEdit.IntervalFromShotsPerSecond(-1));
            Assert.Throws<ContentException>(() => ContentWeaponEdit.ShotsPerSecond(0));
            Assert.Throws<ContentException>(() => ContentWeaponEdit.ShotsPerSecond(ContentWeaponEdit.MaxMilli + 1));
        }

        [Test]
        public void JsonEscapingCultureAndChangeOrderDoNotAlterTheLayer()
        {
            var source = FileContentSource.Rwr();
            string id = "quote\"slash\\line\n";
            var first = Change(new ContentWeaponChange(6780));
            first.Add("rwr.weapon-2", new ContentWeaponChange(magazine: 9));
            var reverse = new Dictionary<string, ContentWeaponChange> {
                ["rwr.weapon-2"] = first["rwr.weapon-2"], [Weapon] = first[Weapon] };
            CultureInfo prior = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                string json = ContentWeaponEdit.Generate(source, "rwr-m6", id, first);
                Assert.That(ContentWeaponEdit.Generate(source, "rwr-m6", id, reverse), Is.EqualTo(json));
                Assert.That(Load(json, id).BuildCombat().Weapons[0].FireIntervalMilli, Is.EqualTo(6780));
            }
            finally { CultureInfo.CurrentCulture = prior; }
        }

        [Test]
        public void ReplacingBaseOrAncestorIsRejected()
        {
            var empty = new Dictionary<string, ContentWeaponChange>();
            Assert.Throws<ContentException>(() => ContentWeaponEdit.Generate(FileContentSource.Rwr(), "rwr", "rwr", empty));
            Assert.That(Assert.Throws<ContentException>(() =>
                ContentWeaponEdit.Generate(FileContentSource.Rwr(), "rwr-m6", "rwr", empty)).Message, Does.Contain("cycle"));
        }

        [Test]
        public void M5RecipeFirstShooterOffsetsDoubleOverTheFirst200Ticks()
        {
            var basis = ContentLoader.Load(FileContentSource.Rwr(), "rwr");
            long interval = basis.BuildCombat().Weapons[basis.WeaponIndex(Weapon)].FireIntervalMilli;
            ContentPack edited = Load(Generate(Change(new ContentWeaponChange(interval * 2)), "rwr"));
            // Keep the M5 seed, spawns, movement and script. An open map and the C1 zero-damage
            // resolver keep sight loss and deaths from changing the cadence being measured.
            byte[] map = WcMapWriter.Write(M3TestWorld.Boxes(64, 32));
            var normal = SimSession.Create(CadenceRecipe(basis), map);
            var slower = SimSession.Create(CadenceRecipe(edited), map);
            var normalTicks = new List<long>();
            var slowerTicks = new List<long>();
            int normalAmmo = basis.BuildCombat().Weapons[0].Magazine, slowerAmmo = normalAmmo;
            for (int tick = 1; tick <= 200; tick++)
            {
                RecordShot(normal, normalTicks, ref normalAmmo);
                RecordShot(slower, slowerTicks, ref slowerAmmo);
            }
            const int shots = 20; // Before reload, including the slower run.
            Assert.That(normalTicks.Count, Is.GreaterThanOrEqualTo(shots));
            Assert.That(slowerTicks.Count, Is.GreaterThanOrEqualTo(shots));
            Assert.That(normalTicks[0], Is.EqualTo(1), "first M5 unit has a target from the first tick");
            Assert.That(slowerTicks[0], Is.EqualTo(normalTicks[0]));
            for (int n = 1; n <= shots; n++)
            {
                long a = normalTicks[n - 1] - normalTicks[0], b = slowerTicks[n - 1] - slowerTicks[0];
                Assert.That(a, Is.EqualTo(((n - 1) * interval + 999) / 1000), "base shot=" + n);
                Assert.That(b, Is.EqualTo(((n - 1) * interval * 2 + 999) / 1000), "edited shot=" + n);
                Assert.That(Math.Abs(b - 2 * a), Is.LessThanOrEqualTo(1), "one tick ceiling tolerance, shot=" + n);
            }
        }

        private static WorldRecipe CadenceRecipe(ContentPack content)
        {
            CombatPack pack = content.BuildCombat();
            return DemoRecipes.M5().WithCombat(new CombatPack(pack.Weapons.ToArray(), new ZeroResolver(), pack.Selector, pack.MaxHealthRaw,
                friendlyFire: false, friendlyFireLocked: false, allyCheckMissRaw: 65536)); // WP-17: AI never holds fire, so only the cadence is measured
        }

        private static void RecordShot(SimSession session, List<long> ticks, ref int ammo)
        {
            session.StepOnce();
            Assert.That(session.TryUnitStatus(1, out UnitStatus status), Is.True);
            if (status.Ammo < ammo) ticks.Add(session.Tick);
            ammo = status.Ammo;
        }
    }
}
