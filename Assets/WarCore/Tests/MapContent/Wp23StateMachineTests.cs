using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // T1a-2a acceptance 9/14: the progress state machine and its edges, checked one transition at a time. Most tests plant a
    // MoveProgress (or move a unit) and take a single Step, because the end-to-end fixtures in Wp23FailureTests are decided by
    // the order-level guard and would not notice a wrong retry budget.
    public sealed class Wp23StateMachineTests
    {
        private const long S = SimFix.Scale;
        private static readonly MoverKind[] Movers = { MoverKind.InterimPath, MoverKind.InterimCrowd };

        private static EntityStore Store(SimSession s) => M3TestWorld.Sim(s).Entities;
        private static EntityId Id(SimSession s, ulong key = 1) => M3TestWorld.Unit(s, key);

        private static void Plant(SimSession s, MoveProgress p) => Store(s).Set(Id(s), p);

        private static void Teleport(SimSession s, SimVec3 to)
        {
            Position position = Store(s).Get<Position>(Id(s));
            position.Value = to;
            Store(s).Set(Id(s), position);
        }

        // A unit that cannot move at all: it starts closer than the footprint to the blocked cell (Wp23FailureTests (b)).
        private static SimSession Immobile(MoverKind mover, long speed = Wp23Fx.Speed)
        {
            SimSession s = Wp23Fx.World(mover, Wp23Fx.Map((x, z) => x == 0 && z == 1), Wp23.Unit(1, 1, 1100, 1500, speed));
            Wp23Fx.Order(s, 1, 2300, 1500);
            return s;
        }

        // ---- the order-level guard ----

        [TestCaseSource(nameof(Movers))]
        public void OrderGuard_FailsOnTheStepThatReaches240_NotOneLater(MoverKind mover)
        {
            SimSession s = Immobile(mover);
            MoveProgress p = Wp23Fx.Progress(s, 1);
            p.OrderStall = InterimLocomotion.OrderStallLimit - 1;
            Plant(s, p);
            s.StepOnce();
            Assert.That(Wp23Fx.Failed(s, 1), Is.True);
        }

        // A step of a few raw units: a quarter of it is zero, so the margin must still be one raw. Standing still is no progress.
        [TestCaseSource(nameof(Movers))]
        public void OrderGuard_StandingStillIsNoProgress_EvenForATinyStep(MoverKind mover)
        {
            SimSession s = Immobile(mover, speed: 1);
            MoveProgress p = Wp23Fx.Progress(s, 1);
            p.OrderStall = InterimLocomotion.OrderStallLimit - 1;
            Plant(s, p);
            s.StepOnce();
            Assert.That(Wp23Fx.Failed(s, 1), Is.True);
        }

        // ---- the retry budget ----

        [TestCaseSource(nameof(Movers))]
        public void ThirdRetryUsed_TheNextStallFails_AndTheStallBeforeItReplans(MoverKind mover)
        {
            SimSession s = Immobile(mover);
            MoveProgress p = Wp23Fx.Progress(s, 1);
            p.Stall = InterimLocomotion.StallLimit - 1; p.Retries = InterimLocomotion.MaxRetries;
            Plant(s, p);
            s.StepOnce();
            Assert.That(Wp23Fx.Failed(s, 1), Is.True, "budget spent");

            SimSession t = Immobile(mover);
            p = Wp23Fx.Progress(t, 1);
            p.Stall = InterimLocomotion.StallLimit - 1; p.Retries = InterimLocomotion.MaxRetries - 1;
            Plant(t, p);
            t.StepOnce();
            MoveProgress after = Wp23Fx.Progress(t, 1);
            Assert.That(Wp23Fx.Failed(t, 1), Is.False);
            Assert.That((after.Retries, after.Stall), Is.EqualTo((InterimLocomotion.MaxRetries, 0)));
        }

        [TestCaseSource(nameof(Movers))]
        public void ARetryRestartsTheWaypointMeasure_AtTheNewDistance(MoverKind mover)
        {
            SimSession s = Immobile(mover);
            MoveProgress p = Wp23Fx.Progress(s, 1);
            p.Stall = InterimLocomotion.StallLimit - 1; p.BestDist2 = 1;
            Plant(s, p);
            s.StepOnce();
            SimVec3 waypoint = Store(s).Get<MoveWaypoint>(Id(s)).Point, at = Wp23.At(s);
            long dx = at.X.Raw - waypoint.X.Raw, dz = at.Z.Raw - waypoint.Z.Raw;
            Assert.That(Wp23Fx.Progress(s, 1).Retries, Is.EqualTo(1));
            Assert.That(Wp23Fx.Progress(s, 1).BestDist2, Is.EqualTo(dx * dx + dz * dz));
        }

        // The route disappears under a stalled unit (it is put on a blocked cell): the re-plan finds nothing and the order fails.
        [TestCaseSource(nameof(Movers))]
        public void ReplanWithoutARoute_Fails(MoverKind mover)
        {
            SimSession s = Immobile(mover);
            Teleport(s, Wp23.V(500, 0, 1500));
            MoveProgress p = Wp23Fx.Progress(s, 1);
            p.Stall = InterimLocomotion.StallLimit - 1;
            Plant(s, p);
            s.StepOnce();
            Assert.That(Wp23Fx.Failed(s, 1), Is.True);
            Assert.That(Wp23Fx.Active(s, 1), Is.False);
        }

        // Reaching a waypoint restarts the per-waypoint measure and keeps the order's retries.
        [TestCaseSource(nameof(Movers))]
        public void NewWaypoint_ResetsTheStall_ButKeepsTheRetries(MoverKind mover)
        {
            SimSession s = Wp23Fx.World(mover, Wp23Fx.Map((x, z) => x == 8 && z == 5), Wp23.Unit(1, 1, 2500, 5500, Wp23Fx.Speed));
            Wp23Fx.Order(s, 1, 13500, 5500);
            SimVec3 waypoint = Store(s).Get<MoveWaypoint>(Id(s)).Point;
            // Path: within one step of the waypoint (a bend of the route). Crowd: just short of the cell border (a waypoint changes
            // on entering a cell). The wall cell makes the first waypoint a bend, not the target.
            Assert.That(waypoint, Is.Not.EqualTo(Wp23Fx.Goal(13500, 5500)));
            long back = mover == MoverKind.InterimPath ? 1000 : S / 2 + 100;
            Teleport(s, new SimVec3(SimFix.FromRaw(waypoint.X.Raw - back), waypoint.Y, waypoint.Z));
            MoveProgress p = Wp23Fx.Progress(s, 1);
            p.Stall = 30; p.Retries = 2;
            Plant(s, p);
            s.StepOnce();
            Assert.That(Store(s).Get<MoveWaypoint>(Id(s)).Point, Is.Not.EqualTo(waypoint), "the waypoint moved on");
            MoveProgress after = Wp23Fx.Progress(s, 1);
            Assert.That((after.Stall, after.Retries), Is.EqualTo((0, 2)));
        }

        // ---- fresh progress, arrival and Stop ----

        [TestCaseSource(nameof(Movers))]
        public void OrderStartsFreshProgress_WithTheExactDistanceToTheTarget(MoverKind mover)
        {
            // Pythagorean and near-square distances inside the unit's own cell (so the waypoint is the target and the route beyond
            // it is empty): floor of the square root, never rounded up, exact on squares.
            var cases = new (long dx, long dz)[] { (3000, 4000), (3000, 4001), (12000, 16000), (1, 0), (0, 5), (7, 7), (2999, 4000), (30001, 1) };
            foreach (var (dx, dz) in cases)
            {
                SimSession s = Wp23Fx.World(mover, Wp23Fx.Map((x, z) => false), Wp23.Unit(1, 1, 2500, 5500, Wp23Fx.Speed));
                SimVec3 from = Wp23.At(s);
                var target = new SimVec3(SimFix.FromRaw(from.X.Raw + dx), default, SimFix.FromRaw(from.Z.Raw + dz));
                M3TestWorld.Locomotion(s).Order(Id(s), target);
                MoveProgress p = Wp23Fx.Progress(s, 1);
                long expected = (long)Math.Floor(Math.Sqrt((double)dx * dx + (double)dz * dz));
                Assert.That(p.OrderBest, Is.EqualTo(expected), $"({dx},{dz})");
                Assert.That((p.Stall, p.Retries, p.OrderStall, p.Failed), Is.EqualTo((0, 0, 0, (byte)0)));
            }
        }

        [TestCaseSource(nameof(Movers))]
        public void ArrivalAndStop_ClearTheProgress(MoverKind mover)
        {
            SimSession s = Wp23Fx.World(mover, Wp23Fx.Map((x, z) => false), Wp23.Unit(1, 1, 2500, 5500, Wp23Fx.Speed));
            Wp23Fx.Order(s, 1, 4500, 5500);
            for (int i = 0; i < 4; i++) s.StepOnce();
            Assert.That(Wp23Fx.Progress(s, 1).OrderBest, Is.GreaterThan(0), "under way");
            for (int i = 0; i < 40 && Wp23Fx.Active(s, 1); i++) s.StepOnce();
            Assert.That(Wp23Fx.Active(s, 1), Is.False);
            Assert.That(Wp23Fx.Progress(s, 1), Is.EqualTo(default(MoveProgress)), "arrival");

            Wp23Fx.Order(s, 1, 12500, 5500);
            for (int i = 0; i < 3; i++) s.StepOnce();
            Assert.That(Wp23Fx.Progress(s, 1).OrderBest, Is.GreaterThan(0));
            M3TestWorld.Locomotion(s).Stop(Id(s));
            Assert.That(Wp23Fx.Progress(s, 1), Is.EqualTo(default(MoveProgress)), "explicit stop");
            Assert.That(Wp23Fx.Failed(s, 1), Is.False, "a stop is not a failure");
        }

        // ---- step arithmetic ----

        // Both axes of the normalised step truncate to nothing: the dominant axis moves one raw, X on an exact tie.
        [TestCaseSource(nameof(Movers))]
        public void LowSpeedNudge_MovesTheDominantAxis_AndXOnATie(MoverKind mover)
        {
            WcMap allMud = Wp23.Make(6, 3, 0, 6 * S, null, 4 * S, null, Wp23.Names, (x, z) => 1);
            foreach (var (tx, tz, dx, dz) in new[] { (501L, 501L, 1L, 0L), (503L, 501L, 1L, 0L), (501L, 503L, 0L, 1L) })
            {
                SimSession s = Wp23.Session(allMud, mover, new[] { Wp23.Unit(1, 1, 500, 500, 1) }, null, Wp23.Pack());
                M3TestWorld.Locomotion(s).Order(Id(s), new SimVec3(Wp23.Mm(tx), default, Wp23.Mm(tz)));
                SimVec3 before = Wp23.At(s);
                s.StepOnce();
                SimVec3 after = Wp23.At(s);
                Assert.That((after.X.Raw - before.X.Raw, after.Z.Raw - before.Z.Raw), Is.EqualTo((dx, dz)), $"to ({tx},{tz}) mm");
            }
        }

        // The extra raw of a nudged step is a move like any other: it must not squeeze into the footprint.
        [TestCaseSource(nameof(Movers))]
        public void LowSpeedNudge_IsCheckedForClearance(MoverKind mover)
        {
            // Everything is mud (half speed) except a blocked "road" cell (6,5), so a 1 mm/s unit has a step of one raw and the
            // normalised step truncates to nothing. The unit starts just outside the footprint circle round the cell's lower-left
            // corner (9832, 13106 raw away: squared distance 4 over r squared) and aims along the tangent, mostly east. Only the
            // nudge (one raw east) would take it inside the circle.
            string blockedRoad = TerrainDemoMap.PackJson.Replace("{ \"id\": \"road\", \"speedMilli\": 1250 }", "{ \"id\": \"road\", \"passable\": false }");
            Assert.That(blockedRoad, Is.Not.EqualTo(TerrainDemoMap.PackJson));
            WcMap map = Wp23.Make(16, 16, 0, 6 * S, null, 4 * S, null, Wp23.Names, (x, z) => (byte)(x == 6 && z == 5 ? 2 : 1));
            SimSession s = Wp23.Session(map, mover, new[] { Wp23.Unit(1, 1, 5000, 5000, 1) }, null, Wp23.Pack(blockedRoad));
            var start = new SimVec3(SimFix.FromRaw(6 * S - 9832), default, SimFix.FromRaw(5 * S - 13106));
            Teleport(s, start);
            var target = new SimVec3(SimFix.FromRaw(start.X.Raw + 6553), default, SimFix.FromRaw(start.Z.Raw - 4916));
            M3TestWorld.Locomotion(s).Order(Id(s), target);
            // A Path unit would first go back to its cell centre; aim it along the tangent directly.
            Store(s).Set(Id(s), new MoveWaypoint(target, true));
            const long r = 16384;
            for (int i = 0; i < 30; i++)
            {
                s.StepOnce();
                long dx = 6 * S - Wp23.At(s).X.Raw, dz = 5 * S - Wp23.At(s).Z.Raw;
                if (dx > 0 && dz > 0) Assert.That(dx * dx + dz * dz, Is.GreaterThanOrEqualTo(r * r), $"step {i + 1}");
            }
            Assert.That(Wp23.At(s).X.Raw, Is.LessThanOrEqualTo(start.X.Raw + 40), "it did not creep east");
        }

        // Without a height field the legacy 3-D movement stays: the unit climbs to a target of another height.
        [TestCaseSource(nameof(Movers))]
        public void SurfaceOnlyMap_StillMovesInY(MoverKind mover)
        {
            WcMap map = Wp23.Make(8, 8, 0, 6 * S, null, 4 * S, null, Wp23.Names, (x, z) => 0);
            SimSession s = Wp23.Session(map, mover, new[] { Wp23.Unit(1, 1, 2500, 2500, 3000) }, null, Wp23.Pack());
            M3TestWorld.Locomotion(s).Order(Id(s), new SimVec3(Wp23.Mm(2900), SimFix.FromInt(2), Wp23.Mm(2500)));
            long y0 = Wp23.At(s).Y.Raw;
            for (int i = 0; i < 3; i++) s.StepOnce();
            Assert.That(Wp23.At(s).Y.Raw, Is.GreaterThan(y0), "Y follows the target on a map with no height field");
        }

        // ---- unit-to-unit distances are horizontal on a height field ----

        [Test]
        public void CrowdOnASlope_KeepsTheHorizontalSeparation_AndTheGround()
        {
            // 0.75 slope along X: 0.45 m apart horizontally is 0.56 m in 3-D, over the 0.5 m sum of the radii. A 3-D test would
            // let them overlap on the plane; the horizontal one must not.
            const int slope = (int)(S * 3 / 4);
            WcMap map = Wp23.Make(16, 8, 0, 12 * S, null, 4 * S, (i, j) => i * slope);
            SimSession s = Wp23.Session(map, MoverKind.InterimCrowd,
                new[] { Wp23.Unit(1, 1, 2500, 3500, 3000), Wp23.Unit(2, 2, 3100, 3500, 3000) }, null, Wp23.Pack());
            HeightField ground = TerrainDerivation.Derive(map, Wp23.Pack()).Ground;
            // Unit 2 stands still (no order); unit 1 walks into it.
            Wp23Fx.Order(s, 1, 6500, 3500);
            double nearest = double.MaxValue;
            for (int i = 0; i < 90; i++)
            {
                s.StepOnce();
                SimVec3 a = Wp23.At(s, 1), b = Wp23.At(s, 2);
                double dx = (a.X.Raw - b.X.Raw) / (double)S, dz = (a.Z.Raw - b.Z.Raw) / (double)S;
                nearest = Math.Min(nearest, Math.Sqrt(dx * dx + dz * dz));
                Assert.That(ground.TryHeightAt(a.X, a.Z, out SimFix ya), Is.True);
                Assert.That(a.Y.Raw, Is.EqualTo(ya.Raw), $"tick {i + 1}");
            }
            Assert.That(nearest, Is.GreaterThanOrEqualTo(0.49), "never inside the sum of the radii on the plane");
            Assert.That(Wp23.At(s, 1).X.Raw, Is.GreaterThan(Wp23.Mm(2500).Raw), "it did walk");
        }

        // Every counter of MoveProgress is part of the world hash (a restore from a hash-checked save must agree on them).
        [TestCaseSource(nameof(Movers))]
        public void EveryProgressField_ChangesTheWorldHash(MoverKind mover)
        {
            var changes = new (string name, Func<MoveProgress, MoveProgress> apply)[]
            {
                ("BestDist2", p => { p.BestDist2 += 1; return p; }), ("Stall", p => { p.Stall += 1; return p; }),
                ("Retries", p => { p.Retries += 1; return p; }), ("Failed", p => { p.Failed = (byte)(p.Failed ^ 1); return p; }),
                ("OrderBest", p => { p.OrderBest += 1; return p; }), ("OrderStall", p => { p.OrderStall += 1; return p; }),
                ("Tail", p => { p.Tail += 1; return p; }),
            };
            SimSession reference = Immobile(mover);
            ulong baseline = reference.StateHash;
            foreach (var (name, apply) in changes)
            {
                SimSession s = Immobile(mover);
                Assert.That(s.StateHash, Is.EqualTo(baseline), "same world, same hash");
                Plant(s, apply(Wp23Fx.Progress(s, 1)));
                Assert.That(s.StateHash, Is.Not.EqualTo(baseline), name);
            }
        }

        // Already overlapping on the plane (0.45 m, but 0.56 m in 3-D on this slope): a small step that widens the gap (still
        // inside the 0.5 m) is allowed.
        [Test]
        public void OverlappingPairOnASlope_CanWalkApart()
        {
            const int slope = (int)(S * 3 / 4);
            WcMap map = Wp23.Make(16, 8, 0, 12 * S, null, 4 * S, (i, j) => i * slope);
            SimSession s = Wp23.Session(map, MoverKind.InterimCrowd,
                new[] { Wp23.Unit(1, 1, 3000, 3500, 300), Wp23.Unit(2, 2, 3450, 3500, 300) }, null, Wp23.Pack());
            long start = Wp23.At(s, 1).X.Raw;
            Wp23Fx.Order(s, 1, 1500, 3500);
            for (int i = 0; i < 5; i++) s.StepOnce();
            Assert.That(Wp23.At(s, 1).X.Raw, Is.LessThan(start), "it moved away");
        }

        // A step the wall clipped eases back toward the cell centre; when the centre is within one step the unit lands on it,
        // and that point is on the ground like every other.
        [Test]
        public void EaseBackToTheCellCentre_LandsOnTheGround()
        {
            WcMap map = Wp23.Make(16, 16, 0, 12 * S, null, 4 * S, (i, j) => (int)(i * (S / 8)), Wp23.Names, (x, z) => (byte)(x == 6 && z == 5 ? 1 : 0));
            string closed = TerrainDemoMap.PackJson.Replace("{ \"id\": \"mud\", \"speedMilli\": 500, \"cost\": 3 }", "{ \"id\": \"mud\", \"passable\": false }");
            // 7500 mm/s is exactly the step cap, 0.25 m: the cell centre (5.5 m) is one step from the footprint limit (5.75 m).
            SimSession s = Wp23.Session(map, MoverKind.InterimCrowd, new[] { Wp23.Unit(1, 1, 5750, 5500, 7500) }, null, Wp23.Pack(closed));
            HeightField ground = TerrainDerivation.Derive(map, Wp23.Pack(closed)).Ground;
            Wp23Fx.Order(s, 1, 5990, 5500);
            s.StepOnce();
            SimVec3 at = Wp23.At(s);
            Assert.That(at.X.Raw, Is.EqualTo(5 * S + S / 2), "eased back onto the centre");
            Assert.That(ground.TryHeightAt(at.X, at.Z, out SimFix y), Is.True);
            Assert.That(at.Y.Raw, Is.EqualTo(y.Raw));
        }

        [Test]
        public void ProgressHash_CoversEveryField()
        {
            ulong Of(MoveProgress p) { var h = new StateHash(); p.WriteState(h); return h.Value; }
            MoveProgress b = default;
            ulong zero = Of(b);
            b.BestDist2 = 1; Assert.That(Of(b), Is.Not.EqualTo(zero)); b = default;
            b.Stall = 1; Assert.That(Of(b), Is.Not.EqualTo(zero)); b = default;
            b.Retries = 1; Assert.That(Of(b), Is.Not.EqualTo(zero)); b = default;
            b.Failed = 1; Assert.That(Of(b), Is.Not.EqualTo(zero)); b = default;
            b.OrderBest = 1; Assert.That(Of(b), Is.Not.EqualTo(zero)); b = default;
            b.OrderStall = 1; Assert.That(Of(b), Is.Not.EqualTo(zero)); b = default;
            b.Tail = 1; Assert.That(Of(b), Is.Not.EqualTo(zero));
        }

        // Tail = the length of the planned route beyond the current waypoint: one cell is 65536 raw, a diagonal 92682.
        [Test]
        public void RouteTail_IsTheLengthBeyondTheWaypoint()
        {
            WcMap open = Wp23Fx.Map((x, z) => false);
            // Crowd heads for the next cell centre: from (2,2) to (6,2) three straight edges remain beyond it; to (5,5) two diagonals.
            foreach (var (tx, tz, expected) in new[] { (6500L, 2500L, 3 * S), (5500L, 5500L, 2 * 92682L) })
            {
                SimSession c = Wp23Fx.World(MoverKind.InterimCrowd, open, Wp23.Unit(1, 1, 2500, 2500, Wp23Fx.Speed));
                Wp23Fx.Order(c, 1, tx, tz);
                Assert.That(Wp23Fx.Progress(c, 1).Tail, Is.EqualTo(expected), $"crowd to ({tx},{tz})");
            }
            // Path first returns to the centre of its own cell: the whole route of four straight edges is still ahead.
            SimSession p = Wp23Fx.World(MoverKind.InterimPath, open, Wp23.Unit(1, 1, 2200, 2500, Wp23Fx.Speed));
            Wp23Fx.Order(p, 1, 6500, 2500);
            Assert.That(Wp23Fx.Progress(p, 1).Tail, Is.EqualTo(4 * S));
        }

        // A re-plan takes the tail of the new route at once, not a step later.
        [TestCaseSource(nameof(Movers))]
        public void Replan_RefreshesTheTailImmediately(MoverKind mover)
        {
            SimSession s = Immobile(mover);
            MoveProgress p = Wp23Fx.Progress(s, 1);
            p.Stall = InterimLocomotion.StallLimit - 1; p.Tail = 123456789;
            Plant(s, p);
            s.StepOnce();
            Assert.That(Wp23Fx.Progress(s, 1).Tail, Is.Not.EqualTo(123456789));
        }
    }
}
