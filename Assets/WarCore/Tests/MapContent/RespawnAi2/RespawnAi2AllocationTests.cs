// Strict direct-byte proof needs the headless CoreCLR run (as the M0c2 allocation tests); Unity's counter cannot prove zero.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Presentation;
using static WarCore.Tests.RespawnAi2;
using static WarCore.Tests.RespawnAi2World;

namespace WarCore.Tests
{
    // RESPAWN-AI-2 A13: with the switch on and a chooser bound, a step that runs the whole path (the count of the dead, the
    // list, the chooser, every grid cell of every entry) does not allocate. One of team 1 is dead and due, and eight of team 1
    // stand on the cells of B1 and P, so every step team 1 is owed one, asks the chooser, fails on every cell and tries again.
    // 60 ticks warm up after the death is due, then four measured windows of 100 ticks, each after a collection, after a
    // positive control of the counter.
    public sealed class RespawnAi2AllocationTests
    {
        [Test]
        public void A13_TheDebtPathDoesNotAllocate()
        {
            var blockers = new List<UnitSpawn>(Blockers(40, B1));
            blockers.AddRange(Blockers(50, PCentre));
            M6World w = World(Units(4, blockers.ToArray()), Pack());
            int calls = 0;
            w.Reinforce.BindChooser(new RespawnAi1.Rule(92, (team, attempt, count) => { calls++; return 0; }));
            Kill(w, 10, 2);
            w.Step(10 + Reentry + 60);
            Assert.That((Counts(w, 1), calls), Is.EqualTo(((1L, 0L, 1L, 0L), 61)), "owed one, asked every step since it was due");

            long c0 = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - c0;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768), "the counter works");

            int before0 = calls;
            long[] windows = new long[4];
            for (int r = 0; r < 4; r++)
            {
                AllocationMeter.Stabilize();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) w.Sim.Step(default);
                windows[r] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Assert.That(calls - before0, Is.EqualTo(400), "the chooser is asked every tick");
            Assert.That(Counts(w, 1), Is.EqualTo((1L, 0L, 1L, 0L)));
            Assert.That(windows, Is.EqualTo(new long[4]), "bytes in each 100-tick window");
        }
    }
}
#endif
