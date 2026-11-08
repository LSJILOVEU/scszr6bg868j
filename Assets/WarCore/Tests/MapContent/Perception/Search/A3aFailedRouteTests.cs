using System.Collections.Generic;
using System.Reflection;
using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 6 and 12: B stands in cell X = (30, 14), walled in on all sides (standable, unreachable, out of A's
    // sight). After tick 5 the test plants A's failed-slot record O = 0 (a real guard slot) and a memory of B (P = X,
    // S = 5). From A's first thinking tick (6) every walk to X fails on the spot.
    public sealed class A3aFailedRouteTests
    {
        private static readonly SimVec3 X = A2.Cell(30, 14);
        private const long Planted = 5;

        private static M6World World(out L4.Calls calls)
        {
            M6World w = A3a.World(X, A3a.Ai(60, 60, 300), L4.Join(A2.DWalls, L4.Ring(30, 14)), conceal: 500);
            calls = L4.Observe(w);
            Assert.That(A3a.FirstThink(w, A3a.KeyA), Is.EqualTo(6));
            return w;
        }

        private static (int ordered, int failed, long at) Query(M6World w) =>
            ((int, int, long))typeof(FailedSlotMemory).GetMethod("Query", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(w.System<FailedSlotMemory>(), new object[] { A3a.Id(w, A3a.KeyA) });

        private static void Plant(M6World w, L4.Calls calls)
        {
            // Slot 0 is a real canonical guard slot: point 1's first slot (the AI's own GuardSlots).
            object slots = typeof(SimpleAiSystem).GetField("slots", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<SimpleAiSystem>());
            Type type = slots.GetType();
            int start = (int)type.GetMethod("Start").Invoke(slots, new object[] { 0 });
            Assert.That((int)type.GetMethod("Canonical").Invoke(slots, new object[] { start }), Is.EqualTo(0), "slot 0 is canonical");
            Assert.That(A3a.IsSlot(w, (SimVec3)type.GetMethod("At").Invoke(slots, new object[] { start })), Is.True);
            Assert.That(Query(w), Is.EqualTo((-1, -1, 0L)), "clean before planting");
            Assert.That(calls.Inner.HasFailed(A3a.Id(w, A3a.KeyA)), Is.False);
            typeof(FailedSlotMemory).GetMethod("Ordered", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(w.System<FailedSlotMemory>(), new object[] { A3a.Id(w, A3a.KeyA), 0 });
            typeof(TargetMemory).GetMethod("Saw", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(w.System<TargetMemory>(), new object[] { A3a.Id(w, A3a.KeyA), w.Unit(A3a.KeyB), Planted });
            Assert.That(Query(w).ordered, Is.EqualTo(0), "O planted");
        }

        // B holds from tick 1 and stays under the squad hold for the whole run.
        private static void Step(M6World w, long t)
        {
            w.Sim.Step(A3a.Drive(w, t == 1 ? A3a.Command(SquadCommandKind.Hold, 2) : default));
            Assert.That(w.Has<SquadStanding>(A3a.KeyB) && w.Read<SquadStanding>(A3a.KeyB).Kind == StandingKind.Hold, Is.True, "B holds, tick " + t);
        }

        [Test]
        public void X_IsStandableAndUnreachable()
        {
            M6World w = World(out _);
            Assert.That(L4.Moves(w).CanStandAt(X), Is.True);
            TerrainTables t = TerrainDerivation.Derive(A3a.Map(L4.Join(A2.DWalls, L4.Ring(30, 14))), A3a.Load(A3a.PackJson(null)));
            Assert.That(L4.Reachable(t, A3a.AAt, X), Is.False);
            Assert.That(L4.Reachable(t, A3a.AAt, A3a.Point2), Is.True, "control: point 2 is reachable");
        }

        // 6: the failed walk to P is not booked on the slot the record held.
        [Test]
        public void FailedWalk_DoesNotBlameASlot()
        {
            M6World w = World(out L4.Calls calls);
            for (long t = 1; t <= Planted; t++) Step(w, t);
            Plant(w, calls);
            Step(w, 6);
            Assert.That(calls.TicksTo(X), Is.EqualTo(new List<long> { 6 }));
            Assert.That(calls.Inner.HasFailed(A3a.Id(w, A3a.KeyA)), Is.True, "the walk failed on the spot");
            Step(w, 7);
            Assert.That(Query(w), Is.EqualTo((-1, -1, 0L)), "O cleared, F untouched");
        }

        // 12: ordered again on every thinking tick while the memory lasts (S + M = 65), then back to the points on 66.
        [Test]
        public void Unreachable_RetriedUntilTheMemoryEnds()
        {
            M6World w = World(out L4.Calls calls);
            for (long t = 1; t <= Planted; t++) Step(w, t);
            Plant(w, calls);
            for (long t = 6; t <= 66; t++)
            {
                Step(w, t);
                Assert.That(Query(w).failed, Is.EqualTo(-1), "no slot blamed, tick " + t);
                Assert.That(A3a.SearchCount(w), Is.EqualTo(0), "tick " + t);
                if (t <= 64) Assert.That(A3a.Recall(w, t, out _, out _, out long seen) && seen == Planted, Is.True, "B never seen, tick " + t);
            }
            Assert.That(calls.TicksTo(X), Is.EqualTo(new List<long> { 6, 16, 26, 36, 46, 56 }));
            Assert.That(A3a.Recall(w, 65, out _, out _, out _), Is.False);
            Assert.That(calls.Orders[calls.Orders.Count - 1].tick, Is.EqualTo(66));
            Assert.That(A3a.Same(calls.Orders[calls.Orders.Count - 1].target, A3a.Point2), Is.True);
        }
    }
}
