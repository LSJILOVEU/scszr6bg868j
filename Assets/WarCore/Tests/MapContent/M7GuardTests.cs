using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M7GuardTests
    {
        internal static IMoveOrders Adapter(M6World w) => (IMoveOrders)typeof(SimpleAiSystem)
            .GetField("moves", M7DifferentialTests.Private).GetValue(w.System<SimpleAiSystem>());
        // Independent content-grid oracle, including the nine quarter-metre footprint probes.
        private static bool Standable(WcMap map, SimVec3 at)
        {
            for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++)
                if (!map.TryCell(at.X + SimFix.Ratio(x, 4), at.Z + SimFix.Ratio(z, 4), out int cx, out int cz)
                    || !map.IsPassable(cx, cz)) return false;
            return true;
        }
        [TestCase(false)] [TestCase(true)]
        public void FortyEightUnitsMatchTheStandableNearestSlotOracleInEitherCreationOrder(bool wall)
        {
            var units = new UnitSpawn[48];
            for (int i = 0; i < 48; i++) units[i] = M6World.U((ulong)i + 1, 1, 2 + i % 12 * 2, 2 + i / 12 * 2);
            var point = new ControlPointDef(1, M6World.P(60), SimFix.FromInt(6));
            var map = wall ? M3TestWorld.Boxes(80, 40, (54, 14, 4, 6)) : M3TestWorld.Boxes(80, 40);
            var recipe = M6World.RecipeOf(units, M6World.Pack(new[] { point }, think: 1));
            var w = new M6World(recipe, WcMapWriter.Write(map)); var reversed = new M6World(recipe, WcMapWriter.Write(map), reverse: true);
            var calls = M6AiTests.Observe(w); w.Step(); reversed.Step();
            var expected = GuardOracle.Sequential(point, units, at => Standable(map, at));
            int skipped = 0;
            foreach (var u in units)
            {
                var actual = w.Read<MoveIntent>(u.Key);
                Assert.That(actual.Active, Is.True); Assert.That(Standable(map, actual.Target), Is.True, "unit=" + u.Key);
                Assert.That(actual.Target, Is.EqualTo(expected[u.Key]), "nearest standable unclaimed slot, unit=" + u.Key);
                Assert.That(reversed.Read<MoveIntent>(u.Key).Target, Is.EqualTo(actual.Target));
                if (!Standable(map, GuardOracle.Nearest(point, GuardOracle.Start(u), _ => true))) skipped++;
            }
            Assert.That(calls.Orders, Is.EqualTo(48)); Assert.That(skipped, wall ? Is.GreaterThan(0) : Is.EqualTo(0));
        }
        [TestCase(false)] [TestCase(true)]
        public void AllSlotsBlockedIssuesNoOrderAndStopsMovingUnit(bool moving)
        {
            var point = new ControlPointDef(1, M6World.P(20), SimFix.FromInt(6));
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, 2, 2) },
                M6World.Pack(new[] { point }, think: 1)), WcMapWriter.Write(M3TestWorld.Boxes(40, 40, (14, 14, 12, 12))));
            if (moving) Adapter(w).Order(w.Sim.Entities.StableOf(w.Unit(1)), M6World.P(8, 2));
            var calls = M6AiTests.Observe(w); w.Step();
            Assert.That(calls.Orders, Is.Zero); Assert.That(calls.Stops, Is.EqualTo(moving ? 1 : 0));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.False);
        }
        [Test]
        public void StandabilityIncludesFootprintBoundaryAndIgnoresDynamicUnits()
        {
            var map = M3TestWorld.Boxes(40, 40, (20, 10, 1, 10));
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, 5, 5) }), WcMapWriter.Write(map));
            var adapter = Adapter(w);
            Assert.That(adapter.CanStandAt(M6World.P(5, 5)), Is.True, "Occupied by a live unit.");
            foreach (var point in new[] { SimVec3.Metres(20, 0, 15),
                new SimVec3(SimFix.Ratio(199, 10), default, SimFix.FromInt(15)),
                new SimVec3(SimFix.Ratio(1, 10), default, SimFix.FromInt(5)) })
            { Assert.That(Standable(map, point), Is.False); Assert.That(adapter.CanStandAt(point), Is.False); }
        }
    }
}
