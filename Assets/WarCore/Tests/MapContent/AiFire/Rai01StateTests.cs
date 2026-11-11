using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Rai01StateTests
    {
        internal static byte[] Payload(BurstCadence cadence)
        {
            var writer = new SimStateWriter();
            cadence.SaveState(writer);
            return writer.ToArray();
        }

        [Test]
        public void StateListsShooterDeadlinesInStableOrder()
        {
            M6World w = P3.RunTo(Rai01TimingTests.Ai(), 59, false, P2.Cast(P2.A2Aside));
            BurstCadence cadence = w.System<BurstCadence>();
            Assert.That((cadence.StateTypeId, cadence.StateVersion, cadence.Order), Is.EqualTo(("battle.burst-cadence", 1, 15)));
            ulong a = P2.Id(w, P3.KeyA).Value, a2 = P2.Id(w, P3.KeyA2).Value;
            Assert.That(a, Is.LessThan(a2));
            var reader = new SimStateReader(Payload(cadence));
            Assert.That(reader.Int32(), Is.EqualTo(2));
            Assert.That((reader.UInt64(), reader.Int64()), Is.EqualTo((a, 90L)));
            Assert.That((reader.UInt64(), reader.Int64()), Is.EqualTo((a2, 92L)));
            Assert.That(reader.Remaining, Is.EqualTo(0));
            var expected = new StateHash();
            expected.Add("battle.burst-cadence"); expected.Add(1); expected.Add(2);
            expected.Add(a); expected.Add(90L); expected.Add(a2); expected.Add(92L);
            var actual = new StateHash();
            cadence.WriteState(actual);
            Assert.That(actual.Value, Is.EqualTo(expected.Value));
        }

        [Test]
        public void RepeatReverseAndRestoreKeepPauseThroughTargetGap()
        {
            UnitSpawn[] units = P2.Cast(P2.A2Aside);
            string ai = Rai01TimingTests.Ai();
            P3.Trace forward = P3.Run(units, Rai01TimingTests.SwitchScript, ai, 150, false, new P1.DestroyProbe(60, P3.KeyB));
            P3.Trace repeat = P3.Run(units, Rai01TimingTests.SwitchScript, ai, 150, false, new P1.DestroyProbe(60, P3.KeyB));
            P3.Trace reverse = P3.Run(units, Rai01TimingTests.SwitchScript, ai, 150, true, new P1.DestroyProbe(60, P3.KeyB));
            Assert.That(reverse.W.Unit(P3.KeyA).Id, Is.Not.EqualTo(forward.W.Unit(P3.KeyA).Id));
            Assert.That(reverse.Id(P3.KeyA), Is.EqualTo(forward.Id(P3.KeyA)));
            Assert.That(repeat.Hashes, Is.EqualTo(forward.Hashes));
            Assert.That(reverse.Hashes, Is.EqualTo(forward.Hashes));
            Assert.That(reverse.A.FireTicks(150), Is.EqualTo(forward.A.FireTicks(150)));
            Assert.That(reverse.A2.FireTicks(150), Is.EqualTo(forward.A2.FireTicks(150)));
            Assert.That(reverse.W.Sim.SaveRetainedSnapshot(), Is.EqualTo(forward.W.Sim.SaveRetainedSnapshot()));

            M6World source = P3.World(units, ai, false, new P1.DestroyProbe(60, P3.KeyB));
            for (long tick = 1; tick <= 60; tick++) source.Sim.Step(P2.Drive(source, tick, Rai01TimingTests.SwitchScript));
            Assert.That(P1.Selected(source, P3.KeyA), Is.EqualTo(0UL));
            Assert.That(P2.Count(source), Is.EqualTo(0));
            Assert.That(Payload(source.System<BurstCadence>()).Length, Is.EqualTo(36));
            M6World restored = P3.World(units, ai, true, new P1.DestroyProbe(60, P3.KeyB));
            restored.Sim.RestoreRetainedSnapshot(source.Sim.SaveRetainedSnapshot(), reverseAllocation: true);
            Assert.That(restored.Hash, Is.EqualTo(source.Hash));
            Assert.That(Payload(restored.System<BurstCadence>()), Is.EqualTo(Payload(source.System<BurstCadence>())));
            for (long tick = 61; tick <= 150; tick++)
            {
                restored.Sim.Step(P2.Drive(restored, tick, Rai01TimingTests.SwitchScript));
                Assert.That(restored.Hash, Is.EqualTo(forward.Hashes[(int)tick]), "tick " + tick);
                Assert.That(P1.Fired(restored, P3.KeyA), Is.EqualTo(forward.A.Fire[(int)tick]), "A, tick " + tick);
            }
            Assert.That(forward.A.FirstFire(51), Is.EqualTo(90L));
            Assert.That(forward.A2.FirstFire(53), Is.EqualTo(92L));
        }

        [Test]
        public void EmptyRestoreClearsPreviousPause()
        {
            M6World empty = P3.RunTo(Rai01TimingTests.Ai(), 39);
            M6World paused = P3.RunTo(Rai01TimingTests.Ai(), 60);
            Assert.That(Payload(paused.System<BurstCadence>()).Length, Is.EqualTo(20));
            paused.Sim.RestoreRetainedSnapshot(empty.Sim.SaveRetainedSnapshot());
            Assert.That(Payload(paused.System<BurstCadence>()), Is.EqualTo(new byte[4]));
            Assert.That(paused.Hash, Is.EqualTo(empty.Hash));
            for (long tick = 40; tick <= 100; tick++)
            {
                empty.Sim.Step(P2.Drive(empty, tick, P1.Walk));
                paused.Sim.Step(P2.Drive(paused, tick, P1.Walk));
                Assert.That(paused.Hash, Is.EqualTo(empty.Hash), "tick " + tick);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void DisabledAndEnabledSnapshotsHaveDistinctManifests(bool intoEnabled)
        {
            string old = P3.AiBurst(3, 40), enabled = Rai01TimingTests.Ai();
            M6World from = P3.RunTo(intoEnabled ? old : enabled, 60);
            M6World to = P3.World(P2.Cast(), intoEnabled ? enabled : old);
            var error = Assert.Throws<SimSnapshotException>(() => to.Sim.RestoreRetainedSnapshot(from.Sim.SaveRetainedSnapshot()));
            Assert.That(error.Message, Is.EqualTo("System registration manifest count mismatch."));
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        public void NonpositiveDeadlineIsRefused(long deadline)
        {
            M6World w = P3.World(P2.Cast(), Rai01TimingTests.Ai());
            var writer = new SimStateWriter();
            writer.Int32(1); writer.UInt64(P2.Id(w, P3.KeyA).Value); writer.Int64(deadline);
            var error = Assert.Throws<SimSnapshotException>(() => w.System<BurstCadence>().LoadState(new SimStateReader(writer.ToArray())));
            Assert.That(error.Message, Is.EqualTo("AI burst deadline is not positive."));
        }

        [Test]
        public void UnresolvedShooterIsRefused()
        {
            M6World w = P3.World(P2.Cast(), Rai01TimingTests.Ai());
            var writer = new SimStateWriter();
            writer.Int32(1); writer.UInt64(12345); writer.Int64(90);
            var error = Assert.Throws<SimSnapshotException>(() => w.System<BurstCadence>().LoadState(new SimStateReader(writer.ToArray())));
            Assert.That(error.Message, Is.EqualTo("AI burst shooter does not exist."));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnsortedOrDuplicateShootersAreRefused(bool duplicate)
        {
            M6World w = P3.World(P2.Cast(P2.A2Aside), Rai01TimingTests.Ai());
            ulong a = P2.Id(w, P3.KeyA).Value, a2 = P2.Id(w, P3.KeyA2).Value;
            Assert.That(a, Is.LessThan(a2));
            var writer = new SimStateWriter();
            writer.Int32(2); writer.UInt64(a2); writer.Int64(90); writer.UInt64(duplicate ? a2 : a); writer.Int64(92);
            var error = Assert.Throws<SimSnapshotException>(() => w.System<BurstCadence>().LoadState(new SimStateReader(writer.ToArray())));
            Assert.That(error.Message, Is.EqualTo("AI burst shooters are not in strictly increasing order."));
        }
    }
}
