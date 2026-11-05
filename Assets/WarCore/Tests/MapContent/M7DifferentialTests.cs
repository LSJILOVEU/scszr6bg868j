using System;
using System.Collections.Generic;
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
    // Frozen fa5c23c selector, including its policy identity, for independent execution of the old path.
    internal sealed class M7OriginalNearest : ITargetSelector
    {
        public ulong PolicyKey => 0x4E45415245535401UL;
        public bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target)
        {
            target = default;
            bool found = false;
            SimWide best = default;
            StableId bestId = default;
            SimWide limit = new SimVec3(range, default, default).LengthSquared;
            foreach (var hit in world.Candidates(shooter, range))
            {
                EntityId candidate = hit.Entity;
                if (!world.IsEnemy(shooter, candidate)) continue;
                SimWide distance = CombatSystem.Horizontal(world.Point(candidate) - world.Point(shooter)).LengthSquared;
                StableId id = world.Entities.StableOf(candidate);
                if (distance > limit || (found && (distance > best || (distance == best && id.CompareTo(bestId) >= 0)))) continue;
                if (!world.Visible(shooter, candidate)) continue;
                target = candidate; best = distance; bestId = id; found = true;
            }
            return found;
        }
    }
    public sealed class M7DifferentialTests
    {
        internal static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static void DisableCrowd(InterimLocomotion locomotion) =>
            typeof(InterimLocomotion).GetField("useCrowdBroadPhase", Private).SetValue(locomotion, false);
        internal static WorldRecipe OriginalSelector(WorldRecipe recipe)
        {
            var p = recipe.Combat;
            ITargetSelector old = new M7OriginalNearest();
            if (p.Selector is SightLimited sight) old = new SightLimited(old, sight.Sight);
            return recipe.WithCombat(new CombatPack(p.Weapons.ToArray(), p.Resolver, old, p.MaxHealthRaw, p.FriendlyFire, p.FriendlyFireLocked, p.AllyCheckMissRaw, p.AllyHoldMilli));
        }
        internal sealed class TraceOrders : IMoveOrders
        {
            private readonly IMoveOrders inner;
            internal readonly List<(bool stop, StableId unit, SimVec3 target)> Calls = new List<(bool, StableId, SimVec3)>();
            internal TraceOrders(IMoveOrders inner) { this.inner = inner; }
            public void Order(StableId unit, SimVec3 target) { Calls.Add((false, unit, target)); inner.Order(unit, target); }
            public void Stop(StableId unit) { Calls.Add((true, unit, default)); inner.Stop(unit); }
            public bool TryGetOrder(StableId unit, out SimVec3 target) => inner.TryGetOrder(unit, out target);
            public bool CanStandAt(SimVec3 at) => inner.CanStandAt(at);
        }
        private static TraceOrders Trace(M6World w)
        {
            var observed = M6AiTests.Observe(w);
            var trace = new TraceOrders(observed);
            typeof(SimpleAiSystem).GetField("moves", Private).SetValue(w.System<SimpleAiSystem>(), trace);
            return trace;
        }
        private static void Compare(M6World a, M6World b, int ticks, bool untilEnd = false)
        {
            var left = Trace(a); var right = Trace(b);
            Assert.That(b.Hash, Is.EqualTo(a.Hash), "initial");
            for (int tick = 1; tick <= ticks; tick++)
            {
                left.Calls.Clear(); right.Calls.Clear(); a.Step(); b.Step();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick=" + tick);
                Assert.That(right.Calls, Is.EqualTo(left.Calls), "orders tick=" + tick);
                Assert.That(b.Combat.Fires.Length, Is.EqualTo(a.Combat.Fires.Length), "fires tick=" + tick);
                for (int i = 0; i < a.Combat.Fires.Length; i++)
                {
                    var x = a.Combat.Fires[i]; var y = b.Combat.Fires[i];
                    Assert.That(y.Shooter, Is.EqualTo(x.Shooter)); Assert.That(y.Target, Is.EqualTo(x.Target));
                    Assert.That(y.Distance, Is.EqualTo(x.Distance));
                }
                if (untilEnd && a.Match.Winner != 0) break;
            }
        }
        [TestCase(41)] [TestCase(42)] [TestCase(43)] [TestCase(44)] [TestCase(45)]
        [TestCase(46)] [TestCase(47)] [TestCase(48)] [TestCase(49)] [TestCase(50)]
        public void CombatM6EveryTick(int seed)
        {
            var r = M7BehaviorLockTests.Demo((ulong)seed); var map = DemoRecipes.M6MapBytes();
            Compare(new M6World(r, map), new M6World(OriginalSelector(r), map), 9000, true);
        }
        [Test]
        public void CombatRealEveryTick()
        {
            var a = M7BehaviorLockTests.Real(endless: true);
            Compare(a, new M6World(OriginalSelector(a.Recipe), M7BehaviorLockTests.RealMap()), 900);
        }
        [Test]
        public void WallDeathReservesNeutralAndTeamZeroEveryTick()
        {
            var units = new[] { M6World.U(1, 1, 5, weapon: 0), M6World.U(2, 2, 15),
                M6World.U(3, 2, 30, weapon: 0), M6World.U(4, 1, reserve: true, weapon: 0),
                M6World.U(5, 2, reserve: true, weapon: 0), M6World.U(6, 0, 9), M6World.U(7, 3, 50) };
            var match = M6World.Pack(ticks: 100000, interval: 5, think: 1);
            var r = M6World.RecipeOf(units, match, M6World.Guns(new LethalResolver()));
            var map = WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (18, 0, 1, 30)));
            var a = new M6World(r, map); var b = new M6World(OriginalSelector(r), map);
            Compare(a, b, 600);
            Assert.That(a.Dead(2), Is.True); Assert.That(a.Reinforce.Successes(1), Is.GreaterThan(0));
            Assert.That(a.Reinforce.Successes(2), Is.GreaterThan(0));
        }
        [TestCase(false)] [TestCase(true)]
        public void CrowdM6EveryTick(bool narrow)
        {
            for (ulong seed = 41; seed <= 50; seed++)
            {
                var r = M7BehaviorLockTests.Demo(seed); var map = narrow ? M7BehaviorLockTests.NarrowMap() : DemoRecipes.M6MapBytes();
                var a = new M6World(r, map); var b = new M6World(r, map);
                DisableCrowd(b.System<InterimLocomotion>()); Compare(a, b, 9000, true);
            }
        }
        [Test]
        public void CrowdM4NarrowOpeningEveryTick()
        {
            var map = M3TestWorld.Boxes(32, 12, (15, 0, 1, 5), (15, 7, 1, 5));
            var units = new UnitSpawn[20];
            for (int i = 0; i < 20; i++) units[i] = new UnitSpawn((ulong)i + 1, 0, i + 1,
                i < 10 ? 2500 : 28500, 1500 + i % 10 * 1000, 6000);
            var a = new M4TestWorld(map, units); var b = new M4TestWorld(map, units); DisableCrowd(b.Locomotion);
            for (int i = 0; i < 20; i++) { a.Order(i, i < 10 ? 28500 : 2500, 1500 + i % 10 * 1000); b.Order(i, i < 10 ? 28500 : 2500, 1500 + i % 10 * 1000); }
            for (int tick = 1; tick <= 1200; tick++)
            { a.Sim.Step(default); b.Sim.Step(default); Assert.That(b.Sim.StateHashRetained(), Is.EqualTo(a.Sim.StateHashRetained()), "tick=" + tick); }
        }
        [Test]
        public void CrowdM4DemoWithOriginalCommandsEveryTick()
        {
            var a = new M6World(DemoRecipes.M4(), DemoRecipes.M2bMapBytes());
            var b = new M6World(DemoRecipes.M4(), DemoRecipes.M2bMapBytes());
            DisableCrowd(b.System<InterimLocomotion>());
            for (int tick = 1; tick <= 600; tick++)
            {
                a.Session.StepOnce(); b.Session.StepOnce();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "M4 demo tick=" + tick);
            }
            Assert.That(a.Hash, Is.EqualTo(0x605D078885B2B633UL));
        }
        [Test]
        public void CrowdRealEveryTick()
        {
            var a = M7BehaviorLockTests.Real(endless: true); var b = M7BehaviorLockTests.Real(endless: true);
            DisableCrowd(b.System<InterimLocomotion>()); Compare(a, b, 900);
        }
    }
}
