using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    // The failed-slot memory on fixtures A and B (L4Fixture): skip windows, the off switch, save and restore, liveness.
    public sealed class L4MemoryTests
    {
        private static List<long> Expect(long t0, int step, int count)
        {
            var list = new List<long>();
            for (int k = 0; k < count; k++) list.Add(t0 + step * (long)k);
            return list;
        }

        // Acceptance 1: N = 300. The order at t0 fails on the spot, the memory records it at t0 + 1, the slot is skipped
        // through t0 + 300, and the next thinking tick t0 + 310 orders again: t0, t0+310, t0+620, t0+930 (t0+1240 > 1200).
        [Test]
        public void FixtureA_N300_OrdersTheWalledSlotFourTimes()
        {
            M6World w = L4.A(10, L4.Ticks(300));
            L4.Calls calls = L4.Observe(w);
            long t0 = L4.FirstThink(w, 10);
            w.Step(1200);
            Assert.That(t0, Is.InRange(1, 10));
            Assert.That(calls.TicksTo(L4.ACenter), Is.EqualTo(new List<long> { t0, t0 + 310, t0 + 620, t0 + 930 }));
            Assert.That(calls.Orders.Count, Is.EqualTo(4));
        }

        // Acceptance 2: N = 0 behaves as no ai key (recorded on the base code in L4RecordTests).
        [Test]
        public void FixtureA_N0_OrdersTheWalledSlotEveryThinkingTick()
        {
            M6World w = L4.A(10, L4.Ticks(0));
            L4.Calls calls = L4.Observe(w);
            long t0 = L4.FirstThink(w, 10);
            w.Step(1200);
            Assert.That(calls.TicksTo(L4.ACenter), Is.EqualTo(Expect(t0, 10, 120)));
            Assert.That(calls.Orders.Count, Is.EqualTo(120));
        }

        // Acceptance 3(a): N = 0 written out hashes exactly as the recorded run without the key.
        [Test]
        public void FixtureA_N0_HashesAsTheRecordWithoutTheKey()
        {
            var plain = new List<ulong>(); var zero = new List<ulong>();
            ulong a = L4.HashRun(L4.A(10, null), 600, plain), b = L4.HashRun(L4.A(10, L4.Ticks(0)), 600, zero);
            Assert.That(a, Is.EqualTo(L4RecordTests.AHashFold600));
            Assert.That(b, Is.EqualTo(L4RecordTests.AHashFold600));
            for (int t = 0; t <= 600; t++) Assert.That(zero[t], Is.EqualTo(plain[t]), "tick " + t);
        }

        // Acceptance 3(b): only a terrain world with N > 0 registers battle.ai-memory, at order 9 in the request phase.
        [Test]
        public void OnlyATerrainWorldWithPositiveTicksRegistersTheMemory()
        {
            Assert.That(L4.Systems(L4.A(10, null)), Does.Not.Contain("battle.ai-memory"));
            Assert.That(L4.Systems(L4.A(10, L4.Ticks(0))), Does.Not.Contain("battle.ai-memory"));
            Assert.That(L4.Systems(L4.A(10, L4.Ticks(300), terrain: false)), Does.Not.Contain("battle.ai-memory"));
            Assert.That(L4.Systems(L4.A(10, L4.Ticks(300))), Does.Contain("battle.reinforce|0|8;battle.ai-memory|0|9;interim.orders|0|10;battle.ai|0|12;"));
        }

        // Acceptance 4: N = 5, thinking every tick. Order at 1, fail recorded at 2, skipped while t - 2 < 5, ordered at 7:
        // period N + 1.
        [Test]
        public void FixtureA_N5_ThinkEveryTick_SkipsExactlyNTicks()
        {
            M6World w = L4.A(1, L4.Ticks(5));
            L4.Calls calls = L4.Observe(w);
            w.Step(6);
            Assert.That(calls.TicksTo(L4.ACenter), Is.EqualTo(new List<long> { 1 }), "tick 6 does not order");
            w.Step();
            Assert.That(calls.TicksTo(L4.ACenter), Is.EqualTo(new List<long> { 1, 7 }), "tick 7 orders");
            w.Step(13);
            Assert.That(calls.TicksTo(L4.ACenter), Is.EqualTo(new List<long> { 1, 7, 13, 19 }));
        }

        private static ulong[] Uninterrupted(int last)
        {
            var w = L4.A(10, L4.Ticks(300)); var list = new List<ulong>();
            L4.HashRun(w, last, list); return list.ToArray();
        }

        // Acceptance 5: save at the end of tick t0 (failure not yet recorded) or at tick 150 (recorded), restore into a new
        // world (both allocation orders); every later hash matches the uninterrupted run and the orders keep their ticks.
        [TestCase(false, false), TestCase(false, true), TestCase(true, false), TestCase(true, true)]
        public void FixtureA_SaveAndRestore_KeepsTheMemory(bool at150, bool reverse)
        {
            ulong[] expected = Uninterrupted(1200);
            M6World a = L4.A(10, L4.Ticks(300));
            long t0 = L4.FirstThink(a, 10);
            long save = at150 ? 150 : t0;
            a.Step((int)save);
            if (at150) Assert.That(L4.Memory(a), Is.EqualTo((-1, 0, t0 + 1)));
            else Assert.That(L4.Memory(a), Is.EqualTo((0, -1, 0L)));
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            M6World b = L4.A(10, L4.Ticks(300), reverse: reverse);
            b.Sim.RestoreRetainedSnapshot(snapshot);
            L4.Calls calls = L4.Observe(b);
            Assert.That(b.Hash, Is.EqualTo(expected[save]), "restored hash");
            for (long t = save + 1; t <= 1200; t++)
            {
                b.Step();
                if (t == t0 + 1) Assert.That(L4.Memory(b), Is.EqualTo((-1, 0, t0 + 1)), "failure recorded after restore");
                Assert.That(b.Hash, Is.EqualTo(expected[t]), "tick " + t);
            }
            Assert.That(calls.TicksTo(L4.ACenter), Is.EqualTo(new List<long> { t0 + 310, t0 + 620, t0 + 930 }));
        }

        [Test]
        public void FixtureA_RestoreIntoAnotherLength_Throws()
        {
            M6World a = L4.A(10, L4.Ticks(300));
            a.Step(150);
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            var other = Assert.Throws<SimSnapshotException>(() => L4.A(10, L4.Ticks(200)).Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(other.Message, Is.EqualTo("AI failed-slot memory length differs."));
            Assert.Throws(Is.InstanceOf<InvalidOperationException>(), () => L4.A(10, L4.Ticks(0)).Sim.RestoreRetainedSnapshot(snapshot));
        }

        // Choose's rule from the unit's start: nearest standable slot, ties to the lower number, optionally without one slot.
        private static int Preferred(M6World w, SimVec3 from, int without)
        {
            SimVec3[] slots = GuardSlots.Build(L4.BCenter, L4.BPoint.Radius);
            int chosen = -1; SimWide best = default;
            for (int i = 0; i < slots.Length; i++)
            {
                if (i == without || !L4.Moves(w).CanStandAt(slots[i])) continue;
                SimWide d = CombatSystem.Horizontal(from - slots[i]).LengthSquared;
                if (chosen < 0 || d < best) { chosen = i; best = d; }
            }
            return chosen;
        }

        // Fixture B, standable slots by horizontal distance from the start (10.5, 10.5):
        //   17 (18.5, 10.5) 8.00 m - walled in (S1); 0 (20.5, 10.5) 10.00 m (S2); 8 (20.5, 12.232) and 14 (20.5, 8.768)
        //   10.15 m; 2 (21.0, 11.366) and 4 (21.0, 9.634) 10.54 m; 3 (21.5, 10.5) 11.00 m; 9 (21.5, 12.232) and 13 (21.5, 8.768)
        //   11.14 m; 10 (22.0, 11.366) and 12 (22.0, 9.634) 11.53 m; 11 (22.5, 10.5) 12.00 m.
        //   Not standable (walled or crowded by the ring): 1, 5, 6, 7, 15, 16, 18.
        [Test]
        public void FixtureB_Geometry()
        {
            SimVec3[] slots = GuardSlots.Build(L4.BCenter, L4.BPoint.Radius);
            Assert.That(slots.Length, Is.EqualTo(19));
            M6World w = L4.B(10, null);
            TerrainTables t = L4.Terrain(L4.Map(L4.BWalls));
            int s1 = Preferred(w, L4.BStart, -1), s2 = Preferred(w, L4.BStart, s1);
            Assert.That(s1, Is.EqualTo(17)); Assert.That(s2, Is.EqualTo(0));
            Assert.That(L4.Moves(w).CanStandAt(slots[s1]), Is.True);
            Assert.That(L4.Reachable(t, L4.BStart, slots[s1]), Is.False);
            Assert.That(L4.Moves(w).CanStandAt(slots[s2]), Is.True);
            Assert.That(L4.Reachable(t, L4.BStart, slots[s2]), Is.True);
            int[] standable = { 0, 2, 3, 4, 8, 9, 10, 11, 12, 13, 14, 17 };
            for (int i = 0; i < slots.Length; i++)
                Assert.That(L4.Moves(w).CanStandAt(slots[i]), Is.EqualTo(Array.IndexOf(standable, i) >= 0), "slot " + i);
        }

        // Acceptance 6: N = 300 tries S1 once, turns to S2 on the next thinking tick and gets there; N = 0 never moves.
        [Test]
        public void FixtureB_N300_TurnsToTheNextSlotAndArrives()
        {
            SimVec3[] slots = GuardSlots.Build(L4.BCenter, L4.BPoint.Radius);
            M6World w = L4.B(10, L4.Ticks(300));
            L4.Calls calls = L4.Observe(w);
            long t0 = L4.FirstThink(w, 10);
            w.Step(1200);
            Assert.That(calls.TicksTo(slots[17]), Is.EqualTo(new List<long> { t0 }));
            Assert.That(calls.TicksTo(slots[0]), Is.EqualTo(new List<long> { t0 + 10 }));
            SimVec3 at = w.Read<Position>(L4.Unit).Value;
            SimFix quarter = SimFix.Ratio(1, 4);
            Assert.That(CombatSystem.Horizontal(at - slots[0]).LengthSquared <= new SimVec3(quarter, default, default).LengthSquared, Is.True, "within 0.25 m of S2");
            // |start - S2| = 10 m, so the unit moved at least 9.75 m.
            Assert.That(CombatSystem.Horizontal(at - L4.BStart).LengthSquared >= new SimVec3(SimFix.FromInt(10) - quarter, default, default).LengthSquared, Is.True, "moved >= 9.75 m");
        }

        [Test]
        public void FixtureB_N0_TriesS1EveryThinkingTickAndNeverMoves()
        {
            SimVec3[] slots = GuardSlots.Build(L4.BCenter, L4.BPoint.Radius);
            M6World w = L4.B(10, L4.Ticks(0));
            L4.Calls calls = L4.Observe(w);
            long t0 = L4.FirstThink(w, 10);
            w.Step(1200);
            Assert.That(calls.TicksTo(slots[17]), Is.EqualTo(Expect(t0, 10, 120)));
            SimVec3 at = w.Read<Position>(L4.Unit).Value;
            Assert.That((at.X.Raw, at.Z.Raw), Is.EqualTo((L4.BStart.X.Raw, L4.BStart.Z.Raw)));
        }
    }
}
