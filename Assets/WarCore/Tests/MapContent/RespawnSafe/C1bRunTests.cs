using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Control;
using WarCore.Sim;
using static WarCore.Tests.C1bAnchorTests;
using static WarCore.Tests.RespawnWorld;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    public sealed class C1bRunTests
    {
        // (10) The enemy-in-A world saved inside the wait (K + 2), restored forward and reversed, compared for 20 ticks.
        [TestCase(false)]
        [TestCase(true)]
        public void SnapshotInsideTheWait_RestoresToTheSameRun(bool reverse)
        {
            var units = Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2));
            long[] kills = { K };
            var original = World(units, AbPack(), kills: kills);
            original.Step(K + 2);
            byte[] saved = original.Sim.SaveRetainedSnapshot();
            var restored = World(units, AbPack(), reverse, kills);
            restored.Sim.RestoreRetainedSnapshot(saved, reverseAllocation: reverse);
            Assert.That(restored.Hash, Is.EqualTo(original.Hash));
            var switchTicks = new List<long>();
            for (int i = 0; i < 20; i++)
            {
                original.Step(); restored.Step();
                long tick = original.Sim.Clock.Tick;
                Assert.That(restored.Sim.Clock.Tick, Is.EqualTo(tick));
                Assert.That(restored.Hash, Is.EqualTo(original.Hash), "tick " + tick);
                Assert.That(Controlled(restored), Is.EqualTo(Controlled(original)), "tick " + tick);
                Assert.That(restored.System<PlayerControl>().Current, Is.EqualTo(restored.Unit(Controlled(restored))));
                Assert.That(Switches(restored), Is.EqualTo(Switches(original)), "tick " + tick);
                if (Switches(original) == 1) switchTicks.Add(tick);
                Assert.That(restored.Has<Position>(Reserve), Is.EqualTo(original.Has<Position>(Reserve)), "tick " + tick);
                if (original.Has<Position>(Reserve)) Assert.That(Point(restored, Reserve), Is.EqualTo(Point(original, Reserve)), "tick " + tick);
            }
            Assert.That(switchTicks, Is.EqualTo(new long[] { K + D }));
            Assert.That(Point(original, Reserve), Is.EqualTo(P(39, 19)));
        }

        // (11) Two reserves, killed at 1 and 8, A contested throughout: switches at 6 and 13 to keys 3 then 4, both at B's cell 0.
        // The first reserve is dead by tick 13; deployment skips dead units, so its body does not take the cell.
        [Test]
        public void RepeatedRespawns_BothSkipTheContestedPoint()
        {
            const ulong second = 4;
            var w = World(Units(U(Reserve, 0, 0, reserve: true), U(second, 0, 0, reserve: true), U(Other, 24, 21, team: 2)), AbPack(), kills: new long[] { 1, 8 });
            var switches = new List<(long tick, ulong key, SimVec3 at)>();
            for (long tick = 1; tick <= 20; tick++)
            {
                w.Step();
                Assert.That(InsideA(w, Other), Is.True, "tick " + tick);
                if (Switches(w) == 1)
                {
                    ulong key = Controlled(w);
                    Assert.That(w.Has<Position>(key), Is.True, "tick " + tick);
                    switches.Add((tick, key, Point(w, key)));
                }
            }
            Assert.That(w.Dead(Reserve), Is.True);
            Assert.That(switches, Is.EqualTo(new List<(long, ulong, SimVec3)> { (6, Reserve, P(39, 19)), (13, second, P(39, 19)) }));
        }
    }
}
