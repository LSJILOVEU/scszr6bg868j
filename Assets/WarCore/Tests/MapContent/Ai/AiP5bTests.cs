using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-P5b: squad tempers (ai.squadRash*, squadSteady*, squadShaken*) and regrouping (ai.squadRegroup*). A squad's mood
    // comes from its leader's key and from how many of its members are left; a squad that has lost too many goes back to a
    // point of its own until it is strong again. Both are off unless their fields are written.
    public sealed partial class AiP5bTests
    {
        // The system's constant, written out (Squads.TemperSalt).
        private const ulong TemperSalt = 0xe6222af5c6534b75UL, ElectSalt = 0x8f1bbcdc6ed9eba1UL;
        private static readonly string[] Fields =
        {
            "squadMinSize", "squadMaxSize", "squadSkirmisherPermille", "squadRecruitRadiusMm", "squadFormTicks", "squadSpreadMinMm", "squadSpreadMaxMm",
        };
        private static readonly string[] TemperFields = { "squadRashPermille", "squadSteadyPermille", "squadShakenPermille", "squadRashMinRangeMm" };
        private static readonly string[] RegroupFields = { "squadRegroupBelowPermille", "squadRegroupUntilPermille", "squadRegroupMaxTicks" };
        // min, max, skirmishers, radius, form ticks, spread min, spread max: squads of up to 9 members, nobody skirmishes.
        private static readonly int[] Seven = { 1, 9, 0, 10000, 15, 0, 0 };
        private const int Form = 15;

        private static string Group(string[] names, int[] values)
        {
            if (values == null) return null;
            var parts = new List<string>();
            for (int i = 0; i < names.Length; i++) parts.Add("\"" + names[i] + "\": " + values[i]);
            return string.Join(", ", parts);
        }
        private static string Join(params string[] parts)
        {
            var kept = new List<string>();
            foreach (string part in parts) if (!string.IsNullOrEmpty(part)) kept.Add(part);
            return string.Join(", ", kept);
        }
        private static string Json(int[] seven, int[] temper = null, int[] regroup = null, string more = null) =>
            "{ " + Join(more, Group(Fields, seven), Group(TemperFields, temper), Group(RegroupFields, regroup)) + " }";
        private static int[] Rules(params int[] values) => values;

        private static ContentPack Pack(string ai, string extends = "rwr") => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["p5b-test"] = "{ \"id\": \"p5b-test\", \"extends\": \"" + extends + "\"" + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), "p5b-test");

        private static byte[] Flat() => WcMapWriter.Write(Wp23.Make(160, 60, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null));
        private static ulong Stable(ulong key) => StableEntityKey.Placed(1, key).Id.Value;
        private static ulong Elect(ulong key) => AiParts.Mix(Stable(key) ^ ElectSalt);
        // The keys in election order (Mix(stable id ^ ElectSalt), smallest first): the first of a group leads.
        private static ulong[] ByElect(params ulong[] keys)
        {
            ulong[] sorted = (ulong[])keys.Clone();
            Array.Sort(sorted, (a, b) => Elect(a).CompareTo(Elect(b)));
            return sorted;
        }
        // The temper number of a squad led by the man with this spawn key: Mix(stable id ^ TemperSalt) % 1000.
        private static int T(ulong key) => (int)(AiParts.Mix(Stable(key) ^ TemperSalt) % 1000UL);

        // An unarmed man of team 1 or 2 who all but stands (1 mm/s) unless given a speed.
        private static UnitSpawn Man(ulong key, int team, long xMm, long zMm = 20000, long speed = 1) =>
            new UnitSpawn(key, 0, team, xMm, zMm, speed, team, -1, false);

        private static readonly ControlPointDef[] FarPoint = { new ControlPointDef(1, SimVec3.Metres(150, 0, 50), SimFix.FromInt(6), 2) };
        // A flat 160 m x 60 m field. Nobody thinks unless the test gives a think interval, so only the squads system acts.
        private static M6World Field(string ai, UnitSpawn[] units, Action<M6World> setup = null, int think = 1000000, ControlPointDef[] points = null)
        {
            var bases = new[] { new SpawnAnchor(1, SimVec3.Metres(4, 0, 4)), new SpawnAnchor(2, SimVec3.Metres(156, 0, 56)) };
            WorldRecipe recipe = M6World.RecipeOf(units, M6World.Pack(points ?? FarPoint, ticks: 1000000, think: think, bases: bases), M6World.Guns(new ZeroResolver()))
                .WithRuleSet(MatchMode.TestNeverEndsId).WithStandingOrders();
            if (ai != null) recipe = recipe.WithPack(Pack(ai));
            var world = new M6World(recipe, Flat());
            setup?.Invoke(world);
            return world;
        }
        private static void Kill(M6World w, long tick, params ulong[] keys) =>
            w.Sim.RegisterGameplay("test.death-" + tick + "-" + keys[0], new M6Damage(w, tick, keys), SimSystemPhase.Queries);
        // Men who fall on ticks of their own (one system, since two of M6Damage would share an order).
        private sealed class Falls : IRetainedGameplaySystem
        {
            private readonly M6World world;
            private readonly (long tick, ulong key)[] falls;
            internal Falls(M6World world, (long tick, ulong key)[] falls) { this.world = world; this.falls = falls; }
            public int Order => 25;
            public string StateTypeId => "test.p5b-falls";
            public int StateVersion => 1;
            public void Step(SimContext c)
            {
                foreach ((long tick, ulong key) in falls)
                {
                    if (c.Clock.Tick != tick) continue;
                    StableId id = world.Sim.Entities.StableOf(world.Unit(key));
                    c.Damage(world.Sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)tick, 0, id, (ulong)tick, 0), 1, world.Recipe.Combat.MaxHealthRaw);
                }
            }
            public void SaveState(SimStateWriter w) { foreach ((long tick, ulong key) in falls) { w.Int64(tick); w.UInt64(key); } }
            public void LoadState(SimStateReader r) { foreach ((long tick, ulong key) in falls) SimStateReader.Require(r.Int64() == tick && r.UInt64() == key, "falls"); }
            public void WriteState(StateHash h) { foreach ((long tick, ulong key) in falls) { h.Add(tick); h.Add(key); } }
        }
        private static void Kills(M6World w, params (long tick, ulong key)[] falls) =>
            w.Sim.RegisterGameplay("test.p5b-falls", new Falls(w, falls), SimSystemPhase.Queries);
        // Steps until the step of the given tick has run.
        private static void To(M6World w, long tick) { while (w.Sim.Clock.Tick < tick) w.Step(); }

        private static Squads Sq(M6World w) => w.System<Squads>();
        private static StableId Id(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key));
        private static SimVec3 At(M6World w, ulong key) => w.Read<Position>(key).Value;
        private static SquadMood Mood(M6World w, ulong key) => Sq(w).MoodOf(Id(w, key));
        private static SquadRally Rally(M6World w, ulong key) => Sq(w).RegroupOf(Id(w, key));
        private static int Strength(M6World w, ulong key) => Sq(w).StrengthPermille(Id(w, key));
        // The spawn key of the leader of the man's squad (his own when he leads it); 0 for a skirmisher.
        private static ulong Band(M6World w, ulong key)
        {
            ulong leader = Sq(w).SquadOf(Id(w, key));
            if (leader == 0) return 0;
            foreach (UnitSpawn unit in w.Recipe.Units) if (Stable(unit.Key) == leader) return unit.Key;
            throw new InvalidOperationException("The squad's leader is no unit of the recipe.");
        }
        private static int Members(M6World w, ulong leader)
        {
            int members = 0;
            foreach (UnitSpawn unit in w.Recipe.Units) if (unit.Key != leader && Band(w, unit.Key) == leader) members++;
            return members;
        }

        // The saved state written out by hand: the seven numbers, the three regroup numbers when the battle regroups,
        // then leader -> size (and its mark), then member -> leader, both in key order.
        private static byte[] Payload(int[] seven, int[] regroup, (ulong leader, int size, long mark)[] squads, (ulong member, ulong leader)[] members)
        {
            var writer = new SimStateWriter();
            foreach (int value in seven) writer.Int32(value);
            if (regroup != null) foreach (int value in regroup) writer.Int32(value);
            writer.Int32(squads.Length);
            foreach ((ulong leader, int size, long mark) in squads)
            {
                writer.UInt64(leader); writer.Int32(size);
                if (regroup != null) writer.Int64(mark);
            }
            writer.Int32(members.Length);
            foreach ((ulong member, ulong leader) in members) { writer.UInt64(member); writer.UInt64(leader); }
            return writer.ToArray();
        }

        // One squad as the tests write it: the leader's spawn key, its size, its mark, its members' spawn keys.
        private sealed class Squad
        {
            internal ulong Leader; internal int Size; internal long Mark; internal ulong[] Members;
            internal Squad(ulong leader, int size, long mark, params ulong[] members) { Leader = leader; Size = size; Mark = mark; Members = members; }
        }
        private static byte[] Payload(int[] seven, int[] regroup, params Squad[] squads)
        {
            var leaders = new List<(ulong, int, long)>(); var members = new List<(ulong, ulong)>();
            foreach (Squad squad in squads)
            {
                leaders.Add((Stable(squad.Leader), squad.Size, squad.Mark));
                foreach (ulong member in squad.Members) members.Add((Stable(member), Stable(squad.Leader)));
            }
            leaders.Sort((a, b) => a.Item1.CompareTo(b.Item1)); members.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return Payload(seven, regroup, leaders.ToArray(), members.ToArray());
        }
        // Puts the squads into the world as a restore would, without a step.
        private static void Tables(M6World w, int[] seven, int[] regroup, params Squad[] squads) =>
            Sq(w).LoadState(new SimStateReader(Payload(seven, regroup, squads)));

        // Twenty men of team 1 in a row (keys 1..20), 1 m apart, and one of team 2 far away.
        private static UnitSpawn[] Twenty()
        {
            var units = new UnitSpawn[21];
            for (int i = 0; i < 20; i++) units[i] = Man((ulong)i + 1, 1, 20000 + i * 1000);
            units[20] = Man(90, 2, 150000, 50000);
            return units;
        }
        // The twenty with tempers, and one squad written into the tables: the leader, its size and its members.
        private static M6World Tempered(int rash, int steady, int shaken, ulong leader, int size, params ulong[] members)
        {
            M6World w = Field(Json(Seven, Rules(rash, steady, shaken, 8000)), Twenty());
            Tables(w, Seven, null, new Squad(leader, size, -1, members));
            return w;
        }
        // The first of the keys 1..10 whose temper number is at most the limit.
        private static ulong Mild(int limit)
        {
            for (ulong key = 1; key <= 10; key++) if (T(key) <= limit) return key;
            throw new InvalidOperationException("No key with a temper number that low.");
        }

        // 2. The temper numbers of the keys 1..10, pinned: another salt or another formula gives other numbers. Under
        // rash 300 and steady 250 a whole squad is rash below 300, steady from 750, normal between.
        [Test]
        public void TheTemperNumbersOfTenKeysArePinned()
        {
            var numbers = new List<string>(); var moods = new List<string>();
            for (ulong key = 1; key <= 10; key++)
            {
                numbers.Add(T(key).ToString());
                M6World w = Tempered(300, 250, 600, key, 1, key + 10);
                Assert.That(Mood(w, key + 10), Is.EqualTo(Mood(w, key)), "a member has its squad's mood");
                moods.Add(Mood(w, key).ToString().Substring(0, 1));
            }
            Assert.That(string.Join(",", numbers), Is.EqualTo(TenNumbers));
            Assert.That(string.Join("", moods), Is.EqualTo(TenMoods));
            for (int i = 0; i < 10; i++)
            {
                int t = int.Parse(TenNumbers.Split(',')[i]);
                Assert.That(TenMoods[i], Is.EqualTo(t < 300 ? 'R' : t >= 750 ? 'S' : 'N'), "key " + (i + 1));
            }
            Assert.That(TenMoods, Does.Contain("R").And.Contain("N").And.Contain("S"), "the ten show all three moods");
        }
        private const string TenNumbers = "625,800,457,404,494,147,278,212,850,431", TenMoods = "NSNNNRRRSN";

        // 2. The two lines, on a whole squad (no shift): rash while the temper number is below the rash line, steady from
        // 1000 minus the steady share on.
        [Test]
        public void ASquadIsRashBelowTheRashLineAndSteadyFromTheSteadyLine()
        {
            for (ulong key = 1; key <= 10; key++)
            {
                int t = T(key);
                Assert.That(Mood(Tempered(t + 1, 0, 0, key, 1, key + 10), key), Is.EqualTo(SquadMood.Rash), "t = rash - 1, key " + key);
                Assert.That(Mood(Tempered(t, 0, 0, key, 1, key + 10), key), Is.EqualTo(SquadMood.Normal), "t = rash, key " + key);
                Assert.That(Mood(Tempered(0, 999 - t, 0, key, 1, key + 10), key), Is.EqualTo(SquadMood.Normal), "t = 1000 - steady - 1, key " + key);
                Assert.That(Mood(Tempered(0, 1000 - t, 0, key, 1, key + 10), key), Is.EqualTo(SquadMood.Steady), "t = 1000 - steady, key " + key);
            }
        }

        // 2. Strength is (1 + members) * 1000 / (1 + size): a squad of size 4 with 4, 3, 2, 1 members has 1000, 800, 600,
        // 400. With shaken 500 its temper number moves up by half of what it lacks: 0, 100, 200, 300. The move is found
        // exactly by putting each line right at and right above the moved number.
        [TestCase(4, 1000, 0)]
        [TestCase(3, 800, 100)]
        [TestCase(2, 600, 200)]
        [TestCase(1, 400, 300)]
        public void ASquadShortOfMembersMovesTowardSteady(int members, int strength, int shift)
        {
            ulong leader = Mild(350);
            int t = T(leader);
            var others = new ulong[members];
            for (int i = 0; i < members; i++) others[i] = 11 + (ulong)i;
            M6World w = Tempered(t + shift + 1, 0, 500, leader, 4, others);
            Assert.That(Strength(w, leader), Is.EqualTo(strength));
            Assert.That(Strength(w, others[0]), Is.EqualTo(strength), "a member's strength is its squad's");
            Assert.That(Mood(w, leader), Is.EqualTo(SquadMood.Rash), "the rash line one above the moved number");
            Assert.That(Mood(Tempered(t + shift, 0, 500, leader, 4, others), leader), Is.EqualTo(SquadMood.Normal), "the rash line at the moved number");
            Assert.That(Mood(Tempered(0, 1000 - t - shift, 500, leader, 4, others), leader), Is.EqualTo(SquadMood.Steady), "the steady line at the moved number");
            Assert.That(Mood(Tempered(0, 999 - t - shift, 500, leader, 4, others), leader), Is.EqualTo(SquadMood.Normal), "the steady line one above it");
            // Without the shaken share nothing moves, with all of it the whole lack does.
            Assert.That(Mood(Tempered(t + 1, 0, 0, leader, 4, others), leader), Is.EqualTo(SquadMood.Rash));
            Assert.That(Mood(Tempered(t + 2 * shift + 1, 0, 1000, leader, 4, others), leader), Is.EqualTo(SquadMood.Rash));
            Assert.That(Mood(Tempered(t + 2 * shift, 0, 1000, leader, 4, others), leader), Is.EqualTo(SquadMood.Normal));
        }

        // 2. A squad of size 9 with one member: strength 2 * 1000 / 10 = 200, and with shaken 600 a move of
        // 800 * 600 / 1000 = 480. Whole again (nine members), it has its leader's own number back.
        [Test]
        public void TheStrengthCountsTheLeaderAndTheSizePlusOne()
        {
            ulong leader = Mild(500);
            int t = T(leader);
            M6World w = Tempered(t + 481, 0, 600, leader, 9, 11);
            Assert.That(Strength(w, leader), Is.EqualTo(200));
            Assert.That(Mood(w, leader), Is.EqualTo(SquadMood.Rash));
            Assert.That(Mood(Tempered(t + 480, 0, 600, leader, 9, 11), leader), Is.EqualTo(SquadMood.Normal));
            M6World whole = Tempered(t + 1, 0, 600, leader, 9, 11, 12, 13, 14, 15, 16, 17, 18, 19);
            Assert.That(Strength(whole, leader), Is.EqualTo(1000));
            Assert.That(Mood(whole, leader), Is.EqualTo(SquadMood.Rash));
            Assert.That(Mood(Tempered(t + 1, 0, 600, leader, 9, 11, 12, 13, 14, 15, 16, 17, 18), leader), Is.EqualTo(SquadMood.Normal), "one short: 900, a move of 60");
        }

        // 2. With the two shares meeting (rash + steady = 1000) no squad is normal.
        [Test]
        public void WithTheTwoSharesMeetingNoSquadIsNormal()
        {
            for (ulong key = 1; key <= 10; key++)
                Assert.That(Mood(Tempered(400, 600, 0, key, 1, key + 10), key), Is.EqualTo(T(key) < 400 ? SquadMood.Rash : SquadMood.Steady), "key " + key);
        }

        // 2. A skirmisher has no mood and no strength; in a battle without the temper fields nobody has a mood, and the
        // strength is still told.
        [Test]
        public void SkirmishersAndBattlesWithoutTempersHaveNoMood()
        {
            M6World w = Tempered(300, 250, 600, 1, 4, 11, 12);
            Assert.That(Mood(w, 2), Is.EqualTo(SquadMood.None));
            Assert.That(Strength(w, 2), Is.Zero);
            Assert.That(Rally(w, 1), Is.EqualTo(SquadRally.None), "no regroup fields");
            M6World plain = Field(Json(Seven), Twenty());
            Tables(plain, Seven, null, new Squad(1, 4, -1, 11, 12));
            foreach (ulong key in new ulong[] { 1, 11, 12, 2 }) Assert.That(Mood(plain, key), Is.EqualTo(SquadMood.None), "key " + key);
            Assert.That(Strength(plain, 11), Is.EqualTo(600));
        }

        // 2. The mood is the leader's: when the leader falls and the men form again under another, the squad has the new
        // leader's number. Five men, sizes 4..4; the rash line stands between the two leaders' numbers.
        [Test]
        public void ANewLeaderBringsHisOwnTemper()
        {
            ulong[] k = ByElect(1, 2, 3, 4, 5);
            int first = T(k[0]), second = T(k[1]);
            Assert.That(first, Is.Not.EqualTo(second), "the fixture's two leaders differ");
            var units = new List<UnitSpawn>();
            for (int i = 0; i < 5; i++) units.Add(Man(k[i], 1, 20000 + i * 1000));
            units.Add(Man(90, 2, 150000, 50000));
            int[] seven = { 4, 4, 0, 10000, 15, 0, 0 };
            M6World w = Field(Json(seven, Rules(Math.Max(first, second), 0, 0, 8000)), units.ToArray(), x => Kill(x, 20, k[0]));
            To(w, Form);
            Assert.That(Band(w, k[4]), Is.EqualTo(k[0]));
            Assert.That(Mood(w, k[4]), Is.EqualTo(first < second ? SquadMood.Rash : SquadMood.Normal));
            To(w, 2 * Form);
            Assert.That(Band(w, k[4]), Is.EqualTo(k[1]), "the next by election order leads the four that are left");
            Assert.That(Strength(w, k[4]), Is.EqualTo(800));
            Assert.That(Mood(w, k[4]), Is.EqualTo(second < first ? SquadMood.Rash : SquadMood.Normal));
            Assert.That(Mood(w, k[1]), Is.EqualTo(Mood(w, k[4])));
        }

        // 2. The temper salt is no other salt of the battle: every constant of WarCore.Battle whose name has Salt in it is
        // collected, the temper salt differs from all the others, and the only equal pairs are the four that were there
        // before (they are left as they are: changing one would change saved battles).
        [Test]
        public void TheTemperSaltIsItsOwn()
        {
            var salts = new List<(string name, ulong value)>();
            foreach (Type type in typeof(Squads).Assembly.GetTypes())
                foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    if (field.IsLiteral && field.FieldType == typeof(ulong) && field.Name.Contains("Salt"))
                        salts.Add((type.Name + "." + field.Name, (ulong)field.GetRawConstantValue()));
            Assert.That(salts.Count, Is.GreaterThanOrEqualTo(19));
            var known = new HashSet<string>
            {
                "Pressure.CourageSalt=ReactionFire.ReactionSalt", "Pressure.BreakSalt=ReactionFire.BurstSalt",
                "BoundEngage.ShareSalt=NearestDispatch.SpreadSalt", "Cover.SaltX=ReactionFire.PauseSalt",
            };
            int temper = 0;
            for (int i = 0; i < salts.Count; i++)
            {
                if (salts[i].name == "Squads.TemperSalt") { temper++; Assert.That(salts[i].value, Is.EqualTo(TemperSalt)); }
                for (int j = i + 1; j < salts.Count; j++)
                {
                    if (salts[i].value != salts[j].value) continue;
                    string a = salts[i].name, b = salts[j].name;
                    string pair = string.CompareOrdinal(a, b) < 0 ? a + "=" + b : b + "=" + a;
                    Assert.That(known.Remove(pair), pair + " are the same salt");
                }
            }
            Assert.That(temper, Is.EqualTo(1));
            Assert.That(known, Is.Empty, "the four known pairs are still there");
        }
    }
}
