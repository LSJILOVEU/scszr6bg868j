using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class A1BehaviorTests
    {
        private static string Chase(int range) => "{ \"meleeChaseRange\": " + range + " }";
        private static SimVec3 At(M6World w, ulong key) => w.Read<Position>(key).Value;
        private static long FirstThink(M6World w)
        {
            long tick = (A1.Think - (long)(w.Sim.Entities.StableOf(w.Unit(1)).Value % A1.Think)) % A1.Think;
            return tick == 0 ? A1.Think : tick;
        }
        private static void To(M6World w, long tick) { while (w.Sim.Clock.Tick < tick) w.Step(); }
        private static SimWide Distance(M6World w) => CombatSystem.Horizontal(At(w, 1) - At(w, 2)).LengthSquared;

        [Test]
        public void A1_OutOfRangeKeepsRoute()
        {
            var units = new[] { A1.Unit(1, 1, 10, 20), A1.Unit(2, 2, 70, 20) };
            var off = A1.World(null, units); var on = A1.World(Chase(10), units);
            SimWide range = SimVec3.Metres(10, 0, 0).LengthSquared;
            for (int tick = 1; tick <= 300; tick++)
            {
                Assert.That(Distance(on), Is.GreaterThan(range), "before tick " + tick);
                off.Step(); on.Step();
                foreach (UnitSpawn unit in units)
                    Assert.That(At(on, unit.Key), Is.EqualTo(At(off, unit.Key)), "position at tick " + tick);
            }
            Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(off.Read<MoveIntent>(1).Target));
        }

        [Test]
        public void A1_TargetDiesBackToRoute()
        {
            var w = A1.World(Chase(60), new[] { A1.Unit(1, 1, 10, 10), A1.Unit(2, 2, 30, 10, melee: -1, speed: 1) }, endless: true);
            long first = FirstThink(w), next = first + A1.Think;
            w.Sim.RegisterGameplay("test.m6-damage", new M6Damage(w, first + 1, 2), SimSystemPhase.Queries);
            To(w, first - 1); SimVec3 enemy = At(w, 2); w.Step();
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(enemy));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            To(w, next - 1);
            Assert.That(w.Dead(2), Is.True);
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(enemy), "the order lasts until the next thought");
            SimVec3 route = M6AiTests.ExpectedGuard(w.Recipe.Match.Points[0], At(w, 1));
            w.Step();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(next));
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(route));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
        }

        [Test]
        public void A1_ChaseReleasesRememberedGuardSlot()
        {
            var w = A1.World("{ \"meleeChaseRange\": 10, \"failedSlotTicks\": 100 }",
                new[] { A1.Unit(1, 1, 10, 10), A1.Unit(2, 2, 30, 10, melee: -1, speed: 1) });
            long first = FirstThink(w); To(w, first);
            var slots = new GuardSlots(w.Recipe.Match.Points);
            Assert.That(slots.TryFind(w.Read<MoveIntent>(1).Target, -1, out int slot), Is.True);
            Assert.That(L4.Memory(w).ordered, Is.EqualTo(slot));
            To(w, first + A1.Think - 1);
            SimVec3 enemy = At(w, 1) + SimVec3.Metres(5, 0, 0);
            w.Set(2, new Position(enemy)); w.Sim.CompleteSpatialBoundary(); w.Step();
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(enemy));
            Assert.That(L4.Memory(w).ordered, Is.EqualTo(-1));
        }

        [TestCase(-1, true)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        public void A1_ChaseRadiusIncludesBoundary(int rawDelta, bool chases)
        {
            var w = A1.World(Chase(20), new[] { A1.Unit(1, 1, 10, 20), A1.Unit(2, 2, 30, 20, melee: -1, speed: 1) });
            To(w, FirstThink(w) - 1);
            SimVec3 enemy = At(w, 1) + new SimVec3(SimFix.FromRaw(20 * SimFix.Scale + rawDelta), default, default);
            w.Set(2, new Position(enemy)); w.Sim.CompleteSpatialBoundary();
            SimVec3 route = M6AiTests.ExpectedGuard(w.Recipe.Match.Points[0], At(w, 1));
            w.Step();
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(chases ? enemy : route));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
        }

        [Test]
        public void A1_NearestEnemyIsChosen()
        {
            var w = A1.World(Chase(60), new[] { A1.Unit(1, 1, 10, 20), A1.Unit(2, 2, 50, 20, melee: -1, speed: 1),
                A1.Unit(3, 2, 25, 20, melee: -1, speed: 1), A1.Unit(4, 1, 15, 20, melee: -1, speed: 1) });
            To(w, FirstThink(w) - 1); SimVec3 nearest = At(w, 3); w.Step();
            Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(nearest));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
        }

        [Test]
        public void A1_EqualDistanceUsesStableIdentity()
        {
            var units = new[] { A1.Unit(1, 1, 10, 20), A1.Unit(2, 2, 30, 18, melee: -1, speed: 1),
                A1.Unit(3, 2, 30, 22, melee: -1, speed: 1) };
            var normal = A1.World(Chase(60), units);
            var reverse = new M6World(A1.Recipe(Chase(60), units), A1.Map(), reverse: true);
            ulong expectedKey = normal.Sim.Entities.StableOf(normal.Unit(2)).CompareTo(normal.Sim.Entities.StableOf(normal.Unit(3))) < 0 ? 2UL : 3UL;
            foreach (M6World w in new[] { normal, reverse })
            {
                To(w, FirstThink(w) - 1);
                w.Set(2, new Position(SimVec3.Metres(30, 0, 18)));
                w.Set(3, new Position(SimVec3.Metres(30, 0, 22)));
                w.Sim.CompleteSpatialBoundary();
                SimVec3 expected = At(w, expectedKey); w.Step();
                Assert.That(w.Read<MoveIntent>(1).Target, Is.EqualTo(expected));
            }
        }

        [Test]
        public void A1_InvisibleEnemyKeepsRoute()
        {
            var units = new[] { A1.Unit(1, 1, 10, 20), A1.Unit(2, 2, 30, 20, melee: -1, speed: 1) };
            var walls = new[] { (20, 0, 1, 40) };
            var off = A1.World(null, units, walls); var on = A1.World(Chase(60), units, walls);
            var offCalls = L4.Observe(off); var onCalls = L4.Observe(on);
            Assert.That(on.Combat.Visible(on.Unit(1), on.Unit(2)), Is.False);
            To(off, FirstThink(off)); To(on, FirstThink(on));
            Assert.That(onCalls.Orders, Is.EqualTo(offCalls.Orders));
            Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(off.Read<MoveIntent>(1).Target));
            Assert.That(on.Read<MoveIntent>(1).Target, Is.Not.EqualTo(At(on, 2)));
            Assert.That(At(on, 1), Is.EqualTo(At(off, 1)));
        }

        [Test]
        public void A1_ArmedUnitKeepsRouteBeyondGunRange()
        {
            var units = new[] { A1.Unit(1, 1, 10, 20, weapon: 0), A1.Unit(2, 2, 50, 20, melee: -1, speed: 1) };
            var off = A1.World(null, units); var on = A1.World(Chase(60), units);
            To(off, FirstThink(off) - 1); To(on, FirstThink(on) - 1);
            Assert.That(Distance(on), Is.GreaterThan(new SimVec3(on.Recipe.Combat.Weapons[0].FalloffEnd, default, default).LengthSquared));
            Assert.That(on.Combat.Visible(on.Unit(1), on.Unit(2)), Is.True);
            off.Step(); on.Step();
            Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(off.Read<MoveIntent>(1).Target));
            Assert.That(on.Read<MoveIntent>(1).Target, Is.Not.EqualTo(At(on, 2)));
            Assert.That(At(on, 1), Is.EqualTo(At(off, 1)));
        }

        [Test]
        public void A1_UnitWithoutMeleeKeepsRoute()
        {
            var units = new[] { A1.Unit(1, 1, 10, 20, melee: -1), A1.Unit(2, 2, 30, 20, melee: -1, speed: 1) };
            var off = A1.World(null, units); var on = A1.World(Chase(60), units);
            To(off, FirstThink(off)); To(on, FirstThink(on));
            Assert.That(on.Read<MoveIntent>(1).Target, Is.EqualTo(off.Read<MoveIntent>(1).Target));
            Assert.That(At(on, 1), Is.EqualTo(At(off, 1)));
        }

        [TestCase(-1, true, true)]
        [TestCase(0, true, true)]
        [TestCase(1, false, true)]
        [TestCase(0, true, false)]
        public void A1_StopsAtOwnEngageRange(int rawDelta, bool stops, bool moving)
        {
            var recipe = A1.Recipe(Chase(60), new[] { A1.Unit(1, 1, 10, 20, melee: 1), A1.Unit(2, 2, 30, 20, melee: -1, speed: 1) })
                .WithCombat(MeleeWorld.Pack(new[] { MeleeWorld.Spec(), MeleeWorld.Spec(engage: 1.2) }));
            var w = new M6World(recipe, A1.Map());
            To(w, FirstThink(w) - 1);
            SimVec3 here = At(w, 1);
            long reach = w.Recipe.Combat.Melee[1].EngageRange.Raw;
            w.Set(2, new Position(here + new SimVec3(SimFix.FromRaw(reach + rawDelta), default, default)));
            w.Sim.CompleteSpatialBoundary();
            if (moving) L4.Moves(w).Order(w.Sim.Entities.StableOf(w.Unit(1)), SimVec3.Metres(60, 0, 20));
            w.Step();
            Assert.That(w.Read<MoveIntent>(1).Active, Is.EqualTo(!stops));
            if (stops)
            {
                Assert.That(At(w, 1), Is.EqualTo(here));
                Assert.That(w.Read<MeleeState>(1).Pending, Is.True);
            }
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        [Test]
        public void A1_EnabledStepsDoNotAllocate()
        {
            var w = A1.World(Chase(80), new[] { A1.Unit(1, 1, 10, 10), A1.Unit(2, 2, 70, 10, melee: -1, speed: 1) });
            w.Step(20); SimWide beforeDistance = Distance(w);
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            AllocationMeter.Stabilize();
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            w.Step(180);
            long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(200));
            Assert.That(Distance(w), Is.LessThan(beforeDistance));
            Assert.That(w.Read<MoveIntent>(1).Active, Is.True);
            Assert.That(bytes, Is.EqualTo(0L));
        }
#endif
    }
}
