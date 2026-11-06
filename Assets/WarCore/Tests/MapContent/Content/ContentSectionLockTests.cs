using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Direct tests of what the loader does with each of the seven data sections: how a child layer merges onto its
    // parent (…_Merge…) and how the merged entries become typed values (…_Build…). Every assertion is an exact value
    // written down from the shipped data or from the layer text in the test, not read back from the loader.
    public sealed class ContentSectionLockTests
    {
        private static ContentPack Pack(string id, params KeyValuePair<string, string>[] layers)
        {
            var map = new Dictionary<string, string>();
            foreach (var pair in layers) map[pair.Key] = pair.Value;
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = ContentLoader.Load(new MemoryContentSource(map, FileContentSource.Rwr()), id));
            return pack;
        }

        private static KeyValuePair<string, string> L(string id, string extends, string body) =>
            new KeyValuePair<string, string>(id, "{ \"id\": \"" + id + "\"" + (extends == null ? "" : ", \"extends\": \"" + extends + "\"") + (body.Length == 0 ? "" : ", " + body) + " }");

        private static (long interval, int magazine, long reload, long falloffStart, long falloffEnd, long effect, int sBase, int sGrowth, int sRecover, int sMax) Weapon(WeaponDef w) =>
            (w.FireIntervalMilli, w.Magazine, w.ReloadMilli, w.FalloffStart.Raw, w.FalloffEnd.Raw, w.EffectRaw, w.Spread.BaseMilli, w.Spread.GrowthMilli, w.Spread.RecoverPerSecMilli, w.Spread.MaxMilli);

        private static long Raw(int whole) => SimFix.FromInt(whole).Raw;

        // ---- inheritance (the three assertions the section split must keep) ----

        [Test]
        public void Inheritance_ChildOverridesOneFieldOfOneWeaponAndEveryOtherFieldKeepsTheParentValue()
        {
            ContentPack pack = Pack("kid", L("kid", "rwr", "\"weapons\": [ { \"id\": \"rwr.weapon-2\", \"fireIntervalMilli\": 5000 } ]"));
            WeaponDef changed = pack.BuildCombat().Weapons[1];
            Assert.That(Weapon(changed), Is.EqualTo((5000L, 30, 60000L, Raw(33), Raw(68), 36045L, 1000, 340, 1400, 3000)));
            Assert.That(pack.WeaponId(1), Is.EqualTo("rwr.weapon-2"));
        }

        [Test]
        public void Inheritance_ChildNewWeaponsComeAfterEveryParentWeaponWhateverTheirIds()
        {
            const string Fields = "\"fireIntervalMilli\": 2000, \"magazine\": 5, \"reloadMilli\": 3000, \"falloffStart\": 1, \"falloffEnd\": 2, \"effectRaw\": 7,"
                + " \"spreadBase\": 0, \"spreadGrowth\": 0, \"spreadRecoverPerSec\": 0, \"spreadMax\": 0";
            ContentPack pack = Pack("kid",
                L("mid", "rwr", "\"weapons\": [ { \"id\": \"mid.gun\", " + Fields + " } ]"),
                L("kid", "mid", "\"weapons\": [ { \"id\": \"aaa.first\", " + Fields + " } ]"));
            Assert.That(pack.WeaponCount, Is.EqualTo(5));
            Assert.That(new[] { pack.WeaponId(0), pack.WeaponId(1), pack.WeaponId(2), pack.WeaponId(3), pack.WeaponId(4) },
                Is.EqualTo(new[] { "rwr.weapon-1", "rwr.weapon-2", "rwr.weapon-3", "mid.gun", "aaa.first" }));
            Assert.That(pack.WeaponIndex("aaa.first"), Is.EqualTo(4));
            Assert.That(pack.BuildCombat().Weapons[4].EffectRaw, Is.EqualTo(7));
        }

        [Test]
        public void Inheritance_WeaponsTheChildDoesNotMentionKeepTheParentValuesInEveryField()
        {
            ContentPack pack = Pack("kid", L("kid", "rwr", "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"magazine\": 31 } ]"));
            var weapons = pack.BuildCombat().Weapons;
            Assert.That(Weapon(weapons[0]), Is.EqualTo((3390L, 31, 60000L, Raw(34), Raw(67), 32768L, 1000, 340, 1400, 3000)));
            Assert.That(Weapon(weapons[1]), Is.EqualTo((3690L, 30, 60000L, Raw(33), Raw(68), 36045L, 1000, 340, 1400, 3000)));
            Assert.That(Weapon(weapons[2]), Is.EqualTo((45000L, 10, 60000L, Raw(99), Raw(144), 65536L, 1000, 340, 1400, 3000)));
        }

        // ---- weapons ----

        [Test]
        public void Weapons_Merge_TheChildFieldWinsAndTheParentFieldsRemain_AnExplicitZeroIsAValue()
        {
            ContentPack pack = Pack("kid", L("kid", "rwr", "\"weapons\": [ { \"id\": \"rwr.weapon-3\", \"effectRaw\": 0, \"spreadMax\": 4000 } ]"));
            Assert.That(Weapon(pack.BuildCombat().Weapons[2]), Is.EqualTo((45000L, 10, 60000L, Raw(99), Raw(144), 0L, 1000, 340, 1400, 4000)));
            Assert.That(pack.WeaponCount, Is.EqualTo(3));
        }

        [Test]
        public void Weapons_Build_ConvertsDecimalsAndStringsAndKeepsTheOrder()
        {
            ContentPack pack = Pack("kid", L("kid", "rwr", "\"weapons\": [ { \"id\": \"k.w\", \"fireIntervalMilli\": 1000, \"magazine\": 1, \"reloadMilli\": 1000000000000,"
                + " \"falloffStart\": 1.5, \"falloffEnd\": \"20\", \"effectRaw\": 1099511627776, \"spreadBase\": 5, \"spreadGrowth\": 6, \"spreadRecoverPerSec\": 7, \"spreadMax\": 90000 } ]"));
            Assert.That(Weapon(pack.BuildCombat().Weapons[3]), Is.EqualTo((1000L, 1, 1000000000000L, 98304L, Raw(20), 1099511627776L, 5, 6, 7, 90000)));
            Assert.That(pack.WeaponId(3), Is.EqualTo("k.w"));
            Assert.That(pack.WeaponIndex("rwr.weapon-3"), Is.EqualTo(2));
        }

        // ---- melee ----

        private const string SwordFields = "\"windupTicks\": 6, \"activeTicks\": 2, \"recoveryTicks\": 9, \"radius\": 2, \"cosHalfAnglePermille\": 500,"
            + " \"halfHeight\": 1, \"damage\": 34, \"maxTargets\": 3, \"engageRange\": 1.8";

        private static (int windup, int active, int recovery, long radius, int cosine, long halfHeight, long damage, int targets, long engage, long lineHalfWidth) Melee(MeleeSpec m) =>
            (m.WindupTicks, m.ActiveTicks, m.RecoveryTicks, m.Radius.Raw, m.CosHalfAnglePermille, m.HalfHeight.Raw, m.Damage.Raw, m.MaxTargets, m.EngageRange.Raw, m.LineHalfWidth.Raw);

        [Test]
        public void Melee_Merge_TheChildFieldWinsAndTheParentFieldsRemain()
        {
            ContentPack pack = Pack("kid",
                L("mid", "rwr", "\"melee\": [ { \"id\": \"m\", " + SwordFields + " }, { \"id\": \"n\", " + SwordFields + " } ], \"unitTypes\": [ { \"id\": \"u\", \"weapon\": \"\", \"melee\": \"m\" } ]"),
                L("kid", "mid", "\"melee\": [ { \"id\": \"m\", \"damage\": 50, \"lineHalfWidth\": 0.5 } ]"));
            var melee = pack.BuildCombat().Melee;
            Assert.That(pack.MeleeCount, Is.EqualTo(2));
            Assert.That(Melee(melee[0]), Is.EqualTo((6, 2, 9, Raw(2), 500, Raw(1), Raw(50), 3, SimFix.Ratio(18, 10).Raw, 32768L)));
            Assert.That(Melee(melee[1]), Is.EqualTo((6, 2, 9, Raw(2), 500, Raw(1), Raw(34), 3, SimFix.Ratio(18, 10).Raw, 0L)));
            Assert.That(new[] { pack.MeleeId(0), pack.MeleeId(1) }, Is.EqualTo(new[] { "m", "n" }));
        }

        [Test]
        public void Melee_Build_ConvertsEveryFieldAndAnAbsentLineWidthIsASector()
        {
            ContentPack pack = Pack("kid", L("kid", "rwr", "\"melee\": [ { \"id\": \"s\", " + SwordFields + " }, { \"id\": \"t\", " + SwordFields + ", \"lineHalfWidth\": 0.2 } ],"
                + " \"unitTypes\": [ { \"id\": \"u\", \"weapon\": \"\", \"melee\": \"s\" } ]"));
            var melee = pack.BuildCombat().Melee;
            Assert.That(Melee(melee[0]), Is.EqualTo((6, 2, 9, Raw(2), 500, Raw(1), Raw(34), 3, SimFix.Ratio(18, 10).Raw, 0L)));
            Assert.That(melee[0].IsThrust, Is.False);
            Assert.That(Melee(melee[1]).lineHalfWidth, Is.EqualTo(SimFix.Ratio(2, 10).Raw));
            Assert.That(melee[1].IsThrust, Is.True);
            Assert.That(pack.MeleeId(1), Is.EqualTo("t"));
        }

        // ---- unit types ----

        [Test]
        public void UnitTypes_Merge_TheChildReplacesOneWeaponReferenceAndNewTypesComeLast()
        {
            ContentPack pack = Pack("kid", L("kid", "rwr", "\"unitTypes\": [ { \"id\": \"rwr.soldier-1\", \"weapon\": \"rwr.weapon-3\" }, { \"id\": \"k.new\", \"weapon\": \"rwr.weapon-2\" } ]"));
            Assert.That(pack.UnitTypeCount, Is.EqualTo(3));
            Assert.That(new[] { pack.UnitTypeId(0), pack.UnitTypeId(1), pack.UnitTypeId(2) }, Is.EqualTo(new[] { "rwr.soldier-1", "rwr.soldier-2", "k.new" }));
            Assert.That(new[] { pack.WeaponOfUnitType("rwr.soldier-1"), pack.WeaponOfUnitType("rwr.soldier-2"), pack.WeaponOfUnitType("k.new") }, Is.EqualTo(new[] { 2, 1, 1 }));
        }

        [Test]
        public void UnitTypes_Build_ResolvesWeaponAndMeleeIdsToIndicesAndAnEmptyWeaponIsMinusOne()
        {
            ContentPack pack = Pack("kid", L("kid", "rwr", "\"melee\": [ { \"id\": \"a\", " + SwordFields + " }, { \"id\": \"b\", " + SwordFields + " } ], \"unitTypes\": ["
                + " { \"id\": \"u.knife\", \"weapon\": \"\", \"melee\": \"b\" }, { \"id\": \"u.both\", \"weapon\": \"rwr.weapon-2\", \"melee\": \"a\" } ]"));
            Assert.That((pack.WeaponOfUnitType("u.knife"), pack.MeleeOfUnitType("u.knife")), Is.EqualTo((-1, 1)));
            Assert.That((pack.WeaponOfUnitType("u.both"), pack.MeleeOfUnitType("u.both")), Is.EqualTo((1, 0)));
            Assert.That((pack.WeaponOfUnitType("rwr.soldier-1"), pack.MeleeOfUnitType("rwr.soldier-1")), Is.EqualTo((0, -1)));
        }

        // ---- combat ----

        [Test]
        public void Combat_Merge_TheChildFieldsWinAndTheOthersComeFromTheParent()
        {
            ContentPack pack = Pack("kid", L("kid", "rwr", "\"combat\": { \"friendlyFire\": true, \"allyHoldMilli\": 45000 }"));
            CombatPack combat = pack.BuildCombat();
            Assert.That(combat.MaxHealthRaw, Is.EqualTo(Raw(100)));
            Assert.That(combat.Selector, Is.TypeOf<NearestVisibleEnemy>());
            Assert.That((combat.FriendlyFire, combat.FriendlyFireLocked, combat.AllyCheckMissRaw, combat.AllyHoldMilli), Is.EqualTo((true, false, 16384, 45000L)));
        }

        [Test]
        public void Combat_Build_ConvertsEveryFieldAndTheLockDecidesWhichFriendlyFireWins()
        {
            const string Rules = "\"combat\": { \"maxHealth\": 12.5, \"resolver\": \"rwr.hit\", \"selector\": \"nearest-visible\", \"friendlyFire\": true,"
                + " \"friendlyFireLocked\": true, \"allyCheckMissRate\": 0.5, \"allyHoldMilli\": 1000 }";
            ContentPack pack = Pack("solo", L("solo", null, Rules));
            CombatPack combat = pack.BuildCombat(false);
            Assert.That(combat.MaxHealthRaw, Is.EqualTo(819200));
            Assert.That(combat.Resolver, Is.TypeOf<RwrHitResolver>());
            Assert.That(combat.Selector, Is.TypeOf<NearestVisibleEnemy>());
            Assert.That((combat.FriendlyFire, combat.FriendlyFireLocked, combat.AllyCheckMissRaw, combat.AllyHoldMilli), Is.EqualTo((true, true, 32768, 1000L)));
            Assert.That(combat.Weapons.Length, Is.Zero);
            Assert.That(pack.UnitTypeCount, Is.Zero);
        }

        // ---- match ----

        private static (int num, int den, bool strict, int captureTicks, InterruptMode interrupt, int decay, int interval, int think, int max, long sight, int columns, int rows, long spacing) Match(MatchRules m) =>
            (m.CaptureAdvantageNum, m.CaptureAdvantageDen, m.CaptureStrict, m.CaptureTicks, m.Interrupt, m.DecayPerTick, m.IntervalTicks, m.ThinkInterval, m.MaxTicks, m.Sight.Raw, m.GridColumns, m.GridRows, m.GridSpacing.Raw);

        [Test]
        public void Match_Merge_TheChildFieldsWinAndTheOthersComeFromTheParent()
        {
            ContentPack pack = Pack("kid", L("kid", "rwr", "\"match\": { \"captureTicks\": 150, \"sight\": 20, \"captureStrict\": true }"));
            Assert.That(Match(pack.Match), Is.EqualTo((2, 1, true, 150, InterruptMode.Reset, 1, 90, 30, 9000, Raw(20), 4, 4, Raw(3) / 2)));
        }

        [Test]
        public void Match_Build_ConvertsEveryFieldAndTheSightLimitedSelectorTakesTheSight()
        {
            const string Rules = "\"match\": { \"captureAdvantageNum\": 3, \"captureAdvantageDen\": 2, \"captureStrict\": true, \"captureTicks\": 77, \"interrupt\": \"decay\","
                + " \"decayPerTick\": 4, \"intervalTicks\": 60, \"thinkInterval\": 15, \"maxTicks\": 1234, \"sight\": 12.5, \"gridColumns\": 8, \"gridRows\": 6, \"gridSpacing\": 2.5 }";
            ContentPack pack = Pack("kid", L("kid", "rwr-m6", Rules));
            Assert.That(Match(pack.Match), Is.EqualTo((3, 2, true, 77, InterruptMode.Decay, 4, 60, 15, 1234, 819200L, 8, 6, 163840L)));
            Assert.That(((SightLimited)pack.BuildCombat().Selector).Sight.Raw, Is.EqualTo(819200));
            Assert.That(Assert.Throws<ContentException>(() => { _ = Pack("none", L("none", null, "\"combat\": { \"maxHealth\": 1, \"resolver\": \"rwr.hit\", \"selector\": \"nearest-visible\","
                + " \"friendlyFire\": false, \"friendlyFireLocked\": false, \"allyCheckMissRate\": 0, \"allyHoldMilli\": 1000 }")).Match; }).Message, Does.Contain("has no match section"));
        }

        // ---- surfaces ----

        [Test]
        public void Surfaces_Merge_TheChildFieldWinsTheOthersRemainAndNewIdsAreAdded()
        {
            ContentPack pack = Pack("kid",
                L("mid", "rwr", "\"surfaces\": [ { \"id\": \"grass\" }, { \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 } ]"),
                L("kid", "mid", "\"surfaces\": [ { \"id\": \"mud\", \"speedMilli\": 700 }, { \"id\": \"road\", \"speedMilli\": 1250 } ]"));
            TerrainRules t = pack.Terrain;
            Assert.That(t.SurfaceCount, Is.EqualTo(3));
            Assert.That(t.TryFind("mud", out SurfaceDef mud), Is.True);
            Assert.That((mud.Passable, mud.SpeedMilli, mud.Cost), Is.EqualTo((true, 700, 3)));
            Assert.That(t.TryFind("road", out SurfaceDef road), Is.True);
            Assert.That((road.Passable, road.SpeedMilli, road.Cost), Is.EqualTo((true, 1250, 1)));
            Assert.That(t.TryFind("grass", out SurfaceDef grass), Is.True);
            Assert.That((grass.Passable, grass.SpeedMilli, grass.Cost), Is.EqualTo((true, 1000, 1)));
            Assert.That((t.MaxWalkableNum, t.MaxWalkableDen), Is.EqualTo((2, 1)));
        }

        [Test]
        public void Surfaces_Build_DefaultsExplicitValuesAndABlockedSurfaceStoredAsZero()
        {
            ContentPack pack = Pack("kid", L("kid", "rwr", "\"surfaces\": [ { \"id\": \"s\" }, { \"id\": \"w\", \"passable\": false, \"speedMilli\": 777, \"cost\": 9 },"
                + " { \"id\": \"f\", \"passable\": true, \"speedMilli\": 2000, \"cost\": 255 } ]"));
            TerrainRules t = pack.Terrain;
            Assert.That(t.PackId, Is.EqualTo("kid"));
            Assert.That(new[] { t.Surface(0).Id, t.Surface(1).Id, t.Surface(2).Id }, Is.EqualTo(new[] { "f", "s", "w" }), "kept in ordinal id order");
            Assert.That((t.Surface(0).SpeedMilli, t.Surface(0).Cost), Is.EqualTo((2000, 255)));
            Assert.That((t.Surface(1).Passable, t.Surface(1).SpeedMilli, t.Surface(1).Cost), Is.EqualTo((true, 1000, 1)));
            Assert.That((t.Surface(2).Passable, t.Surface(2).SpeedMilli, t.Surface(2).Cost), Is.EqualTo((false, 0, 0)));
            Assert.That(Pack("plain", L("plain", "rwr", "")).Terrain, Is.Null);
        }

        // ---- slopeRule ----

        [Test]
        public void SlopeRule_Merge_TheChildFieldWinsAndTheParentFieldRemains()
        {
            ContentPack pack = Pack("kid",
                L("mid", "rwr", "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }"),
                L("kid", "mid", "\"slopeRule\": { \"maxWalkableDen\": 8 }"));
            Assert.That((pack.Terrain.MaxWalkableNum, pack.Terrain.MaxWalkableDen), Is.EqualTo((3, 8)));
            Assert.That(pack.Terrain.SurfaceCount, Is.Zero);
        }

        private static (int num, int den) Slope(string id, string rule)
        {
            TerrainRules t = Pack(id, L(id, "rwr", "\"slopeRule\": " + rule)).Terrain;
            return (t.MaxWalkableNum, t.MaxWalkableDen);
        }

        [Test]
        public void SlopeRule_Build_AMissingFieldTakesItsDefault()
        {
            Assert.That(Slope("a", "{ \"maxWalkableDen\": 8 }"), Is.EqualTo((16, 8)), "numerator defaults to MaxSlopeNumerator * den");
            Assert.That(Slope("b", "{ \"maxWalkableNum\": 1 }"), Is.EqualTo((1, 1)), "denominator defaults to MaxSlopeDenominator");
            Assert.That(Slope("c", "{ }"), Is.EqualTo((2, 1)));
            Assert.That(Slope("d", "{ \"maxWalkableNum\": 0, \"maxWalkableDen\": 4096 }"), Is.EqualTo((0, 4096)), "an explicit 0 is a value");
        }
    }
}
