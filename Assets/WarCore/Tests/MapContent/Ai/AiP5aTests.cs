using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-P5a: AI squads (ai.squad*). The system battle.ai-squads keeps who follows whom: a squad is a leader and one to
    // its size in members, everybody else is a skirmisher. A member asks the dispatch and the placement as its leader.
    public sealed partial class AiP5aTests
    {
        // The system's constants, written out (Squads.ElectSalt, SizeSalt, TightSalt, OffsetSalt, SloppySalt).
        private const ulong ElectSalt = 0x8f1bbcdc6ed9eba1UL, SizeSalt = 0xca62c1d6a953fd4eUL, TightSalt = 0x2ad7d2bb4c1a9e37UL,
            OffsetSalt = 0xe07c265404be4294UL, SloppySalt = 0x76f988da831153b5UL;
        private static readonly string[] Fields =
        {
            "squadMinSize", "squadMaxSize", "squadSkirmisherPermille", "squadRecruitRadiusMm", "squadFormTicks", "squadSpreadMinMm", "squadSpreadMaxMm",
        };
        // min, max, skirmishers, radius, form ticks, spread min, spread max.
        private static readonly int[] Values = { 4, 4, 300, 30000, 15, 0, 0 };
        private const int Form = 15;
        // Keys 1..10 by Mix(stable id ^ ElectSalt), smallest first.
        private const string ElectionOrder = "8,6,3,5,4,1,7,9,2,10";

        private static string Json(int[] values, string head = null, int skip = -1)
        {
            var parts = new List<string>();
            if (head != null) parts.Add(head);
            for (int i = 0; i < Fields.Length; i++) if (i != skip) parts.Add("\"" + Fields[i] + "\": " + values[i]);
            return "{ " + string.Join(", ", parts) + " }";
        }
        private static int[] With(params int[] pairs)
        {
            int[] copy = (int[])Values.Clone();
            for (int i = 0; i < pairs.Length; i += 2) copy[pairs[i]] = pairs[i + 1];
            return copy;
        }
        private const int Min = 0, Max = 1, Skirmishers = 2, Radius = 3, FormTicks = 4, SpreadMin = 5, SpreadMax = 6;

        private static ContentPack Pack(string ai, string extends = "rwr") => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["p5a-test"] = "{ \"id\": \"p5a-test\", \"extends\": \"" + extends + "\"" + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), "p5a-test");

        private static byte[] Flat() => WcMapWriter.Write(Wp23.Make(160, 60, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null));

        private static ulong Stable(ulong key) => StableEntityKey.Placed(1, key).Id.Value;
        private static ulong Elect(ulong key) => AiParts.Mix(Stable(key) ^ ElectSalt);
        // The keys in the order the system elects leaders and breaks ties: by Mix(stable id ^ ElectSalt), smallest first.
        private static ulong[] ByElect(params ulong[] keys)
        {
            ulong[] sorted = (ulong[])keys.Clone();
            Array.Sort(sorted, (a, b) => Elect(a).CompareTo(Elect(b)));
            return sorted;
        }
        private static ulong[] Keys(int from, int count) { var keys = new ulong[count]; for (int i = 0; i < count; i++) keys[i] = (ulong)(from + i); return keys; }

        // An unarmed man who never moves (1 mm/s; nobody thinks in these fixtures unless the test says so). squad is the
        // player-squad number a squad command names; it has nothing to do with AI squads.
        private static UnitSpawn Man(ulong key, int team, int xMetres, int zMetres = 20, int squad = 0, long xMm = -1, long speed = 1, long zMm = -1) =>
            new UnitSpawn(key, 0, squad == 0 ? team : squad, xMm >= 0 ? xMm : xMetres * 1000L, zMm >= 0 ? zMm : zMetres * 1000L, speed, team, -1, false);

        // A flat 160 m x 60 m field, one point of team 2 far in a corner, nobody ever thinks (think interval 1000000), so every
        // man stands where he was put and only the squads system changes anything.
        private static M6World Field(string ai, UnitSpawn[] units, Action<M6World> setup = null, ulong player = 0, int think = 1000000, ControlPointDef[] points = null)
        {
            points = points ?? new[] { new ControlPointDef(1, SimVec3.Metres(150, 0, 50), SimFix.FromInt(6), 2) };
            var bases = new[] { new SpawnAnchor(1, SimVec3.Metres(4, 0, 4)), new SpawnAnchor(2, SimVec3.Metres(156, 0, 56)) };
            WorldRecipe recipe = M6World.RecipeOf(units, M6World.Pack(points, ticks: 1000000, think: think, bases: bases), M6World.Guns(new ZeroResolver()))
                .WithRuleSet(MatchMode.TestNeverEndsId).WithStandingOrders();
            if (ai != null) recipe = recipe.WithAiRules(Pack(ai));
            if (player != 0) recipe = recipe.WithPlayer(player);
            var world = new M6World(recipe, Flat());
            setup?.Invoke(world);
            return world;
        }
        private static void Kill(M6World w, long tick, params ulong[] keys) =>
            w.Sim.RegisterGameplay("test.death-" + tick, new M6Damage(w, tick, keys), SimSystemPhase.Queries);
        // Steps until the step of the given tick has run.
        private static void To(M6World w, long tick) { while (w.Sim.Clock.Tick < tick) w.Step(); }

        private static Squads Sq(M6World w) => w.System<Squads>();
        private static StableId Id(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key));
        // The spawn key of the leader of the squad the man is in (his own key when he leads it); 0 for a skirmisher.
        private static ulong Band(M6World w, ulong key)
        {
            ulong leader = Sq(w).SquadOf(Id(w, key));
            if (leader == 0) return 0;
            foreach (UnitSpawn unit in w.Recipe.Units) if (Stable(unit.Key) == leader) return unit.Key;
            throw new InvalidOperationException("The squad's leader is no unit of the recipe.");
        }
        private static ulong[] Bands(M6World w, params ulong[] keys) { var bands = new ulong[keys.Length]; for (int i = 0; i < keys.Length; i++) bands[i] = Band(w, keys[i]); return bands; }
        private static ulong[] Rep(ulong value, int count) { var all = new ulong[count]; for (int i = 0; i < count; i++) all[i] = value; return all; }

        // Every squad has one to its size in members, and the system's two counts agree with what SquadOf says.
        private static int Bodies(M6World w, int team)
        {
            int bodies = 0, leaders = 0, members = 0;
            var count = new Dictionary<ulong, int>();
            foreach (UnitSpawn unit in w.Recipe.Units)
            {
                ulong band = Band(w, unit.Key);
                if (band == 0) continue;
                if (unit.Team == team) bodies++;
                if (band == unit.Key) { leaders++; if (!count.ContainsKey(band)) count[band] = 0; }
                else { members++; count[band] = count.TryGetValue(band, out int n) ? n + 1 : 1; }
            }
            Assert.That(Sq(w).SquadCount, Is.EqualTo(leaders));
            Assert.That(Sq(w).MemberCount, Is.EqualTo(members));
            foreach (KeyValuePair<ulong, int> pair in count)
            {
                int size = Sq(w).SizeOf(Id(w, pair.Key));
                Assert.That(pair.Value, Is.InRange(1, size), "squad of " + pair.Key);
            }
            return bodies;
        }

        // Ten men of team 1 in reach of each other, keys by election order k0..k9: k0 at x = 20, k5..k8 next to him at 21..24,
        // k1..k4 at 30..33, k9 at 40. Target = 10 * 700 / 1000 = 7.
        private static M6World Ten(out ulong[] k, int[] values = null, Action<M6World> setup = null)
        {
            k = ByElect(Keys(1, 10));
            var units = new[]
            {
                Man(k[0], 1, 20), Man(k[5], 1, 21), Man(k[6], 1, 22), Man(k[7], 1, 23), Man(k[8], 1, 24),
                Man(k[1], 1, 30), Man(k[2], 1, 31), Man(k[3], 1, 32), Man(k[4], 1, 33), Man(k[9], 1, 40), Man(90, 2, 150, 50),
            };
            return Field(Json(values ?? Values), units, setup);
        }

        // 2a. The first by election order leads and takes his four nearest (not the next four by election order); the next
        // free man by election order leads the second squad, which gets the one place left under the target.
        [Test]
        public void TenMenMakeASquadOfFiveAndASquadOfTwo()
        {
            M6World w = Ten(out ulong[] k);
            To(w, Form - 1);
            Assert.That(Sq(w).SquadCount, Is.Zero, "nothing forms before the first form tick");
            To(w, Form);
            Assert.That(Bands(w, k[0], k[5], k[6], k[7], k[8]), Is.EqualTo(Rep(k[0], 5)));
            Assert.That(Bands(w, k[1], k[2]), Is.EqualTo(Rep(k[1], 2)));
            Assert.That(Bands(w, k[3], k[4], k[9], 90), Is.EqualTo(Rep(0, 4)));
            Assert.That(Bodies(w, 1), Is.EqualTo(7));
            Assert.That(Sq(w).SizeOf(Id(w, k[0])), Is.EqualTo(4));
            Assert.That(Sq(w).SizeOf(Id(w, k[5])), Is.Zero, "a member leads nothing");
            // The election order of keys 1..10, pinned: a change of the salt or of Mix shows here.
            Assert.That(string.Join(",", k), Is.EqualTo(ElectionOrder));
        }

        // 2a. The size of a squad is min + Mix(leader ^ SizeSalt) % (max - min + 1): with sizes 1..3 the first leader of the
        // ten takes exactly that many, and the sizes of leaders 1..10 cover 1, 2 and 3.
        [Test]
        public void ASquadsSizeComesFromItsLeader()
        {
            M6World w = Ten(out ulong[] k, With(Min, 1, Max, 3, Skirmishers, 0));
            To(w, Form);
            int size = 1 + (int)(AiParts.Mix(Stable(k[0]) ^ SizeSalt) % 3UL);
            Assert.That(Sq(w).SizeOf(Id(w, k[0])), Is.EqualTo(size));
            ulong[] nearest = { k[5], k[6], k[7], k[8] };
            for (int i = 0; i < 4; i++) Assert.That(Band(w, nearest[i]), i < size ? Is.EqualTo(k[0]) : Is.Not.EqualTo(k[0]), "neighbour " + i);
            var seen = new HashSet<int>();
            for (ulong key = 1; key <= 10; key++) seen.Add(1 + (int)(AiParts.Mix(Stable(key) ^ SizeSalt) % 3UL));
            Assert.That(seen, Is.EquivalentTo(new[] { 1, 2, 3 }));
            Bodies(w, 1);
            // With sizes 1..4 the same leader's size is below the largest.
            M6World four = Ten(out _, With(Min, 1, Max, 4, Skirmishers, 0));
            To(four, Form);
            int below = 1 + (int)(AiParts.Mix(Stable(k[0]) ^ SizeSalt) % 4UL);
            Assert.That(below, Is.LessThan(4));
            Assert.That(Sq(four).SizeOf(Id(four, k[0])), Is.EqualTo(below));
        }

        // 2b. The recruit radius is inclusive: a man exactly 30 m away is taken, one a millimetre farther is not, and then
        // nobody leads (a leader needs one member).
        [TestCase(50000, true)]
        [TestCase(50001, false)]
        public void TheRecruitRadiusIsInclusive(int xMm, bool joins)
        {
            ulong[] k = ByElect(1, 2);
            M6World w = Field(Json(With(Skirmishers, 0)), new[] { Man(k[0], 1, 20), Man(k[1], 1, 0, xMm: xMm), Man(90, 2, 150, 50) });
            To(w, Form);
            Assert.That(Bands(w, k[0], k[1]), Is.EqualTo(joins ? Rep(k[0], 2) : Rep(0, 2)));
            Assert.That(Sq(w).SquadCount, Is.EqualTo(joins ? 1 : 0));
        }

        // Two squads of five, 40 m apart, and one man between them, 20 m from both leaders (radius 25 m, no skirmishers):
        // target 11, both squads full, one place left, so he stays a skirmisher. Leaders a = k0, b = k1; he is k10.
        private static M6World TwoSquadsAndOne(out ulong[] k, Action<M6World, ulong[]> deaths, int from = 1)
        {
            ulong[] keys = k = ByElect(Keys(from, 11));
            var units = new[]
            {
                Man(k[0], 1, 20), Man(k[2], 1, 19), Man(k[3], 1, 21), Man(k[4], 1, 20, 19), Man(k[5], 1, 20, 21),
                Man(k[1], 1, 60), Man(k[6], 1, 59), Man(k[7], 1, 61), Man(k[8], 1, 60, 19), Man(k[9], 1, 60, 21),
                Man(k[10], 1, 40), Man(90, 2, 150, 50),
            };
            return Field(Json(With(Skirmishers, 0, Radius, 25000)), units, w => deaths?.Invoke(w, keys));
        }

        // 2h. A full squad recruits nobody, and one place under the target is not enough for a new squad: the man between
        // two full squads stays a skirmisher on every form tick.
        [Test]
        public void AFullSquadTakesNobodyAndOnePlaceMakesNoSquad()
        {
            M6World w = TwoSquadsAndOne(out ulong[] k, null);
            for (int form = 1; form <= 4; form++)
            {
                To(w, form * Form);
                Assert.That(Bands(w, k[0], k[2], k[3], k[4], k[5]), Is.EqualTo(Rep(k[0], 5)));
                Assert.That(Bands(w, k[1], k[6], k[7], k[8], k[9]), Is.EqualTo(Rep(k[1], 5)));
                Assert.That(Band(w, k[10]), Is.Zero);
                Assert.That(Bodies(w, 1), Is.EqualTo(10));
            }
        }

        // 2c. The squad with the larger gap recruits first; equal gaps go to the leader that comes first by election order.
        // Deaths at tick 20, the next form tick is 30. One man is free, so only one squad gets him.
        [TestCase(2, 1, 0)]
        [TestCase(1, 2, 1)]
        [TestCase(1, 1, 0)]
        [TestCase(2, 2, 0)]
        public void TheSquadShortOfMostRecruitsFirst(int lostA, int lostB, int winner)
        {
            M6World w = TwoSquadsAndOne(out ulong[] k, (x, keys) =>
            {
                var dead = new List<ulong>();
                for (int i = 0; i < lostA; i++) dead.Add(keys[2 + i]);
                for (int i = 0; i < lostB; i++) dead.Add(keys[6 + i]);
                Kill(x, 20, dead.ToArray());
            });
            To(w, 2 * Form - 1);
            Assert.That(Band(w, k[10]), Is.Zero);
            To(w, 2 * Form);
            Assert.That(Band(w, k[10]), Is.EqualTo(k[winner]));
            Assert.That(Bodies(w, 1), Is.EqualTo(11 - lostA - lostB));
        }

        // 2c. Equal gaps go by election order whichever of the two leaders has the smaller id: the first of the two by
        // election order gets the free man, once with the smaller id and once with the larger.
        [TestCase(false)]
        [TestCase(true)]
        public void EqualGapsGoByElectionOrder(bool larger)
        {
            int from = 1;
            while (Stable(ByElect(Keys(from, 11))[0]) > Stable(ByElect(Keys(from, 11))[1]) != larger) from += 100;
            M6World w = TwoSquadsAndOne(out ulong[] k, (x, keys) => Kill(x, 20, keys[2], keys[6]), from);
            Assert.That(Stable(k[0]) > Stable(k[1]), Is.EqualTo(larger));
            To(w, 2 * Form);
            Assert.That(Band(w, k[10]), Is.EqualTo(k[0]));
        }

        // 2k. A squad short of more men than there are places under the target takes only as many as there are places.
        // Ten men (target 7): squads of 5 and 2 (the second is three short). All four members of the first squad die at
        // tick 20: six on foot, target 4, two in squads, two places. The second squad takes its two nearest, k3 and k4,
        // and not a third (k0 and k9 stand 10 m from its leader).
        [Test]
        public void ASquadShortOfManyTakesOnlyThePlacesLeft()
        {
            M6World w = Ten(out ulong[] k, null, x => { ulong[] e = ByElect(Keys(1, 10)); Kill(x, 20, e[5], e[6], e[7], e[8]); });
            To(w, 2 * Form);
            Assert.That(Bands(w, k[1], k[2], k[3], k[4]), Is.EqualTo(Rep(k[1], 4)));
            Assert.That(Bands(w, k[0], k[9]), Is.EqualTo(Rep(0, 2)));
            Assert.That(Bodies(w, 1), Is.EqualTo(4));
        }

        // 2a. The target is rounded down: of three men 3 * 700 / 1000 = 2 are in squads, of two men 1, which is no squad.
        [Test]
        public void TheTargetIsRoundedDown()
        {
            ulong[] k = ByElect(Keys(1, 3));
            M6World three = Field(Json(Values), new[] { Man(k[0], 1, 20), Man(k[1], 1, 21), Man(k[2], 1, 23), Man(90, 2, 150, 50) });
            To(three, Form);
            Assert.That(Bands(three, k[0], k[1], k[2]), Is.EqualTo(new[] { k[0], k[0], 0UL }));
            M6World two = Field(Json(Values), new[] { Man(k[0], 1, 20), Man(k[1], 1, 21), Man(90, 2, 150, 50) });
            To(two, Form);
            Assert.That(Sq(two).SquadCount, Is.Zero);
        }

        // 2d. A squad whose leader died is gone on the next tick and its members are skirmishers; on the next form tick the
        // first of them by election order leads a new squad.
        [Test]
        public void ADeadLeadersSquadDissolvesAndFormsAgain()
        {
            M6World w = TwoSquadsAndOne(out ulong[] k, (x, keys) => Kill(x, 20, keys[0]));
            To(w, 19);
            Assert.That(Sq(w).TryLeader(Id(w, k[2]), out StableId leader, out SimVec3 leaderHere), Is.True);
            Assert.That(leader, Is.EqualTo(Id(w, k[0])));
            Assert.That(leaderHere, Is.EqualTo(w.Read<Position>(k[0]).Value));
            To(w, 20);
            // The death lands at the end of tick 20, after the squads step: the table still names him, but his members
            // are told at once that there is no leader to ask as.
            Assert.That(Band(w, k[2]), Is.EqualTo(k[0]));
            Assert.That(Sq(w).TryLeader(Id(w, k[2]), out _, out _), Is.False);
            To(w, 21);
            Assert.That(Bands(w, k[0], k[2], k[3], k[4], k[5]), Is.EqualTo(Rep(0, 5)));
            Assert.That(Sq(w).SquadCount, Is.EqualTo(1));
            To(w, 2 * Form);
            // Foot 10, target 10, squad b full (5), five places: k2 leads (first of the free by election order) and takes
            // min(4, 10 - 5 - 1) = 4 of the men in reach: k3, k4, k5 (1.4 m, 1.4 m, 2 m... all of cluster a) and k10 (21 m).
            Assert.That(Bands(w, k[2], k[3], k[4], k[5], k[10]), Is.EqualTo(Rep(k[2], 5)));
            Assert.That(Bodies(w, 1), Is.EqualTo(10));
        }

        // 2j. A squad whose members are all gone is no squad on the next tick: its leader is a skirmisher and the other squad,
        // short of one man too, may take him or the man in between (the nearer one: the man in between, 20 m; the old leader
        // is 40 m away, out of reach).
        [Test]
        public void ASquadWithNoMemberLeftIsNoSquad()
        {
            M6World w = TwoSquadsAndOne(out ulong[] k, (x, keys) => Kill(x, 20, keys[2], keys[3], keys[4], keys[5], keys[6]));
            To(w, 21);
            Assert.That(Band(w, k[0]), Is.Zero);
            Assert.That(Sq(w).SizeOf(Id(w, k[0])), Is.Zero);
            Assert.That(Sq(w).SquadCount, Is.EqualTo(1));
            To(w, 2 * Form);
            // Foot 6 (k0, k1, k7, k8, k9, k10), target 6, squad b has 4 bodies and a gap of 1: it takes k10. One place is left
            // and k0 stands alone: no new squad.
            Assert.That(Band(w, k[10]), Is.EqualTo(k[1]));
            Assert.That(Band(w, k[0]), Is.Zero);
            Assert.That(Bodies(w, 1), Is.EqualTo(5));
        }

        // 2j. A man with nobody in reach leads nothing, however early he comes in the election order, and the next by that
        // order forms his squad on the same tick.
        [Test]
        public void ALeaderWhoFindsNobodyLeadsNoSquad()
        {
            ulong[] k = ByElect(Keys(1, 4));
            M6World w = Field(Json(With(Skirmishers, 0, Radius, 5000)), new[] { Man(k[0], 1, 100), Man(k[1], 1, 20), Man(k[2], 1, 21), Man(k[3], 1, 22), Man(90, 2, 150, 50) });
            To(w, Form);
            Assert.That(Band(w, k[0]), Is.Zero);
            Assert.That(Sq(w).SizeOf(Id(w, k[0])), Is.Zero);
            Assert.That(Bands(w, k[1], k[2], k[3]), Is.EqualTo(Rep(k[1], 3)));
            Assert.That(Bodies(w, 1), Is.EqualTo(3));
        }

        // 2g. Between form ticks nobody is recruited: a member dies at tick 20, the free man next to the squad joins at 30.
        [Test]
        public void NobodyIsRecruitedBetweenFormTicks()
        {
            M6World w = TwoSquadsAndOne(out ulong[] k, (x, keys) => Kill(x, 20, keys[2]));
            for (long tick = Form + 1; tick < 2 * Form; tick++)
            {
                To(w, tick);
                Assert.That(Band(w, k[10]), Is.Zero, "tick " + tick);
                Assert.That(Sq(w).MemberCount, Is.EqualTo(tick <= 20 ? 8 : 7), "tick " + tick);
            }
            To(w, 2 * Form);
            Assert.That(Band(w, k[10]), Is.EqualTo(k[0]));
        }

        // 2i. Two free men at the same distance from a leader with one place: the one that comes first by election order
        // joins, whether his key is the smaller or the larger of the two.
        [TestCase(false)]
        [TestCase(true)]
        public void EqualDistancesGoByElectionOrder(bool swap)
        {
            // Keys 1..40: the leader is the first by election order; the pair are two later keys, once with the smaller key
            // first by election order and once with the larger key first.
            ulong[] k = ByElect(Keys(1, 40));
            ulong first = 0, second = 0;
            for (int i = 1; i < k.Length - 1 && first == 0; i++)
                if (k[i] < k[i + 1] != swap) { first = k[i]; second = k[i + 1]; }
            Assert.That(first, Is.Not.Zero);
            Assert.That(first < second, Is.EqualTo(!swap));
            // Min = max = 1 and no skirmishers: target 3, the leader takes min(1, 3 - 0 - 1) = 1 of the two, both 5 m away.
            M6World w = Field(Json(With(Min, 1, Max, 1, Skirmishers, 0)), new[] { Man(k[0], 1, 20), Man(first, 1, 20, 25), Man(second, 1, 20, 15), Man(90, 2, 150, 50) });
            To(w, Form);
            Assert.That(Bands(w, k[0], first, second), Is.EqualTo(new[] { k[0], k[0], 0UL }));
            M6World mirrored = Field(Json(With(Min, 1, Max, 1, Skirmishers, 0)), new[] { Man(k[0], 1, 20), Man(first, 1, 20, 15), Man(second, 1, 20, 25), Man(90, 2, 150, 50) });
            To(mirrored, Form);
            Assert.That(Bands(mirrored, k[0], first, second), Is.EqualTo(new[] { k[0], k[0], 0UL }));
        }

        // 2e. The man the player controls is in no squad and leads none, though he comes first by election order.
        [Test]
        public void ThePlayersManIsInNoSquad()
        {
            ulong[] k = ByElect(Keys(1, 4));
            M6World w = Field(Json(With(Skirmishers, 0)), new[] { Man(k[0], 1, 20), Man(k[1], 1, 21), Man(k[2], 1, 22), Man(k[3], 1, 23), Man(90, 2, 150, 50) }, player: k[0]);
            To(w, Form);
            Assert.That(Band(w, k[0]), Is.Zero);
            Assert.That(Bands(w, k[1], k[2], k[3]), Is.EqualTo(Rep(k[1], 3)));
        }

        // 2e. When the player's man dies, the player takes over the nearest team-mate (PlayerFallback, on the tick after the
        // death); on the tick after that the man taken over is out of his squad, and a leader taken over takes his squad
        // with him. The player's man stands next to the leader (x = 19) or next to the last member (x = 24).
        [TestCase(19, true)]
        [TestCase(24, false)]
        public void AManThePlayerTakesOverLeavesHisSquad(int playerX, bool leader)
        {
            ulong[] k = ByElect(Keys(1, 5));
            M6World w = Field(Json(With(Skirmishers, 0)), new[] { Man(k[0], 1, 20), Man(k[1], 1, 21), Man(k[2], 1, 22), Man(k[3], 1, 23), Man(k[4], 1, playerX), Man(90, 2, 150, 50) },
                x => Kill(x, 20, ByElect(Keys(1, 5))[4]), player: k[4]);
            To(w, 21);
            Assert.That(Bands(w, k[0], k[1], k[2], k[3], k[4]), Is.EqualTo(new[] { k[0], k[0], k[0], k[0], 0UL }), "the switch comes after the squads step of its tick");
            To(w, 22);
            if (leader) Assert.That(Bands(w, k[0], k[1], k[2], k[3]), Is.EqualTo(Rep(0, 4)));
            else Assert.That(Bands(w, k[0], k[1], k[2], k[3]), Is.EqualTo(new[] { k[0], k[0], k[0], 0UL }));
            To(w, 2 * Form);
            if (leader) Assert.That(Bands(w, k[0], k[1], k[2], k[3]), Is.EqualTo(new[] { 0UL, k[1], k[1], k[1] }));
            else Assert.That(Bands(w, k[0], k[1], k[2], k[3]), Is.EqualTo(new[] { k[0], k[0], k[0], 0UL }));
        }

        // 2e. A man under a squad command leaves his squad on the tick after the command; a leader under a command takes his
        // squad with him. Player squad 5 is one member, player squad 6 the leader.
        [TestCase(5)]
        [TestCase(6)]
        public void ACommandedManLeavesHisSquad(int commanded)
        {
            ulong[] k = ByElect(Keys(1, 4));
            M6World w = Field(Json(With(Skirmishers, 0)), new[] { Man(k[0], 1, 20, squad: 6), Man(k[1], 1, 21, squad: 5), Man(k[2], 1, 22), Man(k[3], 1, 23), Man(90, 2, 150, 50) });
            To(w, Form);
            Assert.That(Bands(w, k[0], k[1], k[2], k[3]), Is.EqualTo(Rep(k[0], 4)));
            w.Sim.Step(L4.Squad(SquadCommandKind.Hold, commanded, default));
            Assert.That(Bands(w, k[0], k[1], k[2], k[3]), Is.EqualTo(Rep(k[0], 4)), "the command is carried out after the squads step of its tick");
            w.Step();
            if (commanded == 5) Assert.That(Bands(w, k[0], k[1], k[2], k[3]), Is.EqualTo(new[] { k[0], 0UL, k[0], k[0] }));
            else Assert.That(Bands(w, k[0], k[1], k[2], k[3]), Is.EqualTo(Rep(0, 4)));
            To(w, 2 * Form);
            // The commanded man is out of the count: three on foot, target 3. Member commanded: the squad is two short of
            // its size but nobody is free. Leader commanded: k1 leads k2 and k3.
            if (commanded == 5) Assert.That(Bands(w, k[0], k[1], k[2], k[3]), Is.EqualTo(new[] { k[0], 0UL, k[0], k[0] }));
            else Assert.That(Bands(w, k[0], k[1], k[2], k[3]), Is.EqualTo(new[] { 0UL, k[1], k[1], k[1] }));
        }

        // 2f. Teams form their own squads and never recruit across teams: three teams of three, each man next to the other
        // teams' men.
        [Test]
        public void EachTeamFormsItsOwnSquads()
        {
            var units = new List<UnitSpawn>();
            var leaders = new ulong[3];
            for (int team = 1; team <= 3; team++)
            {
                ulong[] k = ByElect(Keys(team * 10, 3));
                leaders[team - 1] = k[0];
                for (int i = 0; i < 3; i++) units.Add(Man(k[i], team, 20 + i, 19 + team));
            }
            M6World w = Field(Json(With(Skirmishers, 0)), units.ToArray());
            To(w, Form);
            for (int team = 1; team <= 3; team++)
            {
                Assert.That(Bands(w, Keys(team * 10, 3)), Is.EqualTo(Rep(leaders[team - 1], 3)), "team " + team);
                Assert.That(Bodies(w, team), Is.EqualTo(3));
            }
        }

        // 2k. Squads never grow past the target: when the skirmishers die the target falls under the number in squads, and
        // a squad short of a man does not take the free man next to it.
        [Test]
        public void SquadsDoNotGrowPastTheTarget()
        {
            // Ten men (target 7): squads of 5 and 2, skirmishers k3, k4, k9. One member of the first squad and two skirmishers
            // die at tick 20: seven on foot, target 4, six in squads. k3 stays free although the first squad has a gap.
            M6World w = Ten(out ulong[] k, null, x => Kill(x, 20, ByElect(Keys(1, 10))[8], ByElect(Keys(1, 10))[4], ByElect(Keys(1, 10))[9]));
            To(w, Form);
            int before = Bodies(w, 1);
            for (long tick = Form + 1; tick <= 4 * Form; tick++)
            {
                To(w, tick);
                int bodies = Bodies(w, 1), alive = 0;
                foreach (UnitSpawn unit in w.Recipe.Units) if (unit.Team == 1 && !w.Dead(unit.Key)) alive++;
                Assert.That(bodies, Is.LessThanOrEqualTo(Math.Max(before, alive * 700 / 1000)), "tick " + tick);
                before = bodies;
            }
            Assert.That(Band(w, k[3]), Is.Zero);
            Assert.That(Bodies(w, 1), Is.EqualTo(6));
        }
    }
}
