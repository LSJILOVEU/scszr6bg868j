using System;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Control;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    public sealed class Wp16StateTests
    {
        [TestCase(1)] // Dead target captured before fallback.
        [TestCase(2)] // Immediately after the stable switch event.
        [TestCase(75)] // Moving successor and nonzero saved InputFrame.
        public void ReverseCreationAndRestoredWorld_MatchEveryTick_CurrentAndEvents(int saveAt)
        {
            var recipe = Recipe(new[] { U(1), U(2, 14000), U(3, 70000, 35000, team: 2), U(4, 20000) });
            M6World Create(bool reverse)
            {
                var w = new M6World(recipe, reverse: reverse);
                w.Sim.RegisterGameplay("test.wp16-deaths", new ScheduledDeaths(w), SimSystemPhase.Queries);
                return w;
            }
            var a = Create(false); var reverseWorld = Create(true);
            M6World restored = null;
            Assert.That(reverseWorld.Hash, Is.EqualTo(a.Hash));
            Assert.That(reverseWorld.Sim.SaveRetainedSnapshot(), Is.EqualTo(a.Sim.SaveRetainedSnapshot()));
            int changes = 0;
            for (int tick = 1; tick <= 600; tick++)
            {
                // Small closed movement loop, plus release frames. No bridge input/replay API in phase 1.
                int phase = tick % 40;
                var input = Input(phase < 10 ? 1 : phase < 20 ? -1 : 0, phase >= 20 && phase < 30 ? 1 : 0);
                a.Sim.Step(input); reverseWorld.Sim.Step(input);
                Assert.That(reverseWorld.Hash, Is.EqualTo(a.Hash), "reverse tick=" + tick);
                SameEvents(a, reverseWorld); changes += Switches(a);
                Assert.That(reverseWorld.Session.ControlledKey, Is.EqualTo(a.Session.ControlledKey));
                if (restored != null)
                {
                    restored.Sim.Step(input);
                    Assert.That(restored.Hash, Is.EqualTo(a.Hash), "restore tick=" + tick);
                    Assert.That(restored.Session.ControlledKey, Is.EqualTo(a.Session.ControlledKey));
                    Assert.That(restored.System<PlayerControl>().Current,
                        Is.EqualTo(restored.Unit(a.Session.ControlledKey.Value)));
                    SameEvents(a, restored);
                    if (tick % 30 == 0) Assert.That(restored.Sim.SaveRetainedSnapshot(), Is.EqualTo(a.Sim.SaveRetainedSnapshot()));
                }
                if (tick == saveAt)
                {
                    byte[] saved = a.Sim.SaveRetainedSnapshot();
                    restored = Create(true);
                    restored.Sim.RestoreRetainedSnapshot(saved, reverseAllocation: true);
                    Assert.That(restored.Unit(1), Is.Not.EqualTo(a.Unit(1)));
                    Assert.That(restored.Sim.SaveRetainedSnapshot(), Is.EqualTo(saved));
                    Assert.That(restored.Session.ControlledKey, Is.EqualTo(a.Session.ControlledKey));
                    Assert.That(restored.System<PlayerControl>().Current, Is.EqualTo(restored.Unit(a.Session.ControlledKey.Value)));
                    SameEvents(a, restored);
                }
            }
            Assert.That(changes, Is.EqualTo(2)); Assert.That(a.Session.ControlledKey, Is.EqualTo(4UL));
        }

        private sealed class ScheduledDeaths : IRetainedGameplaySystem
        {
            private readonly M6Damage first, second;
            internal ScheduledDeaths(M6World world, long secondTick = 100, ulong[] secondKeys = null)
            { first = new M6Damage(world, 1, 1); second = new M6Damage(world, secondTick, secondKeys ?? new ulong[] { 2 }); }
            public int Order => 25;
            public string StateTypeId => "test.wp16-deaths";
            public int StateVersion => 1;
            public void Step(SimContext context) { first.Step(context); second.Step(context); }
            public void SaveState(SimStateWriter writer) { first.SaveState(writer); second.SaveState(writer); }
            public void LoadState(SimStateReader reader) { first.LoadState(reader); second.LoadState(reader); }
            public void WriteState(StateHash hash) { first.WriteState(hash); second.WriteState(hash); }
        }

        [Test]
        public void SwitchAlongsideMassDeaths_UsesExistingEventCapacityWithoutDrops()
        {
            const int count = 25;
            var units = new UnitSpawn[count]; var victims = new ulong[count - 1];
            for (int i = 0; i < count; i++)
            {
                units[i] = U((ulong)i + 1, 2000 + i * 2000, 2000, team: i == count - 1 ? 2 : 1);
                if (i > 0) victims[i - 1] = (ulong)i + 1;
            }
            var w = new M6World(Recipe(units));
            w.Sim.RegisterGameplay("test.wp16-deaths", new ScheduledDeaths(w, 2, victims), SimSystemPhase.Queries);
            w.Step(); w.Step();
            Assert.That(Switches(w), Is.EqualTo(1));
            Assert.That(w.Sim.Events.Read(2).Length, Is.EqualTo(1 + 3 * (count - 1)));
            for (ulong key = 1; key <= count; key++) Assert.That(w.Dead(key), Is.True);
            w.Step(); Assert.That(Switches(w), Is.Zero); Assert.That(w.Match.Winner, Is.EqualTo(-1));
        }

        [Test]
        public void RecipeCopiesKeepPlayerKey_AndRejectUndeployedOrUnknownKey()
        {
            var source = M6World.RecipeOf(new[] { U(1), U(2, reserve: true), U(3, 70000, 35000, team: 2) });
            var before = source.WithPlayer(1).WithCombat(source.Combat).WithMatch(source.Match);
            var after = source.WithMatch(source.Match).WithCombat(source.Combat).WithPlayer(1);
            Assert.That(source.PlayerKey, Is.Null); Assert.That(before.PlayerKey, Is.EqualTo(1UL));
            Assert.That(new M6World(before).Hash, Is.EqualTo(new M6World(after).Hash));
            byte[] map = WarCore.Content.WcMapWriter.Write(M3TestWorld.Boxes(80, 40));
            var unknown = Assert.Throws<ArgumentException>(() => WorldBuilder.Create(source.WithPlayer(99), map));
            StringAssert.Contains("UnitSpawn.Key", unknown.Message);
            var reserve = Assert.Throws<ArgumentException>(() => WorldBuilder.Create(source.WithPlayer(2), map));
            StringAssert.Contains("deployed", reserve.Message);
            var wrongMover = new WorldRecipe(41, MoverKind.InterimPath, source.Units.ToArray(), Array.Empty<BridgeCommand>())
                .WithCombat(source.Combat).WithPlayer(1);
            Assert.Throws<ArgumentException>(() => WorldBuilder.Create(wrongMover, map));
            Assert.Throws<ArgumentException>(() => WorldBuilder.Create(source.WithCombat(null).WithPlayer(1), map));
            Assert.Throws<ArgumentException>(() => WorldBuilder.Create(source.WithMatch(null).WithPlayer(1), map));
        }

        [Test]
        public void NoPlayer_PassesNullToAllConsumers_RegistersNoControlSystems()
        {
            var w = new M6World(M6World.RecipeOf(new[] { U(1), U(2, 70000, 35000, team: 2) }, Match()));
            Assert.That(w.Session.ControlledKey, Is.Null);
            Assert.That(w.System<PlayerControl>(), Is.Null); Assert.That(w.System<PlayerFallback>(), Is.Null);
            foreach (object consumer in new object[] { w.Combat, w.System<SimpleAiSystem>(), w.System<InterimOrders>() })
                Assert.That(consumer.GetType().GetField("controlled", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(consumer), Is.Null);
            Assert.That(typeof(InterimLocomotion).GetField("controlledStep", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(w.System<InterimLocomotion>()), Is.Null);
        }

        [Test]
        public void NoPlayer_AllCommittedWp13TickAndSnapshotGoldensRemainExact()
        {
            // Reuse the committed, read-only comparison (M5, M6 forward/reverse/narrow and M7, including RNG/events).
            // M1-M4 fixed hashes and snapshot checks remain in their existing regression tests, unchanged.
            new Wp13GoldenTests().EveryTickAndSnapshotMatchesBaseline();
        }

        [Test]
        public void SwitchEventNameAndId_AreRegisteredAndDoNotAliasExistingEvents()
        {
            var registry = new EventTypeRegistry();
            var dropped = registry.Register("events-dropped"); var world = registry.Register("world-change");
            EventTypeId switched = registry.Register(PlayerControl.SwitchEventName);
            Assert.That(switched.Value, Is.EqualTo(1563519857));
            Assert.That(switched, Is.EqualTo(registry.Register("control.switch")));
            Assert.That(switched, Is.Not.EqualTo(dropped)); Assert.That(switched, Is.Not.EqualTo(world));
            Assert.That(switched.Value < 1900201 || switched.Value > 1900213, Is.True);
            Assert.That(switched.Value, Is.Not.EqualTo(2147483646));
        }

        [Test]
        public void WarmSimStep_WithMovementAndFallback_AllocatesZero()
        {
            var w = new M6World(Recipe(new[] { U(1), U(2, 18000), U(3, 70000, 35000, team: 2) }));
            KillAt(w, 150, 1);
            for (int tick = 0; tick < 100; tick++) w.Step();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 600; tick++)
                w.Sim.Step(Input(tick % 20 < 10 ? 1 : -1, 0));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(w.Session.ControlledKey, Is.EqualTo(2UL));
            Assert.That(w.Sim.Events.EventsDroppedTotal, Is.Zero);
        }
    }
}
