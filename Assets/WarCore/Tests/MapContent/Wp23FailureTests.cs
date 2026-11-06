using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // T1a-2a acceptance 11: a unit never passes through blocked terrain and never stays Active forever. The fixtures are
    // frozen; nothing here may be tuned to pass.
    public sealed class Wp23FailureTests
    {
        private const long S = SimFix.Scale;
        private const long Speed = Wp23Fx.Speed;

        // One blocked cell x in [0,1), z in [1,2): the same-cell fixtures.
        private static WcMap Single() => Wp23Fx.Map((x, z) => x == 0 && z == 1);

        private static SimSession SingleWorld(MoverKind mover, long startXmm, long targetXmm)
        {
            SimSession s = Wp23Fx.World(mover, Single(), Wp23.Unit(1, 1, startXmm, 1500, Speed));
            Wp23Fx.Order(s, 1, targetXmm, 1500);
            return s;
        }

        private static readonly MoverKind[] Movers = { MoverKind.InterimPath, MoverKind.InterimCrowd };

        // (a) The unit walks up to the clearance limit, cannot go on, and every re-plan drags it back to the same place.
        [TestCaseSource(nameof(Movers))]
        public void SameCellShuffle_FailsWithinTheLimit(MoverKind mover)
        {
            SimSession s = SingleWorld(mover, 1500, 1100);
            NavGrid nav = Wp23Fx.Nav(Single());
            int failed = Wp23Fx.StepsToFailure(s, 1, 400, nav);
            Console.WriteLine($"WP23 reference (a) {mover}: failed at step {failed}");
            Assert.That(failed, Is.InRange(1, 300));
            Assert.That(Wp23Fx.Active(s, 1), Is.False);
            Assert.That(Wp23Fx.Progress(s, 1).Failed, Is.EqualTo(1));
        }

        // (b) The start is closer than the clearance: nothing may move, not even back to the cell centre.
        [TestCaseSource(nameof(Movers))]
        public void StartTooCloseToBlockedCell_FailsExactlyAtStep240_AndANewOrderRestartsTheClock(MoverKind mover)
        {
            SimSession s = SingleWorld(mover, 1100, 2300);
            NavGrid nav = Wp23Fx.Nav(Single());
            SimVec3 start = Wp23.At(s);
            var retriesAt = new List<int>();
            int failed = -1;
            for (int step = 1; step <= 300 && failed < 0; step++)
            {
                s.StepOnce();
                Assert.That(Wp23.At(s), Is.EqualTo(start), "no step may leave the start");
                int r = Wp23Fx.Progress(s, 1).Retries;
                if (retriesAt.Count < r) retriesAt.Add(step);
                if (Wp23Fx.Failed(s, 1)) failed = step;
            }
            Assert.That(failed, Is.EqualTo(240));
            Assert.That(retriesAt, Is.EqualTo(new[] { 60, 120, 180 }));
            Assert.That(Wp23Fx.Active(s, 1), Is.False);
            Assert.That(nav.SegmentClear(start, start, InterimLocomotion.UnitFootprint), Is.False, "the start really is not clear");
            // Same order again: Failed clears at once and the clock runs the full 240 steps again.
            Wp23Fx.Order(s, 1, 2300, 1500);
            Assert.That(Wp23Fx.Failed(s, 1), Is.False);
            Assert.That(Wp23Fx.Active(s, 1), Is.True);
            Assert.That(Wp23Fx.Progress(s, 1).Stall, Is.Zero);
            Assert.That(Wp23Fx.Progress(s, 1).Retries, Is.Zero);
            int again = -1;
            for (int step = 1; step <= 300 && again < 0; step++) { s.StepOnce(); if (Wp23Fx.Failed(s, 1)) again = step; }
            Assert.That(again, Is.EqualTo(240));
        }

        // (c) A snapshot taken before and after the first re-plan resumes to the very same failure step and state.
        [TestCaseSource(nameof(Movers))]
        public void Snapshots_ResumeToTheSameFailure(MoverKind mover)
        {
            foreach (int cut in new[] { 59, 61 })
            {
                SimSession whole = SingleWorld(mover, 1100, 2300);
                SimSession a = SingleWorld(mover, 1100, 2300);
                for (int i = 0; i < cut; i++) a.StepOnce();
                byte[] snapshot = M3TestWorld.Sim(a).SaveRetainedSnapshot();
                SimSession b = SingleWorld(mover, 1100, 2300);
                M3TestWorld.Sim(b).RestoreRetainedSnapshot(snapshot);
                for (int step = 1; step <= 240; step++)
                {
                    whole.StepOnce();
                    if (step <= cut) continue;
                    b.StepOnce();
                    Assert.That(b.StateHash, Is.EqualTo(whole.StateHash), $"cut {cut} step {step}");
                    Assert.That(Wp23Fx.Failed(b, 1), Is.EqualTo(Wp23Fx.Failed(whole, 1)));
                    Assert.That(Wp23Fx.Progress(b, 1).Stall, Is.EqualTo(Wp23Fx.Progress(whole, 1).Stall));
                    Assert.That(Wp23Fx.Progress(b, 1).Retries, Is.EqualTo(Wp23Fx.Progress(whole, 1).Retries));
                }
                Assert.That(Wp23Fx.Failed(b, 1), Is.True, $"cut {cut}");
                Assert.That(Wp23Fx.Failed(whole, 1), Is.True);
            }
        }

        // (d) From a clear start, a new order clears the failure and gets there, before and after the give-up.
        [TestCaseSource(nameof(Movers))]
        public void NewOrderFromAClearStart_ArrivesAndClearsFailure(MoverKind mover)
        {
            foreach (int when in new[] { 100, 400 })
            {
                SimSession s = SingleWorld(mover, 1500, 1100);
                for (int i = 0; i < when; i++) s.StepOnce();
                Assert.That(Wp23Fx.Failed(s, 1), Is.EqualTo(when > 300), $"when {when}");
                Wp23Fx.Order(s, 1, 2300, 1500);
                Assert.That(Wp23Fx.Failed(s, 1), Is.False);
                MoveProgress fresh = Wp23Fx.Progress(s, 1);
                Assert.That((fresh.Stall, fresh.Retries, fresh.OrderStall, (int)fresh.Failed), Is.EqualTo((0, 0, 0, 0)));
                for (int i = 0; i < 200 && Wp23Fx.Active(s, 1); i++) s.StepOnce();
                Assert.That(Wp23Fx.Active(s, 1), Is.False);
                Assert.That(Wp23.At(s), Is.EqualTo(new SimVec3(Wp23.Mm(2300), default, Wp23.Mm(1500))));
                Assert.That(Wp23Fx.Failed(s, 1), Is.False, "arriving is not failing");
                Assert.That(Wp23Fx.Progress(s, 1).Retries, Is.Zero);
            }
        }

        // A long detour that first leads away from the target is not a stall: 8 m as the crow flies, about 84 m along the route
        // (a closed row of cells with one gap at the far west). A straight-line progress measure failed it at ~270.
        [TestCaseSource(nameof(Movers))]
        public void LongDetourAwayFromTheTarget_Arrives(MoverKind mover)
        {
            WcMap map = Wp23Fx.Map64((x, z) => z == 7 && x >= 1);
            SimSession s = Wp23Fx.World(mover, map, Wp23.Unit(1, 1, 40500, 3500, Speed));
            Wp23Fx.Order(s, 1, 40500, 11500);
            NavGrid nav = Wp23Fx.Nav(map);
            int arrived = -1;
            for (int step = 1; step <= 1500 && arrived < 0; step++)
            {
                SimVec3 before = Wp23.At(s);
                s.StepOnce();
                if (!before.Equals(Wp23.At(s))) Assert.That(nav.SegmentClear(before, Wp23.At(s), WarCore.Interim.InterimLocomotion.UnitFootprint), Is.True, $"step {step}");
                Assert.That(Wp23Fx.Failed(s, 1), Is.False, $"failed at step {step}");
                if (!Wp23Fx.Active(s, 1)) arrived = step;
            }
            Console.WriteLine($"WP23 reference long detour {mover}: arrived at step {arrived}");
            Assert.That(arrived, Is.GreaterThan(600).And.LessThan(1500), "it walked the whole way round");
            Assert.That(Wp23.At(s), Is.EqualTo(new SimVec3(Wp23.Mm(40500), default, Wp23.Mm(11500))));
        }

        // (e) A resting unit in a one-cell-wide corridor is a hard block for a crowd: the follower must give up in time,
        // by the order-level guard (it shuffles between two cells, which the per-waypoint counters cannot see).
        [Test]
        public void CrowdFollowerBehindARestingUnit_GivesUpWithinTheLimit()
        {
            WcMap map = Wp23Fx.Map((x, z) => x >= 3 && x < 9 && z != 5);
            SimSession s = Wp23Fx.World(MoverKind.InterimCrowd, map, Wp23.Unit(1, 1, 3500, 5500, Speed), Wp23.Unit(2, 2, 6500, 5500, Speed));
            Wp23Fx.Order(s, 1, 8500, 5500);
            int failed = Wp23Fx.StepsToFailure(s, 1, 400, Wp23Fx.Nav(map));
            MoveProgress dbg = Wp23Fx.Progress(s, 1);
            Console.WriteLine($"WP23 reference (e): failed at step {failed}; pos {Wp23.At(s, 1).X.Raw},{Wp23.At(s, 1).Z.Raw} active {Wp23Fx.Active(s, 1)} stall {dbg.Stall} retries {dbg.Retries} ostall {dbg.OrderStall} best {dbg.OrderBest}");
            Assert.That(failed, Is.InRange(1, 300));
            Assert.That(Wp23At(s, 2), Is.EqualTo(Wp23.V(6500, 0, 5500)), "the resting unit never moved");
        }

        private static SimVec3 Wp23At(SimSession s, ulong key) => Wp23.At(s, key);

        private static WcMap Gate(bool closed) => Wp23Fx.Map((x, z) => (x >= 4 && x < 12 && z != 5 && z != 6) || (closed && x == 8));

        private static UnitSpawn[] Ten(Func<int, (long, long)> start)
        {
            var units = new UnitSpawn[10];
            for (int i = 0; i < 10; i++) { var (x, z) = start(i); units[i] = Wp23.Unit((ulong)(i + 1), i + 1, x, z, Speed); }
            return units;
        }

        private static (long, long) Dir(int i) => (500 + 1000 * (i % 2), 3500 + 1000 * (i / 2));
        // Goals 1.5 m apart both ways: with 1 m rows the rear column is unreachable behind the resting front column, in a flat
        // world as well (the crowd has no pass-through rule before 2b), so that layout cannot test the corridor.
        private static (long, long) DirGoal(int i) => (13500 + 1500 * (i % 2), 2500 + 1500 * (i / 2));

        // (f) Ten single soldiers, one team, into a two-cell corridor: all arrive, none fails, none clips a blocked cell.
        [TestCaseSource(nameof(Movers))]
        public void TenThroughATwoCellCorridor_AllArrive(MoverKind mover)
        {
            WcMap map = Gate(false);
            SimSession s = Wp23Fx.World(mover, map, Ten(Dir));
            // The same corridor as plain wall boxes on a flat map: the control for "terrain changes nothing here".
            WcMap flatMap = Wp23.Make(16, 16, 0, 6 * S, new[] { (4, 0, 8, 5), (4, 7, 8, 9) }, 4 * S);
            SimSession flat = Wp23.Session(flatMap, mover, Ten(Dir));
            for (int i = 0; i < 10; i++)
            {
                var (gx, gz) = DirGoal(i);
                Wp23Fx.Order(s, (ulong)(i + 1), gx, gz);
                M3TestWorld.Locomotion(flat).Order(M3TestWorld.Unit(flat, (ulong)(i + 1)), Wp23Fx.Goal(gx, gz));
            }
            NavGrid nav = Wp23Fx.Nav(map);
            var last = new SimVec3[11];
            for (ulong k = 1; k <= 10; k++) last[k] = Wp23.At(s, k);
            int done = -1;
            for (int step = 1; step <= 500; step++)
            {
                s.StepOnce(); flat.StepOnce();
                bool allDone = true;
                for (ulong k = 1; k <= 10; k++)
                {
                    SimVec3 p = Wp23.At(s, k);
                    Assert.That(p, Is.EqualTo(Wp23.At(flat, k)), $"step {step} unit {k}: terrain and flat worlds must move alike");
                    if (!p.Equals(last[k])) Assert.That(nav.SegmentClear(last[k], p, InterimLocomotion.UnitFootprint), Is.True, $"step {step} unit {k}");
                    last[k] = p;
                    Assert.That(Wp23Fx.Failed(s, k), Is.False, $"unit {k} failed at step {step}");
                    allDone &= !Wp23Fx.Active(s, k);
                }
                if (allDone) { done = step; break; }
            }
            Console.WriteLine($"WP23 reference (f) {mover}: all arrived at step {done}");
            Assert.That(done, Is.GreaterThan(0), "everybody arrived within 500 steps");
        }

        // (g) Two teams head-on in the same corridor: only liveness is demanded (nobody stays Active), and nobody clips.
        [TestCaseSource(nameof(Movers))]
        public void HeadOnTraffic_EveryoneEndsWithinTheLimit(MoverKind mover)
        {
            WcMap map = Gate(false);
            Func<int, (long, long)> start = i => i < 5 ? (500 + 1000 * (i % 2), 4500 + 1000 * (i / 2)) : (15500 - 1000 * ((i - 5) % 2), 4500 + 1000 * ((i - 5) / 2));
            Func<int, (long, long)> goal = i => i < 5 ? (13500 + 1000 * (i % 2), 4500 + 1000 * (i / 2)) : (1500 - 1000 * ((i - 5) % 2), 4500 + 1000 * ((i - 5) / 2));
            SimSession s = Wp23Fx.World(mover, map, Ten(start));
            for (int i = 0; i < 10; i++) { var (gx, gz) = goal(i); Wp23Fx.Order(s, (ulong)(i + 1), gx, gz); }
            NavGrid nav = Wp23Fx.Nav(map);
            var last = new SimVec3[11];
            for (ulong k = 1; k <= 10; k++) last[k] = Wp23.At(s, k);
            int idle = -1;
            for (int step = 1; step <= 900; step++)
            {
                s.StepOnce();
                bool anyActive = false;
                for (ulong k = 1; k <= 10; k++)
                {
                    SimVec3 p = Wp23.At(s, k);
                    if (!p.Equals(last[k])) Assert.That(nav.SegmentClear(last[k], p, InterimLocomotion.UnitFootprint), Is.True, $"step {step} unit {k}");
                    last[k] = p;
                    anyActive |= Wp23Fx.Active(s, k);
                }
                if (!anyActive) { idle = step; break; }
            }
            var report = new List<string>();
            for (ulong k = 1; k <= 10; k++) report.Add($"{k}:{(Wp23Fx.Failed(s, k) ? "failed" : "arrived")}");
            Console.WriteLine($"WP23 reference (g) {mover}: idle at step {idle}; {string.Join(" ", report)}");
            Assert.That(idle, Is.GreaterThan(0), "nobody may stay Active past 900 steps");
        }

        // (h) The corridor is closed by a blocked column: no route, so every order fails at once and nobody moves.
        [TestCaseSource(nameof(Movers))]
        public void ClosedCorridor_FailsEveryOrderImmediately(MoverKind mover)
        {
            WcMap map = Gate(true);
            SimSession s = Wp23Fx.World(mover, map, Ten(Dir));
            var begin = new SimVec3[11];
            for (int i = 0; i < 10; i++) { var (gx, gz) = DirGoal(i); Wp23Fx.Order(s, (ulong)(i + 1), gx, gz); begin[i + 1] = Wp23.At(s, (ulong)(i + 1)); }
            for (int step = 0; step < 50; step++) s.StepOnce();
            for (ulong k = 1; k <= 10; k++)
            {
                Assert.That(Wp23Fx.Failed(s, k), Is.True, $"unit {k}");
                Assert.That(Wp23Fx.Active(s, k), Is.False);
                Assert.That(Wp23.At(s, k), Is.EqualTo(begin[k]));
            }
        }
    }
}
