using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Naive reference for guard-slot selection: horizontal distance squared in raw units, ties to the lower slot number.
    internal static class GuardOracle
    {
        internal static SimVec3 Nearest(ControlPointDef point, SimVec3 from, Func<SimVec3, bool> usable, ISet<(long, long)> taken = null)
        {
            SimVec3[] slots = GuardSlots.Build(point.Center, point.Radius);
            int best = -1; long bestDistance = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (taken != null && taken.Contains((slots[i].X.Raw, slots[i].Z.Raw))) continue;
                if (!usable(slots[i])) continue;
                long dx = from.X.Raw - slots[i].X.Raw, dz = from.Z.Raw - slots[i].Z.Raw, distance = dx * dx + dz * dz;
                if (best < 0 || distance < bestDistance) { best = i; bestDistance = distance; }
            }
            return best < 0 ? default : slots[best];
        }
        internal static SimVec3 Start(UnitSpawn unit) => new SimVec3(SimFix.Ratio(unit.XMm, 1000), default, SimFix.Ratio(unit.ZMm, 1000));
        // Units decide in stable-identity order and each claims the nearest slot nobody has claimed.
        internal static Dictionary<ulong, SimVec3> Sequential(ControlPointDef point, UnitSpawn[] units, Func<SimVec3, bool> usable)
        {
            var taken = new HashSet<(long, long)>(); var result = new Dictionary<ulong, SimVec3>();
            foreach (UnitSpawn unit in units.OrderBy(u => StableEntityKey.Placed(1, u.Key).Id.Value))
            {
                SimVec3 slot = Nearest(point, Start(unit), usable, taken);
                taken.Add((slot.X.Raw, slot.Z.Raw)); result[unit.Key] = slot;
            }
            return result;
        }
    }

    public sealed class Wp21GuardTests
    {
        private static IEnumerable<(long, long)> Brute(ControlPointDef point)
        {
            long limit = point.Radius.Raw * point.Radius.Raw;
            for (int q = -24; q <= 24; q++)
                for (int r = -24; r <= 24; r++)
                {
                    long x = 65536L * (2 * q + r) / 2, z = 65536L * r * 866025L / 1000000L;
                    if (x * x + z * z <= limit) yield return (point.Center.X.Raw + x, point.Center.Z.Raw + z);
                }
        }
        [TestCase(2, 19)] [TestCase(6, 127)] [TestCase(10, -1)]
        public void LatticeSlotsEqualTheBruteForceSetAndKnownCounts(int radius, int expectedCount)
        {
            var point = new ControlPointDef(1, M6World.P(30, 17), SimFix.FromInt(radius));
            SimVec3[] slots = GuardSlots.Build(point.Center, point.Radius);
            var expected = Brute(point).ToHashSet();
            Assert.That(slots.Select(s => (s.X.Raw, s.Z.Raw)).ToHashSet(), Is.EquivalentTo(expected));
            Assert.That(slots.Length, Is.EqualTo(expected.Count)); Assert.That(slots.Distinct().Count(), Is.EqualTo(slots.Length));
            if (expectedCount > 0) Assert.That(slots.Length, Is.EqualTo(expectedCount));
            Assert.That(slots[0], Is.EqualTo(point.Center));
            Assert.That(slots.All(s => s.Y.Equals(point.Center.Y)));
        }
        [Test]
        public void FirstRingFollowsTheDocumentedWalk()
        {
            var point = new ControlPointDef(1, M6World.P(30, 17), SimFix.FromInt(6));
            SimVec3[] slots = GuardSlots.Build(point.Center, point.Radius);
            var ring = new[] { (-1, 1), (0, 1), (1, 0), (1, -1), (0, -1), (-1, 0) };
            for (int i = 0; i < 6; i++)
            {
                long x = 65536L * (2 * ring[i].Item1 + ring[i].Item2) / 2, z = 65536L * ring[i].Item2 * 866025L / 1000000L;
                Assert.That(slots[i + 1], Is.EqualTo(new SimVec3(point.Center.X + SimFix.FromRaw(x), point.Center.Y, point.Center.Z + SimFix.FromRaw(z))));
            }
        }
        [Test]
        public void SameCoordinateInTwoPointsSharesTheRecordOfTheLowerPoint()
        {
            var points = new[] { new ControlPointDef(1, M6World.P(30, 20), SimFix.FromInt(6)), new ControlPointDef(2, M6World.P(31, 20), SimFix.FromInt(6)) };
            var slots = new GuardSlots(points);
            Assert.That(slots.Total, Is.EqualTo(254));
            int shared = 0;
            for (int s = slots.Start(1); s < slots.Start(1) + slots.Count(1); s++)
            {
                Assert.That(slots.TryFind(slots.At(s), -1, out int any), Is.True);
                Assert.That(slots.TryFind(slots.At(s), 1, out int own), Is.True); Assert.That(own, Is.EqualTo(s));
                if (any != s) { shared++; Assert.That(slots.PointOf(any), Is.Zero); Assert.That(slots.Canonical(s), Is.EqualTo(any)); }
                else Assert.That(slots.Canonical(s), Is.EqualTo(s));
            }
            Assert.That(shared, Is.GreaterThan(60));
            Assert.That(slots.TryFind(points[0].Center + SimVec3.Metres(0, 0, 50), -1, out _), Is.False);
        }
        [Test]
        public void NearestPrefersTheClosestSlotWithinOneMetreAndTheLowerNumberOnTies()
        {
            var points = new[] { new ControlPointDef(1, M6World.P(30, 20), SimFix.FromInt(6)) };
            var slots = new GuardSlots(points);
            Assert.That(slots.Nearest(points[0].Center), Is.Zero);
            Assert.That(slots.Nearest(points[0].Center + new SimVec3(SimFix.Ratio(1, 3), default, default)), Is.Zero);
            Assert.That(slots.Nearest(points[0].Center + SimVec3.Metres(0, 0, 40)), Is.EqualTo(-1));
            // A hair over one metre from every slot (past the rim): no standing record.
            Assert.That(slots.Nearest(points[0].Center + new SimVec3(SimFix.FromRaw(65536L * 7 + 1), default, default)), Is.EqualTo(-1));
        }

        private static int KeyThinkingAt(long tick, bool thinks, int skip = 0)
        {
            for (ulong key = 1; ; key++)
            {
                ulong id = StableEntityKey.Placed(1, key).Id.Value;
                if (((tick + (long)(id % 30)) % 30 == 0) == thinks && skip-- <= 0) return (int)key;
            }
        }
        private static M6World OneAt(ControlPointDef point, UnitSpawn[] units, int think = 1, byte[] map = null) =>
            new M6World(M6World.Endless(M6World.RecipeOf(units, M6World.Pack(new[] { point }, think: think))), map);

        [Test]
        public void AnOrderedAvailableSlotIsKeptAndNeverReissued()
        {
            var point = new ControlPointDef(1, M6World.P(60), SimFix.FromInt(6));
            var w = OneAt(point, new[] { M6World.U(1, 1, 5), M6World.U(2, 1, 5) });
            var calls = M6AiTests.Observe(w); w.Step(200);
            Assert.That(calls.Orders, Is.EqualTo(2)); Assert.That(calls.Stops, Is.Zero);
        }
        [Test]
        public void WhenTwoUnitsStandOnTheSameSlotTheLowerIdentityKeepsItAndTheOtherMovesOn()
        {
            var point = new ControlPointDef(1, M6World.P(40), SimFix.FromInt(6));
            var units = new[] { new UnitSpawn(1, 0, 1, 40300, 20000, 6000, 1, -1), new UnitSpawn(2, 0, 1, 39700, 20000, 6000, 1, -1) };
            var w = OneAt(point, units); w.Step();
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True); Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(point.Center));
            Assert.That(w.Read<MoveIntent>(2).Active, Is.True); Assert.That(w.Read<MoveIntent>(2).Target, Is.Not.EqualTo(point.Center));
        }
        [Test]
        public void ASlotLeftByAnEarlierUnitInTheSameStepIsFreeForALaterOne()
        {
            var west = new ControlPointDef(1, M6World.P(30), SimFix.FromInt(4));
            var east = new ControlPointDef(2, M6World.P(52), SimFix.FromInt(4));
            // Unit 1 is ordered to a west slot but stands nearer the east point, so it re-targets; unit 2 then wants the slot it left.
            var units = new[] { new UnitSpawn(1, 0, 1, 44000, 20000, 6000, 1, -1), new UnitSpawn(2, 0, 1, 26600, 20000, 6000, 1, -1) };
            var w = new M6World(M6World.Endless(M6World.RecipeOf(units, M6World.Pack(new[] { west, east }, think: 1))));
            var slot = M6World.P(27);
            w.System<InterimLocomotion>().Order(w.Unit(1), slot);
            w.Step();
            Assert.That(w.Read<MoveIntent>(1).Target, Is.Not.EqualTo(slot));
            Assert.That(w.Read<MoveIntent>(2).Active, Is.True); Assert.That(w.Read<MoveIntent>(2).Target, Is.EqualTo(slot));
        }
        [Test]
        public void TwoUnitsAtTheSamePlaceTakeTheNearestAndTheNextNearestSlot()
        {
            var point = new ControlPointDef(1, M6World.P(60), SimFix.FromInt(6));
            var units = new[] { M6World.U(1, 1, 5), M6World.U(2, 1, 5), M6World.U(3, 1, 5) };
            var w = OneAt(point, units); w.Step();
            var expected = GuardOracle.Sequential(point, units, _ => true);
            foreach (var u in units) Assert.That(w.Read<MoveIntent>(u.Key).Target, Is.EqualTo(expected[u.Key]));
            Assert.That(expected.Values.Distinct().Count(), Is.EqualTo(3));
        }
        [Test]
        public void ARestingUnitBlocksOthersFromItsSlotButNotItself()
        {
            var point = new ControlPointDef(1, M6World.P(60), SimFix.FromInt(6));
            int resting = KeyThinkingAt(1, false), walker = KeyThinkingAt(1, true);
            var units = new[] { M6World.U((ulong)resting, 1, 2), M6World.U((ulong)walker, 1, 5) };
            var w = OneAt(point, units, think: 30);
            SimVec3 nearestForWalker = GuardOracle.Nearest(point, GuardOracle.Start(units[1]), _ => true);
            w.Set((ulong)resting, new Position(nearestForWalker + new SimVec3(SimFix.Ratio(1, 4), default, default)));
            var calls = M6AiTests.Observe(w); w.Step();
            Assert.That(w.Read<MoveIntent>((ulong)walker).Target, Is.EqualTo(GuardOracle.Nearest(point, GuardOracle.Start(units[1]), s => !s.Equals(nearestForWalker))));
            Assert.That(calls.Orders, Is.EqualTo(1), "The resting unit did not think, and the walker thought once.");
            // Once the resting unit thinks, it keeps standing: its own record never blocks itself.
            w.Step(60); Assert.That(w.Read<MoveIntent>((ulong)resting).Active, Is.False); Assert.That(calls.Orders, Is.EqualTo(1));
        }
        [Test]
        public void ANonSlotTargetStillCountsAsStandingAtTheNearestSlot()
        {
            var point = new ControlPointDef(1, M6World.P(60), SimFix.FromInt(6));
            int mover = KeyThinkingAt(1, false), walker = KeyThinkingAt(1, true);
            var units = new[] { M6World.U((ulong)mover, 1, 2), M6World.U((ulong)walker, 1, 5) };
            var w = OneAt(point, units, think: 30);
            SimVec3 contested = GuardOracle.Nearest(point, GuardOracle.Start(units[1]), _ => true);
            w.Set((ulong)mover, new Position(contested));
            var calls = M6AiTests.Observe(w);
            calls.Order(w.Sim.Entities.StableOf(w.Unit((ulong)mover)), contested + SimVec3.Metres(0, 0, 3) + new SimVec3(SimFix.Ratio(1, 7), default, default));
            int before = calls.Orders; w.Step();
            Assert.That(w.Read<MoveIntent>((ulong)walker).Target, Is.Not.EqualTo(contested));
            Assert.That(calls.Orders, Is.EqualTo(before + 1));
        }
        [Test]
        public void AThirdTeamStandsAtASlotWithoutBeingOrdered()
        {
            var point = new ControlPointDef(1, M6World.P(60), SimFix.FromInt(6));
            var units = new[] { M6World.U(1, 3, 2), M6World.U(2, 1, 5) };
            var w = OneAt(point, units);
            SimVec3 contested = GuardOracle.Nearest(point, GuardOracle.Start(units[1]), _ => true);
            w.Set(1, new Position(contested)); w.Step();
            Assert.That(w.Read<MoveIntent>(1).Active, Is.False);
            Assert.That(w.Read<MoveIntent>(2).Target, Is.EqualTo(GuardOracle.Nearest(point, GuardOracle.Start(units[1]), s => !s.Equals(contested))));
        }
        [Test]
        public void AFullCircleLeavesTheRestWithoutOrders()
        {
            var point = new ControlPointDef(1, M6World.P(60), SimFix.Ratio(12, 10));
            var slots = GuardSlots.Build(point.Center, point.Radius); Assert.That(slots.Length, Is.EqualTo(7));
            var units = Enumerable.Range(1, 9).Select(k => M6World.U((ulong)k, 1, 5)).ToArray();
            var w = OneAt(point, units); var calls = M6AiTests.Observe(w); w.Step(30);
            var byId = units.OrderBy(u => StableEntityKey.Placed(1, u.Key).Id.Value).ToArray();
            var expected = GuardOracle.Sequential(point, byId.Take(7).ToArray(), _ => true);
            foreach (var u in byId.Take(7)) Assert.That(w.Read<MoveIntent>(u.Key).Target, Is.EqualTo(expected[u.Key]));
            foreach (var u in byId.Skip(7)) Assert.That(w.Read<MoveIntent>(u.Key).Active, Is.False, "No free slot: no order and no stacking.");
            Assert.That(calls.Orders, Is.EqualTo(7)); Assert.That(calls.Stops, Is.Zero);
        }
        [Test]
        public void AResidentStaysPutAfterArrival()
        {
            var point = new ControlPointDef(1, M6World.P(60), SimFix.FromInt(6));
            var w = OneAt(point, new[] { M6World.U(1, 1, 5) });
            var calls = M6AiTests.Observe(w); w.Step();
            w.Set(1, new Position(w.Read<MoveIntent>(1).Target)); w.Step();
            Assert.That(calls.Orders, Is.EqualTo(1)); Assert.That(calls.Stops, Is.EqualTo(1));
            w.Step(300); Assert.That(calls.Orders, Is.EqualTo(1)); Assert.That(calls.Stops, Is.EqualTo(1));
        }
        [Test]
        public void ThePointAtTheMapEdgeOnlyGetsTargetsWhereTheFootprintFits()
        {
            var point = new ControlPointDef(1, M6World.P(3, 20), SimFix.FromInt(6));
            var units = Enumerable.Range(1, 40).Select(k => M6World.U((ulong)k, 1, 30, 20 + k % 5)).ToArray();
            var w = OneAt(point, units, map: WcMapWriter.Write(M3TestWorld.Boxes(80, 40))); w.Step();
            var map = M3TestWorld.Boxes(80, 40);
            foreach (var u in units)
            {
                var t = w.Read<MoveIntent>(u.Key); Assert.That(t.Active, Is.True);
                for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                    Assert.That(map.TryCell(t.Target.X + SimFix.Ratio(dx, 4), t.Target.Z + SimFix.Ratio(dz, 4), out int cx, out int cz) && map.IsPassable(cx, cz), Is.True);
            }
        }
        [Test]
        public void EverySlotOfASmallAndALargePointIsClaimedOnceBeforeAnyIsShared()
        {
            foreach (int radius in new[] { 2, 10 })
            {
                var point = new ControlPointDef(1, M6World.P(60), SimFix.FromInt(radius));
                int n = GuardSlots.Build(point.Center, point.Radius).Length;
                var units = Enumerable.Range(1, n).Select(k => M6World.U((ulong)k, 1, 5, 18 + k % 4)).ToArray();
                var w = OneAt(point, units, map: WcMapWriter.Write(M3TestWorld.Boxes(80, 40))); w.Step();
                var expected = GuardOracle.Sequential(point, units, _ => true);
                foreach (var u in units) Assert.That(w.Read<MoveIntent>(u.Key).Target, Is.EqualTo(expected[u.Key]), "radius " + radius);
                Assert.That(units.Select(u => w.Read<MoveIntent>(u.Key).Target).Distinct().Count(), Is.EqualTo(n));
            }
        }
    }
}
