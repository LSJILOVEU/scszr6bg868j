using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M6AiTests
    {
        [TestCase(-1, true)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        public void D7_SightBoundaryIncludesExactly35(int rawDelta, bool found)
        {
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, 5), M6World.U(2, 2, 40) }));
            w.Set(2, new Position(new SimVec3(SimFix.FromRaw(40 * SimFix.Scale + rawDelta), default, SimFix.FromInt(20))));
            w.Sim.CompleteSpatialBoundary();
            var selector = new SightLimited(new NearestVisibleEnemy(), SimFix.FromInt(35));
            Assert.That(selector.TrySelect(w.Combat, w.Unit(1), SimFix.FromInt(60), out var target), Is.EqualTo(found));
            if (found) Assert.That(target, Is.EqualTo(w.Unit(2)));
            Assert.That(selector.TrySelect(w.Combat, w.Unit(1), SimFix.FromInt(30), out _), Is.False, "Weapon range still limits sight.");
        }
        [Test]
        public void D7_WallOccludesButFriendlyUnitDoesNot()
        {
            var units = new[] { M6World.U(1, 1, 5), M6World.U(2, 2, 25), M6World.U(3, 1, 15) };
            var selector = new SightLimited(new NearestVisibleEnemy(), SimFix.FromInt(35));
            var open = new M6World(M6World.RecipeOf(units));
            Assert.That(selector.TrySelect(open.Combat, open.Unit(1), SimFix.FromInt(60), out var target), Is.True);
            Assert.That(target, Is.EqualTo(open.Unit(2)));
            var wall = new M6World(M6World.RecipeOf(units), WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (18, 0, 1, 40))));
            Assert.That(selector.TrySelect(wall.Combat, wall.Unit(1), SimFix.FromInt(60), out _), Is.False);
        }

        // Observe calls while retaining the actual bridge and locomotion implementations.
        internal sealed class CountingOrders : IMoveOrders
        {
            private readonly IMoveOrders inner;
            internal int Orders, Stops;
            internal CountingOrders(IMoveOrders inner) { this.inner = inner; }
            public void Order(StableId id, SimVec3 target) { Orders++; inner.Order(id, target); }
            public void Stop(StableId id) { Stops++; inner.Stop(id); }
            public bool TryGetOrder(StableId id, out SimVec3 target) => inner.TryGetOrder(id, out target);
            public bool CanStandAt(SimVec3 at) => inner.CanStandAt(at);
        }
        internal static CountingOrders Observe(M6World w)
        {
            var ai = w.System<SimpleAiSystem>(); var field = typeof(SimpleAiSystem).GetField("moves", BindingFlags.NonPublic | BindingFlags.Instance);
            var counter = new CountingOrders((IMoveOrders)field.GetValue(ai)); field.SetValue(ai, counter); return counter;
        }
        // Naive nearest-slot oracle over the documented lattice: horizontal distance, ties to the lower slot number.
        internal static SimVec3 ExpectedGuard(ControlPointDef point, SimVec3 from) => GuardOracle.Nearest(point, from, _ => true);
        [Test]
        public void D11_FortyEightUnitsClaimDistinctNearestLatticeSlotsAndCreationOrderDoesNotMatter()
        {
            var units = new UnitSpawn[48];
            for (int i = 0; i < units.Length; i++) units[i] = M6World.U((ulong)i + 1, 1, 2 + i % 12 * 2, 2 + i / 12 * 2);
            var point = new ControlPointDef(1, M6World.P(60), SimFix.FromInt(6));
            var recipe = M6World.RecipeOf(units, M6World.Pack(new[] { point }, think: 1));
            var a = new M6World(recipe); var b = new M6World(recipe, reverse: true); a.Step(); b.Step();
            var expected = GuardOracle.Sequential(point, units, _ => true);
            var distinct = new System.Collections.Generic.HashSet<(long, long)>();
            for (int i = 0; i < units.Length; i++)
            {
                Assert.That(a.Read<MoveIntent>(units[i].Key).Target, Is.EqualTo(expected[units[i].Key]), "unit=" + i);
                Assert.That(b.Read<MoveIntent>(units[i].Key).Target, Is.EqualTo(a.Read<MoveIntent>(units[i].Key).Target));
                Assert.That(distinct.Add((expected[units[i].Key].X.Raw, expected[units[i].Key].Z.Raw)), Is.True);
            }
        }
        [Test]
        public void D11_ThinkingStaggeredByStableIdentity()
        {
            var w = new M6World(M6World.Endless(M6World.RecipeOf(new[] { M6World.U(1, 1, 5) }, M6World.Pack(think: 30))));
            var calls = Observe(w); ulong id = w.Sim.Entities.StableOf(w.Unit(1)).Value;
            int first = (int)((30 - id % 30) % 30); if (first == 0) first = 30;
            w.Step(first - 1); Assert.That(calls.Orders, Is.Zero);
            w.Step(); Assert.That(calls.Orders, Is.EqualTo(1));
            w.Step(60); Assert.That(calls.Orders, Is.EqualTo(1));
        }
        [Test]
        public void D11_NearestTieUsesFirstPointAndDoesNotReissueOrder()
        {
            var points = new[] { new ControlPointDef(8, M6World.P(20), SimFix.FromInt(6)), new ControlPointDef(2, M6World.P(60), SimFix.FromInt(6)),
                new ControlPointDef(3, M6World.P(40), SimFix.FromInt(6), 1) };
            var w = new M6World(M6World.Endless(M6World.RecipeOf(new[] { M6World.U(1, 1, 40), M6World.U(2, 0, 75) }, M6World.Pack(points, think: 1))));
            var calls = Observe(w); w.Step(20);
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(ExpectedGuard(points[0], M6World.P(40))));
            Assert.That(calls.Orders, Is.EqualTo(1)); Assert.That(calls.Stops, Is.Zero);
            Assert.That(w.Read<MoveIntent>(2).Active, Is.False, "Team zero has no AI orders.");
        }
        [TestCase(-1, true)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        public void D11_ArrivalEveryTickAtAQuarterMetreEvenOffThinkCadence(int delta, bool stops)
        {
            var point = new ControlPointDef(7, M6World.P(20), SimFix.FromInt(6));
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, 6), M6World.U(2, 2, 74) }, M6World.Pack(new[] { point })));
            var calls = Observe(w); var id = w.Sim.Entities.StableOf(w.Unit(1)); var goal = ExpectedGuard(point, M6World.P(6));
            calls.Order(id, goal);
            w.Set(1, new Position(goal + new SimVec3(SimFix.FromRaw(SimFix.Scale / 4 + delta), SimFix.FromInt(9), default)));
            w.Step(); Assert.That(w.Read<MoveIntent>(1).Active, Is.EqualTo(!stops));
            Assert.That(calls.Stops, Is.EqualTo(stops ? 1 : 0));
        }
        [Test]
        public void D11_EnteringRadiusKeepsMovingThenCapturingSelectsNextAndAllOwnedStops()
        {
            var points = new[] { new ControlPointDef(1, M6World.P(20), SimFix.FromInt(6)), new ControlPointDef(2, M6World.P(55), SimFix.FromInt(6)) };
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, 12), M6World.U(2, 2, 75) }, M6World.Pack(points, think: 1, ticks: 3)));
            var calls = Observe(w); w.Step();
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True, "Outside one metre of the slot is not arrival.");
            var first = ExpectedGuard(points[0], M6World.P(12));
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(first));
            w.Set(1, new Position(first)); w.Step(); Assert.That(w.Read<MoveIntent>(1).Active, Is.False);
            for (int guard = 0; guard < 10 && w.Capture.Points[0].Owner != 1; guard++) w.Step();
            Assert.That(w.Capture.Points[0].Owner, Is.EqualTo(1)); w.Step();
            var second = ExpectedGuard(points[1], first);
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(second));
            w.Set(1, new Position(second)); w.Step(3);
            Assert.That(w.Match.Winner, Is.EqualTo(1)); Assert.That(w.Match.EndReason, Is.EqualTo(MatchEndReason.AllPoints));
            // The match is over: Sim refuses another step, and the winner's unit holds no order.
            Assert.Throws<InvalidOperationException>(() => w.Step()); Assert.That(w.Read<MoveIntent>(1).Active, Is.False);
        }
        [Test]
        public void D11_EnemyStopsMovementFiresThenDeathResumesAdvance()
        {
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, 5, weapon: 0), M6World.U(2, 2, 12) },
                M6World.Pack(think: 1), M6World.Guns(new LethalResolver())));
            var calls = Observe(w); calls.Order(w.Sim.Entities.StableOf(w.Unit(1)), M6World.P(20)); w.Step();
            Assert.That(w.Read<MoveIntent>(1).Active, Is.False); Assert.That(calls.Stops, Is.EqualTo(1));
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1)); Assert.That(w.Dead(2), Is.True);
            w.Step(); Assert.That(w.Read<MoveIntent>(1).Active, Is.True); Assert.That(calls.Orders, Is.EqualTo(3)); // Initial + unarmed enemy + resumed shooter.
        }
    }
}
