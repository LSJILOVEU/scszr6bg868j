using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.RespawnAi2;
using static WarCore.Tests.RespawnAi2World;

namespace WarCore.Tests
{
    // RESPAWN-AI-2 acceptance A11 and A14-A16: the three match fields in a content layer, the reserves a map file start makes
    // from them, and a team that runs out. Map W and its layer are in RespawnAi2World.
    public sealed class RespawnAi2ContentTests
    {
        private const string Five = "\"reserveCount\": 5, \"reserveType\": \"w.reservist\"";
        private static ulong Digest(string match)
        {
            ulong digest = 0;
            Assert.DoesNotThrow(() => digest = MapRecipe.From(MapW(), LoadW(match)).Match.PolicyDigest);
            return digest;
        }
        // Map W's match digest with five reservists a team (tag battle.pack.reserve.v1, the count and the type id after the
        // old fields), first recorded by this card.
        private const ulong FiveDigest = 5996648017062046579UL;
        private static ContentException Refused(string match) => Assert.Throws<ContentException>(() => LoadW(match));

        // A11: reentryTicks is 0 to 36000; 0 is the field left out; the number is in the digest.
        [TestCase("-1")]
        [TestCase("36001")]
        [TestCase("1.5")]
        [TestCase("\"60\"")]
        public void A11_ReentryTicksOutOfRangeNamesTheField(string value)
        {
            ContentException e = Refused("\"reentryTicks\": " + value);
            Assert.That(e.Message, Does.Contain("field 'reentryTicks'").And.Contain(PackId));
        }

        [Test]
        public void A11_ReentryTicksInTheDigest()
        {
            Assert.That(Digest("\"reentryTicks\": 0"), Is.EqualTo(RespawnAi2LockTests.ContentDigest), "0 is the field left out");
            Assert.That((LoadW("\"reentryTicks\": 1").Match.ReentryTicks, LoadW("\"reentryTicks\": 36000").Match.ReentryTicks, LoadW().Match.ReentryTicks), Is.EqualTo((1, 36000, 0)));
            ulong sixty = Digest("\"reentryTicks\": 60");
            Assert.That(sixty, Is.Not.EqualTo(RespawnAi2LockTests.ContentDigest));
            Assert.That(Digest("\"reentryTicks\": 61"), Is.Not.EqualTo(sixty));
            MatchPack match = MapRecipe.From(MapW(), LoadW("\"reentryTicks\": 60")).Match;
            Assert.That((match.ReentryTicks, match.ReserveCount, match.ReserveType), Is.EqualTo((60, 0, (string)null)));
        }

        // A16: reserveCount is 0 to 1024; a positive count needs a type the layer has.
        [TestCase("\"reserveCount\": -1, \"reserveType\": \"w.reservist\"", "field 'reserveCount'")]
        [TestCase("\"reserveCount\": 1025, \"reserveType\": \"w.reservist\"", "field 'reserveCount'")]
        [TestCase("\"reserveCount\": 1", "field 'reserveType'")]
        [TestCase("\"reserveCount\": 1, \"reserveType\": \"w.nobody\"", "field 'reserveType'")]
        [TestCase("\"reserveCount\": 1, \"reserveType\": 7", "field 'reserveType'")]
        public void A16_ReserveFieldsRefusedNameTheField(string match, string field)
        {
            ContentException e = Refused(match);
            Assert.That(e.Message, Does.Contain(field).And.Contain(PackId));
            if (match.Contains("w.nobody")) Assert.That(e.Message, Does.Contain("w.nobody"));
        }

        // A16: a count of 0 is the fields left out, whatever the type says; the count and the type are in the digest.
        [TestCase("\"reserveCount\": 0")]
        [TestCase("\"reserveCount\": 0, \"reserveType\": \"w.reservist\"")]
        [TestCase("\"reserveCount\": 0, \"reserveType\": \"w.nobody\"")]
        [TestCase("\"reserveCount\": 0, \"reserveType\": 7")]
        [TestCase("\"reserveType\": \"w.nobody\"")]
        public void A16_ZeroIsLeftOut(string match)
        {
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = LoadW(match));
            Assert.That((pack.Match.ReserveCount, pack.Match.ReserveType), Is.EqualTo((0, (string)null)));
            WorldRecipe recipe = null;
            Assert.DoesNotThrow(() => recipe = MapRecipe.From(MapW(), pack));
            Assert.That((recipe.Match.PolicyDigest, recipe.Units.Length, recipe.Match.ReserveCount, recipe.Match.ReserveType),
                Is.EqualTo((RespawnAi2LockTests.ContentDigest, 6, 0, (string)null)));
        }

        [Test]
        public void A16_CountAndTypeInTheDigest()
        {
            ulong five = Digest(Five);
            Assert.That(five, Is.Not.EqualTo(RespawnAi2LockTests.ContentDigest));
            Assert.That(five, Is.EqualTo(FiveDigest));
            Assert.That(Digest("\"reserveCount\": 4, \"reserveType\": \"w.reservist\""), Is.Not.EqualTo(five), "the count");
            Assert.That(Digest("\"reserveCount\": 5, \"reserveType\": \"w.line\""), Is.Not.EqualTo(five), "the type");
            Assert.That((LoadW("\"reserveCount\": 1, \"reserveType\": \"w.line\"").Match.ReserveCount, LoadW("\"reserveCount\": 1024, \"reserveType\": \"w.line\"").Match.ReserveCount),
                Is.EqualTo((1, 1024)));
            // The digest says what the layer wrote: a caller of BuildMatch that makes no units carries the two all the same.
            MatchPack direct = LoadW(Five).BuildMatch(new[] { RespawnAi2World.P() }, Bases());
            Assert.That((direct.ReserveCount, direct.ReserveType), Is.EqualTo((5, "w.reservist")));
            Assert.That(direct.PolicyDigest, Is.Not.EqualTo(LoadW().BuildMatch(new[] { RespawnAi2World.P() }, Bases()).PolicyDigest));
        }

        // The pack's own bounds (a pack built without a content layer).
        private static MatchPack Direct(int count, string type) =>
            new MatchPack(new[] { RespawnAi2World.P() }, Bases(), 2, 1, false, 8000, InterruptMode.Reset, 1, Interval, 1000000, 100000,
                SimFix.FromInt(35), 2, 2, SimFix.FromInt(2), reserveCount: count, reserveType: type);

        [Test]
        public void A16_ThePackBoundsTheReserve()
        {
            Assert.That(MatchPack.MaxReserveCount, Is.EqualTo(1024));
            Assert.Throws<ArgumentOutOfRangeException>(() => Direct(-1, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Direct(1025, "x"));
            Assert.Throws<ArgumentException>(() => Direct(1, null));
            Assert.Throws<ArgumentException>(() => Direct(1, ""));
            Assert.That((Direct(1, "x").ReserveCount, Direct(1024, "x").ReserveType), Is.EqualTo((1, "x")));
            Assert.That((Direct(0, "x").ReserveType, Direct(0, "x").PolicyDigest), Is.EqualTo(((string)null, Direct(0, null).PolicyDigest)), "no count: the type is dropped");
            Assert.That(Direct(1, "x").PolicyDigest, Is.Not.EqualTo(Direct(1, "y").PolicyDigest));
            Assert.That(Direct(1, "x").PolicyDigest, Is.Not.EqualTo(Direct(2, "x").PolicyDigest));
        }

        // A16: only the count, no reentryTicks: the timed reinforcement deploys the made reserves by the clock.
        [Test]
        public void A16_WithoutReentryTheClockDeploysThem()
        {
            M6World w = WorldW(MapW(), LoadW(Five));
            Assert.That((Fielded(w, 1), Reserves(w, 1), w.Reinforce.ByDebt), Is.EqualTo((3, 5, false)));
            Assert.DoesNotThrow(() => w.Step(Interval - 1));
            Assert.That((w.Reinforce.Successes(1), Fielded(w, 1)), Is.EqualTo((0L, 3)));
            Assert.DoesNotThrow(() => w.Step());
            Assert.That((w.Reinforce.Successes(1), w.Reinforce.Successes(2), Fielded(w, 1), Reserves(w, 1), Fielded(w, 2), Reserves(w, 2)), Is.EqualTo((1L, 1L, 4, 4, 4, 4)));
            Assert.That(KeyOf(w, w.Reinforce.Deployed[0].Unit), Is.InRange(HighestKey + 1, HighestKey + 5), "one of the made reserves");
        }

        // A14: five more reserves for each team with a base, after the map's units: team 1 keys M + 1..M + 5, team 2
        // M + 6..M + 10, squad = team, no place. The legacy spawn carries the type's weapon and melee.
        private static void AssertMade(WorldRecipe recipe, int from, int team, ulong firstKey, bool typed)
        {
            for (int i = 0; i < 5; i++)
            {
                UnitSpawn u = recipe.Units[from + i];
                Assert.That((u.Key, u.Team, u.Squad, u.Reserve, u.XMm, u.ZMm, u.Layer), Is.EqualTo((firstKey + (ulong)i, team, team, true, 0L, 0L, 0)), "unit " + (from + i));
                Assert.That((u.Weapon, u.Melee), Is.EqualTo((1, 0)), "the reservist's weapon and melee");
                if (typed) Assert.That((u.Archetype, u.SpeedMmPerSec, u.Block, u.DashDistanceMm, u.DashTicks, u.DashCooldownTicks), Is.EqualTo((7, 4200L, 0, 3000L, 6, 60)));
                else Assert.That((u.Archetype, u.SpeedMmPerSec, u.Block, u.HasDash), Is.EqualTo((0, 6000L, -1, false)));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void A14_TheLayerMakesReserves(bool typed)
        {
            MapBakeResult map = MapW();
            ContentPack pack = LoadW(Five);
            WorldRecipe recipe = null, old = MapRecipe.From(map, LoadW(), typed);
            Assert.DoesNotThrow(() => recipe = MapRecipe.From(map, pack, typed));
            Assert.That(recipe.Units.Length, Is.EqualTo(16));
            for (int i = 0; i < 6; i++)
            {
                UnitSpawn u = recipe.Units[i], o = old.Units[i];
                Assert.That((u.Key, u.Team, u.Squad, u.Reserve, u.XMm, u.ZMm, u.Weapon, u.Melee, u.Archetype, u.SpeedMmPerSec, u.Block, u.HasDash),
                    Is.EqualTo((o.Key, o.Team, o.Squad, o.Reserve, o.XMm, o.ZMm, o.Weapon, o.Melee, o.Archetype, o.SpeedMmPerSec, o.Block, o.HasDash)), "the map's unit " + i);
                Assert.That((u.Key, u.Weapon, u.Melee, u.Reserve), Is.EqualTo(((ulong)i + 1, 0, -1, false)), "a line soldier: another weapon, no melee");
            }
            AssertMade(recipe, 6, 1, HighestKey + 1, typed);
            AssertMade(recipe, 11, 2, HighestKey + 6, typed);

            M6World w = null, again = null;
            Assert.DoesNotThrow(() => { w = new M6World(recipe, map.WcMapBytes); again = new M6World(MapRecipe.From(MapW(), LoadW(Five), typed), map.WcMapBytes); });
            Assert.That((Fielded(w, 1), Reserves(w, 1), Fielded(w, 2), Reserves(w, 2)), Is.EqualTo((3, 5, 3, 5)), "eight a team: three on the field, five waiting");
            for (ulong key = HighestKey + 1; key <= HighestKey + 10; key++)
                Assert.That((w.Has<Position>(key), w.Dead(key), w.Combat.Unit(w.Unit(key)).Team), Is.EqualTo((false, false, key <= HighestKey + 5 ? 1 : 2)), "made unit " + key);
            if (typed) Assert.That(w.Combat.CanBlock(w.Unit(HighestKey + 1)), Is.True);
            Assert.That(again.Hash, Is.EqualTo(w.Hash), "tick 0");
            Assert.DoesNotThrow(() => { w.Step(100); again.Step(100); });
            Assert.That(again.Hash, Is.EqualTo(w.Hash), "tick 100");
        }

        // A14: a map without control points has no match rules, where a reserve would stand on the field at the start: none is made.
        [Test]
        public void A14_NoPointsNoReserves()
        {
            MapBakeResult map = MapW(points: "");
            WorldRecipe recipe = MapRecipe.From(map, LoadW(Five));
            Assert.That((recipe.Units.Length, recipe.Match), Is.EqualTo((6, (MatchPack)null)));
            M6World w = null;
            Assert.DoesNotThrow(() => w = new M6World(recipe, map.WcMapBytes));
            Assert.That((Fielded(w, 1), Fielded(w, 2)), Is.EqualTo((3, 3)));
        }

        // A14: a team without a base gets none (its reserves could never deploy), though it has soldiers. Such a map does
        // not pass the editor's check; the bake takes it.
        [Test]
        public void A14_OnlyTeamsWithABaseGetReserves()
        {
            WorldRecipe recipe = MapRecipe.From(MapW(bases: Base1W), LoadW(Five));
            Assert.That(recipe.Units.Length, Is.EqualTo(11));
            AssertMade(recipe, 6, 1, HighestKey + 1, false);
            WorldRecipe second = MapRecipe.From(MapW(bases: Base2W), LoadW(Five));
            Assert.That(second.Units.Length, Is.EqualTo(11));
            AssertMade(second, 6, 2, HighestKey + 1, false);
        }

        // A14: a team with a base and no soldier in the map gets its five all the same.
        [Test]
        public void A14_ABaseWithoutSoldiersStillGetsReserves()
        {
            WorldRecipe recipe = MapRecipe.From(MapW(soldiers: SoldiersW(teamOne: false)), LoadW(Five));
            Assert.That(recipe.Units.Length, Is.EqualTo(13));
            AssertMade(recipe, 3, 1, HighestKey + 1, false);
            AssertMade(recipe, 8, 2, HighestKey + 6, false);
        }

        // A14: the made keys go on from the map's highest key, wherever in the list that unit is; when the keys run out the
        // start is refused with the field's name.
        [Test]
        public void A14_KeysGoOnFromTheHighest()
        {
            string high = E1bSamples.Unit(500, "w.line", 1, 1, 10500, 6500, false) + ", " + SoldiersW();
            WorldRecipe recipe = MapRecipe.From(MapW(soldiers: high, nextUnit: 501), LoadW(Five));
            Assert.That(recipe.Units.Length, Is.EqualTo(17));
            AssertMade(recipe, 7, 1, 501, false);
            AssertMade(recipe, 12, 2, 506, false);
        }

        [TestCase(10UL, true)]
        [TestCase(9UL, false)]
        public void A14_KeysThatRunOutAreRefused(ulong room, bool fits)
        {
            string last = E1bSamples.Unit(ulong.MaxValue - room, "w.line", 1, 1, 10500, 6500, false) + ", " + SoldiersW();
            MapBakeResult map = MapW(soldiers: last, nextUnit: ulong.MaxValue - room + 1);
            if (fits)
            {
                WorldRecipe recipe = null;
                Assert.DoesNotThrow(() => recipe = MapRecipe.From(map, LoadW(Five)));
                Assert.That((recipe.Units[7].Key, recipe.Units[16].Key), Is.EqualTo((ulong.MaxValue - 9, ulong.MaxValue)));
            }
            else
            {
                ContentException e = Assert.Throws<ContentException>(() => MapRecipe.From(map, LoadW(Five)));
                Assert.That(e.Message, Does.Contain("field 'reserveCount'").And.Contain(PackId));
            }
        }

        // A15: map W with reentryTicks 60, five made reserves a team and the one rule "exhausted". Team 1 loses a man every
        // 100 ticks. The first five are replaced 60 ticks later; from the sixth on there is no reserve: nothing is counted and
        // nothing throws. When the eighth dies (tick 710) team 1 has nobody alive: the match ends in the next step, Exhausted, team 2 wins.
        private const string Exhausted = "\"rules\": [ { \"id\": \"exhausted\" } ]";

        [Test]
        public void A15_ReservesRunOut_AndTheTeamLosesWhenAllAreDead()
        {
            M6World w = WorldW(MapW(), LoadW("\"reentryTicks\": 60, " + Five, Exhausted));
            int[] start = AtStart(w);
            KillFielded(w, 1, 10, 110, 210, 310, 410, 510, 610, 710);
            for (int death = 1; death <= 5; death++)
            {
                long died = 100 * death - 90;
                GoTo(w, died + Reentry - 1, start);
                Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1), DeadOf(w, 1)), Is.EqualTo(((death - 1L, death - 1L, 0L, death - 1L), 2, 6 - death, death)), "before the entry of death " + death);
                Go(w, start);
                Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1)), Is.EqualTo(((death, death, 0L, death), 3, 5 - death)), "death " + death + " is replaced");
                Assert.That((w.Sim.IsFinished, w.Match.EndReason, w.Match.Winner), Is.EqualTo((false, MatchEndReason.None, 0)));
            }
            GoTo(w, 709, start);
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1), DeadOf(w, 1)), Is.EqualTo(((7L, 5L, 2L, 5L), 1, 0, 7)), "no reserve is left: owed two, counts as they were");
            Assert.That((w.Sim.IsFinished, w.Match.EndReason, w.Match.Winner), Is.EqualTo((false, MatchEndReason.None, 0)), "one man is alive");
            Go(w, start);
            Assert.That((DeadOf(w, 1), Fielded(w, 1), Reserves(w, 1), RespawnWorld.Entered(w, LastDead(w, 1))), Is.EqualTo((8, 0, 0, 710L)), "the eighth died at 710");
            // The match system judges before a tick's damage lands (as for every death), so it sees the last death in the next step.
            Assert.That((w.Sim.IsFinished, w.Match.EndReason), Is.EqualTo((false, MatchEndReason.None)));
            Go(w, start);
            Assert.That((w.Sim.IsFinished, w.Match.EndReason, w.Match.Winner, w.Match.EndTick), Is.EqualTo((true, MatchEndReason.Exhausted, 2, 711L)));
            Assert.That(Counts(w, 2), Is.EqualTo((0L, 0L, 0L, 0L)), "team 2 lost nobody and used no reserve");
        }

        // The unit of a team whose death was entered last.
        private static ulong LastDead(M6World w, int team)
        {
            ulong last = 0; long tick = -1;
            foreach (UnitSpawn spawn in w.Recipe.Units)
                if (spawn.Team == team && w.Dead(spawn.Key) && RespawnWorld.Entered(w, spawn.Key) > tick) { last = spawn.Key; tick = RespawnWorld.Entered(w, spawn.Key); }
            return last;
        }

        // A15: nobody on the field and one reserve left: the match goes on and the reserve enters on its tick.
        [Test]
        public void A15_AReserveLeftKeepsTheTeamAlive()
        {
            M6World w = WorldW(MapW(), LoadW("\"reentryTicks\": 60, \"reserveCount\": 1, \"reserveType\": \"w.reservist\"", Exhausted));
            Kills(w, (10, 1), (10, 2), (10, 3));
            GoTo(w, 69);
            Assert.That((Fielded(w, 1), Reserves(w, 1), w.Sim.IsFinished, w.Match.EndReason), Is.EqualTo((0, 1, false, MatchEndReason.None)), "nobody on the field, one waiting");
            Go(w);
            Assert.That((Counts(w, 1), Fielded(w, 1), Reserves(w, 1), w.Sim.IsFinished), Is.EqualTo(((3L, 1L, 2L, 1L), 1, 0, false)), "tick 70: it enters");
            GoTo(w, 200);
            Assert.That((Counts(w, 1), Fielded(w, 1), w.Sim.IsFinished, w.Match.EndReason), Is.EqualTo(((3L, 1L, 2L, 1L), 1, false, MatchEndReason.None)));
        }
    }
}
