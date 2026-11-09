using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 7: the retained state of the suppression (D = 100 on the main fixture).
    public sealed class A3bStateTests
    {
        private const string Ai = "{ \"targetMemoryTicks\": 300, \"suppressTicks\": 100 }";
        private const string LengthDiffers = "AI suppression length differs.";
        private const string NotIncreasing = "AI suppression observers are not in strictly increasing order.";
        private const string SeenNegative = "AI suppression seen tick is negative.";
        private const string SincePrecedes = "AI suppression start precedes the sighting.";

        private static M6World RunTo(string ai, long last, bool reverse = false, params UnitSpawn[] more)
        {
            M6World w = A3b.World(ai, null, reverse, null, more);
            for (long t = 1; t <= last; t++) A3b.Step(w, A3b.Script(t));
            return w;
        }
        private static byte[] Own(M6World w) { var writer = new SimStateWriter(); w.System<TargetSuppress>().SaveState(writer); return writer.ToArray(); }

        // 7(a): a snapshot of tick 100 or 130 restores (in either allocation order) to the same hash and the same run to
        // 400, with the remaining shots {121, 151} or {151}; SaveState is 32 bytes read back in binary order.
        [TestCase(100, false)]
        [TestCase(100, true)]
        [TestCase(130, true)]
        public void Restore_ContinuesTheSameRun(long save, bool reverse)
        {
            M6World straight = A3b.World(Ai);
            var hashes = new List<ulong> { straight.Hash };
            var after = new List<long>();
            for (long t = 1; t <= 400; t++) { A3b.Step(straight, A3b.Script(t)); hashes.Add(straight.Hash); if (t > save && A3b.Fired(straight)) after.Add(t); }
            M6World a = RunTo(Ai, save);
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            byte[] own = Own(a);
            Assert.That(own.Length, Is.EqualTo(32));
            var reader = new SimStateReader(own);
            Assert.That(reader.Int32(), Is.EqualTo(100));
            Assert.That(reader.Int32(), Is.EqualTo(1));
            Assert.That(reader.UInt64(), Is.EqualTo(A3b.IdA));
            Assert.That(reader.Int64(), Is.EqualTo(A3b.Seen));
            Assert.That(reader.Int64(), Is.EqualTo(A3b.Since));
            Assert.That(reader.Remaining, Is.EqualTo(0));
            M6World b = A3b.World(Ai, null, reverse);
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: reverse));
            Assert.That(b.Hash, Is.EqualTo(hashes[(int)save]));
            Assert.That(A3b.Count(b), Is.EqualTo(1));
            var restored = new List<long>();
            for (long t = save + 1; t <= 400; t++)
            {
                A3b.Step(b, A3b.Script(t));
                Assert.That(b.Hash, Is.EqualTo(hashes[(int)t]), "tick " + t);
                if (A3b.Fired(b)) restored.Add(t);
            }
            Assert.That(restored, Is.EqualTo(after));
            Assert.That(restored, Is.EqualTo(save == 100 ? A3b.Ticks(121, 151) : A3b.Ticks(151)));
        }

        // 7(b): another D refuses the snapshot with the first message.
        [Test]
        public void Restore_OtherDuration_Refused()
        {
            byte[] snapshot = RunTo(Ai, 100).Sim.SaveRetainedSnapshot();
            M6World other = A3b.World("{ \"targetMemoryTicks\": 300, \"suppressTicks\": 120 }");
            var e = Assert.Throws<SimSnapshotException>(() => other.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(e.Message, Is.EqualTo(LengthDiffers));
        }

        // 7(c): on and off do not mix: the registration manifest differs either way.
        [TestCase(true)]
        [TestCase(false)]
        public void Restore_OnOffMismatch_Refused(bool savedOn)
        {
            const string off = "{ \"targetMemoryTicks\": 300 }";
            byte[] snapshot = RunTo(savedOn ? Ai : off, 100).Sim.SaveRetainedSnapshot();
            M6World other = A3b.World(savedOn ? off : Ai);
            var e = Assert.Throws<SimSnapshotException>(() => other.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(e.Message, Is.EqualTo("System registration manifest count mismatch."));
        }

        private static byte[] Bytes(int d, int count, params (ulong key, long seen, long since)[] entries)
        {
            var w = new SimStateWriter();
            w.Int32(d); w.Int32(count);
            foreach (var (key, seen, since) in entries) { w.UInt64(key); w.Int64(seen); w.Int64(since); }
            return w.ToArray();
        }

        // 7(d): hand-written bytes against a registered world: keys out of order, a negative sighting, a start before the
        // sighting and a count beyond the entity capacity are refused with their messages; the system's own 32 bytes load.
        [Test]
        public void Load_RejectsBadBytes()
        {
            M6World w = RunTo(Ai, 100);
            TargetSuppress s = w.System<TargetSuppress>();
            byte[] own = Own(w);
            Assert.That(Assert.Throws<SimSnapshotException>(() => s.LoadState(new SimStateReader(Bytes(100, 2, (A3b.IdA2, 64, 74), (A3b.IdA, 46, 56))))).Message, Is.EqualTo(NotIncreasing));
            Assert.That(Assert.Throws<SimSnapshotException>(() => s.LoadState(new SimStateReader(Bytes(100, 1, (A3b.IdA, -1, 56))))).Message, Is.EqualTo(SeenNegative));
            Assert.That(Assert.Throws<SimSnapshotException>(() => s.LoadState(new SimStateReader(Bytes(100, 1, (A3b.IdA, 46, 45))))).Message, Is.EqualTo(SincePrecedes));
            Assert.That(Assert.Throws<SimSnapshotException>(() => s.LoadState(new SimStateReader(Bytes(100, w.Combat.Entities.Capacity + 1)))).Message,
                Is.EqualTo("Snapshot count exceeds its section bounds."));
            Assert.That(Assert.Throws<SimSnapshotException>(() => s.LoadState(new SimStateReader(Bytes(99, 1, (A3b.IdA, 46, 56))))).Message, Is.EqualTo(LengthDiffers));
            Assert.That(A3b.Count(w), Is.EqualTo(0), "a refused load leaves no entries");
            Assert.DoesNotThrow(() => s.LoadState(new SimStateReader(own)));
            Assert.That(A3b.Count(w), Is.EqualTo(1));
            Assert.That(A3b.Entry(w, A3b.KeyA, out long seen, out long since), Is.True);
            Assert.That(seen, Is.EqualTo(A3b.Seen)); Assert.That(since, Is.EqualTo(A3b.Since));
        }

        // 7(e): a second shooter (key 4 at Cell(22,13), thinking phase 4) makes two entries at tick 100: (A, 46, 56) and
        // (A2, 64, 74) in key order, 56 bytes, and WriteState equals the hash computed by hand in both allocation orders.
        [TestCase(false)]
        [TestCase(true)]
        public void TwoShooters_WriteStateByHand(bool reverse)
        {
            M6World w = RunTo(Ai, 100, reverse, A3b.Second());
            Assert.That(A3.Id(w, A3b.KeyA).Value, Is.EqualTo(A3b.IdA));
            Assert.That(A3.Id(w, A3b.KeyA2).Value, Is.EqualTo(A3b.IdA2));
            Assert.That(A3.FirstThink(w, A3b.KeyA2), Is.EqualTo(4));
            Assert.That(A3b.Count(w), Is.EqualTo(2));
            Assert.That(A3b.Entry(w, A3b.KeyA2, out long seen, out long since), Is.True);
            Assert.That(seen, Is.EqualTo(64)); Assert.That(since, Is.EqualTo(74));
            byte[] own = Own(w);
            Assert.That(own.Length, Is.EqualTo(56));
            Assert.That(own, Is.EqualTo(Bytes(100, 2, (A3b.IdA, 46, 56), (A3b.IdA2, 64, 74))));
            var expected = new StateHash();
            expected.Add("battle.ai-suppress"); expected.Add(1); expected.Add(100); expected.Add(2);
            expected.Add(A3b.IdA); expected.Add(46L); expected.Add(56L);
            expected.Add(A3b.IdA2); expected.Add(64L); expected.Add(74L);
            var actual = new StateHash();
            w.System<TargetSuppress>().WriteState(actual);
            Assert.That(actual.Value, Is.EqualTo(expected.Value));
        }
    }
}
