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
    // AI-P5a in whole battles: squads together with the other AI parts, the two teams treated alike, and no allocation.
    public sealed partial class AiP5aTests
    {
        private const string Split = "\"holdSharePermille\": 300, \"attackSpreadPoints\": 3";
        private const string Scatter = "\"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"scatter\", \"scatterMm\": 6000";
        private const string Roles = "\"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"assault-roles\", "
            + "\"frontPermille\": 425, \"flankPermille\": 375, \"rearPermille\": 200, \"frontArcMm\": 14000, \"flankSideMm\": 35000, "
            + "\"rearOutMm\": 12000, \"flankMinMm\": 60000, \"anchorMm\": 6000, \"arriveMm\": 3000";
        private const string Bound = "\"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000, "
            + "\"pressureDeathRadiusMm\": 3000, \"pressureDeathShock\": 2000, \"pressureNearRadiusMm\": 3000, \"pressureNearShock\": 1000, \"pressureDecayPermille\": 12, "
            + "\"pressureBrokenAt\": 7000, \"pressureRecoverAt\": 1000, \"pressurePinnedAt\": 2000, \"pressureSpreadPermille\": 500, \"pressureBreakSharePermille\": 300, "
            + "\"coverRadiusMm\": 9000, \"coverStepMm\": 1000, \"coverDepthMm\": 2000";
        // The squad numbers of the replay setups: sizes 4..9, three in ten skirmish, reach 30 m, spread 1.5..6 m.
        private static readonly int[] Real = { 4, 9, 300, 30000, 15, 1500, 6000 };
        private static readonly (int, int, int, int)[] Yards = { (64, 46, 1, 8), (96, 46, 1, 8), (66, 38, 1, 6), (94, 56, 1, 6), (40, 46, 1, 8), (120, 46, 1, 8) };

        private static string Join(params string[] parts)
        {
            var kept = new List<string>();
            foreach (string part in parts) if (!string.IsNullOrEmpty(part)) kept.Add(part);
            return string.Join(", ", kept);
        }
        // 150 against 150 with rifles on a 160 m x 100 m field with six walls; the reserves come in as men fall.
        private static M6World Battle(string ai, bool? fair, ulong seed = 41) =>
            new M6World(FairEFixture.Recipe("control", seed, fair).WithAiRules(Pack(ai)), WcMapWriter.Write(M3TestWorld.Boxes(160, 100, Yards)));

        // 6. Squads with the nearest dispatch (hold share and attack spread on) and each placement, under hold and under
        // bound with pressure and cover, with and without fair decisions: the same battle twice gives the same hash on
        // every tick, and a battle restored half-way goes on like the one that was not. The cells of the table that are
        // not listed here are not tested.
        [TestCase("", Bound, null)]
        [TestCase(Scatter, Bound, null)]
        [TestCase(Roles, Bound, null)]
        [TestCase(Roles, Bound, true)]
        [TestCase("", "", null)]
        public void Battle_RepeatAndRestoreWithSquads(string placement, string engage, bool? fair)
        {
            string ai = Json(Real, Join(Split, placement, engage));
            M6World first = Battle(ai, fair), repeat = Battle(ai, fair), plain = Battle("{ " + Join(Split, placement, engage) + " }", fair);
            bool differs = false; int most = 0, fewest = int.MaxValue;
            for (int tick = 0; tick < 450; tick++)
            {
                first.Step(); repeat.Step(); plain.Step();
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
                differs |= WithoutSquads(first) != WithoutSquads(plain);
                if (tick >= Form) { most = Math.Max(most, Sq(first).SquadCount); fewest = Math.Min(fewest, Sq(first).SquadCount); }
            }
            Assert.That(differs, "squads change the battle");
            Assert.That(Sq(first).MemberCount, Is.GreaterThan(20));
            Assert.That(most, Is.GreaterThan(fewest), "squads came and went");
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();
            M6World restored = Battle(ai, fair);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            Assert.That(Save(restored), Is.EqualTo(Save(first)));
            for (int tick = 0; tick < 450 && !first.Sim.IsFinished; tick++)
            {
                first.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
        }
        // Where every unit stands: the part of the battle a world without the squads system has too.
        private static ulong WithoutSquads(M6World w)
        {
            var hash = new StateHash();
            foreach (UnitSpawn unit in w.Recipe.Units)
            {
                if (!w.Has<Position>(unit.Key)) continue;
                SimVec3 at = At(w, unit.Key); hash.Add(unit.Key); hash.Add(at.X.Raw); hash.Add(at.Z.Raw);
            }
            return hash.Value;
        }

        // What a team's squads look like, without names: the squads (size:members, sorted) and the men in squads.
        private static string Shape(M6World w, int team)
        {
            var members = new Dictionary<ulong, int>();
            int bodies = 0;
            foreach (UnitSpawn unit in w.Recipe.Units)
            {
                if (unit.Team != team) continue;
                ulong band = Sq(w).SquadOf(Id(w, unit.Key));
                if (band == 0) continue;
                bodies++;
                if (!members.ContainsKey(band)) members[band] = 0;
                if (band != Stable(unit.Key)) members[band]++;
            }
            var squads = new List<string>();
            foreach (KeyValuePair<ulong, int> pair in members) squads.Add(Sq(w).SizeOf(new StableId(pair.Key)) + ":" + pair.Value);
            squads.Sort(StringComparer.Ordinal);
            return bodies + " in " + squads.Count + " [" + string.Join(" ", squads) + "]";
        }

        // 7. The two teams are treated alike. Thirty men a side on spots that mirror each other through the middle of the
        // field; swapping the two teams' keys, spot for mirrored spot, swaps what the two teams' squads look like. A rule
        // that went by the team number or by which key is the larger would show here.
        [Test]
        public void SwappingTheTeamsKeysSwapsTheirSquads()
        {
            const int men = 30;
            var plain = new UnitSpawn[2 * men]; var swapped = new UnitSpawn[2 * men];
            for (int i = 0; i < men; i++)
            {
                // Three loose clumps per team; team 2's spots are team 1's turned half round about (80, 30).
                long x = 10000 + i % 3 * 22000 + AiParts.Permille((ulong)i, 0x51UL) * 9L, z = 6000 + AiParts.Permille((ulong)i, 0x52UL) * 20L;
                ulong low = (ulong)i + 1, high = (ulong)(i + 1 + men);
                plain[i] = Man(low, 1, 0, 0, xMm: x, zMm: z); plain[men + i] = Man(high, 2, 0, 0, xMm: 160000 - x, zMm: 60000 - z);
                swapped[i] = Man(high, 1, 0, 0, xMm: x, zMm: z); swapped[men + i] = Man(low, 2, 0, 0, xMm: 160000 - x, zMm: 60000 - z);
            }
            string ai = Json(With(Min, 2, Max, 5, Radius, 12000));
            M6World a = Field(ai, plain), b = Field(ai, swapped);
            To(a, 2 * Form); To(b, 2 * Form);
            Assert.That(Shape(a, 1), Is.EqualTo(Shape(b, 2)));
            Assert.That(Shape(a, 2), Is.EqualTo(Shape(b, 1)));
            Assert.That(Shape(a, 1), Is.Not.EqualTo(Shape(a, 2)), "the fixture's teams differ, so a swap shows");
            // Man for man: the leader of each key's squad is the same key in both worlds.
            for (ulong key = 1; key <= 2 * men; key++)
                Assert.That(Sq(a).SquadOf(new StableId(Stable(key))), Is.EqualTo(Sq(b).SquadOf(new StableId(Stable(key)))), "key " + key);
            Assert.That(Bodies(a, 1), Is.InRange(2, 21));
            Assert.That(Sq(a).SquadCount, Is.GreaterThan(3));
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // 8. A battle with squads steps without allocating, on form ticks and between them, while squads lose men, dissolve
        // and form again (twelve of the hundred die at tick 40, the next form tick is 45).
        [Test]
        public void StepAllocatesZero()
        {
            long[] totals = new long[2]; int formed = 0;
            for (int run = 0; run < 2; run++)
            {
                var units = new UnitSpawn[101];
                for (int i = 0; i < 100; i++) units[i] = Man((ulong)i + 1, 1, 21 + i % 20 * 2, 20 + i / 20 * 2, speed: 3000);
                units[100] = Man(900, 2, 150, 50);
                M6World w = Field(Json(Real), units, x => Kill(x, 40, 3, 11, 12, 13, 14, 27, 28, 41, 55, 56, 78, 93), think: 1);
                w.Step(30);
                int before = Sq(w).SquadCount;
                AllocationMeter.Stabilize();
                for (int i = 0; i < 45; i++)
                {
                    long start = GC.GetAllocatedBytesForCurrentThread();
                    w.Sim.Step(default);
                    totals[run] += GC.GetAllocatedBytesForCurrentThread() - start;
                }
                Assert.That(before, Is.GreaterThan(5));
                formed = Sq(w).SquadCount;
                Assert.That(w.Dead(3) && w.Dead(93));
            }
            Assert.That(totals[1], Is.EqualTo(0L));
            Assert.That(formed, Is.GreaterThan(5));
        }

        // 8. Asking for the leader, the offset, both wrappers and the hash allocate nothing.
        [Test]
        public void ThePartsAllocateZero()
        {
            M6World w = Ten(out ulong[] k, With(SpreadMin, 1500, SpreadMax, 6000));
            To(w, Form);
            Squads squads = Sq(w);
            var dispatch = squads.Wrap(new NearestDispatch(300, 3));
            var placement = squads.Wrap(new ScatterPlacement(6000));
            var waypoint = (IAiWaypoint)squads.Wrap(new AssaultRolesPlacement(425, 375, 200, 14000, 35000, 12000, 60000, 6000, 3000));
            AiField field = FieldOf(w);
            AiUnit member = Thinker(w, k[5]), leader = Thinker(w, k[0]), free = Thinker(w, k[9]);
            var hash = new StateHash();
            long total = 0, sum = 0;
            for (int run = 0; run < 3; run++)
            {
                if (run == 1) AllocationMeter.Stabilize();
                long start = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++)
                {
                    if (squads.TryLeader(member.Id, out StableId found, out SimVec3 here)) sum += here.X.Raw + (long)(found.Value & 1);
                    sum += squads.Offset(member.Id, leader.Id).X.Raw;
                    sum += dispatch.Pick(in member, field) + dispatch.Pick(in leader, field) + dispatch.Pick(in free, field);
                    sum += placement.Anchor(in member, 0, field).X.Raw + placement.Anchor(in leader, 0, field).X.Raw + placement.Anchor(in free, 0, field).X.Raw;
                    if (waypoint.TryWaypoint(in member, 0, field, out SimVec3 via)) sum += via.X.Raw;
                    squads.WriteState(hash);
                }
                if (run > 0) total += GC.GetAllocatedBytesForCurrentThread() - start;
            }
            Assert.That(total, Is.EqualTo(0L));
            Assert.That(sum, Is.Not.Zero);
        }
#endif
    }
}
