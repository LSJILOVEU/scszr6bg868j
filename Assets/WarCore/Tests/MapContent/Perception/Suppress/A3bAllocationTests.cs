// Strict direct-byte proof needs the headless CoreCLR run (as the P3 allocation test); Unity's counter cannot prove zero.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    // The suppression's Step does not allocate. A meter system registered at order 19 (between the suppression at 18 and
    // the combat at 30) calls the registered system's Step a second time on every tick of a window and sums the bytes the
    // thread allocated around the call: the second call sees the same entries, drops and submissions as the first (the
    // intent of the tick is simply written again). The window covers the first blind thinking tick (56, the entry is made
    // by the AI's Decide, which is allowed one node), the submissions (56..155), the silent part of the window and the
    // memory's expiry (346, the entry is dropped).
    public sealed class A3bAllocationTests
    {
        private sealed class Meter : IRetainedGameplaySystem
        {
            private readonly TargetSuppress suppress;
            private readonly long from, to;
            internal long Bytes, Calls;
            internal Meter(TargetSuppress suppress, long from, long to) { this.suppress = suppress; this.from = from; this.to = to; }
            public int Order => 19;
            public string StateTypeId => "test.a3b-meter";
            public int StateVersion => 1;
            public void Step(SimContext c)
            {
                long tick = c.Clock.Tick;
                if (tick < from || tick > to) return;
                long before = GC.GetAllocatedBytesForCurrentThread();
                suppress.Step(c);
                Bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                Calls++;
            }
            public void SaveState(SimStateWriter w) { }
            public void LoadState(SimStateReader r) { }
            public void WriteState(StateHash h) { }
        }

        [Test]
        public void Step_DoesNotAllocate()
        {
            M6World w = A3b.World(A3b.Ai(300, 100));
            var meter = new Meter(w.System<TargetSuppress>(), 50, 400);
            w.Sim.RegisterGameplay("test.a3b-meter", meter, SimSystemPhase.Requests);
            Assert.That(L4.Systems(w), Does.Contain("battle.ai-suppress|0|18;test.a3b-meter|0|19;"));

            long c0 = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - c0;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768), "the counter works");
            AllocationMeter.Stabilize();

            var fires = A3b.Run(w, 400, A3b.Script, null, (x, t) =>
            {
                if (t == 56) Assert.That(A3b.Count(x), Is.EqualTo(1));
                if (t == 346) Assert.That(A3b.Count(x), Is.EqualTo(0));
            });
            Assert.That(fires, Is.EqualTo(A3b.Ticks(1, 31, 61, 121, 151)), "the second Step changed nothing");
            Assert.That(meter.Calls, Is.EqualTo(351));
            Assert.That(meter.Bytes, Is.EqualTo(0L), "bytes: Step over 351 ticks");
        }
    }
}
#endif
