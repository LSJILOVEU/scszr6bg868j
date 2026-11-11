#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Rai01AllocationTests
    {
        private const string Ai = "{ \"burstShots\": 3, \"burstPauseTicks\": 20, \"burstPauseFollowsShooter\": true }";
        private const int Last = 300;

        [Test]
        public void SimStep_FirstPauseAndRepeatedBursts_DoNotAllocate()
        {
            M6World warm = P3.World(P2.Cast(), Ai);
            var inputs = new InputFrame[Last + 1];
            for (int tick = 1; tick <= Last; tick++)
            {
                inputs[tick] = P2.Drive(warm, tick, P1.Walk);
                warm.Sim.Step(inputs[tick]);
            }

            M6World w = P3.World(P2.Cast(), Ai);
            Assert.That(w.System<BurstCadence>(), Is.Not.Null);
            for (int tick = 1; tick < P1.T; tick++) w.Sim.Step(inputs[tick]);
            Assert.That(w.Read<Combatant>(P3.KeyA).Ammo, Is.EqualTo(P3.Magazine));
            var fired = new long[P3TimingTests.Burst3Pause20.Length];
            int count = 0;

            long controlStart = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - controlStart;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768));
            AllocationMeter.Stabilize();

            long bytes = 0;
            for (int tick = (int)P1.T; tick <= Last; tick++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(inputs[tick]);
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                if (P1.Fired(w, P3.KeyA)) fired[count++] = tick;
            }
            Assert.That(count, Is.EqualTo(20));
            Assert.That(fired, Is.EqualTo(P3TimingTests.Burst3Pause20));
            Assert.That(bytes, Is.EqualTo(0L), "full steps include the first pause, repeated bursts and reloads");
        }
    }
}
#endif
