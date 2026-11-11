using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Rai01StateTests
    {
        private const string Enabled = "{ \"burstShots\": 3, \"burstPauseTicks\": 40, \"burstFollowsShooter\": true }";
        private static readonly P1.Move[] Switch =
        {
            new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(35, 3, A2.Cell(8, 4)),
        };

        [TestCase(null)]
        [TestCase("{}")]
        [TestCase("{ \"burstFollowsShooter\": false }")]
        public void PolicyDefaultsOff(string ai) =>
            Assert.That(A3.Load(P1.PackJson(ai)).Section<AiRules>().BurstFollowsShooter, Is.False);

        [TestCase(0)]
        [TestCase(15)]
        public void PolicyWithoutBurstRegistersNoCadence(int reaction)
        {
            string ai = P3.Ai(reaction);
            M6World plain = P3.World(P2.Cast(), ai);
            string enabled = ai.Replace("}", ", \"burstFollowsShooter\": true }");
            Assert.That(A3.Load(P1.PackJson(enabled)).Section<AiRules>().BurstFollowsShooter, Is.True);
            M6World w = P3.World(P2.Cast(), enabled);
            Assert.That(w.System<BurstCadence>(), Is.Null);
            Assert.That(P3.Systems(w), Is.EqualTo(P3.Systems(plain)));
            Assert.That(w.Hash, Is.EqualTo(plain.Hash));
        }

        [TestCase("0")]
        [TestCase("\"true\"")]
        [TestCase("null")]
        public void PolicyRequiresBoolean(string value)
        {
            var error = Assert.Throws<ContentException>(() => A3.Load(P1.PackJson("{ \"burstFollowsShooter\": " + value + " }")));
            Assert.That(error.Message, Is.EqualTo(value == "null"
                ? "content layer 'a2-test' line 1 (ai.burstFollowsShooter): unexpected 'n' (null and exponent numbers are not supported)"
                : "content pack 'a2-test' layer 'a2-test' ai field 'burstFollowsShooter': must be true or false"));
        }

        [TestCase(0, 1, 0)]
        [TestCase(15, 1, 0)]
        [TestCase(0, 10, 0)]
        [TestCase(0, 1, 3)]
        public void DisabledPreservesHashesSnapshotsAndSystems(int reaction, int sense, int burst)
        {
            string ai = P3.Ai(reaction, sense, burst, 40);
            M6World plain = P3.World(P2.Cast(), ai);
            M6World disabled = P3.World(P2.Cast(), ai.Replace("}", ", \"burstFollowsShooter\": false }"));
            Assert.That(P3.Systems(disabled), Is.EqualTo(P3.Systems(plain)));
            Assert.That(disabled.System<BurstCadence>(), Is.Null);
            for (long tick = 0; tick <= 200; tick++)
            {
                if (tick > 0)
                {
                    plain.Sim.Step(P2.Drive(plain, tick, Switch));
                    disabled.Sim.Step(P2.Drive(disabled, tick, Switch));
                    Assert.That(P1.Fired(disabled, P3.KeyA), Is.EqualTo(P1.Fired(plain, P3.KeyA)), "shot, tick " + tick);
                }
                Assert.That(disabled.Hash, Is.EqualTo(plain.Hash), "tick " + tick);
            }
            Assert.That(disabled.Sim.SaveRetainedSnapshot(), Is.EqualTo(plain.Sim.SaveRetainedSnapshot()));
        }

        [Test]
        public void EnabledRerunAndReverseSlotsMatch()
        {
            P3.Trace forward = Run(200), repeat = Run(200), reverse = Run(200, true);
            Assert.That(reverse.W.Unit(P3.KeyA).Id, Is.Not.EqualTo(forward.W.Unit(P3.KeyA).Id));
            Assert.That(P2.Id(reverse.W, P3.KeyA), Is.EqualTo(P2.Id(forward.W, P3.KeyA)));
            Assert.That(repeat.Hashes, Is.EqualTo(forward.Hashes));
            Assert.That(reverse.Hashes, Is.EqualTo(forward.Hashes));
            Assert.That(reverse.A.FireTicks(200), Is.EqualTo(forward.A.FireTicks(200)));
            Assert.That(reverse.A2.FireTicks(200), Is.EqualTo(forward.A2.FireTicks(200)));
            Assert.That(repeat.W.Sim.SaveRetainedSnapshot(), Is.EqualTo(forward.W.Sim.SaveRetainedSnapshot()));
            Assert.That(reverse.W.Sim.SaveRetainedSnapshot(), Is.EqualTo(forward.W.Sim.SaveRetainedSnapshot()));
        }

        [TestCase(47L)]
        [TestCase(60L)]
        [TestCase(90L)]
        public void RestorePreservesBurstAcrossTargetLoss(long save)
        {
            P3.Trace forward = Run(200), prefix = Run(save);
            if (save == 60)
            {
                Assert.That(prefix.A.Sel[60], Is.EqualTo(0UL));
                Assert.That(prefix.A.FireTicks(60), Is.EqualTo(new long[] { 40, 45, 50 }));
            }
            M6World restored = World(true);
            restored.Sim.RestoreRetainedSnapshot(prefix.W.Sim.SaveRetainedSnapshot(), reverseAllocation: true);
            Assert.That(restored.Unit(P3.KeyA).Id, Is.Not.EqualTo(prefix.W.Unit(P3.KeyA).Id));
            Assert.That(restored.Hash, Is.EqualTo(prefix.W.Hash));
            Assert.That(Payload(restored), Is.EqualTo(Payload(prefix.W)));
            for (long tick = save + 1; tick <= 200; tick++)
            {
                restored.Sim.Step(P2.Drive(restored, tick, Switch));
                Assert.That(restored.Hash, Is.EqualTo(forward.Hashes[(int)tick]), "tick " + tick);
                Assert.That(P1.Fired(restored, P3.KeyA), Is.EqualTo(forward.A.Fire[(int)tick]), "shot, tick " + tick);
            }
        }

        [Test]
        public void StateVersionAndHashCoverSavedFields()
        {
            M6World w = Run(60).W;
            BurstCadence cadence = w.System<BurstCadence>();
            Assert.That(cadence.StateVersion, Is.EqualTo(1));
            Assert.That(P3.Systems(w), Does.Contain("battle.ai-burst-cadence|0|15;"));
            var reader = new SimStateReader(Payload(w));
            var expected = new StateHash();
            expected.Add("battle.ai-burst-cadence"); expected.Add(1);
            int burst = reader.Int32(), pause = reader.Int32(), count = reader.Int32();
            Assert.That((burst, pause, count), Is.EqualTo((3, 40, 2)));
            expected.Add(burst); expected.Add(pause); expected.Add(count);
            for (int i = 0; i < count; i++)
            {
                ulong key = reader.UInt64(); int shots = reader.Int32(); long last = reader.Int64();
                if (i == 0) Assert.That((key, shots, last), Is.EqualTo((P2.Id(w, P3.KeyA).Value, 3, 50L)));
                expected.Add(key); expected.Add(shots); expected.Add(last);
            }
            Assert.That(reader.Remaining, Is.EqualTo(0));
            var actual = new StateHash();
            cadence.WriteState(actual);
            Assert.That(actual.Value, Is.EqualTo(expected.Value));
        }

        [Test]
        public void DisabledSnapshotIsRejectedWhenEnabled()
        {
            M6World disabled = P3.RunTo(P3.Ai(0, 1, 3, 40), 50);
            M6World enabled = P3.World(P2.Cast(), Enabled);
            var error = Assert.Throws<SimSnapshotException>(() => enabled.Sim.RestoreRetainedSnapshot(disabled.Sim.SaveRetainedSnapshot()));
            Assert.That(error.Message, Is.EqualTo("System registration manifest count mismatch."));
        }

        [TestCase(0, 4, "AI cadence burst length differs.")]
        [TestCase(4, 41, "AI cadence pause differs.")]
        [TestCase(8, -1, "Snapshot count exceeds its section bounds.")]
        [TestCase(8, int.MaxValue, "Snapshot count exceeds its section bounds.")]
        [TestCase(20, -1, "AI cadence shot count is out of range.")]
        [TestCase(20, 4, "AI cadence shot count is out of range.")]
        public void LoadRejectsInvalidCountsAndConfiguration(int offset, int value, string message)
        {
            M6World w = Run(60).W;
            byte[] bytes = Payload(w);
            Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
            var error = Assert.Throws<SimSnapshotException>(() => w.System<BurstCadence>().LoadState(new SimStateReader(bytes)));
            Assert.That(error.Message, Is.EqualTo(message));
        }

        [TestCase(-1L)]
        [TestCase(0L)]
        public void LoadRejectsInvalidLastShot(long last)
        {
            M6World w = Run(60).W;
            byte[] bytes = Payload(w);
            Array.Copy(BitConverter.GetBytes(last), 0, bytes, 24, 8);
            var error = Assert.Throws<SimSnapshotException>(() => w.System<BurstCadence>().LoadState(new SimStateReader(bytes)));
            Assert.That(error.Message, Is.EqualTo("AI cadence last shot is invalid."));
        }

        [Test]
        public void LoadRejectsDuplicateShooters()
        {
            M6World w = Run(60).W;
            byte[] bytes = Payload(w);
            Array.Copy(bytes, 12, bytes, 32, 8);
            var error = Assert.Throws<SimSnapshotException>(() => w.System<BurstCadence>().LoadState(new SimStateReader(bytes)));
            Assert.That(error.Message, Is.EqualTo("AI cadence shooters are not in strictly increasing order."));
        }

        [Test]
        public void LoadRejectsMissingShooter()
        {
            M6World w = Run(60).W;
            byte[] bytes = Payload(w);
            Array.Clear(bytes, 12, 8);
            var error = Assert.Throws<SimSnapshotException>(() => w.System<BurstCadence>().LoadState(new SimStateReader(bytes)));
            Assert.That(error.Message, Is.EqualTo("AI cadence shooter does not exist."));
        }

        [Test]
        public void LoadReplacesExistingEntries()
        {
            M6World w = Run(60).W;
            byte[] empty = Payload(World());
            Assert.That(BitConverter.ToInt32(Payload(w), 8), Is.EqualTo(2));
            w.System<BurstCadence>().LoadState(new SimStateReader(empty));
            Assert.That(Payload(w), Is.EqualTo(empty));
        }

        private static M6World World(bool reverse = false) =>
            P3.World(P2.Cast(P2.A2Aside), Enabled, reverse, new P1.DestroyProbe(60, P3.KeyB));

        private static P3.Trace Run(long last, bool reverse = false) =>
            P3.Run(P2.Cast(P2.A2Aside), Switch, Enabled, last, reverse, new P1.DestroyProbe(60, P3.KeyB));

        private static byte[] Payload(M6World w)
        {
            var writer = new SimStateWriter();
            w.System<BurstCadence>().SaveState(writer);
            return writer.ToArray();
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        [Test]
        public void FullStepsFirstBurstAndRepeatedPausesAllocateZero()
        {
            const int Last = 300;
            var inputs = new InputFrame[Last + 1];
            M6World warm = P3.World(P2.Cast(), Enabled, false, new P1.DestroyProbe(60, P3.KeyB));
            for (int tick = 1; tick <= Last; tick++)
            {
                inputs[tick] = P2.Drive(warm, tick, Switch);
                warm.Sim.Step(inputs[tick]);
            }
            M6World measured = P3.World(P2.Cast(), Enabled, false, new P1.DestroyProbe(60, P3.KeyB));
            var fired = new bool[Last + 1];
            StableId shooter = P2.Id(measured, P3.KeyA);
            // Movement buffers grow before the first selectable target on tick 40.
            for (int tick = 1; tick <= 20; tick++) measured.Sim.Step(inputs[tick]);
            Assert.That(measured.Sim.Clock.Tick, Is.EqualTo(20));
            Assert.That(BitConverter.ToInt32(Payload(measured), 8), Is.EqualTo(0), "no cadence entry before measurement");
            Assert.That(measured.Combat.Fires.Length, Is.EqualTo(0));
            long beforeControl = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - beforeControl;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768));
            AllocationMeter.Stabilize();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 21; tick <= Last; tick++)
            {
                measured.Sim.Step(inputs[tick]);
                foreach (var shot in measured.Combat.Fires)
                    if (shot.Shooter.Equals(shooter)) fired[tick] = true;
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            var shots = new List<long>();
            for (int tick = 1; tick <= Last; tick++) if (fired[tick]) shots.Add(tick);
            Assert.That(shots, Is.EqualTo(P3.Expect(40, 3, 40, Last)));
            Assert.That(bytes, Is.EqualTo(0L));
        }
#endif
    }
}
