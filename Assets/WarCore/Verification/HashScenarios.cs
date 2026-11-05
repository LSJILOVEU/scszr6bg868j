using System;
using WarCore.Sim;
using WarCore.Verification.Fixtures;
using Simulation = WarCore.Sim.Sim;
using Fixture = WarCore.Verification.Fixtures.Fixtures;

namespace WarCore.Verification
{
    internal static class HashScenarios
    {
        internal static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }

        internal static void WholeMatch(Report report)
        {
            Simulation baseline = Fixture.Create(123);
            for (int tick = 1; tick <= 510; tick++) baseline.Step(Fixture.Input(tick));
            Require(baseline.Scheduler.PendingCount > 0, "Whole match must save a pending timer.");
            byte[] snapshot = baseline.SaveSnapshot();
            ulong saved = baseline.StateHash();
            report.Line("whole.save510", "hash=" + StableDigest.Hex64(saved) + ";pending=" + baseline.Scheduler.PendingCount);
            var expected = new ulong[490];
            var uninterrupted = new StableDigest();
            for (int tick = 511; tick <= 1000; tick++)
            {
                baseline.Step(Fixture.Input(tick));
                expected[tick - 511] = baseline.StateHash();
                uninterrupted.Row(tick + ":" + StableDigest.Hex64(expected[tick - 511]));
            }
            report.Line("whole.final", "hash=" + StableDigest.Hex64(baseline.StateHash()));
            report.Digest("whole.sequence", uninterrupted);
            Require(baseline.StateHash() == 0x8166D6848CDEB66CUL, "Whole-match known vector mismatch.");
            foreach (bool fresh in new[] { false, true })
            {
                Simulation restored = fresh ? Fixture.Create(999) : baseline;
                restored.RestoreSnapshot(snapshot);
                Require(restored.Clock.Tick == 510 && restored.StateHash() == saved, "Whole snapshot restore mismatch.");
                RequireBytes(snapshot, restored.SaveSnapshot());
                var digest = new StableDigest();
                for (int tick = 511; tick <= 1000; tick++)
                {
                    restored.Step(Fixture.Input(tick));
                    ulong value = restored.StateHash();
                    Require(value == expected[tick - 511], "Whole continuation mismatch at " + tick);
                    digest.Row(tick + ":" + StableDigest.Hex64(value));
                }
                string key = fresh ? "whole.fresh" : "whole.inplace";
                report.Line(key + ".final", "hash=" + StableDigest.Hex64(restored.StateHash()));
                report.Digest(key + ".sequence", digest);
            }
        }

        internal static void Heap(Report report, bool reverse)
        {
            string key = reverse ? "heap.reverse" : "heap.forward";
            Simulation baseline = SnapshotFixtures.A3(reverse);
            Simulation restored = SnapshotFixtures.A3(reverse);
            ulong known = Fixture.Hash(baseline.Scheduler);
            report.Line(key + ".known", "hash=" + StableDigest.Hex64(known) + ";tick=1;pending=3");
            Require(known == 0x22CB551F19EC00FEUL, "Nonempty heap known vector mismatch.");
            byte[] snapshot = restored.SaveSnapshot();
            long extra = restored.Scheduler.Schedule(9, SnapshotFixtures.Payload(99));
            restored.Step(default);
            restored.RestoreSnapshot(snapshot);
            Require(!restored.Scheduler.Cancel(extra) && restored.Events.Read(2).Length == 0, "Heap rollback retained later work.");
            RequireBytes(snapshot, restored.SaveSnapshot());
            Require(baseline.Scheduler.Schedule(5, SnapshotFixtures.Payload(6)) == 6, "Heap next id");
            Require(restored.Scheduler.Schedule(5, SnapshotFixtures.Payload(6)) == 6, "Restored heap next id");
            int consumed = 0;
            long[] ids = { 2, 5, 4, 6 };
            var early = new StableDigest();
            for (int tick = 2; tick <= 8; tick++)
            {
                baseline.Step(default);
                restored.Step(default);
                bool found = baseline.Scheduler.TryDequeue(out ScheduledTask a);
                Require(restored.Scheduler.TryDequeue(out ScheduledTask b) == found, "Heap due count");
                if (found)
                {
                    Require(a.Id == ids[consumed++] && a.Id == b.Id && a.Event.Equals(b.Event), "Heap due order/payload");
                    early.Row("consumed=" + a.Id);
                }
                Require(baseline.StateHash() == restored.StateHash(), "Heap early continuation");
                Require(Fixture.Hash(baseline.Scheduler) == Fixture.Hash(restored.Scheduler), "Heap scheduler continuation");
                early.Row(tick + ":" + StableDigest.Hex64(baseline.StateHash()));
            }
            Require(consumed == 4, "Heap tasks must all be consumed.");
            report.Digest(key + ".restore1", early);
            // The frozen A3 tasks expire by tick 8. Report the empty heap at 510 honestly.
            for (int tick = 9; tick <= 510; tick++) baseline.Step(default);
            Continue510(report, key, baseline, restored);
        }

        internal static void ShiftedHeap(Report report)
        {
            // An additional, explicitly shifted A3 workload keeps pending tasks at the 510 save point.
            var baseline = new Simulation(123, config: new SimConfig(3, 8, 8, 4, 8));
            foreach (int tag in new[] { 1, 2, 3, 4, 5 })
                baseline.Scheduler.Schedule(tag == 4 ? 512 : 511, SnapshotFixtures.Payload(tag));
            baseline.Scheduler.Cancel(1);
            baseline.Scheduler.Cancel(3);
            for (int tick = 1; tick <= 510; tick++) baseline.Step(default);
            var restored = new Simulation(999, config: new SimConfig(3, 8, 8, 4, 8));
            Continue510(report, "heap.shifted", baseline, restored);
        }

        private static void Continue510(Report report, string key, Simulation baseline, Simulation restored)
        {
            byte[] snapshot = baseline.SaveSnapshot();
            ulong saved = baseline.StateHash();
            report.Line(key + ".save510", "hash=" + StableDigest.Hex64(saved) + ";pending=" + baseline.Scheduler.PendingCount);
            restored.RestoreSnapshot(snapshot);
            Require(restored.StateHash() == saved, "Heap save510 restore");
            RequireBytes(snapshot, restored.SaveSnapshot());
            var uninterrupted = new StableDigest();
            var resumed = new StableDigest();
            for (int tick = 511; tick <= 1000; tick++)
            {
                baseline.Step(default);
                restored.Step(default);
                bool found = baseline.Scheduler.TryDequeue(out ScheduledTask a);
                Require(restored.Scheduler.TryDequeue(out ScheduledTask b) == found, "Heap save510 due count");
                if (found) Require(a.Id == b.Id && a.Event.Equals(b.Event), "Heap save510 due payload");
                Require(baseline.StateHash() == restored.StateHash(), "Heap save510 continuation at " + tick);
                uninterrupted.Row(tick + ":" + StableDigest.Hex64(baseline.StateHash()));
                resumed.Row(tick + ":" + StableDigest.Hex64(restored.StateHash()));
            }
            report.Line(key + ".final", "hash=" + StableDigest.Hex64(baseline.StateHash()));
            report.Line(key + ".restoredFinal", "hash=" + StableDigest.Hex64(restored.StateHash()));
            report.Digest(key + ".sequence", uninterrupted);
            report.Digest(key + ".restoredSequence", resumed);
        }

        private static void RequireBytes(byte[] expected, byte[] actual)
        {
            Require(expected.Length == actual.Length, "Snapshot length");
            for (int i = 0; i < expected.Length; i++) Require(expected[i] == actual[i], "Snapshot bytes");
        }

        internal static void Load(Report report)
        {
            var sim = new Simulation(123, config: new SimConfig(8, 64, 64, 2000));
            var load = new LoadSystem(sim, 2000);
            sim.Register("load", load);
            var digest = new StableDigest();
            for (int tick = 1; tick <= 1000; tick++)
            {
                sim.Step(default);
                digest.Row(tick + ":" + StableDigest.Hex64(sim.StateHash()));
            }
            Require(load.AppliedTasks == 3992 && sim.Scheduler.PendingCount == 8 && sim.Events.EventsDroppedTotal == 0, "Load work incomplete");
            report.Line("load.final", "entities=2000;ticks=1000;hash=" + StableDigest.Hex64(sim.StateHash()) + ";applied=3992;pending=8;dropped=0");
            report.Digest("load.sequence", digest);
        }
    }
}
