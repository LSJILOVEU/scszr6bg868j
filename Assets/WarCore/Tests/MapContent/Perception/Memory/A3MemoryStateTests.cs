using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 10: snapshots of the memory fixture (B hides in the grass, last seen on 46, M = 60, entry deleted on 106).
    public sealed class A3MemoryStateTests
    {
        private const long Save = 70, Seen = 46;
        private static M6World World(int? memory, bool reverse = false) =>
            A3.World(new[] { A3.Observer(), A3.Target(A3.KeyB, 2, A2.Cell(21, 18)) }, memory, reverse);
        private static InputFrame Script(long t) =>
            t == 1 ? A3.Command(SquadCommandKind.Hold, 2) : t == 20 ? A3.Command(SquadCommandKind.MoveTo, 2, A2.Cell(21, 24)) : default;
        private static M6World RunTo(int? memory, long last)
        {
            M6World w = World(memory);
            for (long t = 1; t <= last; t++) w.Sim.Step(A3.Drive(w, Script(t)));
            return w;
        }

        // 10(a): saved mid-memory and restored (both allocation orders): the same recall, the same hashes, the same deletion tick.
        [TestCase(false)]
        [TestCase(true)]
        public void Restore_ContinuesTheMemory(bool reverse)
        {
            M6World straight = World(60);
            var hashes = new List<ulong> { straight.Hash };
            var counts = new List<int> { 0 };
            SimVec3 p = default;
            for (long t = 1; t <= Save + 150; t++)
            {
                straight.Sim.Step(A3.Drive(straight, Script(t)));
                hashes.Add(straight.Hash); counts.Add(A3.Count(straight));
                if (t == Save) Assert.That(A3.Recall(straight, A3.KeyA, t, out _, out p, out _), Is.True);
            }
            M6World a = RunTo(60, Save);
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            M6World b = World(60, reverse);
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: reverse));
            Assert.That(b.Hash, Is.EqualTo(hashes[(int)Save]));
            Assert.That(A3.Recall(b, A3.KeyA, Save, out StableId target, out SimVec3 at, out long seen), Is.True);
            Assert.That(target, Is.EqualTo(A3.Id(b, A3.KeyB)));
            Assert.That(new[] { at.X.Raw, at.Y.Raw, at.Z.Raw }, Is.EqualTo(new[] { p.X.Raw, p.Y.Raw, p.Z.Raw }));
            Assert.That(seen, Is.EqualTo(Seen));
            for (long t = Save + 1; t <= Save + 150; t++)
            {
                b.Sim.Step(A3.Drive(b, default));
                Assert.That(b.Hash, Is.EqualTo(hashes[(int)t]), "tick " + t);
                Assert.That(A3.Count(b), Is.EqualTo(counts[(int)t]), "count, tick " + t);
            }
            Assert.That(counts[105], Is.EqualTo(1)); Assert.That(counts[106], Is.EqualTo(0));
            Assert.That(A3.Commanded(b, A3.KeyB), Is.True);
        }

        // 10(b): another positive length: the memory's own check.
        [Test]
        public void Restore_OtherLength_IsRefused()
        {
            byte[] snapshot = RunTo(60, Save).Sim.SaveRetainedSnapshot();
            var error = Assert.Throws<SimSnapshotException>(() => World(120).Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(error.Message, Is.EqualTo("AI target memory length differs."));
        }

        // 10(c): on against off (either way): the system count check comes first.
        [TestCase(60, 0)]
        [TestCase(0, 60)]
        public void Restore_OnAgainstOff_IsRefused(int saved, int restored)
        {
            byte[] snapshot = RunTo(saved, Save).Sim.SaveRetainedSnapshot();
            var error = Assert.Throws<SimSnapshotException>(() => World(restored).Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(error.Message, Is.EqualTo("System registration manifest count mismatch."));
        }

        // 10(d): hand-written state: a repeated observer, observers out of order, a negative tick, too many entries.
        private static void Entry(SimStateWriter w, ulong key, long seen)
        {
            w.UInt64(key); w.UInt64(99); w.Int64(1); w.Int64(2); w.Int64(3); w.Int64(seen);
        }

        private static void Refused(System.Action<SimStateWriter> write, string message)
        {
            TargetMemory memory = World(60).System<TargetMemory>();
            var w = new SimStateWriter();
            write(w);
            var error = Assert.Throws<SimSnapshotException>(() => memory.LoadState(new SimStateReader(w.ToArray())));
            Assert.That(error.Message, Is.EqualTo(message));
        }

        [Test]
        public void Load_RepeatedObserver_IsRefused() =>
            Refused(w => { w.Int32(60); w.Int32(2); Entry(w, 5, 1); Entry(w, 5, 2); }, "AI target memory observers are not in strictly increasing order.");

        [Test]
        public void Load_ObserversOutOfOrder_IsRefused() =>
            Refused(w => { w.Int32(60); w.Int32(2); Entry(w, 7, 1); Entry(w, 5, 2); }, "AI target memory observers are not in strictly increasing order.");

        [Test]
        public void Load_NegativeTick_IsRefused() =>
            Refused(w => { w.Int32(60); w.Int32(1); Entry(w, 5, -1); }, "AI target memory tick is negative.");

        [Test]
        public void Load_MoreEntriesThanEntities_IsRefused()
        {
            int capacity = World(60).Sim.Entities.Capacity;
            Refused(w => { w.Int32(60); w.Int32(capacity + 1); }, "Snapshot count exceeds its section bounds.");
        }

        // A well-formed hand-written state loads: the checks above refuse only what they name.
        [Test]
        public void Load_WellFormed_Loads()
        {
            M6World w = World(60);
            TargetMemory memory = w.System<TargetMemory>();
            var writer = new SimStateWriter();
            writer.Int32(60); writer.Int32(2); Entry(writer, 5, 0); Entry(writer, 7, 4);
            Assert.DoesNotThrow(() => memory.LoadState(new SimStateReader(writer.ToArray())));
            Assert.That(A3.Count(w), Is.EqualTo(2));
        }
    }
}
