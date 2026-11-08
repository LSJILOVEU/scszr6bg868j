using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 5 (snapshots, destroyed and reused slots, the load checks) and 6 (allocation order).
    public sealed class P1StateTests
    {
        private const long Save = P1.T + 7;

        private static M6World RunTo(string ai, long last, bool reverse = false, UnitSpawn[] units = null, params IWorldOption[] extensions)
        {
            M6World w = P1.World(units ?? P1.Cast(), ai, reverse, extensions);
            for (long tick = 1; tick <= last; tick++) w.Sim.Step(P1.Drive(w, tick, P1.Walk));
            return w;
        }

        // 5: saved on t + 7 and restored into a fresh world: the same hashes on every later tick, the first shot still on t + 15.
        [Test]
        public void Restore_KeepsTheClock()
        {
            P1.Trace straight = P1.Run(P1.Cast(), P1.Walk, P1.Ai(15), P1.T + 30);
            Assert.That(straight.FirstFire(P1.KeyA), Is.EqualTo(P1.T + 15));
            byte[] snapshot = RunTo(P1.Ai(15), Save).Sim.SaveRetainedSnapshot();
            M6World b = P1.World(P1.Cast(), P1.Ai(15));
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(P1.Count(b), Is.EqualTo(1));
            Assert.That(b.Hash, Is.EqualTo(straight.Hashes[(int)Save]));
            long first = -1;
            for (long tick = Save + 1; tick <= P1.T + 30; tick++)
            {
                b.Sim.Step(P1.Drive(b, tick, P1.Walk));
                Assert.That(b.Hash, Is.EqualTo(straight.Hashes[(int)tick]), "tick " + tick);
                if (first < 0 && P1.Fired(b, P1.KeyA)) first = tick;
            }
            Assert.That(first, Is.EqualTo(P1.T + 15));
        }

        // 5: the load clears what was there: a snapshot without entries restored into a world with one leaves none.
        [Test]
        public void Restore_ClearsOldEntries()
        {
            byte[] empty = RunTo(P1.Ai(15), 30).Sim.SaveRetainedSnapshot();
            M6World b = RunTo(P1.Ai(15), Save);
            Assert.That(P1.Count(b), Is.EqualTo(1));
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(empty));
            Assert.That(P1.Count(b), Is.EqualTo(0));
        }

        // 5: A2 (key 4, next to A) has an entry on tick 69 and is destroyed on tick 70 (a Requests system, Order 25): the
        // save at the end of tick 70 lists A only and restores; after the tick-end recycle a new entity with a fresh key takes
        // A2's slot and inherits nothing (checked before any later Step or Sense could clear the entry).
        [Test]
        public void DestroyedAndReusedSlot_AreNotWritten()
        {
            const long Kill = 70;
            UnitSpawn[] units = Twin();
            M6World w = RunTo(P1.Ai(15), Kill - 1, false, units, new P1.DestroyProbe(Kill, P1.KeyA2));
            Assert.That(P1.Count(w), Is.EqualTo(2), "A and A2 both hold an entry on tick 69");
            int slot = w.Unit(P1.KeyA2).Id;
            StableId old = P1.Id(w, P1.KeyA2);
            w.Sim.Step(P1.Drive(w, Kill, P1.Walk));
            Assert.That(P1.Count(w), Is.EqualTo(1), "end of tick 70: A only");
            byte[] snapshot = w.Sim.SaveRetainedSnapshot();
            M6World b = P1.World(units, P1.Ai(15), false, new P1.DestroyProbe(Kill, P1.KeyA2));
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(b.Hash, Is.EqualTo(w.Hash));

            EntityId fresh = (EntityId)w.Sim.Entities.Create(StableEntityKey.World(99, w.Sim.Clock.Tick, 1));
            w.Sim.Entities.SynchronizeStableEntities();
            Assert.That(fresh.Id, Is.EqualTo(slot), "the new entity takes A2's slot");
            Assert.That(w.Sim.Entities.StableOf(fresh), Is.Not.EqualTo(old));
            Assert.That(P1.Count(w), Is.EqualTo(1), "the reused slot carries nothing");
            var writer = new SimStateWriter();
            w.System<ReactionFire>().SaveState(writer);
            var reader = new SimStateReader(writer.ToArray());
            Assert.That(reader.Int32(), Is.EqualTo(15));
            Assert.That(reader.Int32(), Is.EqualTo(1), "one entry written");
            Assert.That(reader.UInt64(), Is.EqualTo(P1.Id(w, P1.KeyA).Value));
        }

        // A2 stands 2 m east and 3 m north of A (off A's line to B): it first picks B later than A does.
        internal static UnitSpawn[] Twin()
        {
            UnitSpawn[] cast = P1.Cast();
            return new[] { cast[0], P1.Shooter(P1.KeyA2, A2.Cell(24, 13)), cast[1], cast[2] };
        }

        // 6: two entries at once (different First). Both allocation orders hash alike on every tick and save the same bytes;
        // the forward save, restored with reverse allocation (A2 lands in another slot), continues tick for tick.
        [Test]
        public void ReverseAllocation_SameHashesAndRestore()
        {
            const long Both = 65, Last = 120;
            P1.Trace forward = P1.Run(Twin(), P1.Walk, P1.Ai(15), Last);
            P1.Trace reverse = P1.Run(Twin(), P1.Walk, P1.Ai(15), Last, true);
            long a = forward.FirstSelect(P1.KeyA, P1.KeyB), a2 = forward.FirstSelect(P1.KeyA2, P1.KeyB);
            Assert.That(a, Is.EqualTo(P1.T));
            Assert.That(a2, Is.GreaterThan(a).And.LessThanOrEqualTo(Both), "different First, both before the save");
            for (int t = 0; t <= Last; t++) Assert.That(reverse.Hashes[t], Is.EqualTo(forward.Hashes[t]), "tick " + t);
            Assert.That(reverse.FirstFire(P1.KeyA), Is.EqualTo(forward.FirstFire(P1.KeyA)));
            Assert.That(reverse.FirstFire(P1.KeyA2), Is.EqualTo(forward.FirstFire(P1.KeyA2)));
            Assert.That(forward.FirstFire(P1.KeyA2), Is.EqualTo(a2 + 15));

            M6World f = RunTo(P1.Ai(15), Both, false, Twin());
            Assert.That(P1.Count(f), Is.EqualTo(2), "two entries saved");
            Assert.That(RunTo(P1.Ai(15), Both, true, Twin()).Sim.SaveRetainedSnapshot(), Is.EqualTo(f.Sim.SaveRetainedSnapshot()), "same bytes");
            byte[] snapshot = f.Sim.SaveRetainedSnapshot();
            M6World b = P1.World(Twin(), P1.Ai(15), true);
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: true));
            Assert.That(b.Unit(P1.KeyA).Id != f.Unit(P1.KeyA).Id || b.Unit(P1.KeyA2).Id != f.Unit(P1.KeyA2).Id, Is.True, "a shooter changed slot");
            Assert.That(b.Hash, Is.EqualTo(f.Hash));
            Assert.That(P1.Count(b), Is.EqualTo(2));
            for (long tick = Both + 1; tick <= Last; tick++)
            {
                b.Sim.Step(P1.Drive(b, tick, P1.Walk));
                Assert.That(b.Hash, Is.EqualTo(forward.Hashes[(int)tick]), "tick " + tick);
            }
        }

        [Test]
        public void Restore_OtherLength_IsRefused()
        {
            byte[] snapshot = RunTo(P1.Ai(15), Save).Sim.SaveRetainedSnapshot();
            var error = Assert.Throws<SimSnapshotException>(() => P1.World(P1.Cast(), P1.Ai(16)).Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(error.Message, Is.EqualTo("AI reaction time differs."));
        }

        private static void Refused(Action<SimStateWriter> write, string message)
        {
            M6World w = P1.World(P1.Cast(), P1.Ai(15));
            var writer = new SimStateWriter();
            write(writer);
            var error = Assert.Throws<SimSnapshotException>(() => w.System<ReactionFire>().LoadState(new SimStateReader(writer.ToArray())));
            Assert.That(error.Message, Is.EqualTo(message));
        }
        private static void Entry(SimStateWriter w, ulong key, long first, long last)
        {
            w.UInt64(key); w.UInt64(99); w.Int64(first); w.Int64(last);
        }
        private static ulong IdOf(ulong key) => P1.Id(P1.World(P1.Cast(), P1.Ai(15)), key).Value;

        [Test]
        public void Load_OutOfOrder_IsRefused()
        {
            ulong a = IdOf(P1.KeyA), b = IdOf(P1.KeyB);
            ulong hi = Math.Max(a, b), lo = Math.Min(a, b);
            Refused(w => { w.Int32(15); w.Int32(2); Entry(w, hi, 1, 2); Entry(w, lo, 1, 2); },
                "AI reaction shooters are not in strictly increasing order.");
        }

        [Test]
        public void Load_LastBeforeFirst_IsRefused() =>
            Refused(w => { w.Int32(15); w.Int32(1); Entry(w, IdOf(P1.KeyA), 5, 4); }, "AI reaction last tick precedes its first tick.");

        [Test]
        public void Load_NegativeFirst_IsRefused() =>
            Refused(w => { w.Int32(15); w.Int32(1); Entry(w, IdOf(P1.KeyA), -1, 4); }, "AI reaction tick is negative.");

        [Test]
        public void Load_UnknownShooter_IsRefused() =>
            Refused(w => { w.Int32(15); w.Int32(1); Entry(w, 12345, 1, 4); }, "AI reaction shooter does not exist.");
    }
}
