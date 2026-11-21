using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Stance.Stance1Worlds;

namespace WarCore.Tests.Stance
{
    // STANCE-1 S3 (a world without stances is untouched), S4 (the change state machine) and S10 (snapshots, determinism).
    public sealed class Stance1StateTests
    {
        // Two unarmed soldiers 30 m apart on the wall-free map, so nobody dies.
        private static UnitSpawn[] Pair() => new[] { Soldier(1, 1, 5000, 8000, 1, -1), Soldier(2, 2, 35000, 8000, 2, -1) };
        private static SimSession Stanced(UnitSpawn[] units = null) => World(Pack(), units ?? Pair(), Map());

        // ---- S3 ----

        [Test]
        public void S3_AWorldWithoutStancesHasNoneOfIt()
        {
            SimSession s = World(Pack("rwr-field"), Pair(), Map());
            Assert.That(s.Stances, Is.Null);
            Assert.That(s.Gate, Is.Null);
            Assert.That(CombatOf(s).Stances, Is.Null);
            Assert.Throws<InvalidOperationException>(() => SimOf(s).Entities.Resolve<Posture>());
            Assert.That(s.TryStance(1, 1), Is.False);
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(0, -1, 0)));
            s.StepOnce();
            Assert.That(s.Current.Entities[0].Stance, Is.EqualTo(0));
        }

        // The same recipe on the stance layer differs only by the stance parts: the field world's hash is not the stance one's,
        // and the field world steps exactly as a second field world.
        [Test]
        public void S3_TheFieldWorldIsDeterministicAndNotTheStanceWorld()
        {
            SimSession a = World(Pack("rwr-field"), Pair(), Map()), b = World(Pack("rwr-field"), Pair(), Map()), c = Stanced();
            for (int i = 0; i < 30; i++) { a.StepOnce(); b.StepOnce(); c.StepOnce(); }
            Assert.That(b.StateHash, Is.EqualTo(a.StateHash));
            Assert.That(c.StateHash, Is.Not.EqualTo(a.StateHash));
        }

        // ---- S4 ----

        private static void ExpectLevelsAfterEachStep(SimSession s, ulong key, long due, int before, int after)
        {
            while (s.Tick < due + 2)
            {
                s.StepOnce();
                Assert.That(s.StanceOf(key).Level, Is.EqualTo(s.Tick < due ? before : after), "after the step of tick " + s.Tick);
            }
        }

        [Test]
        public void S4_StandToCrouchTakesSixTicksCountedFromTheNextTick()
        {
            SimSession s = Stanced();
            StepTo(s, 10);
            bool started = false;
            Assert.DoesNotThrow(() => started = s.TryStance(1, 1));
            Assert.That(started, Is.True);
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(0, 1, 17)));
            ExpectLevelsAfterEachStep(s, 1, 17, 0, 1);
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(1, -1, 0)));
        }

        [TestCase(0, 2, 21)]
        [TestCase(2, 1, 15)]
        [TestCase(1, 0, 6)]
        [TestCase(2, 0, 21)]
        [TestCase(1, 2, 15)]
        [TestCase(0, 1, 6)]
        public void S4_CostsAddUpAcrossLevels(int from, int to, int ticks)
        {
            SimSession s = Stanced();
            if (from != 0) Force(s, 1, from);
            StepTo(s, 3);
            Assert.That(s.Stances.Cost(from, to), Is.EqualTo(ticks));
            Assert.That(s.TryStance(1, to), Is.True);
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(from, to, 4 + ticks)));
            ExpectLevelsAfterEachStep(s, 1, 4 + ticks, from, to);
        }

        [Test]
        public void S4_RefusalsWriteNothing()
        {
            SimSession s = Stanced();
            s.StepOnce();
            Assert.That(s.TryStance(1, 0), Is.False, "same level");
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(0, -1, 0)));
            Assert.That(s.TryStance(1, 2), Is.True);
            StanceView changing = s.StanceOf(1);
            ulong hash = s.StateHash;
            Assert.That(s.TryStance(1, 1), Is.False, "changing");
            Assert.That(s.TryStance(1, 0), Is.False, "changing back");
            Assert.That(s.StanceOf(1), Is.EqualTo(changing));
            Assert.That(s.StateHash, Is.EqualTo(hash));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.TryStance(2, 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.TryStance(2, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Stances.ForceStance(Unit(s, 2), 3));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Stances.ForceStance(Unit(s, 2), -1));
            Assert.DoesNotThrow(() => s.TryStance(2, 2));
        }

        [Test]
        public void S4_ADeadUnitIsRefused()
        {
            SimSession s = Stanced();
            Kill(s, 2, 2);
            StepTo(s, 2);
            Assert.That(s.TryStance(2, 1), Is.False);
            Assert.That(s.Stances.ForceStance(Unit(s, 2), 1), Is.False);
            Assert.That(s.StanceOf(2), Is.EqualTo(new StanceView(0, -1, 0)));
            Assert.That(s.TryStance(1, 1), Is.True);
        }

        // Dying in the middle of a change is no error; the change still completes on the body, and a snapshot round-trips.
        [Test]
        public void S4_S10_DyingWhileChanging()
        {
            SimSession s = Stanced();
            Kill(s, 4, 1);
            Assert.That(s.TryStance(1, 2), Is.True);
            Assert.DoesNotThrow(() => StepTo(s, 30));
            Assert.That(s.StanceOf(1).Level, Is.EqualTo(2));
            byte[] save = SimOf(s).SaveRetainedSnapshot();
            SimSession b = Stanced();
            Kill(b, 4, 1);
            Assert.DoesNotThrow(() => SimOf(b).RestoreRetainedSnapshot(save));
        }

        // ForceStance in the Requests phase: the unit is in the level within that step, its change is dropped, and it can change
        // again afterwards.
        [Test]
        public void S4_ForceStanceInTheRequestsPhase()
        {
            SimSession s = Stanced();
            int seen = -1;
            Run(s, 5, SimSystemPhase.Requests, 16, c => { Assert.That(s.Stances.ForceStance(Unit(s, 1), 2), Is.True); seen = s.Stances.StanceOf(Unit(s, 1)).Level; });
            Assert.That(s.TryStance(1, 1), Is.True);
            TaskKey task = SimOf(s).Entities.Get<Posture>(Unit(s, 1)).StableTask;
            Assert.That(SimOf(s).Scheduler.TryGetPendingStable(task, out _), Is.True);
            StepTo(s, 5);
            Assert.That(SimOf(s).Scheduler.TryGetPendingStable(task, out _), Is.False, "the dropped change's task is cancelled");
            Assert.That(seen, Is.EqualTo(2));
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(2, -1, 0)));
            StepTo(s, 12);
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(2, -1, 0)), "the dropped change never lands");
            Assert.That(s.TryStance(1, 0), Is.True);
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(2, 0, 13 + 21)));
        }

        // ForceStance does not ask the gate: a changing unit (the gate denies Stance) is still put down.
        [Test]
        public void S4_ForceStanceSkipsTheGate()
        {
            SimSession s = Stanced();
            Assert.That(s.TryStance(1, 1), Is.True);
            Assert.That(s.Gate.Allows(Unit(s, 1), UnitAction.Stance), Is.False);
            Assert.That(s.Stances.ForceStance(Unit(s, 1), 1), Is.True);
            Assert.That(s.StanceOf(1), Is.EqualTo(new StanceView(1, -1, 0)));
            Assert.That(s.Gate.Allows(Unit(s, 1), UnitAction.Stance), Is.True);
        }

        // The Queries phase has sealed the posture writes: ForceStance there is refused by the simulation, loudly (DOWN-1 calls it
        // from the Requests phase of the next tick instead).
        [Test]
        public void S4_ForceStanceInTheQueriesPhaseIsRefused()
        {
            SimSession s = Stanced();
            Exception error = null;
            Run(s, 3, SimSystemPhase.Queries, 46, c => { try { s.Stances.ForceStance(Unit(s, 1), 2); } catch (Exception e) { error = e; } });
            StepTo(s, 3);
            Assert.That(error, Is.InstanceOf<SimBoundaryException>());
        }

        // A change asked inside the step of tick T (Requests phase) and one asked between T - 1 and T land on the same tick.
        [Test]
        public void S4_InStepAndBetweenStepsGiveTheSameDueTick()
        {
            SimSession a = Stanced(), b = Stanced();
            StanceView inStep = default;
            Run(a, 8, SimSystemPhase.Requests, 16, c => { Assert.That(a.Stances.TryStance(c, Unit(a, 1), 2), Is.True); inStep = a.Stances.StanceOf(Unit(a, 1)); });
            StepTo(a, 8);
            StepTo(b, 7);
            Assert.That(b.TryStance(1, 2), Is.True);
            Assert.That(inStep, Is.EqualTo(new StanceView(0, 2, 29)));
            Assert.That(b.StanceOf(1), Is.EqualTo(inStep));
            Assert.Throws<ArgumentException>(() => a.Stances.TryStance(null, Unit(a, 1), 1));
            Assert.Throws<InvalidOperationException>(() => a.Stances.TryStance(SimOf(a).Context, Unit(a, 1), 1), "outside a step");
        }

        // ---- S10 ----

        private static List<ulong> Hashes(SimSession s, int n)
        {
            var list = new List<ulong>();
            for (int i = 0; i < n; i++) { s.StepOnce(); list.Add(s.StateHash); }
            return list;
        }

        [Test]
        public void S10_SnapshotMidChangeResumesTheSameRun()
        {
            SimSession a = Stanced();
            StepTo(a, 4);
            Assert.That(a.TryStance(1, 2), Is.True);
            Assert.That(a.TryStance(2, 1), Is.True);
            StepTo(a, 9);
            byte[] save = SimOf(a).SaveRetainedSnapshot();
            List<ulong> expected = Hashes(a, 40);
            SimSession b = Stanced();
            SimOf(b).RestoreRetainedSnapshot(save);
            b.RefreshView();
            Assert.That(b.StanceOf(1), Is.EqualTo(new StanceView(0, 2, 26)));
            Assert.That(Hashes(b, 40), Is.EqualTo(expected));
            Assert.That(SimOf(b).SaveRetainedSnapshot(), Is.EqualTo(SimOf(a).SaveRetainedSnapshot()));
        }

        [Test]
        public void S10_ForceStanceThenSnapshot()
        {
            SimSession a = Stanced();
            StepTo(a, 4);
            Assert.That(a.TryStance(1, 1), Is.True);
            Force(a, 1, 2);
            byte[] save = SimOf(a).SaveRetainedSnapshot();
            List<ulong> expected = Hashes(a, 30);
            SimSession b = Stanced();
            SimOf(b).RestoreRetainedSnapshot(save);
            Assert.That(b.StanceOf(1), Is.EqualTo(new StanceView(2, -1, 0)));
            Assert.That(Hashes(b, 30), Is.EqualTo(expected));
        }

        [Test]
        public void S10_TwoRunsAreTheSame()
        {
            List<ulong> Run1()
            {
                SimSession s = World(Pack(), new[] { Soldier(1, 1, 10000, 8000, 1), Soldier(2, 2, 30000, 8000, 2) }, Map(),
                    new[] { BridgeCommand.Stance(20, 2, 1), BridgeCommand.Stance(60, 1, 2), BridgeCommand.Stance(100, 2, 0) });
                return Hashes(s, 150);
            }
            Assert.That(Run1(), Is.EqualTo(Run1()));
        }

        // A stance save does not load into a world without stances, nor the other way round.
        [Test]
        public void S10_SavesDoNotCrossTheStanceSwitch()
        {
            SimSession stance = Stanced(), field = World(Pack("rwr-field"), Pair(), Map());
            StepTo(stance, 5); StepTo(field, 5);
            byte[] stanceSave = SimOf(stance).SaveRetainedSnapshot(), fieldSave = SimOf(field).SaveRetainedSnapshot();
            Assert.That(() => SimOf(World(Pack("rwr-field"), Pair(), Map())).RestoreRetainedSnapshot(stanceSave), Throws.Exception);
            Assert.That(() => SimOf(Stanced()).RestoreRetainedSnapshot(fieldSave), Throws.Exception);
        }

        // ---- the gate's stance entries ----

        [Test]
        public void Gate_StanceDenyAndChangingDeny()
        {
            SimSession s = Stanced();
            EntityId u = Unit(s, 1);
            Assert.That(s.Gate.Denied(u), Is.EqualTo(UnitAction.None));
            Assert.That(s.TryStance(1, 1), Is.True);
            Assert.That(s.Gate.Denied(u), Is.EqualTo(UnitAction.Stance | UnitAction.Dash | UnitAction.Traverse));
            StepTo(s, 7);
            Assert.That(s.Gate.Denied(u), Is.EqualTo(UnitAction.Dash | UnitAction.Traverse), "crouched: its deny");
            Force(s, 1, 0);
            Assert.That(s.Gate.Denied(u), Is.EqualTo(UnitAction.None));
        }

        [Test]
        public void Gate_AnEmptyDenyAllowsEverythingButTheChange()
        {
            CombatPack pack = Stance1ContentTests.Load(Stance1ContentTests.Item(deny: "[]") + ", " + Stance1ContentTests.Item("prone", "0.5", "0.25", "0.25", deny: "[\"fire\"]")).BuildCombat();
            SimSession s = World(pack, Pair(), Map());
            Force(s, 1, 1);
            Assert.That(s.Gate.Denied(Unit(s, 1)), Is.EqualTo(UnitAction.None));
            Force(s, 1, 2);
            Assert.That(s.Gate.Denied(Unit(s, 1)), Is.EqualTo(UnitAction.Fire));
            Assert.That(s.TryStance(1, 1), Is.True);
            Assert.That(s.Gate.Denied(Unit(s, 1)), Is.EqualTo(UnitAction.Fire | StanceSystem.ChangingDeny));
        }

        // ---- S9, first sentence: the stance's spread factor ----

        // 1000, 700, 400 in the three stances; while changing the wider of the two levels; 1000 in a world without stances.
        [Test]
        public void S9_StanceSpreadPermille()
        {
            SimSession s = Stanced();
            CombatSystem combat = CombatOf(s);
            EntityId u = Unit(s, 1);
            Assert.That(combat.StanceSpreadPermille(u), Is.EqualTo(1000));
            Assert.That(s.TryStance(1, 2), Is.True);
            Assert.That(combat.StanceSpreadPermille(u), Is.EqualTo(1000), "standing to prone: the standing 1000");
            StepTo(s, 22);
            Assert.That(combat.StanceSpreadPermille(u), Is.EqualTo(400));
            Assert.That(s.TryStance(1, 1), Is.True);
            Assert.That(combat.StanceSpreadPermille(u), Is.EqualTo(700), "prone to crouched: the crouched 700");
            StepTo(s, 38);
            Assert.That(combat.StanceSpreadPermille(u), Is.EqualTo(700));
            Assert.That(s.Stances.SpreadPermille(u), Is.EqualTo(700));
            SimSession field = World(Pack("rwr-field"), Pair(), Map());
            Assert.That(CombatOf(field).StanceSpreadPermille(Unit(field, 1)), Is.EqualTo(1000));
        }
    }
}
