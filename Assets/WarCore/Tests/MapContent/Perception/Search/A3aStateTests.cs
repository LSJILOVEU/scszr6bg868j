using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 8 on the search run of A3aSearchTests (S = 46, walk ordered on 56, standing from H = 146, gives up on
    // G = 206). Saved on the way (100) and while standing (170).
    public sealed class A3aStateTests
    {
        private const long Move = 24, H = 146, G = 206;
        private static readonly SimVec3 Hide = A2.Cell(22, 26);
        private static M6World World(int? search, bool reverse = false) =>
            A3a.World(A3a.BStart, search == null ? A3a.Ai(300) : A3a.Ai(300, search.Value), reverse: reverse);
        private static void Step(M6World w) { long t = w.Sim.Clock.Tick + 1; w.Sim.Step(A3a.Drive(w, A3a.Script(t, Hide, Move))); }

        // 8(a): restored mid-walk and mid-stand (both allocation orders): the same hashes and the same give-up tick.
        [TestCase(100, false)]
        [TestCase(100, true)]
        [TestCase(170, false)]
        [TestCase(170, true)]
        public void Restore_ContinuesTheSearch(long save, bool reverse)
        {
            Assert.That(save, Is.GreaterThanOrEqualTo(56)); Assert.That(save + 150, Is.GreaterThan(G));
            M6World straight = World(60);
            var hashes = new List<ulong> { straight.Hash };
            for (long t = 1; t <= save + 150; t++) { Step(straight); hashes.Add(straight.Hash); }
            M6World a = World(60);
            for (long t = 1; t <= save; t++) Step(a);
            Assert.That(A3a.SearchCount(a), Is.EqualTo(save < H ? 0 : 1));
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            M6World b = World(60, reverse);
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: reverse));
            L4.Calls calls = L4.Observe(b);
            Assert.That(b.Hash, Is.EqualTo(hashes[(int)save]));
            for (long t = save + 1; t <= save + 150; t++)
            {
                Step(b);
                Assert.That(b.Hash, Is.EqualTo(hashes[(int)t]), "tick " + t);
                if (t == H) { Assert.That(A3a.Entry(b, out _, out long since), Is.True); Assert.That(since, Is.EqualTo(H)); }
            }
            Assert.That(calls.Orders.Exists(o => o.tick == G && A3a.Same(o.target, A3a.Point2)), Is.True, "gives up on G");
            Assert.That(calls.Orders.Exists(o => o.tick > save && o.tick < G), Is.False, "nothing else ordered before G");
        }

        private static byte[] SavedAt170(int? search)
        {
            M6World a = World(search);
            for (long t = 1; t <= 170; t++) Step(a);
            return a.Sim.SaveRetainedSnapshot();
        }

        // 8(b): another positive length: the search's own check.
        [Test]
        public void Restore_OtherLength_IsRefused()
        {
            byte[] snapshot = SavedAt170(60);
            var error = Assert.Throws<SimSnapshotException>(() => World(120).Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(error.Message, Is.EqualTo("AI search length differs."));
        }

        // 8(c): on against off, either way: the system count check comes first.
        [TestCase(60, 0)]
        [TestCase(0, 60)]
        public void Restore_OnAgainstOff_IsRefused(int saved, int restored)
        {
            byte[] snapshot = SavedAt170(saved);
            var error = Assert.Throws<SimSnapshotException>(() => World(restored).Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(error.Message, Is.EqualTo("System registration manifest count mismatch."));
        }

        // 8(d): hand-written state.
        private static void Refused(Action<SimStateWriter> write, string message)
        {
            TargetSearch search = World(60).System<TargetSearch>();
            var w = new SimStateWriter();
            write(w);
            var error = Assert.Throws<SimSnapshotException>(() => search.LoadState(new SimStateReader(w.ToArray())));
            Assert.That(error.Message, Is.EqualTo(message));
        }
        private static void Entry(SimStateWriter w, ulong key, long seen, long since) { w.UInt64(key); w.Int64(seen); w.Int64(since); }

        [Test]
        public void Load_RepeatedObserver_IsRefused() =>
            Refused(w => { w.Int32(60); w.Int32(2); Entry(w, 5, 1, 2); Entry(w, 5, 1, 2); }, "AI search observers are not in strictly increasing order.");

        [Test]
        public void Load_ObserversOutOfOrder_IsRefused() =>
            Refused(w => { w.Int32(60); w.Int32(2); Entry(w, 7, 1, 2); Entry(w, 5, 1, 2); }, "AI search observers are not in strictly increasing order.");

        [Test]
        public void Load_NegativeSeen_IsRefused() =>
            Refused(w => { w.Int32(60); w.Int32(1); Entry(w, 5, -1, 2); }, "AI search seen tick is negative.");

        [Test]
        public void Load_ArrivalBeforeSighting_IsRefused() =>
            Refused(w => { w.Int32(60); w.Int32(1); Entry(w, 5, 4, 3); }, "AI search arrival precedes the sighting.");

        [Test]
        public void Load_MoreEntriesThanEntities_IsRefused()
        {
            int capacity = World(60).Sim.Entities.Capacity;
            Refused(w => { w.Int32(60); w.Int32(capacity + 1); }, "Snapshot count exceeds its section bounds.");
        }

        [Test]
        public void Load_WellFormed_Loads()
        {
            M6World w = World(60);
            var writer = new SimStateWriter();
            writer.Int32(60); writer.Int32(2); Entry(writer, 5, 0, 0); Entry(writer, 7, 4, 9);
            Assert.DoesNotThrow(() => w.System<TargetSearch>().LoadState(new SimStateReader(writer.ToArray())));
            Assert.That(A3a.SearchCount(w), Is.EqualTo(2));
        }
    }
}
