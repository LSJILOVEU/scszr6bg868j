using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M7FastPathTests
    {
        private sealed class Probe : ITargetSelector
        {
            internal Action<CombatSystem, EntityId> Check;
            public ulong PolicyKey => 0x4E45415245535401UL;
            public bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target)
            { Check?.Invoke(world, shooter); target = default; return false; }
        }
        private static CombatPack Pack(ITargetSelector selector = null) => M5TestWorld.Pack(new ZeroResolver(), selector,
            new[] { new WeaponDef(1000, 30, 1000, SimFix.FromInt(1), SimFix.FromInt(35), 1) });
        private static void SameSelection(CombatSystem world, EntityId shooter, SimFix range)
        {
            bool a = false, b = false; EntityId ta = default, tb = default;
            Exception ea = null, eb = null;
            try { a = new NearestVisibleEnemy().TrySelect(world, shooter, range, out ta); } catch (Exception ex) { ea = ex; }
            try { b = new M7OriginalNearest().TrySelect(world, shooter, range, out tb); } catch (Exception ex) { eb = ex; }
            Assert.That(ea?.GetType(), Is.EqualTo(eb?.GetType())); Assert.That(ea?.Message, Is.EqualTo(eb?.Message));
            Assert.That(a, Is.EqualTo(b)); Assert.That(ta, Is.EqualTo(tb));
        }
        [TestCase(35000, 1)] [TestCase(35001, 0)] [TestCase(10000, 1)]
        public void ExactRangeAndQueryStructure(long distance, int queries)
        {
            var w = new M5TestWorld(M5TestWorld.Pair(distanceMm: distance), Pack()); w.Step();
            Assert.That(w.Combat.QueriesThisStep, Is.EqualTo(queries));
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(queries));
            new NearestVisibleEnemy().TrySelect(w.Combat, w.Unit(0), SimFix.FromInt(35), out _);
            Assert.That(w.Combat.QueriesThisStep, Is.EqualTo(queries), "External calls are not counted.");
        }
        [Test]
        public void FriendlyOnlyStepMakesZeroQueriesAndCounterResets()
        {
            var w = new M5TestWorld(M5TestWorld.Pair(distanceMm: 10000), Pack()); w.Step();
            Assert.That(w.Combat.QueriesThisStep, Is.GreaterThan(0));
            w.Sim.Entities.Set(w.Combatants, w.Unit(1), new Combatant(1, -1, 0, 0)); w.Step();
            Assert.That(w.Combat.QueriesThisStep, Is.Zero);
        }
        [TestCase(1)] [TestCase(2)]
        public void CapacityOverflowStillThrowsEvenWithOnlyFriends(int team)
        {
            var w = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 1000, team: team, weapon: -1),
                M5TestWorld.Spawn(3, 2000, team: team, weapon: -1) }, Pack(), candidateCapacity: 2);
            var error = Assert.Throws<InvalidOperationException>(() => w.Step());
            Assert.That(error.Message, Is.EqualTo("Combat unit capacity exceeded; target selection would be incomplete."));
            Assert.That(w.Combat.MayHaveEnemyWithin(w.Unit(0), SimFix.FromInt(35)), Is.True);
            SameSelection(w.Combat, w.Unit(0), SimFix.FromInt(35));
        }
        [Test]
        public void CapacityCountIncludesNonCombatantColliders()
        {
            var w = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 1000, weapon: -1) }, Pack(), candidateCapacity: 1);
            w.Sim.Entities.Remove(w.Combatants, w.Unit(1));
            Assert.That(Assert.Throws<InvalidOperationException>(() => w.Step()).Message, Does.Contain("capacity exceeded"));
        }
        [TestCase(false)] [TestCase(true)]
        public void CacheInvalidAfterStepOrException(bool throws)
        {
            var probe = new Probe();
            var w = new M5TestWorld(M5TestWorld.Pair(distanceMm: 50000), Pack(probe));
            Assert.That(w.Combat.MayHaveEnemyWithin(w.Unit(0), SimFix.FromInt(35)), Is.True, "constructor");
            probe.Check = (c, shooter) => {
                Assert.That(c.MayHaveEnemyWithin(shooter, SimFix.FromInt(35)), Is.False, "inside Step");
                if (throws) throw new InvalidOperationException("probe");
            };
            if (throws) Assert.That(Assert.Throws<InvalidOperationException>(() => w.Step()).Message, Is.EqualTo("probe")); else w.Step();
            Assert.That(w.Combat.MayHaveEnemyWithin(w.Unit(0), SimFix.FromInt(35)), Is.True, "outside Step");
            SameSelection(w.Combat, w.Unit(0), SimFix.FromInt(35));
            // A faulted Sim cannot publish another spatial boundary; compare its existing query state directly.
            if (throws) return;
            w.Sim.Entities.Set(w.Positions, w.Unit(1), new Position(SimVec3.Metres(10, 0, 0))); w.Sim.CompleteSpatialBoundary();
            SameSelection(w.Combat, w.Unit(0), SimFix.FromInt(35));
            Assert.That(new NearestVisibleEnemy().TrySelect(w.Combat, w.Unit(0), SimFix.FromInt(35), out _), Is.True);
        }
        [Test]
        public void RestoreInvalidatesCacheBeforeAnyStep()
        {
            var w = new M5TestWorld(M5TestWorld.Pair(distanceMm: 50000), Pack());
            var snapshot = w.Sim.SaveRetainedSnapshot(); w.Step();
            // Simulate stale derived state: LoadState must clear it even if the previous owner left it valid.
            typeof(CombatSystem).GetField("cacheValid", M7DifferentialTests.Private).SetValue(w.Combat, true);
            w.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: true);
            Assert.That(w.Combat.MayHaveEnemyWithin(w.Unit(0), SimFix.FromInt(35)), Is.True);
            SameSelection(w.Combat, w.Unit(0), SimFix.FromInt(35));
        }
        [Test]
        public void IllegalRangesMissingComponentsAndNeutralShooterUseOriginalSemantics()
        {
            var probe = new Probe();
            var w = new M5TestWorld(new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 1000, weapon: -1),
                M5TestWorld.Spawn(3, 50000, team: 2, weapon: -1), M5TestWorld.Spawn(4, 2000, weapon: -1) }, Pack(probe));
            w.Sim.Entities.Remove(w.Positions, w.Unit(3));
            probe.Check = (c, shooter) => {
                foreach (long raw in new[] { -1L, 0L, 16384L * SimFix.Scale + 1 })
                { Assert.That(c.MayHaveEnemyWithin(shooter, SimFix.FromRaw(raw)), Is.True); SameSelection(c, shooter, SimFix.FromRaw(raw)); }
                var friend = w.Unit(1);
                var unit = w.State(1);
                w.Sim.Entities.Remove(w.Combatants, friend);
                Assert.That(c.MayHaveEnemyWithin(friend, SimFix.FromInt(35)), Is.True); SameSelection(c, friend, SimFix.FromInt(35));
                w.Sim.Entities.Set(w.Combatants, friend, unit);
                Assert.That(c.MayHaveEnemyWithin(w.Unit(3), SimFix.FromInt(35)), Is.True); SameSelection(c, w.Unit(3), SimFix.FromInt(35));
                w.Sim.Entities.Set(w.Combatants, friend, new Combatant(0, -1, 0, 0));
                Assert.That(c.MayHaveEnemyWithin(friend, SimFix.FromInt(35)), Is.False); SameSelection(c, friend, SimFix.FromInt(35));
                // Shooter identity comes from components, not its cached team or position.
                w.Sim.Entities.Set(w.Combatants, friend, new Combatant(2, -1, 0, 0));
                Assert.That(c.MayHaveEnemyWithin(friend, SimFix.FromInt(35)), Is.True); SameSelection(c, friend, SimFix.FromInt(35));
                w.Sim.Entities.Set(w.Combatants, friend, unit);
            };
            w.Step(); Assert.That(w.Combat.QueriesThisStep, Is.GreaterThan(0));
            SameSelection(w.Combat, w.Unit(0), SimFix.FromInt(35));
        }
        [Test]
        public void DeadShooterDirectSelectionIsNotRejected()
        {
            var w = new M5TestWorld(M5TestWorld.Pair(armedTarget: true), M5TestWorld.Pack(new LethalResolver()));
            w.SetDue(1, 2000); w.Step(); Assert.That(w.Dead(1), Is.True);
            SameSelection(w.Combat, w.Unit(1), SimFix.FromInt(35));
            Assert.That(new NearestVisibleEnemy().TrySelect(w.Combat, w.Unit(1), SimFix.FromInt(35), out _), Is.True);
        }
        [TestCase(0)] [TestCase(65536)]
        public void CrowdRadiusMinusOneRawRetainsBlocker(int yRaw)
        {
            var w = new M4TestWorld(M3TestWorld.Boxes(16, 16), new[] {
                new UnitSpawn(1, 0, 1, 1000, 4000, 6000), new UnitSpawn(2, 0, 2, 4000, 4000, 6000) });
            w.Sim.Step(default); // Populate the actual provisional buffer without issuing movement.
            var next = new SimVec3(SimFix.FromRaw(4 * SimFix.Scale - 32768 + 1), SimFix.FromRaw(yRaw), SimFix.FromInt(4));
            var method = typeof(InterimLocomotion).GetMethod("UnitsAllow", M7DifferentialTests.Private);
            object[] args = { 0, next, -1 }; bool fast = (bool)method.Invoke(w.Locomotion, args); int blocker = (int)args[2];
            M7DifferentialTests.DisableCrowd(w.Locomotion); object[] original = { 0, next, -1 };
            Assert.That(fast, Is.EqualTo((bool)method.Invoke(w.Locomotion, original)));
            Assert.That(blocker, Is.EqualTo((int)original[2]));
            Assert.That(fast, Is.EqualTo(yRaw != 0)); Assert.That(blocker, Is.EqualTo(yRaw == 0 ? 1 : -1));
        }
    }
}
