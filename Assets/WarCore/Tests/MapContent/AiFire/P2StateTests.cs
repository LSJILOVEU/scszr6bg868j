using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 6: the snapshot with a sense interval (version 2: the version 1 payload followed by K), its load checks,
    // the allocation order and the exact payload.
    public sealed class P2StateTests
    {
        private const int K = 10;
        private const long Save = P2TimingTests.TsA + 7;   // 53: one entry per shooter, before any first shot (R = 15)

        private static M6World RunTo(string ai, long last, bool reverse = false, UnitSpawn[] units = null)
        {
            M6World w = P2.World(units ?? P2.Cast(), ai, reverse);
            for (long tick = 1; tick <= last; tick++) w.Sim.Step(P2.Drive(w, tick, P1.Walk));
            return w;
        }

        // 6(a): K = 10, R = 15, saved on ts + 7 and restored into a fresh world of the same configuration: the same hash on
        // every later tick as the run without a save, the first shot still on ts + 15 = 61.
        [Test]
        public void Restore_KeepsTheClock()
        {
            P2.Trace straight = P2.Run(P2.Cast(), P1.Walk, P2.Ai(15, K), 90);
            Assert.That(straight.A.FirstFire(), Is.EqualTo(P2TimingTests.TsA + 15));
            byte[] snapshot = RunTo(P2.Ai(15, K), Save).Sim.SaveRetainedSnapshot();
            M6World b = P2.World(P2.Cast(), P2.Ai(15, K));
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(P2.Count(b), Is.EqualTo(1));
            Assert.That(b.Hash, Is.EqualTo(straight.Hashes[(int)Save]));
            long first = -1;
            for (long tick = Save + 1; tick <= 90; tick++)
            {
                b.Sim.Step(P2.Drive(b, tick, P1.Walk));
                Assert.That(b.Hash, Is.EqualTo(straight.Hashes[(int)tick]), "tick " + tick);
                if (first < 0 && P1.Fired(b, P2.KeyA)) first = tick;
            }
            Assert.That(first, Is.EqualTo(P2TimingTests.TsA + 15));
        }

        // 6(b): a K = 10 snapshot into a K = 20 world (same R, same version): refused by the load with the dispatch's message.
        [Test]
        public void Restore_OtherInterval_IsRefused()
        {
            byte[] snapshot = RunTo(P2.Ai(15, K), Save).Sim.SaveRetainedSnapshot();
            M6World other = P2.World(P2.Cast(), P2.Ai(15, 20));
            Assert.That(other.System<ReactionFire>().StateVersion, Is.EqualTo(2));
            var error = Assert.Throws<SimSnapshotException>(() => other.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(error.Message, Is.EqualTo("AI sense interval differs."));
        }

        // 6(c): K = 10 is version 2, K = 1 version 1; a version 2 snapshot into a version 1 world (same system count) is
        // refused by the framework's manifest check before the load.
        [Test]
        public void Versions_AndTheManifestCheck()
        {
            M6World ten = RunTo(P2.Ai(15, K), Save), one = P2.World(P2.Cast(), P2.Ai(15, 1));
            Assert.That(ten.System<ReactionFire>().StateVersion, Is.EqualTo(2));
            Assert.That(one.System<ReactionFire>().StateVersion, Is.EqualTo(1));
            Assert.That(P2.Systems(one), Is.EqualTo(P2.Systems(ten)), "the same systems");
            var error = Assert.Throws<SimSnapshotException>(() => one.Sim.RestoreRetainedSnapshot(ten.Sim.SaveRetainedSnapshot()));
            Assert.That(error.Message, Does.Contain("State registration manifest mismatch"));
            Assert.That(error.Message, Does.Contain("battle.ai-fire"));
            Assert.That(error.Message, Does.Contain("v2"));
            Assert.That(error.Message, Does.Contain("v1"));
        }

        // 6(d): K = 10, R = 15, A and A2 (3 m north of A). The reverse allocation gives A another slot (1 against 4, a
        // difference of 3, not a multiple of K): the phase comes from the stable id, so both orders hash alike on every tick
        // to 300 and fire on the same ticks (A 61, A2 59). The forward save on ts + 7 (both entries held) restored with
        // reverse allocation hashes alike at once and on every tick to 300.
        [Test]
        public void ReverseAllocation_SameHashesAndRestore()
        {
            const long Last = 300, FireA = 61, FireA2 = 59;
            P2.Trace forward = P2.Run(P2.Cast(P2.A2Aside), P1.Walk, P2.Ai(15, K), Last);
            P2.Trace reverse = P2.Run(P2.Cast(P2.A2Aside), P1.Walk, P2.Ai(15, K), Last, true);
            int slotA = forward.W.Unit(P2.KeyA).Id, slotA2 = forward.W.Unit(P2.KeyA2).Id;
            Assert.That(reverse.W.Unit(P2.KeyA).Id, Is.Not.EqualTo(slotA), "A changes slot");
            Assert.That((slotA, slotA2, reverse.W.Unit(P2.KeyA).Id, reverse.W.Unit(P2.KeyA2).Id), Is.EqualTo((1, 4, 4, 1)));
            Assert.That(Math.Abs(reverse.W.Unit(P2.KeyA).Id - slotA) % K, Is.Not.EqualTo(0), "the slot difference is not a multiple of K");
            Assert.That(forward.A.FirstFire(), Is.EqualTo(FireA));
            Assert.That(forward.A2.FirstFire(), Is.EqualTo(FireA2));
            Assert.That(Math.Min(FireA, FireA2), Is.GreaterThan(Save), "the save comes before both first shots");
            for (int t = 0; t <= Last; t++) Assert.That(reverse.Hashes[t], Is.EqualTo(forward.Hashes[t]), "tick " + t);
            Assert.That(reverse.A.FireTicks(Last), Is.EqualTo(forward.A.FireTicks(Last)));
            Assert.That(reverse.A2.FireTicks(Last), Is.EqualTo(forward.A2.FireTicks(Last)));

            M6World f = RunTo(P2.Ai(15, K), Save, false, P2.Cast(P2.A2Aside));
            Assert.That(P2.Count(f), Is.EqualTo(2), "two entries saved");
            byte[] snapshot = f.Sim.SaveRetainedSnapshot();
            M6World b = P2.World(P2.Cast(P2.A2Aside), P2.Ai(15, K), true);
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: true));
            Assert.That(b.Unit(P2.KeyA).Id, Is.Not.EqualTo(f.Unit(P2.KeyA).Id), "A changed slot");
            Assert.That(b.Hash, Is.EqualTo(f.Hash));
            Assert.That(P2.Count(b), Is.EqualTo(2));
            for (long tick = Save + 1; tick <= Last; tick++)
            {
                b.Sim.Step(P2.Drive(b, tick, P1.Walk));
                Assert.That(b.Hash, Is.EqualTo(forward.Hashes[(int)tick]), "tick " + tick);
            }
        }

        // The two entries on ts + 7 in stable-id order: (shooter, target, First, Last). A's clock started on its first sample
        // tick 46, A2's on 44; both were kept through 53.
        private static (ulong, ulong, long, long)[] Entries(M6World w)
        {
            ulong a = P2.Id(w, P2.KeyA).Value, a2 = P2.Id(w, P2.KeyA2).Value, b = P2.Id(w, P2.KeyB).Value;
            var entries = new List<(ulong, ulong, long, long)> { (a, b, P2TimingTests.TsA, Save), (a2, b, 44, Save) };
            entries.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            return entries.ToArray();
        }

        // 6(e1): WriteState, called directly, in both allocation orders: the type, version 2, R, the entry count, the entries
        // in stable-id order and, last, K.
        [Test]
        public void WriteState_EndsWithTheInterval()
        {
            M6World f = RunTo(P2.Ai(15, K), Save, false, P2.Cast(P2.A2Aside));
            M6World r = RunTo(P2.Ai(15, K), Save, true, P2.Cast(P2.A2Aside));
            var expected = new StateHash();
            expected.Add("battle.ai-fire"); expected.Add(2); expected.Add(15); expected.Add(2);
            foreach ((ulong shooter, ulong target, long first, long last) in Entries(f))
            {
                expected.Add(shooter); expected.Add(target); expected.Add(first); expected.Add(last);
            }
            expected.Add(K);
            var forward = new StateHash();
            f.System<ReactionFire>().WriteState(forward);
            var reverse = new StateHash();
            r.System<ReactionFire>().WriteState(reverse);
            Assert.That(forward.Value, Is.EqualTo(expected.Value));
            Assert.That(reverse.Value, Is.EqualTo(expected.Value));
        }

        // 6(e2): SaveState, called directly (the framework writes the type and version): 76 bytes = R (4) + count (4) +
        // 2 x 32 + K (4), read back in binary order with 4 bytes left before K and none after.
        [Test]
        public void SaveState_PayloadIsVersionOnePlusTheInterval()
        {
            M6World f = RunTo(P2.Ai(15, K), Save, false, P2.Cast(P2.A2Aside));
            var writer = new SimStateWriter();
            f.System<ReactionFire>().SaveState(writer);
            byte[] bytes = writer.ToArray();
            Assert.That(bytes.Length, Is.EqualTo(76));
            var reader = new SimStateReader(bytes);
            Assert.That(reader.Int32(), Is.EqualTo(15), "R");
            Assert.That(reader.Int32(), Is.EqualTo(2), "the entry count");
            foreach ((ulong shooter, ulong target, long first, long last) in Entries(f))
                Assert.That((reader.UInt64(), reader.UInt64(), reader.Int64(), reader.Int64()), Is.EqualTo((shooter, target, first, last)));
            Assert.That(reader.Remaining, Is.EqualTo(4), "K is left");
            Assert.That(reader.Int32(), Is.EqualTo(K));
            Assert.That(reader.Remaining, Is.EqualTo(0));
        }

        // 6(e3): the same fixture with K = 1 writes the version 1 payload: 72 bytes, nothing after the two entries.
        [Test]
        public void SaveState_IntervalOne_WritesVersionOne()
        {
            M6World f = RunTo(P2.Ai(15, 1), Save, false, P2.Cast(P2.A2Aside));
            Assert.That(P2.Count(f), Is.EqualTo(2));
            var writer = new SimStateWriter();
            f.System<ReactionFire>().SaveState(writer);
            byte[] bytes = writer.ToArray();
            Assert.That(bytes.Length, Is.EqualTo(72));
            var reader = new SimStateReader(bytes);
            Assert.That(reader.Int32(), Is.EqualTo(15), "R");
            Assert.That(reader.Int32(), Is.EqualTo(2), "the entry count");
            for (int i = 0; i < 2; i++) { reader.UInt64(); reader.UInt64(); reader.Int64(); reader.Int64(); }
            Assert.That(reader.Remaining, Is.EqualTo(0));
        }
    }
}
