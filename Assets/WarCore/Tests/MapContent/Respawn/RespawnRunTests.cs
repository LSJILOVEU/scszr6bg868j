using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Control;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    public sealed class RespawnRunTests
    {
        private const int K = 1, D = 5;
        private static readonly InputFrame Fire = new InputFrame(default, P(0, 20), primaryAttack: true);

        // Three reserves; the controlled unit is killed at K and two ticks after every switch, while the trigger stays held.
        [Test]
        public void RepeatedDeaths_SwitchUntilThePoolIsEmpty()
        {
            var units = Units(U(3, 0, 0, reserve: true, weapon: 0), U(4, 0, 0, reserve: true, weapon: 0), U(5, 0, 0, reserve: true, weapon: 0));
            units[0] = U(Player, 20, 20, weapon: 0);
            long[] kills = { K, K + D + 2, K + 2 * (D + 2), K + 3 * (D + 2) };
            var w = World(units, Pack(Points(Spawn(1, 30, 20)), Bases(new SpawnAnchor(1, P(5, 38))), D), kills: kills);
            var switches = new List<(long tick, ulong key)>();
            var deaths = new Dictionary<ulong, long>();
            for (long tick = 1; tick <= kills[3] + 300; tick++)
            {
                ulong before = Controlled(w);
                Assert.DoesNotThrow(() => w.Sim.Step(Fire), "tick " + tick);
                int count = Switches(w);
                Assert.That(count, Is.LessThanOrEqualTo(1));
                if (count == 1) switches.Add((tick, Controlled(w)));
                foreach (ulong key in new ulong[] { Player, 3, 4, 5 })
                    if (w.Dead(key) && !deaths.ContainsKey(key)) deaths[key] = tick;
                if (Array.IndexOf(kills, tick) >= 0) Assert.That(w.Dead(before), Is.True, "tick " + tick);
            }
            Assert.That(switches, Is.EqualTo(new List<(long, ulong)> { (K + D, Order[0]), (kills[1] + D, Order[1]), (kills[2] + D, Order[2]) }));
            Assert.That(deaths, Is.EqualTo(new Dictionary<ulong, long> { { Player, kills[0] }, { Order[0], kills[1] }, { Order[1], kills[2] }, { Order[2], kills[3] } }));
            foreach (var death in deaths) Assert.That(Entered(w, death.Key), Is.EqualTo(death.Value), "key " + death.Key);
            Assert.That(Controlled(w), Is.EqualTo(Order[2]));
            Assert.That(w.Match.Winner, Is.EqualTo(0));
        }

        // Capture() order of reserve keys 3, 4 and 5 (stable-id order: 459506500295203436, 7857098825631990841, 15958299891356236636).
        internal static readonly ulong[] Order = { 3, 5, 4 };

        // The match ends on time at tick 3, before respawn falls due at K + D: Sim refuses every later step.
        [Test]
        public void MatchEndsBeforeDue_NoSwitch_SimRefusesAndSessionStays()
        {
            var w = World(Units(U(3, 0, 0, reserve: true)), Pack(Points(Spawn(1, 30, 20)), Bases(new SpawnAnchor(1, P(5, 38))), D, max: 3));
            KillAt(w, K, Player);
            w.Step(3);
            Assert.That(w.Match.Winner, Is.Not.Zero); Assert.That(w.Match.EndTick, Is.EqualTo(3));
            for (int i = 0; i < 2 * D; i++)
            {
                Assert.Throws<InvalidOperationException>(() => w.Sim.Step(default));
                w.Session.StepOnce();
                Assert.That(w.Session.Tick, Is.EqualTo(3));
                Assert.That(Controlled(w), Is.EqualTo(Player)); Assert.That(Switches(w), Is.Zero);
                Assert.That(w.Has<Position>(3), Is.False);
            }
        }

        // The time limit falls on the due tick itself: respawn runs in Requests, the match judges later in Queries.
        [Test]
        public void MatchEndsOnTheDueTick_SwitchStillHappens()
        {
            var w = World(Units(U(3, 0, 0, reserve: true)), Pack(Points(Spawn(1, 30, 20)), Bases(new SpawnAnchor(1, P(5, 38))), D, max: K + D));
            KillAt(w, K, Player);
            w.Step(K + D - 1);
            Assert.That(w.Match.Winner, Is.Zero); Assert.That(Controlled(w), Is.EqualTo(Player));
            w.Step();
            Assert.That(w.Match.EndTick, Is.EqualTo(K + D)); Assert.That(w.Match.Winner, Is.Not.Zero);
            Assert.That(Controlled(w), Is.EqualTo(3UL)); Assert.That(Switches(w), Is.EqualTo(1));
        }

        // Saved inside the wait (K + 2) and after the switch (K + 7), restored in forward and reverse allocation, then compared
        // tick by tick for 60 ticks against the run that was never interrupted.
        [TestCase(K + 2, false)]
        [TestCase(K + 2, true)]
        [TestCase(K + 7, false)]
        [TestCase(K + 7, true)]
        public void SnapshotInsideAndAfterTheWait_RestoresToTheSameRun(int saveAt, bool reverse)
        {
            var units = Units(U(3, 0, 0, reserve: true), U(4, 0, 0, reserve: true));
            var pack = Pack(Points(Spawn(1, 30, 20)), Bases(new SpawnAnchor(1, P(5, 38))), D);
            long[] kills = { K, K + D + 20 };
            var original = World(units, pack, kills: kills);
            original.Step(saveAt);
            byte[] saved = original.Sim.SaveRetainedSnapshot();
            var restored = World(units, pack, reverse, kills);
            restored.Sim.RestoreRetainedSnapshot(saved, reverseAllocation: reverse);
            Assert.That(restored.Hash, Is.EqualTo(original.Hash));
            Assert.That(Controlled(restored), Is.EqualTo(Controlled(original)));
            var switchTicks = new List<long>();
            for (int i = 0; i < 60; i++)
            {
                original.Step(); restored.Step();
                long tick = original.Sim.Clock.Tick;
                Assert.That(restored.Sim.Clock.Tick, Is.EqualTo(tick));
                Assert.That(restored.Hash, Is.EqualTo(original.Hash), "tick " + tick);
                Assert.That(Controlled(restored), Is.EqualTo(Controlled(original)), "tick " + tick);
                Assert.That(restored.System<PlayerControl>().Current, Is.EqualTo(restored.Unit(Controlled(restored))));
                Assert.That(Switches(restored), Is.EqualTo(Switches(original)), "tick " + tick);
                if (Switches(original) == 1) switchTicks.Add(tick);
                foreach (ulong key in new ulong[] { 3, 4 })
                {
                    Assert.That(restored.Has<Position>(key), Is.EqualTo(original.Has<Position>(key)));
                    if (original.Has<Position>(key)) Assert.That(Point(restored, key), Is.EqualTo(Point(original, key)));
                }
            }
            Assert.That(switchTicks, Is.EqualTo(saveAt < K + D ? new long[] { K + D, kills[1] + D } : new long[] { kills[1] + D }));
        }
    }
}
