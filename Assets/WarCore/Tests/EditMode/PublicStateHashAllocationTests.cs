using System;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class PublicStateHashAllocationTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void PublicHashHasZeroBytesOnEveryDirectSample(bool unicode, bool movement)
        {
            var sim = new Simulation(123, modeId: unicode ? "mode-\u00e9-\ud83d\ude00" : "single-match-v1");
            sim.Register("load", new LoadSystem(sim, 2000));
            if (movement)
            {
                var positions = sim.Entities.Resolve<Position>();
                var movements = sim.Entities.Register<Movement>("movement-\u00e9");
                var navigation = new NavProviderRegistry();
                navigation.Register(MoveMode.Ground, new FlatGroundNavProvider());
                sim.Register("movement", new MovementSystem(sim.Entities, positions, movements, navigation, 2));
            }
            sim.CompleteWarmup();
            ulong expected = sim.StateHash();
            for (int i = 0; i < 32; i++)
                sim.StateHash();
            var meter = new AllocationMeter();
            if (meter.Basis != AllocationBasis.Direct)
                Assert.Inconclusive("Public StateHash strict zero needs a passing direct positive control.");
            Assert.That(meter.DirectControlDelta.Bytes, Is.GreaterThanOrEqualTo(32768));
            long total = 0;
            long maximum = 0;
            bool same = true;
            for (int i = 0; i < 1200; i++)
            {
                AllocationSample before = meter.Read();
                ulong actual = sim.StateHash();
                AllocationSample sample = meter.Difference(before);
                same &= actual == expected && sample.Basis == AllocationBasis.Direct;
                total += sample.Bytes;
                maximum = Math.Max(maximum, sample.Bytes);
            }
            TestContext.WriteLine("Public StateHash direct: unicode={0}; movement={5}; calls=1200; bytes={1}; max_per_call={2}; control_bytes={3}; hash=0x{4:X16}.",
                unicode, total, maximum, meter.DirectControlDelta.Bytes, expected, movement);
            Assert.That(same, Is.True);
            Assert.That(total, Is.Zero);
            Assert.That(maximum, Is.Zero);
        }

        [Test]
        public void RecursiveWriterIsRejectedAndTheSinkRecoversAfterFailure()
        {
            var sim = new Simulation(123);
            var writer = new RecursiveWriter(sim);
            sim.Register("recursive-control", writer);
            Assert.Throws<InvalidOperationException>(() => sim.StateHash());
            writer.Reenter = false;
            var independent = new StateHash();
            sim.WriteState(independent);
            Assert.That(sim.StateHash(), Is.EqualTo(independent.Value));
            Assert.That(sim.StateHash(), Is.EqualTo(independent.Value));
        }

        private sealed class RecursiveWriter : ISimSystem
        {
            private readonly Simulation sim;
            internal bool Reenter = true;
            public int Order => 1;
            internal RecursiveWriter(Simulation sim)
            {
                this.sim = sim;
            }
            public void Step(SimContext context) { }
            public void WriteState(StateHash hash)
            {
                if (Reenter)
                    sim.StateHash();
                hash.Add(42);
            }
        }

        [Test]
        public void ReusedSinkResetsBetweenStatesInstancesAndSnapshotRestore()
        {
            Simulation left = Fixtures.Create(123);
            Simulation right = Fixtures.Create(456);
            byte[] snapshot = left.SaveSnapshot();
            for (int i = 0; i < 40; i++)
            {
                left.Step(Fixtures.Input(i));
                right.Step(Fixtures.Input(i));
                var expectedLeft = new StateHash();
                var expectedRight = new StateHash();
                left.WriteState(expectedLeft);
                right.WriteState(expectedRight);
                Assert.That(left.StateHash(), Is.EqualTo(expectedLeft.Value));
                Assert.That(right.StateHash(), Is.EqualTo(expectedRight.Value));
                Assert.That(left.StateHash(), Is.EqualTo(expectedLeft.Value));
            }
            left.RestoreSnapshot(snapshot);
            var restored = new StateHash();
            left.WriteState(restored);
            Assert.That(left.StateHash(), Is.EqualTo(restored.Value));
        }
    }
}
