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
    // AI-P5b with the real scheduler and in whole battles: a squad that is beaten up walks back to a point of its own,
    // fills up and sets out again; tempers and regrouping together with the other AI parts; the two teams treated alike;
    // no allocation.
    public sealed partial class AiP5bTests
    {
        // Every order the scheduler gives, by unit.
        private sealed class Orders : IMoveOrders
        {
            private readonly IMoveOrders inner;
            private readonly WarCore.Sim.Sim sim;
            internal readonly List<(long tick, ulong unit, SimVec3 target)> Given = new List<(long, ulong, SimVec3)>();
            internal Orders(IMoveOrders inner, WarCore.Sim.Sim sim) { this.inner = inner; this.sim = sim; }
            public void Order(StableId id, SimVec3 target) { Given.Add((sim.Clock.Tick, id.Value, target)); inner.Order(id, target); }
            public void Stop(StableId id) => inner.Stop(id);
            public bool TryGetOrder(StableId id, out SimVec3 target) => inner.TryGetOrder(id, out target);
            public bool CanStandAt(SimVec3 at) => inner.CanStandAt(at);
            public bool IsCommanded(StableId id) => inner.IsCommanded(id);
            public bool HasFailed(StableId id) => inner.HasFailed(id);
            public bool TryRouteCost(SimVec3 from, SimVec3 to, long limitCost, out long cost) => inner.TryRouteCost(from, to, limitCost, out cost);
            // The orders given to the man after the tick `after`, up to the tick `until`.
            internal List<(long tick, SimVec3 target)> To(ulong key, long after, long until)
            {
                var list = new List<(long, SimVec3)>();
                foreach (var order in Given) if (order.unit == Stable(key) && order.tick > after && order.tick <= until) list.Add((order.tick, order.target));
                return list;
            }
        }
        private static Orders Watch(M6World w)
        {
            SimpleAiSystem ai = w.System<SimpleAiSystem>();
            FieldInfo field = typeof(SimpleAiSystem).GetField("moves", BindingFlags.NonPublic | BindingFlags.Instance);
            var orders = new Orders((IMoveOrders)field.GetValue(ai), w.Sim);
            field.SetValue(ai, orders);
            return orders;
        }

        // The chain fixture: the team's own point at (20, 30) and the enemy's at (80, 30), both 2 m wide and never taken.
        private static readonly ControlPointDef[] ChainPoints =
        {
            new ControlPointDef(1, SimVec3.Metres(20, 0, 30), SimFix.FromInt(2), 1), new ControlPointDef(2, SimVec3.Metres(80, 0, 30), SimFix.FromInt(2), 2),
        };
        private static bool IsSlotOf(int point, SimVec3 at)
        {
            foreach (SimVec3 slot in GuardSlots.Build(ChainPoints[point].Center, ChainPoints[point].Radius))
                if (slot.X.Raw == at.X.Raw && slot.Z.Raw == at.Z.Raw) return true;
            return false;
        }

        // 6. The whole chain with the real scheduler (think every 10 ticks, nobody armed, so only the dispatch speaks).
        // Seven men of team 1, nobody skirmishes: five stand together 10 m short of the enemy's point (sizes 4..4, so they
        // are one squad from tick 15: foot 7, target 7, the leader takes min(4, 7 - 0 - 1) = 4, strength 1000), and two
        // stand by their own point, 7 m to either side of it: 14 m apart, more than the reach of 10 m, so neither finds a
        // member (on tick 15 and after: target - bodies = 2, got = 0 for both) and they stay skirmishers. They all but
        // stand (1 mm/s).
        //
        // Three members fall on tick 100. On tick 105: foot 4, target 4, bodies 2; the squad lacks 3 and nobody is in
        // reach; strength 2 * 1000 / 5 = 400, below the line of 500: it regroups. From their next think the leader and the
        // member are sent to slots of their own point, 60 m back, and to nothing else. The leader walks back a little to
        // one side of the line between the points, so he comes within reach of one skirmisher first: on that form tick the
        // squad takes him (lacks 3, one in reach): strength 3 * 1000 / 5 = 600, still short of the line of 750, so it goes
        // on regrouping. On the form tick on which the other is in reach too it takes him: 4 * 1000 / 5 = 800, at least
        // 750, and the mark is cleared on that tick. From their next think all four are sent to slots of the enemy's point
        // again.
        [Test]
        public void ABeatenSquadGoesBackFillsUpAndSetsOutAgain()
        {
            int[] seven = { 4, 4, 0, 10000, 15, 0, 0 }, regroup = Rules(500, 750, 100000);
            ulong[] e = ByElect(1, 2, 3, 4, 5);
            var units = new[]
            {
                Man(e[0], 1, 70000, 30000, 3000), Man(e[1], 1, 70000, 31000, 3000), Man(e[2], 1, 69000, 30000, 3000),
                Man(e[3], 1, 69000, 31000, 3000), Man(e[4], 1, 70000, 29000, 3000),
                Man(6, 1, 20000, 23000), Man(7, 1, 20000, 37000), Man(90, 2, 150000, 50000),
            };
            M6World w = Field(Json(seven, null, regroup), units, x => Kill(x, 100, e[2], e[3], e[4]), think: 10, points: ChainPoints);
            Orders orders = Watch(w);
            To(w, Form);
            foreach (ulong key in e) Assert.That(Band(w, key), Is.EqualTo(e[0]), "key " + key);
            Assert.That(Band(w, 6), Is.Zero);
            Assert.That(Band(w, 7), Is.Zero);
            Assert.That(Strength(w, e[0]), Is.EqualTo(1000));
            To(w, 104);
            Assert.That(Rally(w, e[0]), Is.EqualTo(SquadRally.None));
            foreach (ulong key in e)
            {
                List<(long tick, SimVec3 target)> first = orders.To(key, 0, 104);
                Assert.That(first, Is.Not.Empty, "key " + key);
                foreach (var order in first) Assert.That(IsSlotOf(1, order.target), "key " + key + " tick " + order.tick);
            }
            To(w, 105);
            Assert.That(Members(w, e[0]), Is.EqualTo(1));
            Assert.That(Strength(w, e[0]), Is.EqualTo(400));
            Assert.That(Rally(w, e[1]), Is.EqualTo(SquadRally.Active));
            Assert.That(Mark(w, seven, regroup, e[0]), Is.EqualTo(105L));

            long joined = 0, filled = 0;
            ulong early = 0;
            while (filled == 0 && w.Sim.Clock.Tick < 1500)
            {
                w.Step();
                long tick = w.Sim.Clock.Tick;
                int members = Members(w, e[0]);
                if (members == 3) { filled = tick; break; }
                if (members == 2 && joined == 0) { joined = tick; early = Band(w, 6) == e[0] ? 6UL : 7UL; }
                Assert.That(Strength(w, e[0]), Is.EqualTo(members == 1 ? 400 : 600), "tick " + tick);
                Assert.That(Rally(w, e[0]), Is.EqualTo(SquadRally.Active), "tick " + tick);
                Assert.That(Mark(w, seven, regroup, e[0]), Is.EqualTo(105L), "tick " + tick);
            }
            Assert.That(joined, Is.EqualTo(JoinedTick));
            Assert.That(filled, Is.EqualTo(FilledTick));
            Assert.That(joined % Form, Is.Zero);
            Assert.That(filled % Form, Is.Zero);
            Assert.That(Band(w, 6), Is.EqualTo(e[0]));
            Assert.That(Band(w, 7), Is.EqualTo(e[0]));
            Assert.That(Strength(w, e[0]), Is.EqualTo(800));
            Assert.That(Rally(w, e[0]), Is.EqualTo(SquadRally.None));
            Assert.That(Mark(w, seven, regroup, e[0]), Is.EqualTo(-1L));
            // The leader is within reach of both, and still on his way: the squad set out again before it had arrived.
            SimWide reach = new SimVec3(SimFix.FromInt(10), default, default).LengthSquared;
            foreach (ulong key in new ulong[] { 6, 7 })
                Assert.That(CombatSystem.Horizontal(At(w, key) - At(w, e[0])).LengthSquared, Is.LessThanOrEqualTo(reach), "key " + key);
            foreach (ulong key in new[] { e[0], e[1] })
            {
                List<(long tick, SimVec3 target)> back = orders.To(key, 105, filled);
                Assert.That(back, Is.Not.Empty, "key " + key);
                Assert.That(back[0].tick, Is.InRange(106, 115), "key " + key);
                foreach (var order in back) Assert.That(IsSlotOf(0, order.target), "key " + key + " tick " + order.tick);
            }
            To(w, filled + 10);
            // The skirmisher taken first was sent back with the squad at his next think, and is sent out again with it. The
            // one taken on the tick the squad set out was never sent back: as a skirmisher he was on his way to a slot of
            // the enemy's point already, and he keeps it.
            ulong late = early == 6 ? 7UL : 6UL;
            List<(long tick, SimVec3 target)> took = orders.To(early, joined, filled);
            Assert.That(took, Has.Count.EqualTo(1));
            Assert.That(IsSlotOf(0, took[0].target));
            foreach (ulong key in new[] { e[0], e[1], early })
            {
                List<(long tick, SimVec3 target)> again = orders.To(key, filled, filled + 10);
                Assert.That(again, Has.Count.EqualTo(1), "key " + key);
                Assert.That(IsSlotOf(1, again[0].target), "key " + key);
            }
            Assert.That(orders.To(late, 0, filled + 10), Has.Count.EqualTo(1));
            Assert.That(IsSlotOf(1, orders.To(late, 0, filled + 10)[0].target));
            Assert.That(L4.Moves(w).TryGetOrder(Id(w, late), out SimVec3 kept), Is.True);
            Assert.That(IsSlotOf(1, kept));
        }
        private const long JoinedTick = 630, FilledTick = 645;

        private const string Split = "\"holdSharePermille\": 300, \"attackSpreadPoints\": 3";
        private const string Scatter = "\"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"scatter\", \"scatterMm\": 6000";
        private const string Roles = "\"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"assault-roles\", "
            + "\"frontPermille\": 425, \"flankPermille\": 375, \"rearPermille\": 200, \"frontArcMm\": 14000, \"flankSideMm\": 35000, "
            + "\"rearOutMm\": 12000, \"flankMinMm\": 60000, \"anchorMm\": 6000, \"arriveMm\": 3000";
        private const string Bound = BoundAi + ", "
            + "\"pressureDeathRadiusMm\": 3000, \"pressureDeathShock\": 2000, \"pressureNearRadiusMm\": 3000, \"pressureNearShock\": 1000, \"pressureDecayPermille\": 12, "
            + "\"pressureBrokenAt\": 7000, \"pressureRecoverAt\": 1000, \"pressurePinnedAt\": 2000, \"pressureSpreadPermille\": 500, \"pressureBreakSharePermille\": 300, "
            + "\"coverRadiusMm\": 9000, \"coverStepMm\": 1000, \"coverDepthMm\": 2000";
        // The squad numbers of the replay setups: sizes 4..9, three in ten skirmish, reach 30 m, spread 1.5..6 m.
        private static readonly int[] Real = { 4, 9, 300, 30000, 15, 1500, 6000 };
        private static readonly (int, int, int, int)[] Yards = { (64, 46, 1, 8), (96, 46, 1, 8), (66, 38, 1, 6), (94, 56, 1, 6), (40, 46, 1, 8), (120, 46, 1, 8) };
        // 150 against 150 with rifles on a 160 m x 100 m field with six walls; the reserves come in as men fall.
        private static M6World Battle(string ai, bool? fair, ulong seed = 41) =>
            new M6World(FairEFixture.Recipe("control", seed, fair).WithAiRules(Pack(ai)), WcMapWriter.Write(M3TestWorld.Boxes(160, 100, Yards)));
        // Where every unit stands: the part of the battle a world without tempers and regrouping has too.
        private static ulong Places(M6World w)
        {
            var hash = new StateHash();
            foreach (UnitSpawn unit in w.Recipe.Units)
            {
                if (!w.Has<Position>(unit.Key)) continue;
                SimVec3 at = At(w, unit.Key); hash.Add(unit.Key); hash.Add(at.X.Raw); hash.Add(at.Z.Raw);
            }
            return hash.Value;
        }
        // The squads of a team by mood and by mark: "rash normal steady / regrouping idle".
        private static string Tempers(M6World w, int team)
        {
            int[] moods = new int[4], marks = new int[3];
            foreach (UnitSpawn unit in w.Recipe.Units)
            {
                if (unit.Team != team || !w.Has<Position>(unit.Key) || w.Dead(unit.Key) || Sq(w).SizeOf(Id(w, unit.Key)) == 0) continue;
                moods[(int)Mood(w, unit.Key)]++; marks[(int)Rally(w, unit.Key)]++;
            }
            return moods[(int)SquadMood.Rash] + " " + moods[(int)SquadMood.Normal] + " " + moods[(int)SquadMood.Steady] + " / "
                + marks[(int)SquadRally.Active] + " " + marks[(int)SquadRally.Idle];
        }

        // 9. Tempers and regrouping with the nearest dispatch (hold share and attack spread on) and each placement, under
        // bound with pressure and cover and under hold, with and without fair decisions: the same battle twice gives the
        // same hash on every tick, and a battle restored half-way goes on like the one that was not. On the way every mood
        // is seen and squads regroup, and by the end the battle is another than the one with squads alone (the first men
        // meet late in this fixture, so the difference may only show after the restore).
        [TestCase("", Bound, null)]
        [TestCase(Scatter, Bound, null)]
        [TestCase(Roles, Bound, null)]
        [TestCase(Roles, Bound, true)]
        [TestCase("", "", null)]
        public void Battle_RepeatAndRestoreWithTempers(string placement, string engage, bool? fair)
        {
            string more = Join(Split, placement, engage), ai = Json(Real, Temper, Regroup, more);
            M6World first = Battle(ai, fair), repeat = Battle(ai, fair), plain = Battle(Json(Real, null, null, more), fair);
            bool differs = false;
            int rash = 0, normal = 0, steady = 0, regrouping = 0, idle = 0;
            for (int tick = 0; tick < 450; tick++)
            {
                first.Step(); repeat.Step(); plain.Step();
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
                differs |= Places(first) != Places(plain);
                if (first.Sim.Clock.Tick % Form != 0) continue;
                for (int team = 1; team <= 2; team++)
                {
                    string[] parts = Tempers(first, team).Split(' ');
                    rash += int.Parse(parts[0]); normal += int.Parse(parts[1]); steady += int.Parse(parts[2]);
                    regrouping += int.Parse(parts[4]); idle += int.Parse(parts[5]);
                }
            }
            Assert.That(rash, Is.GreaterThan(0));
            Assert.That(normal, Is.GreaterThan(0));
            Assert.That(steady, Is.GreaterThan(0));
            Assert.That(regrouping, Is.GreaterThan(0), "squads regrouped");
            Assert.That(Sq(first).StateVersion, Is.EqualTo(2));
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();
            M6World restored = Battle(ai, fair);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            Assert.That(Save(restored), Is.EqualTo(Save(first)));
            for (int team = 1; team <= 2; team++) Assert.That(Tempers(restored, team), Is.EqualTo(Tempers(first, team)));
            for (int tick = 0; tick < 450 && !first.Sim.IsFinished; tick++)
            {
                first.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
                if (!differs && !plain.Sim.IsFinished) { plain.Step(); differs = Places(first) != Places(plain); }
            }
            Assert.That(differs, "tempers and regrouping change the battle");
        }

        // 10. The two teams are treated alike. Thirty men a side on spots that mirror each other through the middle of the
        // field, and the same spots' men fall on tick 20; swapping the two teams' keys, spot for mirrored spot, swaps the
        // two teams' squads by mood and by mark.
        [Test]
        public void SwappingTheTeamsKeysSwapsTheirTempers()
        {
            const int men = 30;
            var plain = new UnitSpawn[2 * men]; var swapped = new UnitSpawn[2 * men];
            var fallen = new List<ulong>();
            for (int i = 0; i < men; i++)
            {
                // Three loose clumps per team; team 2's spots are team 1's turned half round about (80, 30).
                long x = 10000 + i % 3 * 22000 + AiParts.Permille((ulong)i, 0x51UL) * 9L, z = 6000 + AiParts.Permille((ulong)i, 0x52UL) * 20L;
                ulong low = (ulong)i + 1, high = (ulong)(i + 1 + men);
                plain[i] = Man(low, 1, x, z); plain[men + i] = Man(high, 2, 160000 - x, 60000 - z);
                swapped[i] = Man(high, 1, x, z); swapped[men + i] = Man(low, 2, 160000 - x, 60000 - z);
                if (i % 4 == 0) { fallen.Add(low); fallen.Add(high); }
            }
            int[] seven = { 2, 5, 300, 12000, 15, 0, 0 };
            string ai = Json(seven, Rules(350, 300, 600, 8000), Rules(900, 1000, 100000));
            M6World a = Field(ai, plain, x => Kill(x, 20, fallen.ToArray())), b = Field(ai, swapped, x => Kill(x, 20, fallen.ToArray()));
            To(a, Form); To(b, Form);
            string before = Tempers(a, 1) + " | " + Tempers(a, 2);
            To(a, 3 * Form); To(b, 3 * Form);
            Assert.That(Tempers(a, 1), Is.EqualTo(Tempers(b, 2)));
            Assert.That(Tempers(a, 2), Is.EqualTo(Tempers(b, 1)));
            Assert.That(Tempers(a, 1), Is.Not.EqualTo(Tempers(a, 2)), "the fixture's teams differ, so a swap shows");
            Assert.That(Tempers(a, 1) + " | " + Tempers(a, 2), Is.Not.EqualTo(before), "the falls changed the squads");
            int regrouping = 0;
            foreach (int team in new[] { 1, 2 }) regrouping += int.Parse(Tempers(a, team).Split(' ')[4]);
            Assert.That(regrouping, Is.GreaterThan(0), "squads regroup in the fixture");
            for (ulong key = 1; key <= 2 * men; key++)
            {
                if (a.Dead(key)) continue;
                Assert.That(Mood(a, key), Is.EqualTo(Mood(b, key)), "key " + key);
                Assert.That(Rally(a, key), Is.EqualTo(Rally(b, key)), "key " + key);
            }
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // 11. A battle with tempers and regrouping steps without allocating, on form ticks and between them, while squads
        // lose men, recruit and regroup (twelve of the hundred fall on tick 40, the next form tick is 45).
        [Test]
        public void StepAllocatesZero()
        {
            long[] totals = new long[2]; int regrouping = 0;
            for (int run = 0; run < 2; run++)
            {
                var units = new UnitSpawn[101];
                for (int i = 0; i < 100; i++) units[i] = Man((ulong)i + 1, 1, 21000 + i % 20 * 2000, 20000 + i / 20 * 2000, 3000);
                units[100] = Man(900, 2, 150000, 50000);
                // Twelve members fall (no leader, so nobody is set free): the men in squads drop below the target by three,
                // three skirmishers are taken, and the other squads that lost a man stay short and regroup.
                string ai = Json(Real, Temper, Rules(900, 1000, 100000));
                M6World dry = Field(ai, units, think: 1);
                dry.Step(30);
                var fallen = new List<ulong>();
                for (ulong key = 1; key <= 100 && fallen.Count < 12; key++) if (Band(dry, key) != 0 && Band(dry, key) != key) fallen.Add(key);
                M6World w = Field(ai, units, x => Kill(x, 40, fallen.ToArray()), think: 1);
                w.Step(30);
                AllocationMeter.Stabilize();
                for (int i = 0; i < 45; i++)
                {
                    long start = GC.GetAllocatedBytesForCurrentThread();
                    w.Sim.Step(default);
                    totals[run] += GC.GetAllocatedBytesForCurrentThread() - start;
                }
                Assert.That(w.Dead(fallen[0]) && w.Dead(fallen[11]));
                regrouping = int.Parse(Tempers(w, 1).Split(' ')[4]);
            }
            Assert.That(totals[1], Is.EqualTo(0L));
            Assert.That(regrouping, Is.GreaterThan(0));
        }

        // 11. The questions and the three wrappers allocate nothing.
        [Test]
        public void ThePartsAllocateZero()
        {
            M6World w = Rallying(45000, 12000, 30);
            int[] regroup = Rules(500, 750, 1200);
            M6World moods = ThreeMoods(out ulong rash, out ulong normal, out ulong steady, regroup);
            Squads squads = Sq(w), tempered = Sq(moods);
            IAiDispatch dispatch = squads.Wrap(new NearestDispatch(300, 3));
            IAiEngage engage = tempered.Wrap(new BoundEngage(60, 90, 700, 12000)), going = squads.Wrap(new HoldEngage());
            AiField field = FieldOf(w), other = FieldOf(moods);
            AiUnit member = Thinker(w, 2), leader = Thinker(w, 1), free = Thinker(w, 5), settled = Thinker(w, 4);
            AiUnit[] men = { Thinker(moods, rash), Thinker(moods, rash + 10), Thinker(moods, normal + 10), Thinker(moods, steady + 10, AiNerve.Pinned), Thinker(moods, steady) };
            var hash = new StateHash();
            long total = 0, sum = 0;
            for (int run = 0; run < 3; run++)
            {
                if (run == 1) AllocationMeter.Stabilize();
                long start = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++)
                {
                    sum += dispatch.Pick(in member, field) + dispatch.Pick(in leader, field) + dispatch.Pick(in free, field) + dispatch.Pick(in settled, field);
                    sum += (int)going.Decide(in member, Ahead(member.Here, 20000), field) + (int)going.Decide(in free, Ahead(free.Here, 20000), field);
                    for (int m = 0; m < men.Length; m++)
                    {
                        sum += (int)engage.Decide(in men[m], Ahead(men[m].Here, 20000), other) + (int)engage.Decide(in men[m], Ahead(men[m].Here, 1000), other);
                        sum += (int)engage.Unseen(in men[m], other);
                        sum += (int)tempered.MoodOf(men[m].Id) + (int)tempered.RegroupOf(men[m].Id) + tempered.StrengthPermille(men[m].Id);
                    }
                    sum += (int)squads.RegroupOf(member.Id);
                    squads.WriteState(hash); tempered.WriteState(hash);
                }
                if (run > 0) total += GC.GetAllocatedBytesForCurrentThread() - start;
            }
            Assert.That(total, Is.EqualTo(0L));
            Assert.That(sum, Is.Not.Zero);
        }
#endif
    }
}
