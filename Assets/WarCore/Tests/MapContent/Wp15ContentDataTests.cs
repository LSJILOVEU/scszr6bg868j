using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    internal sealed class MemoryContentSource : IContentSource
    {
        private readonly Dictionary<string, string> layers;
        private readonly IContentSource fallback;
        internal MemoryContentSource(Dictionary<string, string> layers, IContentSource fallback = null) { this.layers = layers; this.fallback = fallback; }
        public bool TryRead(string layerId, out string text, out string location)
        {
            location = "memory:" + layerId;
            if (layers.TryGetValue(layerId, out text)) return true;
            if (fallback != null) return fallback.TryRead(layerId, out text, out location);
            return false;
        }
    }

    public sealed class Wp15ContentDataTests
    {
        private static ContentPack OnRwr(string id, string json) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { [id] = json }, FileContentSource.Rwr()), id);

        private static string Layer(string id, string body) => "{ \"id\": \"" + id + "\", \"extends\": \"rwr\", " + body + " }";

        private static MatchPack Match(ContentPack pack) => pack.BuildMatch(
            new[] { new ControlPointDef(7, M6World.P(20), SimFix.FromInt(6)) },
            new[] { new SpawnAnchor(1, M6World.P(6)), new SpawnAnchor(2, M6World.P(74)) });

        [TestCase("rwr", 300)]
        [TestCase("fast", 150)]
        public void CaptureTicksFromDataGiveTheExactCaptureTick(string layer, int ticks)
        {
            var pack = layer == "rwr" ? RwrPack.Load("rwr") : OnRwr("fast", Layer("fast", "\"match\": { \"captureTicks\": 150 }"));
            var units = new[] { M6World.U(1, 1), M6World.U(2, 1), M6World.U(3, 2, 70) };
            var w = new M6World(M6World.RecipeOf(units, Match(pack)));
            w.Step(ticks - 1);
            Assert.That(w.Capture.Points[0].Owner, Is.Zero);
            w.Step();
            Assert.That(w.Capture.Points[0].Owner, Is.EqualTo(1));
            Assert.That(w.Capture.Changes.Length, Is.EqualTo(1));
            Assert.That(w.Capture.Changes[0].Tick, Is.EqualTo(ticks));
        }

        [Test]
        public void ChildOverridesOnlyTheNamedFieldAndNewEntriesComeLast()
        {
            var pack = OnRwr("kid", Layer("kid", @"""weapons"": [
                { ""id"": ""rwr.weapon-1"", ""fireIntervalMilli"": 6780 },
                { ""id"": ""kid.spear"", ""fireIntervalMilli"": 2000, ""magazine"": 5, ""reloadMilli"": 3000, ""falloffStart"": 1, ""falloffEnd"": 2, ""effectRaw"": 0, ""spreadBase"": 0, ""spreadGrowth"": 0, ""spreadRecoverPerSec"": 0, ""spreadMax"": 0 } ]"));
            var weapons = pack.BuildCombat().Weapons.ToArray();
            var original = RwrPack.Create().Weapons.ToArray();
            Assert.That(weapons.Length, Is.EqualTo(4));
            Assert.That(weapons[0].FireIntervalMilli, Is.EqualTo(6780));
            Assert.That(weapons[0].Magazine, Is.EqualTo(original[0].Magazine));
            Assert.That(weapons[0].ReloadMilli, Is.EqualTo(original[0].ReloadMilli));
            Assert.That(weapons[0].FalloffStart, Is.EqualTo(original[0].FalloffStart));
            Assert.That(weapons[0].FalloffEnd, Is.EqualTo(original[0].FalloffEnd));
            Assert.That(weapons[0].EffectRaw, Is.EqualTo(original[0].EffectRaw));
            Assert.That(weapons[1], Is.EqualTo(original[1]));
            Assert.That(weapons[2], Is.EqualTo(original[2]));
            Assert.That(weapons[3].EffectRaw, Is.Zero, "explicit 0 is a value, not 'missing'");
            Assert.That(pack.WeaponId(3), Is.EqualTo("kid.spear"));
            Assert.That(pack.BuildCombat().PolicyDigest, Is.Not.EqualTo(RwrPack.Create().PolicyDigest));
        }

        [Test]
        public void ExplicitZeroOverridesAnInheritedNonZero()
        {
            var weapons = OnRwr("zero", Layer("zero", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"effectRaw\": 0 } ]")).BuildCombat().Weapons;
            Assert.That(RwrPack.Create().Weapons[0].EffectRaw, Is.Not.Zero);
            Assert.That(weapons[0].EffectRaw, Is.Zero);
        }

        [Test]
        public void ExplicitFalseOverridesAnInheritedTrue()
        {
            var layers = new Dictionary<string, string>
            {
                ["strict"] = Layer("strict", "\"match\": { \"captureStrict\": true }"),
                ["loose"] = "{ \"id\": \"loose\", \"extends\": \"strict\", \"match\": { \"captureStrict\": false } }"
            };
            var source = new MemoryContentSource(layers, FileContentSource.Rwr());
            Assert.That(ContentLoader.Load(source, "strict").Match.CaptureStrict, Is.True);
            Assert.That(ContentLoader.Load(source, "loose").Match.CaptureStrict, Is.False);
        }

        [TestCase("0.1")] [TestCase("1.5")] [TestCase("33.333")] [TestCase("0.00003")] [TestCase("68")]
        public void DecimalTextConvertsLikeSimFixRatio(string text)
        {
            var pack = OnRwr("dec", Layer("dec", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffStart\": 0, \"falloffEnd\": " + text + " } ]"));
            int dot = text.IndexOf('.');
            long digits = long.Parse(text.Replace(".", ""));
            long scale = (long)Math.Pow(10, dot < 0 ? 0 : text.Length - dot - 1);
            Assert.That(pack.BuildCombat().Weapons[0].FalloffEnd.Raw, Is.EqualTo(SimFix.Ratio(digits, scale).Raw));
        }

        [Test]
        public void EffectRawKeepsTheOriginalInteger()
        {
            Assert.That(RwrPack.Create().Weapons[1].EffectRaw, Is.EqualTo(36045));
            Assert.That(RwrPack.Create().Weapons[2].EffectRaw, Is.EqualTo(65536));
        }

        private static long[] FireTicks(CombatPack pack, int weapon, int shots)
        {
            var world = new M5TestWorld(M5TestWorld.Pair(weapon), new CombatPack(pack.Weapons.ToArray(), new ZeroResolver(), pack.Selector, pack.MaxHealthRaw));
            var ticks = new List<long>();
            while (ticks.Count < shots && world.Sim.Clock.Tick < 400)
            { world.Step(); if (world.Combat.Fires.Length != 0) ticks.Add(world.Sim.Clock.Tick); }
            return ticks.ToArray();
        }

        [Test]
        public void ChangingOnlyTheDataChangesTheActualFireCadence()
        {
            var fast = FireTicks(RwrPack.Create(), 0, 3);
            var slowPack = OnRwr("slow", Layer("slow", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMilli\": 6780 } ]")).BuildCombat();
            var slow = FireTicks(slowPack, 0, 3);
            Assert.That(fast[1] - fast[0], Is.EqualTo(4));
            Assert.That(slow[1] - slow[0], Is.EqualTo(7));
            Assert.That(slow[2] - slow[0], Is.EqualTo((2 * 6780 + 999) / 1000));
        }

        [Test]
        public void ChangingOnlyAUnitTypeWeaponReferenceChangesWhatTheUnitCarries()
        {
            var pack = OnRwr("rearm", Layer("rearm", "\"unitTypes\": [ { \"id\": \"rwr.soldier-1\", \"weapon\": \"rwr.weapon-3\" } ]"));
            Assert.That(RwrPack.Load("rwr").WeaponOfUnitType(RwrPack.SoldierTeam1), Is.EqualTo(0));
            int weapon = pack.WeaponOfUnitType(RwrPack.SoldierTeam1);
            Assert.That(weapon, Is.EqualTo(2));
            var combat = pack.BuildCombat();
            var world = new M5TestWorld(M5TestWorld.Pair(weapon), new CombatPack(combat.Weapons.ToArray(), new ZeroResolver(), combat.Selector, combat.MaxHealthRaw));
            Assert.That(world.State(0).Weapon, Is.EqualTo(2));
            Assert.That(world.State(0).Ammo, Is.EqualTo(10));
            var ticks = FireTicks(combat, weapon, 2);
            Assert.That(ticks[1] - ticks[0], Is.EqualTo(45));
        }

        [Test]
        public void ANewPurelyDataPackEquipsAndFiresWithoutNewCSharp()
        {
            var pack = OnRwr("ancient", Layer("ancient", @"""weapons"": [ { ""id"": ""anc.javelin"", ""fireIntervalMilli"": 5000, ""magazine"": 3,
                ""reloadMilli"": 30000, ""falloffStart"": 2, ""falloffEnd"": 20, ""effectRaw"": 40000, ""spreadBase"": 0, ""spreadGrowth"": 0, ""spreadRecoverPerSec"": 0, ""spreadMax"": 0 } ],
                ""unitTypes"": [ { ""id"": ""anc.thrower"", ""weapon"": ""anc.javelin"" } ],
                ""match"": { ""captureTicks"": 60, ""maxTicks"": 600, ""sight"": 20 }"));
            int weapon = pack.WeaponOfUnitType("anc.thrower");
            Assert.That(weapon, Is.EqualTo(3));
            Assert.That(pack.Match.CaptureTicks, Is.EqualTo(60));
            var ticks = FireTicks(pack.BuildCombat(), weapon, 2);
            Assert.That(ticks[1] - ticks[0], Is.EqualTo(5));
        }

        [Test]
        public void ChangedDataSnapshotRestoresIntoChangedDataAndIsRejectedByDefaultData()
        {
            var changed = OnRwr("slow", Layer("slow", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMilli\": 6780 } ]")).BuildCombat();
            var a = new M5TestWorld(M5TestWorld.Pair(), changed);
            for (int i = 0; i < 20; i++) a.Step();
            var snapshot = a.Sim.SaveRetainedSnapshot();
            var same = new M5TestWorld(M5TestWorld.Pair(), changed);
            same.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(same.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()));
            var other = new M5TestWorld(M5TestWorld.Pair(), RwrPack.Create());
            var error = Assert.Throws<InvalidOperationException>(() => other.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(error.Message, Does.Contain("Combat policy or layer masks differ"));
        }

        [Test]
        public void SameDataLoadsFromAnyWorkingDirectory()
        {
            ulong expected = RwrPack.Create().PolicyDigest;
            string start = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(Path.Combine(start, "Assets", "WarCore"));
                Assert.That(RwrPack.Create().PolicyDigest, Is.EqualTo(expected));
            }
            finally { Directory.SetCurrentDirectory(start); }
        }

        // ---- errors: every message names the pack or layer, the entry id and the field ----

        private static string Fail(string id, string json, string extendsRwr = null)
        {
            var source = new MemoryContentSource(new Dictionary<string, string> { [id] = json }, FileContentSource.Rwr());
            return Assert.Throws<ContentException>(() => ContentLoader.Load(source, id)).Message;
        }

        private static void Names(string message, params string[] parts)
        {
            foreach (string part in parts) Assert.That(message, Does.Contain(part), message);
        }

        [Test]
        public void MissingFileNamesTheLayerAndThePathsTried()
        {
            var error = Assert.Throws<ContentException>(() => ContentLoader.Load(new FileContentSource("no-such-dir"), "ghost"));
            Names(error.Message, "ghost", "no-such-dir");
        }

        [Test]
        public void DanglingExtendsNamesBothLayers() =>
            Names(Fail("a", "{ \"id\": \"a\", \"extends\": \"nowhere\" }"), "nowhere", "'a'");

        [Test]
        public void InheritanceCycleIsRejected()
        {
            var source = new MemoryContentSource(new Dictionary<string, string>
            {
                ["x"] = "{ \"id\": \"x\", \"extends\": \"y\" }", ["y"] = "{ \"id\": \"y\", \"extends\": \"x\" }"
            });
            Names(Assert.Throws<ContentException>(() => ContentLoader.Load(source, "x")).Message, "cycle", "x", "y");
        }

        [Test]
        public void DanglingUnitTypeWeaponNamesPackLayerEntryAndField() =>
            Names(Fail("bad", Layer("bad", "\"unitTypes\": [ { \"id\": \"bad.unit\", \"weapon\": \"no.such\" } ]")), "'bad'", "bad.unit", "'weapon'", "no.such");

        [Test]
        public void DuplicateIdInOneLayerIsRejected() =>
            Names(Fail("dup", Layer("dup", "\"weapons\": [ { \"id\": \"w\" }, { \"id\": \"w\" } ]")), "'dup'", "'w'", "duplicated");

        [Test]
        public void MisspelledFieldIsRejectedNotIgnored() =>
            Names(Fail("typo", Layer("typo", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMili\": 1 } ]")), "'typo'", "rwr.weapon-1", "fireIntervalMili", "unknown field");

        [Test]
        public void UnknownTopLevelFieldIsRejected() =>
            Names(Fail("top", "{ \"id\": \"top\", \"extends\": \"rwr\", \"weapon\": [] }"), "'top'", "'weapon'");

        [Test]
        public void MissingFieldInANewWeaponIsRejectedNotDefaulted() =>
            Names(Fail("miss", Layer("miss", "\"weapons\": [ { \"id\": \"miss.w\", \"magazine\": 3 } ]")), "'miss'", "miss.w", "missing field");

        [Test]
        public void DuplicateJsonPropertyIsRejected() =>
            Names(Fail("twice", Layer("twice", "\"match\": { \"captureTicks\": 1, \"captureTicks\": 2 }")), "'twice'", "captureTicks", "repeated");

        [Test]
        public void UnknownPolicyListsTheAllowedOnes() =>
            Names(Fail("pol", Layer("pol", "\"combat\": { \"resolver\": \"made.up\" }")), "'pol'", "'resolver'", "made.up", "rwr.hit");

        [Test]
        public void UnknownSelectorIsRejected() =>
            Names(Fail("sel", Layer("sel", "\"combat\": { \"selector\": \"made.up\" }")), "'sel'", "'selector'", "nearest-visible");

        [Test]
        public void NumbersThatOverflowOrAreOutOfRangeNameTheField()
        {
            Names(Fail("o1", Layer("o1", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"magazine\": 99999999999 } ]")), "'o1'", "rwr.weapon-1", "'magazine'");
            Names(Fail("o2", Layer("o2", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"effectRaw\": 99999999999999999999 } ]")), "'o2'", "'effectRaw'");
            Names(Fail("o3", Layer("o3", "\"match\": { \"gridColumns\": 4097 } ")), "'o3'", "'gridColumns'");
            Names(Fail("o4", Layer("o4", "\"match\": { \"gridRows\": 0 } ")), "'o4'", "'gridRows'");
            Names(Fail("o5", Layer("o5", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffEnd\": 1234567890.1234567 } ]")), "'o5'", "'falloffEnd'");
        }

        [Test]
        public void WeaponRangeRulesComeFromWeaponDefAndNameTheField()
        {
            Names(Fail("r1", Layer("r1", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffStart\": 80 } ]")), "'r1'", "rwr.weapon-1", "falloffStart");
            Names(Fail("r2", Layer("r2", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMilli\": 999 } ]")), "'r2'", "fireIntervalMilli");
            // effectRaw above 65536 is legal for a weapon; a probability limit belongs to a specific resolver.
            Assert.That(OnRwr("big", Layer("big", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"effectRaw\": 65537 } ]")).BuildCombat().Weapons[0].EffectRaw, Is.EqualTo(65537));
        }

        [Test]
        public void MaxHealthMustBePositiveAndInterruptMustBeKnown()
        {
            Names(Fail("h", Layer("h", "\"combat\": { \"maxHealth\": 0 }")), "'h'", "'maxHealth'");
            Names(Fail("i", Layer("i", "\"match\": { \"interrupt\": \"stop\" }")), "'i'", "'interrupt'", "reset");
        }

        [Test]
        public void MatchRuleErrorsFromMatchPackNameThePack()
        {
            var pack = OnRwr("m", Layer("m", "\"match\": { \"maxTicks\": 600 }"));
            var points = new[] { new ControlPointDef(7, M6World.P(20), SimFix.FromInt(6)), new ControlPointDef(7, M6World.P(30), SimFix.FromInt(6)) };
            Names(Assert.Throws<ContentException>(() => pack.BuildMatch(points, new[] { new SpawnAnchor(1, M6World.P(6)) })).Message, "'m'", "match section", "unique");
        }

        [Test]
        public void LayerIdMustEqualTheFileName() =>
            Names(Fail("one", "{ \"id\": \"two\" }"), "'one'", "'two'");

        [Test]
        public void MalformedJsonReportsTheLine() =>
            Names(Fail("junk", "{ \"id\": \"junk\",\n \"weapons\": [ nope ] }"), "'junk'", "line 2");

        [Test]
        public void ErrorsNameTheLayerThatWroteTheBadField()
        {
            var layers = new Dictionary<string, string>
            {
                ["parent"] = Layer("parent", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffStart\": 90 } ]"),
                ["child"] = "{ \"id\": \"child\", \"extends\": \"parent\" }",
                ["fixer"] = "{ \"id\": \"fixer\", \"extends\": \"parent\", \"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffStart\": 10 } ] }",
                ["breaker"] = "{ \"id\": \"breaker\", \"extends\": \"fixer\", \"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffStart\": 90 } ] }",
            };
            var source = new MemoryContentSource(layers, FileContentSource.Rwr());
            Names(Assert.Throws<ContentException>(() => ContentLoader.Load(source, "child")).Message, "pack 'child'", "layer 'parent'", "rwr.weapon-1", "'falloffStart'");
            Names(Assert.Throws<ContentException>(() => ContentLoader.Load(source, "breaker")).Message, "pack 'breaker'", "layer 'breaker'", "'falloffStart'");
            Assert.That(ContentLoader.Load(source, "fixer").BuildCombat().Weapons[0].FalloffStart, Is.EqualTo(SimFix.FromInt(10)));
        }

        [Test]
        public void DuplicatePropertyErrorNamesThePathAndTheEntry() =>
            Names(Fail("twice2", Layer("twice2", "\"weapons\": [ { \"id\": \"rwr.weapon-2\", \"magazine\": 1, \"magazine\": 2 } ]")), "'twice2'", "weapons", "id=rwr.weapon-2", "magazine");

        [Test]
        public void RawControlCharactersInStringsAreRejected()
        {
            Names(Fail("tab", "{ \"id\": \"tab\t\" }"), "'tab'", "control character");
            Names(Fail("nul", "{ \"id\": \"nul\0\" }"), "'nul'", "control character");
        }

        [Test]
        public void DecimalStringsAreAcceptedAndNonNumbersAreNot()
        {
            var weapon = OnRwr("str", Layer("str", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffStart\": \"1.5\", \"falloffEnd\": \"34\" } ]")).BuildCombat().Weapons[0];
            Assert.That(weapon.FalloffStart, Is.EqualTo(SimFix.Ratio(3, 2)));
            Assert.That(weapon.FalloffEnd, Is.EqualTo(SimFix.FromInt(34)));
            Names(Fail("nan", Layer("nan", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffEnd\": \"abc\" } ]")), "'nan'", "'falloffEnd'", "decimal");
            Names(Fail("emp", Layer("emp", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffEnd\": \"\" } ]")), "'emp'", "'falloffEnd'");
        }

        [Test]
        public void HugeDecimalsAreContentErrorsNotOverflowExceptions()
        {
            Names(Fail("big1", Layer("big1", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffEnd\": 999999999999999 } ]")), "'big1'", "'falloffEnd'", "too large");
            Names(Fail("big2", Layer("big2", "\"combat\": { \"maxHealth\": 999999999999999 }")), "'big2'", "'maxHealth'");
        }

        [Test]
        public void TimeAndEffectBoundsKeepCombatArithmeticSafe()
        {
            Names(Fail("t1", Layer("t1", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMilli\": 9223372036854775807 } ]")), "'t1'", "'fireIntervalMilli'", "out of range");
            Names(Fail("t2", Layer("t2", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"reloadMilli\": 1000000000001 } ]")), "'t2'", "'reloadMilli'");
            Names(Fail("t3", Layer("t3", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"effectRaw\": 9223372036854775807 } ]")), "'t3'", "'effectRaw'");
            var edge = OnRwr("edge", Layer("edge", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMilli\": 1000000000000, \"reloadMilli\": 1000000000000, \"effectRaw\": 1099511627776 } ]")).BuildCombat().Weapons[0];
            Assert.That(edge.EffectRaw * SimFix.Scale, Is.GreaterThan(0), "the largest legal effectRaw times Scale stays inside long");
        }

        [Test]
        public void IllegalMatchRulesFailAtLoadNamingTheFieldThatIsWrong()
        {
            Names(Fail("c0", Layer("c0", "\"match\": { \"captureTicks\": 0 }")), "'c0'", "'captureTicks'");
            Names(Fail("c1", Layer("c1", "\"match\": { \"intervalTicks\": 0 }")), "'c1'", "'intervalTicks'");
            Names(Fail("c2", Layer("c2", "\"match\": { \"thinkInterval\": 0 }")), "'c2'", "'thinkInterval'");
            Names(Fail("c3", Layer("c3", "\"match\": { \"maxTicks\": 0 }")), "'c3'", "'maxTicks'");
            Names(Fail("c4", Layer("c4", "\"match\": { \"captureAdvantageNum\": 1, \"captureAdvantageDen\": 2 }")), "'c4'", "'captureAdvantageNum'");
            Names(Fail("c5", Layer("c5", "\"match\": { \"sight\": 0 }")), "'c5'", "'sight'");
            Names(Fail("c6", Layer("c6", "\"match\": { \"gridSpacing\": 0 }")), "'c6'", "'gridSpacing'");
            Names(Fail("c7", Layer("c7", "\"match\": { \"decayPerTick\": 0 }")), "'c7'", "'decayPerTick'");
        }

        [Test]
        public void UnitTypesCanBeEnumerated()
        {
            var pack = RwrPack.Load("rwr");
            Assert.That(pack.UnitTypeCount, Is.EqualTo(2));
            Assert.That(pack.UnitTypeId(0), Is.EqualTo(RwrPack.SoldierTeam1));
            Assert.That(pack.UnitTypeId(1), Is.EqualTo(RwrPack.SoldierTeam2));
        }

        [Test]
        public void ALayerOverridingRulesAndEquipmentSeesBothInTheM6AndM7Recipes()
        {
            var layers = new Dictionary<string, string>
            {
                ["both"] = "{ \"id\": \"both\", \"extends\": \"rwr-m6\", " +
                    "\"unitTypes\": [ { \"id\": \"rwr.soldier-2\", \"weapon\": \"rwr.weapon-3\" } ], " +
                    "\"match\": { \"captureTicks\": 150, \"sight\": 20 } }"
            };
            var pack = ContentLoader.Load(new MemoryContentSource(layers, FileContentSource.Rwr()), "both");
            foreach (var recipe in new[] { DemoRecipes.M6(pack), DemoRecipes.M7(pack) })
            {
                Assert.That(recipe.Match.CaptureTicks, Is.EqualTo(150));
                Assert.That(recipe.Match.Sight, Is.EqualTo(SimFix.FromInt(20)));
                var selector = (SightLimited)recipe.Combat.Selector;
                Assert.That(selector.Sight, Is.EqualTo(SimFix.FromInt(20)));
                var team2 = recipe.Units.ToArray().First(u => u.Team == 2);
                var team1 = recipe.Units.ToArray().First(u => u.Team == 1);
                Assert.That(team2.Weapon, Is.EqualTo(2));
                Assert.That(team1.Weapon, Is.EqualTo(0));
            }
            Assert.That(DemoRecipes.M6().Match.CaptureTicks, Is.EqualTo(300));
        }

        [Test]
        public void RwrLayersLoadByIdWithTheExpectedShape()
        {
            var pack = RwrPack.Load("rwr-m6");
            Assert.That(pack.WeaponCount, Is.EqualTo(3));
            Assert.That(pack.WeaponId(0), Is.EqualTo("rwr.weapon-1"));
            Assert.That(pack.WeaponOfUnitType(RwrPack.SoldierTeam2), Is.EqualTo(1));
            Assert.That(pack.BuildCombat().Selector, Is.TypeOf<SightLimited>());
            Assert.That(RwrPack.Create().Selector, Is.TypeOf<NearestVisibleEnemy>());
        }
    }
}
