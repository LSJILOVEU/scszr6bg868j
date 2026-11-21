// Strict direct-byte proof needs the headless CoreCLR run (as the M0c2 allocation tests); Unity's counter cannot prove zero.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using static WarCore.Tests.RespawnAi1;
using static WarCore.Tests.RespawnAi1World;

namespace WarCore.Tests
{
    // RESPAWN-AI-1 A12: with the switch on and a chooser bound, a step that runs the whole entry path (the contested check, the
    // list, the chooser, every grid cell) does not allocate. Reinforcement every tick; P is contested, so team 1's list is [B1],
    // and four team 1 soldiers stand on B1's four grid cells, so every attempt asks the chooser and fails on every cell and the
    // reserve stays for the next tick. 60 ticks warm up, then four measured windows of 100 ticks, each after a collection,
    // after a positive control of the counter.
    public sealed class RespawnAi1AllocationTests
    {
        [Test]
        public void A12_TheEntryPathDoesNotAllocate()
        {
            var units = Units(U(Contester, 41000, 21000, 2), U(5, 5000, 19000, 1), U(6, 7000, 19000, 1), U(7, 5000, 21000, 1), U(8, 7000, 21000, 1));
            M6World w = World(units, Pack(P(), true, interval: 1));
            int calls = 0;
            w.Reinforce.BindChooser(new Rule(91, (team, attempt, count) => { calls++; return 0; }));
            w.Step(60);
            Assert.That((w.Reinforce.Attempts(1), w.Reinforce.Successes(1)), Is.EqualTo((60L, 0L)), "every attempt fails");

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
            Assert.That(calls - before0, Is.GreaterThanOrEqualTo(400), "the chooser is asked every tick");
            Assert.That(w.Reinforce.Successes(1), Is.Zero);
            Assert.That(windows, Is.EqualTo(new long[4]), "bytes in each 100-tick window");
        }
    }
}
#endif
