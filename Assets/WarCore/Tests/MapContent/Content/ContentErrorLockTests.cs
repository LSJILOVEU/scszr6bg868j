using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Content;

namespace WarCore.Tests
{
    // Reads layers from memory and reports a fixed location, so a "layer not found" message has no machine path in it.
    // The shipped rwr layers are copied in from the data directory, so a test layer can extend them.
    internal sealed class FixedLocationSource : IContentSource
    {
        private readonly Dictionary<string, string> layers = new Dictionary<string, string>();

        internal FixedLocationSource(IEnumerable<KeyValuePair<string, string>> own)
        {
            var files = FileContentSource.Rwr();
            foreach (string id in new[] { "rwr", "rwr-m6", "rwr-melee" })
            {
                Assert.That(files.TryRead(id, out string text, out _), Is.True, id);
                layers[id] = text;
            }
            foreach (var pair in own) layers[pair.Key] = pair.Value;
        }

        public bool TryRead(string layerId, out string text, out string location)
        {
            location = "memory:" + layerId;
            return layers.TryGetValue(layerId, out text);
        }
    }

    // Every error message the content loader can raise, recorded before the loader was split into sections and compared
    // letter for letter afterwards. Each row names the existing test it comes from (or "new" when no test looked at the
    // message), the fixture, the parameters and the full expected message. Rows whose source test only checked a
    // substring are the point of this file: the full text is what the refactor must keep.
    public sealed class ContentErrorLockTests
    {
        private const string X = "@@EXPECT@@";

        public sealed class Row
        {
            internal string Source, Fixture, Args, LoadId, Expected;
            internal KeyValuePair<string, string>[] Layers;
            public override string ToString() => Source + " | " + Fixture + " | " + Args;
        }

        private static string Layer(string id, string body) => "{ \"id\": \"" + id + "\", \"extends\": \"rwr\", " + body + " }";

        private static KeyValuePair<string, string> L(string id, string json) => new KeyValuePair<string, string>(id, json);

        private static readonly List<Row> RowList = new List<Row>();

        // A single layer that extends rwr and has the same id as the layer that is loaded.
        private static void One(string source, string fixture, string args, string id, string body, string expected) =>
            RowList.Add(new Row { Source = source, Fixture = fixture, Args = args, LoadId = id, Layers = new[] { L(id, Layer(id, body)) }, Expected = expected });

        // Raw layer text (no extends added) or several layers.
        private static void Raw(string source, string fixture, string args, string loadId, string expected, params KeyValuePair<string, string>[] layers) =>
            RowList.Add(new Row { Source = source, Fixture = fixture, Args = args, LoadId = loadId, Layers = layers, Expected = expected });

        private const string W = "Wp15ContentDataTests.";

        static ContentErrorLockTests()
        {
            // ---- Wp15ContentDataTests ----
            Raw(W + "DanglingExtendsNamesBothLayers", "layer a extends a missing layer", "-", "a", "content layer 'nowhere' not found (extended by 'a'), looked at: memory:nowhere", L("a", "{ \"id\": \"a\", \"extends\": \"nowhere\" }"));
            Raw(W + "InheritanceCycleIsRejected", "x extends y extends x", "-", "x", "content layer inheritance cycle: x -> y -> x",
                L("x", "{ \"id\": \"x\", \"extends\": \"y\" }"), L("y", "{ \"id\": \"y\", \"extends\": \"x\" }"));
            One(W + "DanglingUnitTypeWeaponNamesPackLayerEntryAndField", "bad", "unit type weapon no.such", "bad",
                "\"unitTypes\": [ { \"id\": \"bad.unit\", \"weapon\": \"no.such\" } ]", "content pack 'bad' layer 'bad' unit type 'bad.unit' field 'weapon': referenced weapon 'no.such' does not exist");
            One(W + "DuplicateIdInOneLayerIsRejected", "dup", "weapons w, w", "dup", "\"weapons\": [ { \"id\": \"w\" }, { \"id\": \"w\" } ]", "content layer 'dup' weapon 'w' is duplicated");
            One(W + "MisspelledFieldIsRejectedNotIgnored", "typo", "fireIntervalMili", "typo",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMili\": 1 } ]", "content pack 'typo' layer 'typo' weapon 'rwr.weapon-1' field 'fireIntervalMili': unknown field name (misspelled?)");
            Raw(W + "UnknownTopLevelFieldIsRejected", "top", "weapon (singular)", "top", "content layer 'top' has unknown field 'weapon' (line 1); allowed: id, extends, weapons, melee, block, unitTypes, combat, match, surfaces, slopeRule, ai, rules", L("top", "{ \"id\": \"top\", \"extends\": \"rwr\", \"weapon\": [] }"));
            One(W + "MissingFieldInANewWeaponIsRejectedNotDefaulted", "miss", "new weapon with only magazine", "miss",
                "\"weapons\": [ { \"id\": \"miss.w\", \"magazine\": 3 } ]", "content pack 'miss' layer 'miss' weapon 'miss.w' is missing field 'fireIntervalMilli'");
            One(W + "DuplicateJsonPropertyIsRejected", "twice", "captureTicks twice", "twice", "\"match\": { \"captureTicks\": 1, \"captureTicks\": 2 }", "content layer 'twice' line 1 (match): field 'captureTicks' is repeated in the same object");
            One(W + "UnknownPolicyListsTheAllowedOnes", "pol", "resolver made.up", "pol", "\"combat\": { \"resolver\": \"made.up\" }", "content pack 'pol' layer 'pol' combat field 'resolver': unknown policy 'made.up', allowed: rwr.hit");
            One(W + "UnknownSelectorIsRejected", "sel", "selector made.up", "sel", "\"combat\": { \"selector\": \"made.up\" }", "content pack 'sel' layer 'sel' combat field 'selector': unknown policy 'made.up', allowed: nearest-visible, sight-limited");
            One(W + "NumbersThatOverflowOrAreOutOfRangeNameTheField", "o1", "magazine 99999999999", "o1",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"magazine\": 99999999999 } ]", "content pack 'o1' layer 'o1' weapon 'rwr.weapon-1' field 'magazine': out of range [-2147483648, 2147483647]");
            One(W + "NumbersThatOverflowOrAreOutOfRangeNameTheField", "o2", "effectRaw 99999999999999999999", "o2",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"effectRaw\": 99999999999999999999 } ]", "content pack 'o2' layer 'o2' weapon 'rwr.weapon-1' field 'effectRaw': number too large");
            One(W + "NumbersThatOverflowOrAreOutOfRangeNameTheField", "o3", "gridColumns 4097", "o3", "\"match\": { \"gridColumns\": 4097 } ", "content pack 'o3' layer 'o3' match field 'gridColumns': out of range [1, 4096]");
            One(W + "NumbersThatOverflowOrAreOutOfRangeNameTheField", "o4", "gridRows 0", "o4", "\"match\": { \"gridRows\": 0 } ", "content pack 'o4' layer 'o4' match field 'gridRows': out of range [1, 4096]");
            One(W + "NumbersThatOverflowOrAreOutOfRangeNameTheField", "o5", "falloffEnd 1234567890.1234567", "o5",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffEnd\": 1234567890.1234567 } ]", "content pack 'o5' layer 'o5' weapon 'rwr.weapon-1' field 'falloffEnd': too many digits (at most 15)");
            One(W + "WeaponRangeRulesComeFromWeaponDefAndNameTheField", "r1", "falloffStart 80", "r1",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffStart\": 80 } ]", "content pack 'r1' layer 'r1' weapon 'rwr.weapon-1' field 'falloffStart' is out of its legal range");
            One(W + "WeaponRangeRulesComeFromWeaponDefAndNameTheField", "r2", "fireIntervalMilli 999", "r2",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMilli\": 999 } ]", "content pack 'r2' layer 'r2' weapon 'rwr.weapon-1' field 'fireIntervalMilli' is out of its legal range");
            One(W + "MaxHealthMustBePositiveAndInterruptMustBeKnown", "h", "maxHealth 0", "h", "\"combat\": { \"maxHealth\": 0 }", "content pack 'h' layer 'h' combat field 'maxHealth': must be greater than 0");
            One(W + "MaxHealthMustBePositiveAndInterruptMustBeKnown", "i", "interrupt stop", "i", "\"match\": { \"interrupt\": \"stop\" }", "content pack 'i' layer 'i' match field 'interrupt': must be reset, pause or decay");
            Raw(W + "LayerIdMustEqualTheFileName", "one", "id two", "one", "content layer file name is 'one' but its id is 'two'", L("one", "{ \"id\": \"two\" }"));
            Raw(W + "MalformedJsonReportsTheLine", "junk", "bad token on line 2", "junk", "content layer 'junk' line 2 (weapons.[0]): unexpected 'n' (null and exponent numbers are not supported)", L("junk", "{ \"id\": \"junk\",\n \"weapons\": [ nope ] }"));
            {
                // ErrorsNameTheLayerThatWroteTheBadField
                var layers = new[]
                {
                    L("parent", Layer("parent", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffStart\": 90 } ]")),
                    L("child", "{ \"id\": \"child\", \"extends\": \"parent\" }"),
                    L("breaker", "{ \"id\": \"breaker\", \"extends\": \"fixer\", \"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffStart\": 90 } ] }"),
                    L("fixer", "{ \"id\": \"fixer\", \"extends\": \"parent\", \"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffStart\": 10 } ] }"),
                };
                Raw(W + "ErrorsNameTheLayerThatWroteTheBadField", "parent / child / fixer / breaker", "load child", "child", "content pack 'child' layer 'parent' weapon 'rwr.weapon-1' field 'falloffStart' is out of its legal range", layers);
                Raw(W + "ErrorsNameTheLayerThatWroteTheBadField", "parent / child / fixer / breaker", "load breaker", "breaker", "content pack 'breaker' layer 'breaker' weapon 'rwr.weapon-1' field 'falloffStart' is out of its legal range", layers);
            }
            One(W + "DuplicatePropertyErrorNamesThePathAndTheEntry", "twice2", "magazine twice in a weapon", "twice2",
                "\"weapons\": [ { \"id\": \"rwr.weapon-2\", \"magazine\": 1, \"magazine\": 2 } ]", "content layer 'twice2' line 1 (weapons.[id=rwr.weapon-2]): field 'magazine' is repeated in the same object");
            Raw(W + "RawControlCharactersInStringsAreRejected", "tab", "tab in an id", "tab", "content layer 'tab' line 1 (id): a string cannot contain a raw control character (use an escape)", L("tab", "{ \"id\": \"tab\t\" }"));
            Raw(W + "RawControlCharactersInStringsAreRejected", "nul", "NUL in an id", "nul", "content layer 'nul' line 1 (id): a string cannot contain a raw control character (use an escape)", L("nul", "{ \"id\": \"nul\0\" }"));
            One(W + "DecimalStringsAreAcceptedAndNonNumbersAreNot", "nan", "falloffEnd abc", "nan", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffEnd\": \"abc\" } ]", "content pack 'nan' layer 'nan' weapon 'rwr.weapon-1' field 'falloffEnd': must be a decimal number such as 34 or 1.5");
            One(W + "DecimalStringsAreAcceptedAndNonNumbersAreNot", "emp", "falloffEnd empty string", "emp", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffEnd\": \"\" } ]", "content pack 'emp' layer 'emp' weapon 'rwr.weapon-1' field 'falloffEnd': must be a decimal number such as 34 or 1.5");
            One(W + "HugeDecimalsAreContentErrorsNotOverflowExceptions", "big1", "falloffEnd 999999999999999", "big1",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"falloffEnd\": 999999999999999 } ]", "content pack 'big1' layer 'big1' weapon 'rwr.weapon-1' field 'falloffEnd': value too large");
            One(W + "HugeDecimalsAreContentErrorsNotOverflowExceptions", "big2", "maxHealth 999999999999999", "big2", "\"combat\": { \"maxHealth\": 999999999999999 }", "content pack 'big2' layer 'big2' combat field 'maxHealth': value too large");
            One(W + "TimeAndEffectBoundsKeepCombatArithmeticSafe", "t1", "fireIntervalMilli long.MaxValue", "t1",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMilli\": 9223372036854775807 } ]", "content pack 't1' layer 't1' weapon 'rwr.weapon-1' field 'fireIntervalMilli': out of range [1, 1000000000000]");
            One(W + "TimeAndEffectBoundsKeepCombatArithmeticSafe", "t2", "reloadMilli 1000000000001", "t2",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"reloadMilli\": 1000000000001 } ]", "content pack 't2' layer 't2' weapon 'rwr.weapon-1' field 'reloadMilli': out of range [1, 1000000000000]");
            One(W + "TimeAndEffectBoundsKeepCombatArithmeticSafe", "t3", "effectRaw long.MaxValue", "t3",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"effectRaw\": 9223372036854775807 } ]", "content pack 't3' layer 't3' weapon 'rwr.weapon-1' field 'effectRaw': out of range [0, 1099511627776]");
            One(W + "IllegalMatchRulesFailAtLoadNamingTheFieldThatIsWrong", "c0", "captureTicks 0", "c0", "\"match\": { \"captureTicks\": 0 }", "content pack 'c0' layer 'c0' match field 'captureTicks': out of range [1, 2147483647]");
            One(W + "IllegalMatchRulesFailAtLoadNamingTheFieldThatIsWrong", "c1", "intervalTicks 0", "c1", "\"match\": { \"intervalTicks\": 0 }", "content pack 'c1' layer 'c1' match field 'intervalTicks': out of range [1, 2147483647]");
            One(W + "IllegalMatchRulesFailAtLoadNamingTheFieldThatIsWrong", "c2", "thinkInterval 0", "c2", "\"match\": { \"thinkInterval\": 0 }", "content pack 'c2' layer 'c2' match field 'thinkInterval': out of range [1, 2147483647]");
            One(W + "IllegalMatchRulesFailAtLoadNamingTheFieldThatIsWrong", "c3", "maxTicks 0", "c3", "\"match\": { \"maxTicks\": 0 }", "content pack 'c3' layer 'c3' match field 'maxTicks': out of range [1, 2147483647]");
            One(W + "IllegalMatchRulesFailAtLoadNamingTheFieldThatIsWrong", "c4", "captureAdvantageNum 1 < Den 2", "c4",
                "\"match\": { \"captureAdvantageNum\": 1, \"captureAdvantageDen\": 2 }", "content pack 'c4' layer 'c4' match field 'captureAdvantageNum': must be at least captureAdvantageDen");
            One(W + "IllegalMatchRulesFailAtLoadNamingTheFieldThatIsWrong", "c5", "sight 0", "c5", "\"match\": { \"sight\": 0 }", "content pack 'c5' layer 'c5' match field 'sight': must be greater than 0");
            One(W + "IllegalMatchRulesFailAtLoadNamingTheFieldThatIsWrong", "c6", "gridSpacing 0", "c6", "\"match\": { \"gridSpacing\": 0 }", "content pack 'c6' layer 'c6' match field 'gridSpacing': must be greater than 0");
            One(W + "IllegalMatchRulesFailAtLoadNamingTheFieldThatIsWrong", "c7", "decayPerTick 0", "c7", "\"match\": { \"decayPerTick\": 0 }", "content pack 'c7' layer 'c7' match field 'decayPerTick': out of range [1, 2147483647]");

            // ---- Wp17CombatTests.BadCombatAndSpreadValuesNameTheField (fixture: layer 'bad' extends rwr) ----
            const string C = "Wp17CombatTests.BadCombatAndSpreadValuesNameTheField";
            One(C, "bad", "allyCheckMissRate 1.5", "bad", "\"combat\": { \"allyCheckMissRate\": 1.5 }", "content pack 'bad' layer 'bad' combat field 'allyCheckMissRate': must be between 0 and 1");
            One(C, "bad", "allyHoldMilli 10", "bad", "\"combat\": { \"allyHoldMilli\": 10 }", "content pack 'bad' layer 'bad' combat field 'allyHoldMilli': out of range [1000, 1000000000000]");
            One(C, "bad", "spreadBase 5000 above spreadMax", "bad", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"spreadBase\": 5000 } ]", "content pack 'bad' layer 'rwr' weapon 'rwr.weapon-1' field 'spreadMax': must be at least spreadBase");
            One(C, "bad", "spreadGrowth 99999", "bad", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"spreadGrowth\": 99999 } ]", "content pack 'bad' layer 'bad' weapon 'rwr.weapon-1' field 'spreadGrowth': out of range [0, 90000]");
            One(C, "bad", "new weapon without spread fields", "bad",
                "\"weapons\": [ { \"id\": \"n.gun\", \"fireIntervalMilli\": 3000, \"magazine\": 3, \"reloadMilli\": 9000, \"falloffStart\": 1, \"falloffEnd\": 2, \"effectRaw\": 0 } ]", "content pack 'bad' layer 'bad' weapon 'n.gun' is missing field 'spreadBase'");

            // ---- Wp20MeleeRulesTests.LoaderRejectsBadMeleeData (fixture: Melee(tweak), layer 't') ----
            const string M = "Wp20MeleeRulesTests.LoaderRejectsBadMeleeData";
            Raw(M, "Melee(', \"extra\": 1')", "unknown melee field", "t", "content pack 't' layer 't' melee 'm' field 'extra': unknown field name (misspelled?)", L("t", Melee(", \"extra\": 1")));
            Raw(M, "Melee() without windupTicks", "missing melee field", "t", "content pack 't' layer 't' melee 'm' is missing field 'windupTicks'", L("t", Melee().Replace("\"windupTicks\": 6, ", "")));
            Raw(M, "Melee() windupTicks 0", "windupTicks 0", "t", "content pack 't' layer 't' melee 'm' field 'windupTicks' is out of its legal range", L("t", Melee().Replace("\"windupTicks\": 6", "\"windupTicks\": 0")));
            Raw(M, "Melee() engageRange 2.5", "engageRange above radius", "t", "content pack 't' layer 't' melee 'm' field 'engageRange' is out of its legal range", L("t", Melee().Replace("\"engageRange\": 1.8", "\"engageRange\": 2.5")));
            Raw(M, "Melee() maxTargets 17", "maxTargets 17", "t", "content pack 't' layer 't' melee 'm' field 'maxTargets' is out of its legal range", L("t", Melee().Replace("\"maxTargets\": 3", "\"maxTargets\": 17")));
            Raw(M, "Melee() unit melee nope", "unit type refers to a missing melee", "t", "content pack 't' layer 't' unit type 'u' field 'melee': referenced melee 'nope' does not exist", L("t", Melee().Replace("\"melee\": \"m\"", "\"melee\": \"nope\"")));
            Raw(M, "Melee() unit without melee", "unit type with neither weapon nor melee", "t", "content pack 't' layer 't' unit type 'u' field 'weapon': a unit type without a weapon needs a melee", L("t", Melee().Replace(", \"melee\": \"m\"", "")));
            Raw(M, "Melee() unit weapon nope", "unit type refers to a missing weapon", "t", "content pack 't' layer 't' unit type 'u' field 'weapon': referenced weapon 'nope' does not exist", L("t", Melee().Replace("\"weapon\": \"\"", "\"weapon\": \"nope\"")));

            // ---- ThrustTests.ContentCanDefineAThrust_AndAbsenceIsASector (fixture: Json(tweak), layer 't') ----
            const string T = "ThrustTests.ContentCanDefineAThrust_AndAbsenceIsASector";
            Raw(T, "Json(', \"lineHalfWidth\": -1')", "lineHalfWidth -1", "t", "content pack 't' layer 't' melee 'm' field 'lineHalfWidth' is out of its legal range", L("t", Melee(", \"lineHalfWidth\": -1")));
            Raw(T, "Json(', \"lineHalfWidth\": 17')", "lineHalfWidth 17", "t", "content pack 't' layer 't' melee 'm' field 'lineHalfWidth' is out of its legal range", L("t", Melee(", \"lineHalfWidth\": 17")));
            Raw(T, "Json(', \"lineHalfWidths\": 1')", "misspelled lineHalfWidth", "t", "content pack 't' layer 't' melee 'm' field 'lineHalfWidths': unknown field name (misspelled?)", L("t", Melee(", \"lineHalfWidths\": 1")));

            // ---- Wp22TerrainTests.Rules_BadValuesNamePackEntryAndField and Rules_DuplicateSurfaceIdIsRejected ----
            const string R = "Wp22TerrainTests.Rules_BadValuesNamePackEntryAndField";
            Terrain(R, "\"speedMilli\": 500", "\"speedMilli\": 0", "content pack 'rwr-terrain-demo' layer 'rwr-terrain-demo' surface 'mud' field 'speedMilli': out of range [1, 2000]");
            Terrain(R, "\"speedMilli\": 500", "\"speedMilli\": 2001", "content pack 'rwr-terrain-demo' layer 'rwr-terrain-demo' surface 'mud' field 'speedMilli': out of range [1, 2000]");
            Terrain(R, "\"cost\": 3", "\"cost\": 0", "content pack 'rwr-terrain-demo' layer 'rwr-terrain-demo' surface 'mud' field 'cost': out of range [1, 255]");
            Terrain(R, "\"cost\": 3", "\"cost\": 256", "content pack 'rwr-terrain-demo' layer 'rwr-terrain-demo' surface 'mud' field 'cost': out of range [1, 255]");
            Terrain(R, "\"maxWalkableNum\": 3", "\"maxWalkableNum\": 9", "content pack 'rwr-terrain-demo' layer 'rwr-terrain-demo' slopeRule field 'maxWalkableNum': out of range [0, 8]");
            Terrain(R, "\"maxWalkableDen\": 4", "\"maxWalkableDen\": 4097", "content pack 'rwr-terrain-demo' layer 'rwr-terrain-demo' slopeRule field 'maxWalkableDen': out of range [1, 4096]");
            Terrain(R, "\"cost\": 3", "\"costs\": 3", "content pack 'rwr-terrain-demo' layer 'rwr-terrain-demo' surface 'mud' field 'costs': unknown field name (misspelled?)");
            Terrain("Wp22TerrainTests.Rules_DuplicateSurfaceIdIsRejected", "{ \"id\": \"grass\" }", "{ \"id\": \"mud\" }", "content layer 'rwr-terrain-demo' surface 'mud' is duplicated");

            // ---- ContentWeaponEditTests.ReplacingBaseOrAncestorIsRejected (the cycle is found by ContentLoader.Load) ----
            Raw("ContentWeaponEditTests.ReplacingBaseOrAncestorIsRejected", "generated layer rwr extends rwr-m6, which extends rwr", "load rwr", "rwr", "content layer inheritance cycle: rwr -> rwr-m6 -> rwr",
                L("rwr", "{\n  \"id\": \"rwr\",\n  \"extends\": \"rwr-m6\",\n  \"weapons\": []\n}\n"));

            // ---- new: loader messages no existing test looked at ----
            const string N = "new";
            Raw(N, "root is an array", "-", "r1", "content layer 'r1' must be a JSON object", L("r1", "[ 1 ]"));
            Raw(N, "no id", "-", "r2", "content layer 'r2' is missing field 'id'", L("r2", "{ \"extends\": \"rwr\" }"));
            Raw(N, "id is a number", "-", "r3", "content layer 'r3' field 'id' must be a string", L("r3", "{ \"id\": 5 }"));
            Raw(N, "extends is a number", "-", "r4", "content layer 'r4' field 'extends' must be a string", L("r4", "{ \"id\": \"r4\", \"extends\": 5 }"));
            One(N, "weapons is an object", "-", "s1", "\"weapons\": { }", "content layer 's1' weapon must be an array");
            One(N, "melee is an object", "-", "s2", "\"melee\": { }", "content layer 's2' melee must be an array");
            One(N, "unitTypes is an object", "-", "s3", "\"unitTypes\": { }", "content layer 's3' unit type must be an array");
            One(N, "combat is an array", "-", "s4", "\"combat\": [ ]", "content layer 's4' combat must be an object");
            One(N, "match is an array", "-", "s5", "\"match\": [ ]", "content layer 's5' match must be an object");
            One(N, "surfaces is an object", "-", "s6", "\"surfaces\": { }", "content layer 's6' surface must be an array");
            One(N, "slopeRule is an array", "-", "s7", "\"slopeRule\": [ ]", "content layer 's7' slopeRule must be an object");
            One(N, "weapon entry is a number", "-", "e1", "\"weapons\": [ 5 ]", "content layer 'e1' weapon entry must be an object (line 1)");
            One(N, "weapon entry without id", "-", "e2", "\"weapons\": [ { \"magazine\": 5 } ]", "content layer 'e2' weapon entry needs a string id (line 1)");
            One(N, "weapon entry with an empty id", "-", "e3", "\"weapons\": [ { \"id\": \"\" } ]", "content layer 'e3' weapon entry needs a string id (line 1)");
            One(N, "weapon field is an object", "-", "e4", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"magazine\": { } } ]", "content layer 'e4' weapon 'rwr.weapon-1' field 'magazine' must be a number, string or boolean");
            One(N, "combat field is an array", "-", "e5", "\"combat\": { \"maxHealth\": [ 1 ] }", "content layer 'e5' combat field 'maxHealth' must be a number, string or boolean");
            One(N, "match field with a non-integer", "-", "e6", "\"match\": { \"captureTicks\": 1.5 }", "content pack 'e6' layer 'e6' match field 'captureTicks': must be an integer");
            One(N, "weapon magazine 0 (WeaponDef rejects it)", "-", "e7", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"magazine\": 0 } ]", "content pack 'e7' layer 'e7' weapon 'rwr.weapon-1' field 'magazine' is out of its legal range");
            One(N, "unit type weapon is a number", "-", "e8", "\"unitTypes\": [ { \"id\": \"rwr.soldier-1\", \"weapon\": 3 } ]", "content pack 'e8' layer 'e8' unit type 'rwr.soldier-1' field 'weapon': must be a string");
            One(N, "surface id longer than 255 bytes", "-", "e9", "\"surfaces\": [ { \"id\": \"" + new string('a', 256) + "\" } ]", "content pack 'e9' layer 'e9' surface '" + new string('a', 256) + "' id must be 1-255 bytes");
            One(N, "surface passable is a string", "-", "e10", "\"surfaces\": [ { \"id\": \"s\", \"passable\": \"yes\" } ]", "content pack 'e10' layer 'e10' surface 's' field 'passable': must be true or false");
            Raw(N, "pack without any combat section", "-", "nc", "content pack 'nc' has no combat section", L("nc", "{ \"id\": \"nc\" }"));
            Raw(N, "sight-limited selector without a match section", "-", "nm", "content pack 'nm' uses selector sight-limited, which needs match.sight",
                L("nm", "{ \"id\": \"nm\", \"combat\": { \"maxHealth\": 100, \"resolver\": \"rwr.hit\", \"selector\": \"sight-limited\", \"friendlyFire\": false,"
                    + " \"friendlyFireLocked\": false, \"allyCheckMissRate\": 0.25, \"allyHoldMilli\": 30000 } }"));
            One(N, "melee referenced by id but no such melee entry", "-", "e11",
                "\"unitTypes\": [ { \"id\": \"rwr.soldier-1\", \"weapon\": \"rwr.weapon-1\", \"melee\": \"gone\" } ]", "content pack 'e11' layer 'e11' unit type 'rwr.soldier-1' field 'melee': referenced melee 'gone' does not exist");
            One(N, "unit type melee is a number", "-", "e12", "\"unitTypes\": [ { \"id\": \"rwr.soldier-1\", \"melee\": 3 } ]", "content pack 'e12' layer 'e12' unit type 'rwr.soldier-1' field 'melee': must be a string");

            // ---- new: which error comes first when the input has several ----
            // Merge order: every section holds a value of the wrong kind; dropping the first one each time exposes the next.
            string[] wrongKind =
            {
                "\"weapons\": { }", "\"melee\": { }", "\"unitTypes\": { }", "\"combat\": [ ]", "\"match\": [ ]", "\"surfaces\": { }", "\"slopeRule\": [ ]"
            };
            string[] mergeExpected = { "content layer 'mo0' weapon must be an array", "content layer 'mo1' melee must be an array", "content layer 'mo2' unit type must be an array", "content layer 'mo3' combat must be an object", "content layer 'mo4' match must be an object", "content layer 'mo5' surface must be an array", "content layer 'mo6' slopeRule must be an object" };
            for (int first = 0; first < wrongKind.Length; first++)
                One("new.merge-order", "sections " + first + ".. have the wrong kind", "the first reported is section " + first, "mo" + first,
                    string.Join(", ", wrongKind, first, wrongKind.Length - first), mergeExpected[first]);
            // Layer-level order: the top-level key check and the id check come before the parent is read; the parent merges before the child.
            Raw("new.layer-order", "unknown key and a missing parent", "-", "lo1", "content layer 'lo1' has unknown field 'nope' (line 1); allowed: id, extends, weapons, melee, block, unitTypes, combat, match, surfaces, slopeRule, ai, rules", L("lo1", "{ \"id\": \"lo1\", \"extends\": \"missing\", \"nope\": 1 }"));
            Raw("new.layer-order", "wrong id and a missing parent", "-", "lo2", "content layer file name is 'lo2' but its id is 'other'", L("lo2", "{ \"id\": \"other\", \"extends\": \"missing\" }"));
            Raw("new.layer-order", "bad weapons in parent and child", "-", "lo4", "content layer 'lo3' weapon must be an array",
                L("lo3", Layer("lo3", "\"weapons\": { }")), L("lo4", "{ \"id\": \"lo4\", \"extends\": \"lo3\", \"weapons\": { } }"));
            Raw("new.layer-order", "bad combat in parent, bad weapons in child", "-", "lo6", "content layer 'lo5' combat must be an object",
                L("lo5", Layer("lo5", "\"combat\": [ ]")), L("lo6", "{ \"id\": \"lo6\", \"extends\": \"lo5\", \"weapons\": { } }"));
            // Build order: weapons, melee, unitTypes, combat, match, slopeRule, surfaces; each section has a build error.
            string[] buildBad =
            {
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"magazine\": 99999999999 } ]",
                "\"melee\": [ { \"id\": \"mm\" } ]",
                "\"unitTypes\": [ { \"id\": \"u.bad\", \"weapon\": \"no.such\" } ]",
                "\"combat\": { \"maxHealth\": 0 }",
                "\"match\": { \"gridRows\": 0 }",
                "\"slopeRule\": { \"maxWalkableDen\": 0 }",
                "\"surfaces\": [ { \"id\": \"s\", \"speedMilli\": 0 } ]"
            };
            string[] buildExpected = { "content pack 'bo0' layer 'bo0' weapon 'rwr.weapon-1' field 'magazine': out of range [-2147483648, 2147483647]", "content pack 'bo1' layer 'bo1' melee 'mm' is missing field 'windupTicks'", "content pack 'bo2' layer 'bo2' unit type 'u.bad' field 'weapon': referenced weapon 'no.such' does not exist", "content pack 'bo3' layer 'bo3' combat field 'maxHealth': must be greater than 0", "content pack 'bo4' layer 'bo4' match field 'gridRows': out of range [1, 4096]", "content pack 'bo5' layer 'bo5' slopeRule field 'maxWalkableDen': out of range [1, 4096]", "content pack 'bo6' layer 'bo6' surface 's' field 'speedMilli': out of range [1, 2000]" };
            for (int first = 0; first < buildBad.Length; first++)
                One("new.build-order", "sections " + first + ".. have a build error", "the first reported is build step " + first, "bo" + first,
                    string.Join(", ", buildBad, first, buildBad.Length - first), buildExpected[first]);
            One("new.build-order", "slopeRule den 0 and surface speedMilli 0 (the merge order is the reverse)", "slopeRule is reported", "bo7",
                "\"surfaces\": [ { \"id\": \"s\", \"speedMilli\": 0 } ], \"slopeRule\": { \"maxWalkableDen\": 0 }", "content pack 'bo7' layer 'bo7' slopeRule field 'maxWalkableDen': out of range [1, 4096]");
            One("new.build-order", "two weapon entries, the first has an unknown field, the second a bad number", "the first entry is reported", "bo8",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMili\": 1 }, { \"id\": \"rwr.weapon-2\", \"magazine\": 99999999999 } ]", "content pack 'bo8' layer 'bo8' weapon 'rwr.weapon-1' field 'fireIntervalMili': unknown field name (misspelled?)");
            One("new.build-order", "two weapon entries, the first has a bad number, the second an unknown field", "the first entry is reported", "bo9",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"magazine\": 99999999999 }, { \"id\": \"rwr.weapon-2\", \"fireIntervalMili\": 1 } ]", "content pack 'bo9' layer 'bo9' weapon 'rwr.weapon-1' field 'magazine': out of range [-2147483648, 2147483647]");
            One("new.build-order", "two surface entries, each with a bad value", "the first entry is reported", "bo10",
                "\"surfaces\": [ { \"id\": \"s1\", \"cost\": 0 }, { \"id\": \"s2\", \"speedMilli\": 0 } ]", "content pack 'bo10' layer 'bo10' surface 's1' field 'cost': out of range [1, 255]");
            One("new.build-order", "one weapon entry with a bad interval and a bad reload", "the interval is reported", "bo11",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMilli\": 0, \"reloadMilli\": 0 } ]", "content pack 'bo11' layer 'bo11' weapon 'rwr.weapon-1' field 'fireIntervalMilli': out of range [1, 1000000000000]");
            One("new.build-order", "one weapon entry with an unknown field and a bad number", "the bad number is reported", "bo12",
                "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"fireIntervalMili\": 1, \"magazine\": 99999999999 } ]", "content pack 'bo12' layer 'bo12' weapon 'rwr.weapon-1' field 'magazine': out of range [-2147483648, 2147483647]");
        }

        private static string Melee(string tweak = "") =>
            "{ \"id\": \"t\", \"extends\": \"rwr\", \"melee\": [ { \"id\": \"m\", \"windupTicks\": 6, \"activeTicks\": 2, \"recoveryTicks\": 9, \"radius\": 2, \"cosHalfAnglePermille\": 500,"
            + " \"halfHeight\": 1, \"damage\": 34, \"maxTargets\": 3, \"engageRange\": 1.8" + tweak + " } ],"
            + " \"unitTypes\": [ { \"id\": \"u\", \"weapon\": \"\", \"melee\": \"m\" } ] }";

        private static void Terrain(string source, string from, string to, string expected)
        {
            Assert.That(TerrainDemoMap.PackJson, Does.Contain(from), "test text drifted");
            RowList.Add(new Row
            {
                Source = source, Fixture = "TerrainDemoMap.PackJson", Args = from + " -> " + to, LoadId = TerrainDemoMap.PackId,
                Layers = new[] { L(TerrainDemoMap.PackId, TerrainDemoMap.PackJson.Replace(from, to)) }, Expected = expected
            });
        }

        internal static IEnumerable<TestCaseData> Rows()
        {
            foreach (Row row in RowList) yield return new TestCaseData(row).SetName(row.ToString());
        }

        [TestCaseSource(nameof(Rows))]
        public void TheLoaderRaisesThisExactMessage(Row row)
        {
            var source = new FixedLocationSource(row.Layers);
            ContentException error = Assert.Throws<ContentException>(() => ContentLoader.Load(source, row.LoadId));
            Assert.That(error.Message, Is.EqualTo(row.Expected));
        }

        [Test]
        public void EveryRowHasARecordedMessageAndTheRowsAreDistinct()
        {
            var seen = new HashSet<string>();
            foreach (Row row in RowList)
            {
                Assert.That(row.Expected, Is.Not.EqualTo(X), row.ToString());
                Assert.That(seen.Add(row.Source + "|" + row.Fixture + "|" + row.Args), Is.True, row.ToString());
            }
            Assert.That(RowList.Count, Is.EqualTo(115));
        }

        // The messages that carry a file path are built at run time from the path the source reports.

        [Test]
        public void ALayerMissingFromTheFileSourceNamesTheFileItLookedAt()
        {
            var files = FileContentSource.Rwr();
            Assert.That(files.TryRead("missing", out _, out string location), Is.False);
            string expected = "content layer 'missing' not found, looked at: " + location;
            Assert.That(Assert.Throws<ContentException>(() => ContentLoader.Load(files, "missing")).Message, Is.EqualTo(expected));
            // ContentLayersTests.RecipesUseSelectedLayerForCombatUnitsAndMatch / WeaponsExposeLoadedValuesAndUnitNames reach the same message.
            Assert.That(Assert.Throws<ContentException>(() => WarCore.Presentation.ContentLayers.M6("missing")).Message, Is.EqualTo(expected));
            Assert.That(Assert.Throws<ContentException>(() => WarCore.Presentation.ContentLayers.M7("missing")).Message, Is.EqualTo(expected));
            Assert.That(Assert.Throws<ContentException>(() => WarCore.Presentation.ContentLayers.Weapons("missing")).Message, Is.EqualTo(expected));
        }

        [Test]
        public void Wp15MissingFileCase_ADirectoryThatDoesNotExist()
        {
            var source = new FileContentSource("no-such-dir");
            string expected = "content layer 'ghost' not found, looked at: " + Path.Combine("no-such-dir", "ghost.json");
            Assert.That(Assert.Throws<ContentException>(() => ContentLoader.Load(source, "ghost")).Message, Is.EqualTo(expected));
        }
    }
}
