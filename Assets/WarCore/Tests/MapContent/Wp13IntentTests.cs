using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Wp13IntentTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void AutoEqualsAtTarget_AllCombatObservationsAndSelectorCalls(bool lethal)
        {
            var autoSpy = new Wp13SelectorSpy(); var intentSpy = new Wp13SelectorSpy();
            IHitResolver resolver = lethal ? (IHitResolver)new RwrHitResolver() : new ZeroResolver();
            var a = new Wp13TestWorld(pack: M5TestWorld.Pack(resolver, autoSpy), controlled: false);
            var b = new Wp13TestWorld(pack: M5TestWorld.Pack(resolver, intentSpy), reverse: true);
            int submitted = 0, autoQueries = 0, shots = 0;
            b.Produce = c =>
            {
                var unit = b.State(0);
                if (b.Dead(0) || unit.DueMilli > c.Clock.Tick * 1000) return;
                if (intentSpy.TrySelect(b.Combat, b.Unit(0), b.Pack.Weapons[unit.Weapon].FalloffEnd, out EntityId target))
                { b.Combat.Submit(c, FireIntent.AtTarget(b.Id(0), b.Sim.Entities.StableOf(target))); submitted++; }
            };
            for (int tick = 1; tick <= 600; tick++)
            {
                a.Step(); b.Step(); Wp13TestWorld.EqualCombat(a, b, autoVsIntent: true);
                Assert.That(intentSpy.Calls, Is.EqualTo(autoSpy.Calls), "selector must not run again inside intent Step");
                Assert.That(b.Combat.QueriesThisStep, Is.Zero, "producer queries are outside Combat.Step");
                autoQueries += a.Combat.QueriesThisStep; shots += a.Combat.Fires.Length;
            }
            Assert.That(shots, Is.GreaterThan(0)); Assert.That(submitted, Is.EqualTo(shots));
            Assert.That(autoQueries, Is.GreaterThan(0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FourArmedShooters_AutoEqualsAtTarget_RandomDrawsAndEventOrder(bool reverse)
        {
            var units = new[] { M5TestWorld.Spawn(1, 0), M5TestWorld.Spawn(2, 10000, team: 2),
                M5TestWorld.Spawn(3, 0, 100000), M5TestWorld.Spawn(4, 20000, 100000, team: 2) };
            var autoSpy = new Wp13SelectorSpy(); var intentSpy = new Wp13SelectorSpy();
            var a = new Wp13TestWorld(units, M5TestWorld.Pack(new RollDamageResolver(), autoSpy), controlled: false);
            var b = new Wp13TestWorld(units, M5TestWorld.Pack(new RollDamageResolver(), intentSpy), reverse: reverse, controlled: false);
            var rng = Wp13AimedTests.ReplayRandom(); var stream = rng.Stream("combat.hit");
            var order = new[] { a.Id(0), a.Id(1), a.Id(2), a.Id(3) }; Array.Sort(order);
            int submitted = 0, volleys = 0;
            b.Produce = c =>
            {
                // Submit in local slot order; Combat must consume in stable identity order.
                for (int slot = 0; slot < b.Count; slot++)
                {
                    int i = reverse ? b.Count - slot - 1 : slot;
                    var unit = b.State(i);
                    if (b.Dead(i) || unit.DueMilli > c.Clock.Tick * 1000) continue;
                    Assert.That(intentSpy.TrySelect(b.Combat, b.Unit(i), b.Pack.Weapons[unit.Weapon].FalloffEnd, out EntityId target), Is.True);
                    Assert.That(target, Is.EqualTo(b.Unit(i ^ 1)), "each armed pair exchanges fire");
                    b.Combat.Submit(c, FireIntent.AtTarget(b.Id(i), b.Sim.Entities.StableOf(target))); submitted++;
                }
            };
            for (int tick = 1; tick <= 600; tick++)
            {
                a.Step(); b.Step(); Wp13TestWorld.EqualCombat(a, b, autoVsIntent: true);
                Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "tick=" + tick);
                Assert.That(intentSpy.Calls, Is.EqualTo(autoSpy.Calls));
                Assert.That(b.Combat.QueriesThisStep, Is.Zero);
                if (a.Combat.Fires.Length != 0)
                {
                    volleys++;
                    Assert.That(a.Combat.Fires.Length, Is.EqualTo(4));
                    Assert.That(a.EventCount(GameplayEvents.Damage), Is.EqualTo(4));
                    for (int i = 0; i < 4; i++)
                    {
                        Assert.That(a.Combat.Fires[i].Shooter, Is.EqualTo(order[i]));
                        Assert.That(a.Combat.Fires[i].DamageRaw, Is.EqualTo(1L + rng.NextInt(stream, 65536) / 64));
                    }
                }
                Assert.That(Wp13TestWorld.Hash(a.Sim.Rng), Is.EqualTo(Wp13TestWorld.Hash(rng)));
            }
            Assert.That(volleys, Is.GreaterThan(90)); Assert.That(submitted, Is.EqualTo(volleys * 4));
        }

        private sealed class RollDamageResolver : IHitResolver
        {
            public ulong PolicyKey => 0x5750313302UL;
            // Distinct rolls remain visible in FireRecord and events without killing the four shooters.
            public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) => 1L + rollRaw / 64;
        }

        private sealed class DirectSelector : ITargetSelector
        {
            internal int Calls;
            public ulong PolicyKey => 0x5750313301UL;
            public bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target)
            {
                Calls++;
                target = world.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, 2).Id));
                return true;
            }
        }

        // WP-17 (was: a selected target 100 m away, beyond the 67 m weapon range, was hit anyway). The shot is still fired,
        // but the ray ends at weapon range: nothing is struck and no damage is dealt. The selector is still called once.
        [Test]
        public void AutoTrustsSelectorWithoutRepeatingFindValidation()
        {
            var selector = new DirectSelector();
            var w = new Wp13TestWorld(M5TestWorld.Pair(distanceMm: 100000),
                M5TestWorld.Pack(new ZeroResolver(), selector), controlled: false);
            w.Step();
            Assert.That(selector.Calls, Is.EqualTo(1)); Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            Assert.That(w.Combat.Fires[0].Outcome, Is.EqualTo(FireOutcome.Open));
            Assert.That(w.Combat.Fires[0].Distance, Is.EqualTo(w.Pack.Weapons[0].FalloffEnd));
            Assert.That(w.EventCount(GameplayEvents.Damage), Is.Zero);
        }

        [Test]
        public void ControlledWithoutIntent600Ticks_ReleaseFiresOnFirstReadyTick()
        {
            var spy = new Wp13SelectorSpy(); var w = new Wp13TestWorld(pack: M5TestWorld.Pack(new ZeroResolver(), spy));
            for (int i = 0; i < 600; i++) { w.Step(); Assert.That(w.Combat.Fires.Length, Is.Zero); }
            Assert.That(spy.Calls, Is.Zero, "no early selector call for controlled shooter");
            w.Produce = c => w.Control.Switch(c, default);
            w.Step(); Assert.That(w.Combat.Fires.Length, Is.EqualTo(1)); Assert.That(spy.Calls, Is.EqualTo(1));
        }

        [Test]
        public void RequestsIntentThenMovementOutOfRange_IsNone()
        {
            var w = new Wp13TestWorld(); var initial = w.State(0); ulong rng = Wp13TestWorld.Hash(w.Sim.Rng);
            w.Requests = c => w.Target(c, 0, 1);
            w.Move = c => c.SubmitMovement(w.Unit(1), SimVec3.Metres(100, 0, 0));
            w.Step();
            Assert.That(w.Point(1), Is.EqualTo(SimVec3.Metres(100, 0, 0)));
            Assert.That(w.Combat.Fires.Length, Is.Zero); Assert.That(w.State(0), Is.EqualTo(initial));
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(rng)); Assert.That(w.Sim.Events.Read(1).Length, Is.Zero);
        }

        [TestCase("friendly")]
        [TestCase("neutral")]
        [TestCase("position")]
        [TestCase("range")]
        [TestCase("wall")]
        public void AtTargetInvalidAtConsumption_IsNone(string reason)
        {
            var map = reason == "wall" ? M2DemoMap.Build() : null;
            var w = new Wp13TestWorld(new[] { M5TestWorld.Spawn(1, 20000, 2000),
                M5TestWorld.Spawn(2, reason == "range" ? 100000 : 28000, 2000, team: 2, weapon: -1) }, map: map);
            var initial = w.State(0); ulong rng = Wp13TestWorld.Hash(w.Sim.Rng);
            w.Produce = c =>
            {
                w.Target(c, 0, 1);
                var u = w.State(1);
                if (reason == "friendly" || reason == "neutral")
                    w.Sim.Entities.Set(w.Combatants, w.Unit(1), new Combatant(reason == "friendly" ? 1 : 0, u.Weapon, u.Ammo, u.DueMilli));
                if (reason == "position") w.Sim.Entities.Remove(w.Positions, w.Unit(1));
            };
            // Position removal belongs before spatial publication; Submit itself is valid in Requests.
            if (reason == "position") { w.Requests = w.Produce; w.Produce = null; }
            w.Step(); Assert.That(w.Combat.Fires.Length, Is.Zero); Assert.That(w.State(0), Is.EqualTo(initial));
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(rng)); Assert.That(w.EventCount(GameplayEvents.Damage), Is.Zero);
        }

        [Test]
        public void SubmitRejectsInactiveLateForeignAndInvalidIdentities()
        {
            var w = new Wp13TestWorld(); var other = new Wp13TestWorld();
            var valid = FireIntent.AtTarget(w.Id(0), w.Id(1));
            Assert.Throws<InvalidOperationException>(() => w.Combat.Submit(w.Sim.Context, valid));
            Assert.Throws<InvalidOperationException>(() => w.Combat.Submit(null, valid));
            other.Produce = c => Assert.Throws<InvalidOperationException>(() => w.Combat.Submit(c, valid));
            other.Step();
            w.Produce = c =>
            {
                Assert.Throws<ArgumentException>(() => w.Combat.Submit(c, default));
                Assert.Throws<ArgumentException>(() => w.Combat.Submit(c, FireIntent.AtTarget(default, w.Id(1))));
                Assert.Throws<ArgumentException>(() => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), default)));
            };
            w.After = c => Assert.Throws<InvalidOperationException>(() => w.Combat.Submit(c, valid));
            w.Step();
            Assert.Throws<InvalidOperationException>(() => w.Combat.Submit(w.Sim.Context, valid));
        }

        [Test]
        public void InvalidSubmissionPreservesEarlierValidIntent_AndRejectsMissingNonzeroIdentity()
        {
            var w = new Wp13TestWorld();
            var missing = StableEntityKey.Placed(1, 999).Id;
            Assert.That(missing.Value, Is.Not.Zero);
            Assert.That(w.Sim.Entities.TryResolve(missing, out EntityRef _), Is.False);
            w.Produce = c =>
            {
                w.Target(c, 0, 1);
                Assert.Throws<ArgumentException>(() => w.Combat.Submit(c, FireIntent.AtTarget(w.Id(0), missing)));
                Assert.Throws<ArgumentException>(() => w.Combat.Submit(c, FireIntent.AtTarget(missing, w.Id(1))));
                Assert.Throws<ArgumentException>(() => w.Combat.Submit(c, FireIntent.Aimed(missing, w.Point(1))));
                Assert.That((int)Field("intentCount").GetValue(w.Combat), Is.EqualTo(1));
            };
            w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            Assert.That(w.Combat.Fires[0].Shooter, Is.EqualTo(w.Id(0)));
            Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(w.Id(1)));
            Assert.That(w.State(0).Ammo, Is.EqualTo(29));
            Assert.That(w.EventCount(GameplayEvents.Damage), Is.EqualTo(1)); Empty(w);
        }

        [Test]
        public void SubmitRejectsDeadShooterAndTarget_ConsumptionAlsoRejectsDeadTarget()
        {
            var w = new Wp13TestWorld(pack: M5TestWorld.Pack(new LethalResolver()));
            w.Produce = c => w.Target(c, 0, 1); w.Step(); Assert.That(w.Dead(1), Is.True);
            w.SetDue(0, 0);
            w.Produce = c =>
            {
                Assert.Throws<ArgumentException>(() => w.Target(c, 0, 1));
                Assert.Throws<ArgumentException>(() => w.Combat.Submit(c, FireIntent.Aimed(w.Id(1), w.Point(0))));
                // Bypass admission to model a target that ceased to be alive after submission.
                Seed(w, FireIntent.AtTarget(w.Id(0), w.Id(1)), c.Clock.Tick);
            };
            var initial = w.State(0); ulong rng = Wp13TestWorld.Hash(w.Sim.Rng);
            w.Step(); Assert.That(w.Combat.Fires.Length, Is.Zero); Assert.That(w.State(0), Is.EqualTo(initial));
            Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(rng)); Empty(w);
        }

        [Test]
        public void IntentCapacityCountsShooters_DuplicateOverwriteDoesNotConsumeCapacity()
        {
            var w = new Wp13TestWorld(capacity: 1, map: M2DemoMap.Build());
            Assert.That(w.Unit(0).Id, Is.GreaterThanOrEqualTo(1), "walls occupy slots before units");
            w.Produce = c =>
            {
                w.Target(c, 0, 1); w.Target(c, 0, 0); // last wins: self is not an enemy
                Assert.That(((int[])Field("intentSlots").GetValue(w.Combat)).Length, Is.EqualTo(1));
                Assert.That((int)Field("intentCount").GetValue(w.Combat), Is.EqualTo(1));
                Assert.Throws<InvalidOperationException>(() => w.Target(c, 1, 0));
            };
            w.Step(); Assert.That(w.Combat.Fires.Length, Is.Zero); Assert.That(w.State(0).Ammo, Is.EqualTo(30)); Empty(w);
        }

        [Test]
        public void LastIntentWins_SelectorNeverCalledForExplicitNoneOrHit()
        {
            var spy = new Wp13SelectorSpy(); var w = new Wp13TestWorld(pack: M5TestWorld.Pack(new ZeroResolver(), spy));
            w.Produce = c => { w.Target(c, 0, 1); w.Target(c, 0, 0); };
            w.Step(); Assert.That(w.Combat.Fires.Length, Is.Zero); Assert.That(spy.Calls, Is.Zero); Empty(w);
            w.Produce = c => { w.Target(c, 0, 0); w.Target(c, 0, 1); };
            w.Step(); Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(w.Id(1))); Assert.That(spy.Calls, Is.Zero); Empty(w);
        }

        [Test]
        public void NotReadyDropsIntentBeforeFindAndSelector_PLACEHOLDER()
        {
            var spy = new Wp13SelectorSpy();
            // Would overflow the ray buffer if Find runs before the readiness check.
            var w = new Wp13TestWorld(capacity: 1, pack: M5TestWorld.Pack(new ZeroResolver(), spy));
            w.SetDue(0, 2000); w.Produce = c => w.Aim(c, 0, w.Point(1));
            w.Step(); Empty(w); Assert.That(w.Combat.Fires.Length, Is.Zero); Assert.That(spy.Calls, Is.Zero);
            w.Produce = null; w.Step(); Assert.That(w.Combat.Fires.Length, Is.Zero); Assert.That(w.State(0).Ammo, Is.EqualTo(30));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void StaleTickIgnoredAndBufferCleared_EvenWithoutSubmission(bool controlled)
        {
            var spy = new Wp13SelectorSpy(); var w = new Wp13TestWorld(controlled: controlled, pack: M5TestWorld.Pack(new ZeroResolver(), spy));
            w.Produce = c => Seed(w, FireIntent.AtTarget(w.Id(0), w.Id(0)), c.Clock.Tick - 1);
            w.Step(); Assert.That(w.Combat.Fires.Length, Is.EqualTo(controlled ? 0 : 1));
            Assert.That(spy.Calls, Is.EqualTo(controlled ? 0 : 1)); Empty(w);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void SameTickWrongShooterInSlotIsIgnored(bool controlled, bool reverse)
        {
            var spy = new Wp13SelectorSpy();
            var w = new Wp13TestWorld(controlled: controlled, reverse: reverse, pack: M5TestWorld.Pack(new ZeroResolver(), spy));
            var initial = w.State(0); ulong rng = Wp13TestWorld.Hash(w.Sim.Rng);
            // Unit 0's slot has the wrong owner. Without the identity guard, controlled fires
            // an unauthorized hit, while Auto consumes a self-target None instead of selecting.
            w.Produce = c => Seed(w, FireIntent.AtTarget(w.Id(1), controlled ? w.Id(1) : w.Id(0)), c.Clock.Tick);
            w.Step();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(controlled ? 0 : 1));
            Assert.That(spy.Calls, Is.EqualTo(controlled ? 0 : 1));
            if (controlled)
            {
                Assert.That(w.State(0), Is.EqualTo(initial));
                Assert.That(Wp13TestWorld.Hash(w.Sim.Rng), Is.EqualTo(rng));
            }
            else Assert.That(w.Combat.Fires[0].Target, Is.EqualTo(w.Id(1)));
            Empty(w);
        }

        // Mutation probes intentionally inspect transient storage: next-tick behavior alone cannot
        // distinguish a missing clear from a working tick guard (or the reverse).
        internal static FieldInfo Field(string name) => typeof(CombatSystem).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        internal static void Seed(Wp13TestWorld w, FireIntent intent, long tick)
        {
            var entries = (FireIntent[])Field("intents").GetValue(w.Combat);
            var ticks = (long[])Field("intentTicks").GetValue(w.Combat);
            entries[w.Unit(0).Id] = intent; ticks[w.Unit(0).Id] = tick;
            ((int[])Field("intentSlots").GetValue(w.Combat))[0] = w.Unit(0).Id;
            Field("intentCount").SetValue(w.Combat, 1); Field("intentTick").SetValue(w.Combat, tick);
        }
        internal static void Empty(Wp13TestWorld w)
        {
            Assert.That((int)Field("intentCount").GetValue(w.Combat), Is.Zero);
            foreach (var intent in (FireIntent[])Field("intents").GetValue(w.Combat)) Assert.That(intent, Is.EqualTo(default(FireIntent)));
            foreach (long tick in (long[])Field("intentTicks").GetValue(w.Combat)) Assert.That(tick, Is.Zero);
        }
    }
}
