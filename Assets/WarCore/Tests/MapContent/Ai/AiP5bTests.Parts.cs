using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-P5b, the squad parts: the engage wrapper changes what a calm leader or member does when it sees an enemy (by
    // the squad's mood, or going on when the squad regroups), and the dispatch wrapper sends a regrouping squad to the
    // point of its team that is nearest its leader. Everything else is the inner part's answer.
    public sealed partial class AiP5bTests
    {
        private static ulong Tag(string tag, ulong digest) { var h = new StateHash(); h.Add(tag); h.Add(digest); return h.Value; }

        // An engage part that answers what the test says and keeps every unit it was asked for.
        private sealed class SeenEngage : IAiEngage
        {
            internal AiEngage Sees = AiEngage.Fallback, SeesNobody = AiEngage.Cover;
            internal readonly List<AiUnit> Seen = new List<AiUnit>(), Unseens = new List<AiUnit>();
            internal readonly List<SimVec3> Enemies = new List<SimVec3>();
            public ulong Fold(ulong digest) => Tag("test.engage", digest);
            public AiEngage Decide(in AiUnit unit, SimVec3 enemy, AiField field) { Seen.Add(unit); Enemies.Add(enemy); return Sees; }
            public AiEngage Unseen(in AiUnit unit, AiField field) { Unseens.Add(unit); return SeesNobody; }
        }
        private sealed class SeenDispatch : IAiDispatch
        {
            internal int Answer = 7;
            internal readonly List<AiUnit> Seen = new List<AiUnit>();
            public ulong Fold(ulong digest) => Tag("test.dispatch", digest);
            public int Pick(in AiUnit unit, AiField field) { Seen.Add(unit); return Answer; }
        }

        private static AiUnit Thinker(M6World w, ulong key, AiNerve nerve = AiNerve.Calm) => new AiUnit(Id(w, key), 1, At(w, key), nerve);
        private static SimVec3 Ahead(SimVec3 from, long mm, long up = 0) =>
            from + new SimVec3(SimFix.Ratio(mm, 1000), SimFix.Ratio(up, 1000), default);
        private static readonly AiNerve[] Nerves = { AiNerve.Calm, AiNerve.Pinned, AiNerve.Broken };
        // The first of the twenty who is in no squad.
        private static ulong Loner(M6World w)
        {
            for (ulong key = 1; key <= 20; key++) if (Band(w, key) == 0) return key;
            throw new InvalidOperationException("Everybody is in a squad.");
        }

        // Three squads among the twenty, one of each mood: the three leaders are the keys 1..10 with the lowest, a middle
        // and the highest temper number, and the two lines stand right above the lowest and right at the highest. Each
        // leads the man ten keys on (size 1, so every squad is whole). rash, normal, steady: the three leaders.
        private static M6World ThreeMoods(out ulong rash, out ulong normal, out ulong steady, int[] regroup = null, long mark = -1, int range = 8000)
        {
            var keys = new List<ulong>();
            for (ulong key = 1; key <= 10; key++) keys.Add(key);
            keys.Sort((a, b) => T(a).CompareTo(T(b)));
            rash = keys[0]; normal = keys[5]; steady = keys[9];
            Assert.That(T(rash), Is.LessThan(T(normal)));
            Assert.That(T(normal), Is.LessThan(T(steady)));
            M6World w = Field(Json(Seven, Rules(T(rash) + 1, 1000 - T(steady), 0, range), regroup), Twenty());
            Tables(w, Seven, regroup, new Squad(rash, 1, mark, rash + 10), new Squad(normal, 1, mark, normal + 10), new Squad(steady, 1, mark, steady + 10));
            Assert.That(Mood(w, rash), Is.EqualTo(SquadMood.Rash));
            Assert.That(Mood(w, normal), Is.EqualTo(SquadMood.Normal));
            Assert.That(Mood(w, steady), Is.EqualTo(SquadMood.Steady));
            return w;
        }

        // 3. The whole table, enemy in sight and far away (20 m): a calm leader or member of a rash squad goes on, of a
        // steady squad stands, of a normal squad gets the inner part's answer; a pinned or broken one always gets the inner
        // part's answer. The inner part is asked for the man himself: his own id, place and nerve, and the enemy's point.
        [Test]
        public void SeeingAnEnemyACalmManActsByHisSquadsMood()
        {
            M6World w = ThreeMoods(out ulong rash, out ulong normal, out ulong steady);
            var inner = new SeenEngage();
            IAiEngage wrap = Sq(w).Wrap(inner);
            foreach (ulong leader in new[] { rash, normal, steady })
                foreach (ulong key in new[] { leader, leader + 10 })
                    foreach (AiNerve nerve in Nerves)
                    {
                        AiUnit unit = Thinker(w, key, nerve);
                        SimVec3 enemy = Ahead(unit.Here, 20000);
                        int asked = inner.Seen.Count;
                        AiEngage answer = wrap.Decide(in unit, enemy, null);
                        string what = "leader " + leader + " key " + key + " " + nerve;
                        bool own = nerve == AiNerve.Calm && leader != normal;
                        Assert.That(answer, Is.EqualTo(!own ? AiEngage.Fallback : leader == rash ? AiEngage.Advance : AiEngage.Hold), what);
                        Assert.That(inner.Seen.Count, Is.EqualTo(asked + (own ? 0 : 1)), what);
                        if (own) continue;
                        AiUnit seen = inner.Seen[asked];
                        Assert.That(seen.Id, Is.EqualTo(unit.Id), what);
                        Assert.That(seen.Here, Is.EqualTo(unit.Here), what);
                        Assert.That(seen.Nerve, Is.EqualTo(nerve), what);
                        Assert.That(seen.Team, Is.EqualTo(1), what);
                        Assert.That(inner.Enemies[asked], Is.EqualTo(enemy), what);
                    }
            // The inner part's other answers come through as they are.
            foreach (AiEngage other in new[] { AiEngage.Hold, AiEngage.Advance, AiEngage.Cover })
            {
                inner.Sees = other;
                Assert.That(wrap.Decide(Thinker(w, normal + 10), Ahead(At(w, normal + 10), 20000), null), Is.EqualTo(other));
                Assert.That(wrap.Decide(Thinker(w, rash, AiNerve.Pinned), Ahead(At(w, rash), 20000), null), Is.EqualTo(other));
            }
        }

        // 3. With no enemy in sight the inner part is always asked, for the man himself, whatever the mood and the nerve.
        [Test]
        public void SeeingNobodyEverybodyAsksTheInnerPart()
        {
            M6World w = ThreeMoods(out ulong rash, out ulong normal, out ulong steady, Rules(500, 750, 1200), 0);
            var inner = new SeenEngage();
            IAiEngage wrap = Sq(w).Wrap(inner);
            int asked = 0;
            foreach (ulong key in new[] { rash, rash + 10, normal, normal + 10, steady, steady + 10, Loner(w) })
                foreach (AiNerve nerve in Nerves)
                {
                    AiUnit unit = Thinker(w, key, nerve);
                    Assert.That(wrap.Unseen(in unit, null), Is.EqualTo(AiEngage.Cover), "key " + key + " " + nerve);
                    Assert.That(inner.Unseens.Count, Is.EqualTo(++asked));
                    Assert.That(inner.Unseens[asked - 1].Id, Is.EqualTo(unit.Id));
                    Assert.That(inner.Unseens[asked - 1].Here, Is.EqualTo(unit.Here));
                    Assert.That(inner.Unseens[asked - 1].Nerve, Is.EqualTo(nerve));
                }
            inner.SeesNobody = AiEngage.Advance;
            Assert.That(wrap.Unseen(Thinker(w, steady, AiNerve.Pinned), null), Is.EqualTo(AiEngage.Advance));
            Assert.That(inner.Seen, Is.Empty);
        }

        // 3. The rash range: a rash man goes on while the enemy is at least the range away, measured on the ground from
        // where he himself stands; one millimetre nearer he asks the inner part. A range of 0 never asks.
        [Test]
        public void ARashManGoesOnUntilTheEnemyIsNearerThanTheRange()
        {
            M6World w = ThreeMoods(out ulong rash, out _, out _);
            var inner = new SeenEngage();
            IAiEngage wrap = Sq(w).Wrap(inner);
            foreach (ulong key in new[] { rash, rash + 10 })
            {
                AiUnit unit = Thinker(w, key);
                Assert.That(wrap.Decide(in unit, Ahead(unit.Here, 8000), null), Is.EqualTo(AiEngage.Advance), "at the range, key " + key);
                Assert.That(wrap.Decide(in unit, Ahead(unit.Here, 8000, 30000), null), Is.EqualTo(AiEngage.Advance), "height does not count, key " + key);
                Assert.That(inner.Seen, Is.Empty);
                Assert.That(wrap.Decide(in unit, Ahead(unit.Here, 7999), null), Is.EqualTo(AiEngage.Fallback), "one millimetre nearer, key " + key);
                Assert.That(wrap.Decide(in unit, Ahead(unit.Here, 7999, 30000), null), Is.EqualTo(AiEngage.Fallback));
                Assert.That(inner.Seen.Count, Is.EqualTo(2));
                Assert.That(inner.Seen[0].Id, Is.EqualTo(unit.Id));
                Assert.That(inner.Seen[0].Here, Is.EqualTo(unit.Here));
                inner.Seen.Clear();
            }
            // The member stands 10 m from its leader (keys ten apart, 1 m a key). An enemy 3 m beyond the leader is 13 m
            // from the member and 3 m from the leader: the member goes on, the leader asks.
            SimVec3 enemy = Ahead(At(w, rash), -3000);
            Assert.That(CombatSystem.Horizontal(At(w, rash + 10) - At(w, rash)).LengthSquared, Is.EqualTo(new SimVec3(SimFix.FromInt(10), default, default).LengthSquared));
            Assert.That(wrap.Decide(Thinker(w, rash + 10), enemy, null), Is.EqualTo(AiEngage.Advance));
            Assert.That(wrap.Decide(Thinker(w, rash), enemy, null), Is.EqualTo(AiEngage.Fallback));
            // And the other way round: near the member, far from the leader.
            enemy = Ahead(At(w, rash + 10), 3000);
            Assert.That(wrap.Decide(Thinker(w, rash + 10), enemy, null), Is.EqualTo(AiEngage.Fallback));
            Assert.That(wrap.Decide(Thinker(w, rash), enemy, null), Is.EqualTo(AiEngage.Advance));

            M6World none = ThreeMoods(out rash, out _, out _, range: 0);
            Assert.That(Sq(none).Wrap(inner).Decide(Thinker(none, rash), At(none, rash), null), Is.EqualTo(AiEngage.Advance));
        }

        // 3. A skirmisher asks the inner part; so does a member whose leader fell on this tick (the tables still name the
        // leader until the next step) and, from the next step on, as the skirmisher he then is. The mood told to the view
        // is still the table's on the tick the leader fell.
        [Test]
        public void SkirmishersAndMembersWithoutALeaderAskTheInnerPart()
        {
            M6World w = ThreeMoods(out ulong rash, out _, out ulong steady);
            var inner = new SeenEngage();
            IAiEngage wrap = Sq(w).Wrap(inner);
            ulong loner = Loner(w);
            Assert.That(wrap.Decide(Thinker(w, loner), Ahead(At(w, loner), 20000), null), Is.EqualTo(AiEngage.Fallback));
            Assert.That(inner.Seen[0].Id, Is.EqualTo(Id(w, loner)));

            ulong[] k = ByElect(1, 2, 3);
            var units = new[] { Man(k[0], 1, 20000), Man(k[1], 1, 21000), Man(k[2], 1, 22000), Man(90, 2, 150000, 50000) };
            int[] seven = { 2, 2, 0, 10000, 15, 0, 0 };
            // Steady whatever the leader: the steady share is everything.
            M6World fallen = Field(Json(seven, Rules(0, 1000, 0, 8000)), units, x => Kill(x, 20, k[0]));
            To(fallen, 19);
            IAiEngage wrapped = Sq(fallen).Wrap(inner);
            Assert.That(wrapped.Decide(Thinker(fallen, k[1]), Ahead(At(fallen, k[1]), 20000), null), Is.EqualTo(AiEngage.Hold));
            To(fallen, 20);
            Assert.That(fallen.Dead(k[0]));
            Assert.That(Band(fallen, k[1]), Is.EqualTo(k[0]), "the tables still name him");
            Assert.That(Mood(fallen, k[1]), Is.EqualTo(SquadMood.Steady));
            inner.Seen.Clear();
            Assert.That(wrapped.Decide(Thinker(fallen, k[1]), Ahead(At(fallen, k[1]), 20000), null), Is.EqualTo(AiEngage.Fallback));
            Assert.That(inner.Seen[0].Id, Is.EqualTo(Id(fallen, k[1])));
            To(fallen, 21);
            Assert.That(Band(fallen, k[1]), Is.Zero);
            Assert.That(wrapped.Decide(Thinker(fallen, k[1]), Ahead(At(fallen, k[1]), 20000), null), Is.EqualTo(AiEngage.Fallback));
            Assert.That(inner.Seen.Count, Is.EqualTo(2));
        }

        // 5. A regrouping squad's calm men go on when they see an enemy, whatever the squad's mood and however near the
        // enemy; its pinned and broken men ask the inner part. A mark of 0 is a tick like any other. Squads that are not
        // regrouping (-1) or are short and idle (-2) act by their mood.
        [TestCase(0L)]
        [TestCase(45L)]
        public void ARegroupingSquadsCalmMenGoOn(long mark)
        {
            M6World w = ThreeMoods(out ulong rash, out ulong normal, out ulong steady, Rules(500, 750, 1200), mark);
            var inner = new SeenEngage();
            IAiEngage wrap = Sq(w).Wrap(inner);
            foreach (ulong key in new[] { rash, rash + 10, normal, normal + 10, steady, steady + 10 })
            {
                Assert.That(Rally(w, key), Is.EqualTo(SquadRally.Active));
                foreach (long range in new long[] { 20000, 1000 })
                {
                    Assert.That(wrap.Decide(Thinker(w, key), Ahead(At(w, key), range), null), Is.EqualTo(AiEngage.Advance), "key " + key + " at " + range);
                    Assert.That(inner.Seen, Is.Empty);
                    Assert.That(wrap.Decide(Thinker(w, key, AiNerve.Pinned), Ahead(At(w, key), range), null), Is.EqualTo(AiEngage.Fallback));
                    Assert.That(wrap.Decide(Thinker(w, key, AiNerve.Broken), Ahead(At(w, key), range), null), Is.EqualTo(AiEngage.Fallback));
                    Assert.That(inner.Seen.Count, Is.EqualTo(2));
                    inner.Seen.Clear();
                }
            }
            foreach (long quiet in new long[] { -1, -2 })
            {
                M6World calm = ThreeMoods(out rash, out normal, out steady, Rules(500, 750, 1200), quiet);
                IAiEngage byMood = Sq(calm).Wrap(inner);
                Assert.That(Rally(calm, steady), Is.EqualTo(quiet == -2 ? SquadRally.Idle : SquadRally.None));
                Assert.That(Rally(calm, steady + 10), Is.EqualTo(Rally(calm, steady)));
                Assert.That(byMood.Decide(Thinker(calm, steady + 10), Ahead(At(calm, steady + 10), 20000), null), Is.EqualTo(AiEngage.Hold), "mark " + quiet);
                Assert.That(byMood.Decide(Thinker(calm, normal), Ahead(At(calm, normal), 20000), null), Is.EqualTo(AiEngage.Fallback), "mark " + quiet);
                Assert.That(byMood.Decide(Thinker(calm, rash), Ahead(At(calm, rash), 20000), null), Is.EqualTo(AiEngage.Advance), "mark " + quiet);
                Assert.That(byMood.Decide(Thinker(calm, rash), Ahead(At(calm, rash), 1000), null), Is.EqualTo(AiEngage.Fallback), "mark " + quiet);
            }
            Assert.That(Rally(w, Loner(w)), Is.EqualTo(SquadRally.None), "a skirmisher");
        }

        // 5. Regrouping without tempers: the engage part is wrapped all the same; a regrouping squad's calm men go on, and
        // everybody else gets the inner part's answer.
        [Test]
        public void RegroupingAloneWrapsTheEngagePartToo()
        {
            int[] regroup = Rules(500, 750, 1200);
            M6World w = Field(Json(Seven, null, regroup), Twenty());
            Tables(w, Seven, regroup, new Squad(1, 1, 30, 11), new Squad(2, 1, -1, 12), new Squad(3, 1, -2, 13));
            var inner = new SeenEngage();
            IAiEngage wrap = Sq(w).Wrap(inner);
            Assert.That(wrap.Decide(Thinker(w, 11), Ahead(At(w, 11), 20000), null), Is.EqualTo(AiEngage.Advance));
            Assert.That(wrap.Decide(Thinker(w, 1), Ahead(At(w, 1), 20000), null), Is.EqualTo(AiEngage.Advance));
            Assert.That(inner.Seen, Is.Empty);
            foreach (ulong key in new ulong[] { 2, 12, 3, 13, 20 })
                Assert.That(wrap.Decide(Thinker(w, key), Ahead(At(w, key), 20000), null), Is.EqualTo(AiEngage.Fallback), "key " + key);
            Assert.That(inner.Seen.Count, Is.EqualTo(5));
            Assert.That(Mood(w, 1), Is.EqualTo(SquadMood.None));
        }

        // Four points for the rally tests, on the line z = 20: team 1 owns the points at x = 10 and x = 70, team 2 the one
        // at x = 44 (between them) and the one at x = 150.
        private static readonly ControlPointDef[] RallyPoints =
        {
            new ControlPointDef(1, SimVec3.Metres(10, 0, 20), SimFix.FromInt(3), 1), new ControlPointDef(2, SimVec3.Metres(44, 0, 20), SimFix.FromInt(3), 2),
            new ControlPointDef(3, SimVec3.Metres(70, 0, 20), SimFix.FromInt(3), 1), new ControlPointDef(4, SimVec3.Metres(150, 0, 50), SimFix.FromInt(3), 2),
        };
        private static AiField FieldOf(M6World w) =>
            (AiField)typeof(SimpleAiSystem).GetField("field", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<SimpleAiSystem>());
        // A leader (key 1) at leaderX and its member (key 2) at memberX, a second squad (3 leads 4) and a skirmisher (5).
        private static M6World Rallying(long leaderXMm, long memberXMm, long mark, ControlPointDef[] points = null, long other = -1)
        {
            int[] regroup = Rules(500, 750, 1200);
            var units = new[] { Man(1, 1, leaderXMm), Man(2, 1, memberXMm), Man(3, 1, 30000), Man(4, 1, 31000), Man(5, 1, 32000), Man(90, 2, 150000, 50000) };
            M6World w = Field(Json(Seven, null, regroup), units, points: points ?? RallyPoints);
            Tables(w, Seven, regroup, new Squad(1, 2, mark, 2), new Squad(3, 2, other, 4));
            w.Step();
            Assert.That(Band(w, 2), Is.EqualTo(1UL));
            Assert.That(Rally(w, 1), Is.EqualTo(mark >= 0 ? SquadRally.Active : mark == -2 ? SquadRally.Idle : SquadRally.None));
            return w;
        }

        // 5. A regrouping squad's leader and member are both sent to the point of their team nearest the leader: the
        // leader stands at x = 45, nearer to the point at 70 (25 m) than to the one at 10 (35 m), and the member at x = 12,
        // right by the other. The enemy's point at 44, a metre from the leader, does not count. The inner part is not asked.
        [TestCase(0L)]
        [TestCase(30L)]
        public void ARegroupingSquadGoesToItsTeamsPointNearestItsLeader(long mark)
        {
            M6World w = Rallying(45000, 12000, mark);
            var inner = new SeenDispatch();
            IAiDispatch wrap = Sq(w).Wrap(inner);
            AiField field = FieldOf(w);
            Assert.That(wrap.Pick(Thinker(w, 1), field), Is.EqualTo(2));
            Assert.That(wrap.Pick(Thinker(w, 2), field), Is.EqualTo(2));
            Assert.That(wrap.Pick(Thinker(w, 2, AiNerve.Broken), field), Is.EqualTo(2), "the dispatch does not look at the nerve");
            Assert.That(inner.Seen, Is.Empty);
            // The other way round: the leader by the point at 10, the member by the one at 70.
            M6World back = Rallying(12000, 68000, mark);
            wrap = Sq(back).Wrap(inner); field = FieldOf(back);
            Assert.That(wrap.Pick(Thinker(back, 1), field), Is.Zero);
            Assert.That(wrap.Pick(Thinker(back, 2), field), Is.Zero);
            Assert.That(inner.Seen, Is.Empty);
            // The squad that is not regrouping and the skirmisher ask the inner part: the member as its leader.
            Assert.That(wrap.Pick(Thinker(back, 4), field), Is.EqualTo(7));
            Assert.That(inner.Seen[0].Id, Is.EqualTo(Id(back, 3)));
            Assert.That(wrap.Pick(Thinker(back, 5), field), Is.EqualTo(7));
            Assert.That(inner.Seen[1].Id, Is.EqualTo(Id(back, 5)));
        }

        // 5. Two points of the team equally far from the leader (x = 40 between 10 and 70): the lower index. One
        // millimetre nearer to the higher one, the higher one.
        [Test]
        public void EquallyFarPointsGoToTheLowerIndex()
        {
            M6World w = Rallying(40000, 69000, 0);
            Assert.That(Sq(w).Wrap(new SeenDispatch()).Pick(Thinker(w, 2), FieldOf(w)), Is.Zero);
            M6World nearer = Rallying(40001, 11000, 0);
            Assert.That(Sq(nearer).Wrap(new SeenDispatch()).Pick(Thinker(nearer, 2), FieldOf(nearer)), Is.EqualTo(2));
        }

        // 5. A squad that is not regrouping (-1), or is short and idle (-2), asks the inner part as before; so does a
        // regrouping squad whose team owns no point: the member asks as its leader, the leader as himself.
        [Test]
        public void WithoutAMarkOrAPointOfItsOwnASquadAsksTheInnerPart()
        {
            foreach (long mark in new long[] { -1, -2 })
            {
                M6World w = Rallying(45000, 12000, mark);
                var inner = new SeenDispatch();
                Assert.That(Sq(w).Wrap(inner).Pick(Thinker(w, 2), FieldOf(w)), Is.EqualTo(7), "mark " + mark);
                Assert.That(inner.Seen[0].Id, Is.EqualTo(Id(w, 1)));
                Assert.That(inner.Seen[0].Here, Is.EqualTo(At(w, 1)));
            }
            ControlPointDef[] theirs =
            {
                new ControlPointDef(1, SimVec3.Metres(10, 0, 20), SimFix.FromInt(3), 2), new ControlPointDef(2, SimVec3.Metres(70, 0, 20), SimFix.FromInt(3), 0),
            };
            M6World none = Rallying(45000, 12000, 30, theirs);
            var asked = new SeenDispatch { Answer = -1 };
            IAiDispatch wrap = Sq(none).Wrap(asked);
            Assert.That(wrap.Pick(Thinker(none, 2), FieldOf(none)), Is.EqualTo(-1));
            Assert.That(asked.Seen[0].Id, Is.EqualTo(Id(none, 1)));
            Assert.That(wrap.Pick(Thinker(none, 1), FieldOf(none)), Is.EqualTo(-1));
            Assert.That(asked.Seen[1].Id, Is.EqualTo(Id(none, 1)));
            Assert.That(asked.Seen.Count, Is.EqualTo(2));
            // Its calm men still go on when they see an enemy: to the point the inner part named.
            Assert.That(Sq(none).Wrap(new SeenEngage()).Decide(Thinker(none, 2), Ahead(At(none, 2), 20000), null), Is.EqualTo(AiEngage.Advance));
        }

        // 5. A member whose leader fell on this tick is not sent with the squad: it asks the inner part as itself.
        [Test]
        public void AMemberWhoseLeaderFellDoesNotRegroupWithHim()
        {
            int[] regroup = Rules(500, 750, 1200);
            var units = new[] { Man(1, 1, 45000), Man(2, 1, 12000), Man(90, 2, 150000, 50000) };
            M6World w = Field(Json(Seven, null, regroup), units, x => Kill(x, 3, 1), points: RallyPoints);
            Tables(w, Seven, regroup, new Squad(1, 2, 0, 2));
            To(w, 2);
            var inner = new SeenDispatch();
            IAiDispatch wrap = Sq(w).Wrap(inner);
            Assert.That(wrap.Pick(Thinker(w, 2), FieldOf(w)), Is.EqualTo(2));
            To(w, 3);
            Assert.That(w.Dead(1));
            Assert.That(Rally(w, 2), Is.EqualTo(SquadRally.Active), "the tables still name the squad");
            Assert.That(wrap.Pick(Thinker(w, 2), FieldOf(w)), Is.EqualTo(7));
            Assert.That(inner.Seen[0].Id, Is.EqualTo(Id(w, 2)));
        }

        // 4. The wrappers' digests. Engage: its tag, the inner part's digest, the four temper numbers (zeros without
        // tempers) and whether squads regroup. Dispatch: as without regrouping (its tag, the inner digest, the seven
        // numbers), then with regrouping the regroup tag and the three numbers.
        [Test]
        public void TheWrappersFoldTheTemperAndRegroupNumbers()
        {
            int[] temper = Rules(300, 250, 600, 8000), regroup = Rules(500, 750, 1200);
            ulong Engage(int[] t, int[] r)
            {
                var h = new StateHash(); h.Add("battle.ai.squad-engage.v1"); h.Add(Tag("test.engage", 77));
                foreach (int value in t ?? new[] { 0, 0, 0, 0 }) h.Add(value);
                h.Add(r != null ? 1 : 0);
                return h.Value;
            }
            ulong Dispatch(int[] r)
            {
                var h = new StateHash(); h.Add("battle.ai.squads.v1"); h.Add(Tag("test.dispatch", 77));
                foreach (int value in Seven) h.Add(value);
                if (r == null) return h.Value;
                h.Add("battle.ai.squad-regroup.v1");
                foreach (int value in r) h.Add(value);
                return h.Value;
            }
            var seen = new HashSet<ulong>();
            foreach ((int[] t, int[] r) in new (int[], int[])[] { (temper, null), (null, regroup), (temper, regroup), (null, null) })
            {
                Squads squads = Sq(Field(Json(Seven, t, r), Twenty()));
                Assert.That(squads.Wrap(new SeenEngage()).Fold(77), Is.EqualTo(Engage(t, r)));
                Assert.That(squads.Wrap(new SeenDispatch()).Fold(77), Is.EqualTo(Dispatch(r)));
                Assert.That(seen.Add(Engage(t, r)));
            }
            Assert.That(Dispatch(regroup), Is.Not.EqualTo(Dispatch(null)));
            // Each number is in it.
            for (int i = 0; i < 4; i++)
            {
                int[] other = (int[])temper.Clone(); other[i]++;
                Assert.That(Sq(Field(Json(Seven, other, regroup), Twenty())).Wrap(new SeenEngage()).Fold(77), Is.EqualTo(Engage(other, regroup)), TemperFields[i]);
                Assert.That(seen.Add(Engage(other, regroup)), TemperFields[i]);
            }
            var dispatches = new HashSet<ulong> { Dispatch(regroup), Dispatch(null) };
            for (int i = 0; i < 3; i++)
            {
                int[] other = (int[])regroup.Clone(); other[i]++;
                Assert.That(Sq(Field(Json(Seven, temper, other), Twenty())).Wrap(new SeenDispatch()).Fold(77), Is.EqualTo(Dispatch(other)), RegroupFields[i]);
                Assert.That(dispatches.Add(Dispatch(other)), RegroupFields[i]);
            }
            Assert.Throws<ArgumentNullException>(() => Sq(Field(Json(Seven, temper), Twenty())).Wrap((IAiEngage)null));
        }
    }
}
