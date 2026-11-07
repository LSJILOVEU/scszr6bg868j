using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 8 on fixture C: O holds only the AI's own order. "End of tick" means read after that tick ran.
    // The unit walks 0.1 m per tick (L4.Speed), under the 0.25 m arrival distance.
    public sealed class L4SquadTests
    {
        private const int Think = 10, N = 300;

        [Test]
        public void FixtureC_Geometry()
        {
            Assert.That(GuardSlots.Build(L4.CCenter, L4.CPoint.Radius).Length, Is.EqualTo(1));
            M6World w = L4.C(Think, L4.Ticks(N), false);
            TerrainTables t = L4.Terrain(L4.Map(L4.CWalls));
            Assert.That(L4.Moves(w).CanStandAt(L4.CCenter), Is.True);
            Assert.That(L4.Reachable(t, L4.CStart, L4.CCenter), Is.True);
            Assert.That(L4.Moves(w).CanStandAt(L4.CX), Is.True);
            Assert.That(L4.Reachable(t, L4.CStart, L4.CX), Is.False);
        }

        // Steps to the end of t0 (the AI's first order, to the centre) and checks t0 + 3 is not a thinking tick.
        private static (M6World w, L4.Calls calls, long t0) Start(bool standing)
        {
            M6World w = L4.C(Think, L4.Ticks(N), standing);
            L4.Calls calls = L4.Observe(w);
            long t0 = L4.FirstThink(w, Think);
            Assert.That((t0 + 3 + (long)(L4.Id(w).Value % Think)) % Think, Is.Not.EqualTo(0), "t0 + 3 is not a thinking tick");
            w.Step((int)t0);
            Assert.That(calls.TicksTo(L4.CCenter), Is.EqualTo(new List<long> { t0 }));
            Assert.That(L4.Memory(w).ordered, Is.EqualTo(0));
            w.Step(2);
            return (w, calls, t0);
        }

        // (a) No squad command: O is the centre from t0 until the AI stops the unit on arrival, then -1; F stays -1.
        [Test]
        public void A_OwnOrder_HeldUntilTheArrivalStop()
        {
            M6World w = L4.C(Think, L4.Ticks(N), false);
            L4.Calls calls = L4.Observe(w);
            long t0 = L4.FirstThink(w, Think);
            w.Step((int)t0);
            int ticks = 0;
            while (calls.Stops == 0 && ticks < 600)
            {
                Assert.That(L4.Memory(w), Is.EqualTo((0, -1, 0L)), "tick " + (t0 + ticks));
                w.Step(); ticks++;
            }
            Assert.That(calls.Stops, Is.EqualTo(1), "the arrival stop ran");
            Assert.That(L4.Memory(w).ordered, Is.EqualTo(-1), "O cleared on the stop tick");
            Assert.That(L4.Memory(w).failed, Is.EqualTo(-1));
            Assert.That(ticks, Is.InRange(90, 200), "walked about 10 m at 0.1 m per tick");
        }

        private static void MoveToX(bool standing)
        {
            var (w, calls, t0) = Start(standing);
            w.Sim.Step(L4.Squad(SquadCommandKind.MoveTo, 1, L4.CX));
            IMoveOrders moves = calls.Inner; StableId id = L4.Id(w);
            Assert.That(moves.HasFailed(id), Is.True, "the squad order failed on the spot");
            Assert.That(moves.IsCommanded(id), Is.False, "the failed move left no standing order");
            Assert.That(L4.Memory(w).ordered, Is.EqualTo(-1), "end of t0 + 3");
            w.Step();
            Assert.That(L4.Memory(w).failed, Is.EqualTo(-1), "end of t0 + 4: the squad failure is not the AI's");
            w.Step(6);
            Assert.That(calls.TicksTo(L4.CCenter), Is.EqualTo(new List<long> { t0, t0 + 10 }), "the AI orders the centre again at t0 + 10");
        }

        // (b), (c) A squad move to the walled cell X fails at t0 + 3, with and without standing orders.
        [Test] public void B_Standing_FailedSquadMove_IsNotRecorded() => MoveToX(true);
        [Test] public void C_NoStanding_FailedSquadMove_IsNotRecorded() => MoveToX(false);

        // (d) A squad move to the centre itself (the same target as O) still clears O.
        [Test]
        public void D_NoStanding_SquadMoveToTheSameSlot_ClearsO()
        {
            var (w, _, _) = Start(false);
            w.Sim.Step(L4.Squad(SquadCommandKind.MoveTo, 1, L4.CCenter));
            Assert.That(L4.Moves(w).TryGetOrder(L4.Id(w), out SimVec3 target), Is.True, "the squad order took");
            Assert.That((target.X.Raw, target.Z.Raw), Is.EqualTo((L4.CCenter.X.Raw, L4.CCenter.Z.Raw)));
            Assert.That(L4.Memory(w).ordered, Is.EqualTo(-1));
        }

        // (e) A hold leaves a standing order: the AI skips the unit and that clears O.
        [Test]
        public void E_Standing_Hold_ClearsO()
        {
            var (w, _, _) = Start(true);
            w.Sim.Step(L4.Squad(SquadCommandKind.Hold, 1, default));
            Assert.That(L4.Moves(w).IsCommanded(L4.Id(w)), Is.True);
            Assert.That(L4.Memory(w).ordered, Is.EqualTo(-1));
        }

        // A squad hold without standing orders changes nothing for the unit, so O stays: only MoveTo clears it.
        [Test]
        public void G_NoStanding_Hold_KeepsO()
        {
            var (w, _, _) = Start(false);
            w.Sim.Step(L4.Squad(SquadCommandKind.Hold, 1, default));
            Assert.That(L4.Moves(w).IsCommanded(L4.Id(w)), Is.False);
            Assert.That(L4.Memory(w).ordered, Is.EqualTo(0));
        }

        // (f) A squad move for another squad number does not reach the unit: O stays.
        [Test]
        public void F_NoStanding_OtherSquad_KeepsO()
        {
            var (w, _, _) = Start(false);
            w.Sim.Step(L4.Squad(SquadCommandKind.MoveTo, 2, L4.CX));
            Assert.That(L4.Memory(w).ordered, Is.EqualTo(0));
        }
    }
}
