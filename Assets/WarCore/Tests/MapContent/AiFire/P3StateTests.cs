using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 9: the snapshot with a burst (version 3), its load checks, the allocation order and the exact payload.
    public sealed class P3StateTests
    {
        private const int B = 3, Q = 20, K = 10;
        private static string Ai => P3.Ai(0, null, B, Q);
        private static string AiK => P3.Ai(0, K, B, Q);

        private static void Restored(long save, long[] firesAfter)
        {
            P3.Trace straight = P3.Run(P2.Cast(), P1.Walk, Ai, 200);
            Assert.That(straight.A.FireTicks(200), Is.EqualTo(P3.Expect(P1.T, B, Q, 200)));
            byte[] snapshot = P3.RunTo(Ai, save).Sim.SaveRetainedSnapshot();
            M6World b = P3.World(P2.Cast(), Ai);
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(P2.Count(b), Is.EqualTo(1));
            Assert.That(b.Hash, Is.EqualTo(straight.Hashes[(int)save]), "at once");
            var fires = new List<long>();
            for (long tick = save + 1; tick <= 200; tick++)
            {
                b.Sim.Step(P2.Drive(b, tick, P1.Walk));
                Assert.That(b.Hash, Is.EqualTo(straight.Hashes[(int)tick]), "tick " + tick);
                if (P1.Fired(b, P3.KeyA)) fires.Add(tick);
            }
            Assert.That(fires, Is.EqualTo(firesAfter));
        }

        // 9(a): saved on 47, inside the first burst (two shots fired): the third shot still on 50, then the pause.
        [Test]
        public void Restore_MidBurst() => Restored(47, new long[] { 50, 70, 75, 80, 100, 105, 110, 130, 190, 195, 200 });

        // 9(b): saved on 60, inside the pause (the weapon ready since 55): the first shot after the restore on 70, not 61.
        [Test]
        public void Restore_MidPause() => Restored(60, new long[] { 70, 75, 80, 100, 105, 110, 130, 190, 195, 200 });

        // A legal record with the count 0 and a latest shot above 0: the tenth round (130) emptied the magazine and reset
        // the count. Saved on 150 (reloading) the record is (0, 130); restored, it hashes alike and fires 190, 195, 200.
        [Test]
        public void Restore_AfterTheEmptyingShot_CountZeroWithALatestShot()
        {
            P3.Trace straight = P3.Run(P2.Cast(), P1.Walk, Ai, 200);
            M6World f = P3.RunTo(Ai, 150);
            P3.Saved s = P3.Parse(P3.Payload(f.System<ReactionFire>()));
            Assert.That(s.Count, Is.EqualTo(1));
            Assert.That(s.Bursts, Is.EqualTo(new[] { (0, 130L) }), "the count reset by the emptying shot, the shot itself recorded");
            Assert.That(s.Entries[0].First, Is.LessThan(130L));
            byte[] snapshot = f.Sim.SaveRetainedSnapshot();
            M6World b = P3.World(P2.Cast(), Ai);
            Assert.DoesNotThrow(() => b.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(b.Hash, Is.EqualTo(straight.Hashes[150]));
            Assert.That(P3.Parse(P3.Payload(b.System<ReactionFire>())).Bursts, Is.EqualTo(new[] { (0, 130L) }));
            var fires = new List<long>();
            for (long tick = 151; tick <= 200; tick++)
            {
                b.Sim.Step(P2.Drive(b, tick, P1.Walk));
                Assert.That(b.Hash, Is.EqualTo(straight.Hashes[(int)tick]), "tick " + tick);
                if (P1.Fired(b, P3.KeyA)) fires.Add(tick);
            }
            Assert.That(fires, Is.EqualTo(new long[] { 190, 195, 200 }));
        }

        // 9(c): a { R 0, K 10, B 3, Q 20 } snapshot into a world of the same version with another B, another Q, or K = 1.
        [TestCase(10, 4, 20, "AI burst length differs.")]
        [TestCase(10, 3, 30, "AI burst pause differs.")]
        [TestCase(1, 3, 20, "AI sense interval differs.")]
        public void Restore_Mismatch_IsRefused(int sense, int burst, int pause, string message)
        {
            byte[] snapshot = P3.RunTo(AiK, 60).Sim.SaveRetainedSnapshot();
            M6World other = P3.World(P2.Cast(), P3.Ai(0, sense, burst, pause));
            Assert.That(other.System<ReactionFire>().StateVersion, Is.EqualTo(3));
            var error = Assert.Throws<SimSnapshotException>(() => other.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(error.Message, Is.EqualTo(message));
        }

        // 9(d): B above 0 is version 3 whatever K; K = 10 without a burst is version 2; a version 3 snapshot into a version 2
        // world with the same systems is refused by the framework's manifest check before the load.
        [Test]
        public void Versions_AndTheManifestCheck()
        {
            Assert.That(P3.World(P2.Cast(), Ai).System<ReactionFire>().StateVersion, Is.EqualTo(3), "K = 1");
            M6World three = P3.RunTo(AiK, 60), two = P3.World(P2.Cast(), P3.Ai(0, K, 0));
            Assert.That(three.System<ReactionFire>().StateVersion, Is.EqualTo(3), "K = 10");
            Assert.That(two.System<ReactionFire>().StateVersion, Is.EqualTo(2));
            Assert.That(P3.Systems(two), Is.EqualTo(P3.Systems(three)), "the same systems");
            var error = Assert.Throws<SimSnapshotException>(() => two.Sim.RestoreRetainedSnapshot(three.Sim.SaveRetainedSnapshot()));
            Assert.That(error.Message, Does.Contain("State registration manifest mismatch"));
            Assert.That(error.Message, Does.Contain("battle.ai-fire"));
            Assert.That(error.Message, Does.Contain("v3"));
            Assert.That(error.Message, Does.Contain("v2"));
        }

        // 9(e): K = 10, B = 3, Q = 20, R = 0, A and A2 aside (phases 4 and 6, first samples 46 and 44). Both allocation
        // orders hash alike on every tick to 300 and fire on the same ticks; the forward save on 61 restored with the
        // reverse allocation hashes alike at once and afterwards. Its payload: 108 bytes, (A, B, 46, 61) with (3, 56) and
        // (A2, B, 44, 61) with (3, 54), in stable-id order.
        [Test]
        public void ReverseAllocation_SameHashesAndRestore()
        {
            const long Last = 300, Save = 61;
            P3.Trace forward = P3.Run(P2.Cast(P2.A2Aside), P1.Walk, AiK, Last);
            P3.Trace reverse = P3.Run(P2.Cast(P2.A2Aside), P1.Walk, AiK, Last, true);
            Assert.That((forward.W.Unit(P3.KeyA).Id, forward.W.Unit(P3.KeyA2).Id), Is.EqualTo((1, 4)));
            Assert.That(reverse.W.Unit(P3.KeyA).Id, Is.Not.EqualTo(forward.W.Unit(P3.KeyA).Id), "A changes slot");
            ulong a = forward.Id(P3.KeyA), a2 = forward.Id(P3.KeyA2), b = forward.Id(P3.KeyB);
            Assert.That(P2.Phase(a2, K), Is.EqualTo(6L));
            Assert.That(forward.A2.FirstSelect(b), Is.EqualTo(42L));
            Assert.That(forward.A.FireTicks(Save), Is.EqualTo(new long[] { 46, 51, 56 }));
            Assert.That(forward.A2.FireTicks(Save), Is.EqualTo(new long[] { 44, 49, 54 }));
            for (int t = 0; t <= Last; t++) Assert.That(reverse.Hashes[t], Is.EqualTo(forward.Hashes[t]), "tick " + t);
            Assert.That(reverse.A.FireTicks(Last), Is.EqualTo(forward.A.FireTicks(Last)));
            Assert.That(reverse.A2.FireTicks(Last), Is.EqualTo(forward.A2.FireTicks(Last)));

            M6World f = P3.RunTo(AiK, Save, false, P2.Cast(P2.A2Aside));
            Assert.That(P2.Count(f), Is.EqualTo(2), "two entries saved");
            byte[] snapshot = f.Sim.SaveRetainedSnapshot();
            M6World r = P3.World(P2.Cast(P2.A2Aside), AiK, true);
            Assert.DoesNotThrow(() => r.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: true));
            Assert.That(r.Unit(P3.KeyA).Id, Is.Not.EqualTo(f.Unit(P3.KeyA).Id), "A changed slot");
            Assert.That(r.Hash, Is.EqualTo(f.Hash));
            Assert.That(P2.Count(r), Is.EqualTo(2));
            Assert.That(P3.Payload(r.System<ReactionFire>()), Is.EqualTo(P3.Payload(f.System<ReactionFire>())), "the restored reverse world writes the same bytes");
            for (long tick = Save + 1; tick <= Last; tick++)
            {
                r.Sim.Step(P2.Drive(r, tick, P1.Walk));
                Assert.That(r.Hash, Is.EqualTo(forward.Hashes[(int)tick]), "tick " + tick);
            }

            byte[] bytes = P3.Payload(f.System<ReactionFire>());
            Assert.That(bytes.Length, Is.EqualTo(108));
            P3.Saved s = P3.Parse(bytes);
            Assert.That(a, Is.LessThan(a2));
            Assert.That(s.Entries, Is.EqualTo(new[] { (a, b, 46L, Save), (a2, b, 44L, Save) }));
            Assert.That((s.K, s.B, s.Q), Is.EqualTo((K, B, Q)));
            Assert.That(s.Bursts, Is.EqualTo(new[] { (3, 56L), (3, 54L) }));
            Assert.That(s.RemainingAtEnd, Is.EqualTo(0));
        }

        // 9(f): with B = 0 the pause is neither saved nor compared: a snapshot restores into a world with another Q, in
        // version 1 (R alone) and in version 2 (K = 10).
        [TestCase(15, null, 1)]
        [TestCase(0, 10, 2)]
        public void ZeroBurst_PauseIsNotCompared(int reaction, int? sense, int version)
        {
            M6World f = P3.RunTo(P3.Ai(reaction, sense, 0, 20), 60);
            M6World o = P3.World(P2.Cast(), P3.Ai(reaction, sense, 0, 50));
            Assert.That(f.System<ReactionFire>().StateVersion, Is.EqualTo(version));
            Assert.That(o.System<ReactionFire>().StateVersion, Is.EqualTo(version));
            byte[] snapshot = f.Sim.SaveRetainedSnapshot();
            Assert.DoesNotThrow(() => o.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(o.Hash, Is.EqualTo(f.Hash));
        }

        // 9(g): the 108-byte payload of acceptance 7 (A's entry first: First at offset 24, the burst block from 84: A's
        // count at 84, A's latest shot at 88, A2's count at 96) patched and loaded into the decision of a fresh world of the
        // same configuration.
        private static byte[] Payload7(out M6World fresh)
        {
            P3.Trace r = P3.Run(P2.Cast(P2.A2Behind), P1.Walk, Ai, 61);
            byte[] bytes = P3.Payload(r.W.System<ReactionFire>());
            Assert.That(bytes.Length, Is.EqualTo(108));
            Assert.That(BitConverter.ToInt64(bytes, 24), Is.EqualTo(40L), "A's First");
            Assert.That(BitConverter.ToInt32(bytes, 84), Is.EqualTo(3), "A's count");
            Assert.That(BitConverter.ToInt64(bytes, 88), Is.EqualTo(50L), "A's latest shot");
            Assert.That(BitConverter.ToInt32(bytes, 96), Is.EqualTo(0), "A2's count");
            Assert.That(BitConverter.ToInt64(bytes, 100), Is.EqualTo(0L), "A2's latest shot");
            fresh = P3.World(P2.Cast(P2.A2Behind), Ai);
            return bytes;
        }
        private static void Patch(byte[] bytes, int offset, int value) => Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
        private static void Patch(byte[] bytes, int offset, long value) => Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 8);

        [Test]
        public void Load_UnpatchedPayload_IsAccepted()
        {
            byte[] bytes = Payload7(out M6World w);
            Assert.DoesNotThrow(() => w.System<ReactionFire>().LoadState(new SimStateReader(bytes)));
            Assert.That(P3.Payload(w.System<ReactionFire>()), Is.EqualTo(bytes), "written back as read");
        }

        // A's record patched to the legal (0, 50): the count 0 with a latest shot inside the entry is accepted.
        [Test]
        public void Load_CountZeroWithALatestShot_IsAccepted()
        {
            byte[] bytes = Payload7(out M6World w);
            Patch(bytes, 84, 0);
            Assert.DoesNotThrow(() => w.System<ReactionFire>().LoadState(new SimStateReader(bytes)));
            Assert.That(P3.Parse(P3.Payload(w.System<ReactionFire>())).Bursts, Is.EqualTo(new[] { (0, 50L), (0, 0L) }));
        }

        [TestCase(-1)]
        [TestCase(4)]
        public void Load_CountOutOfRange_IsRefused(int count)
        {
            byte[] bytes = Payload7(out M6World w);
            Patch(bytes, 84, count);
            var error = Assert.Throws<SimSnapshotException>(() => w.System<ReactionFire>().LoadState(new SimStateReader(bytes)));
            Assert.That(error.Message, Is.EqualTo("AI burst shot count is out of range."));
        }

        // The latest shot before First, after Last, or 0 with a count above 0 (also with First patched to 0, which a
        // bounds check alone would accept); A2's count 1 with its latest shot 0.
        [TestCase(39L, false)]
        [TestCase(62L, false)]
        [TestCase(0L, false)]
        [TestCase(0L, true)]
        public void Load_LatestShotOutsideTheEntry_IsRefused(long last, bool firstZero)
        {
            byte[] bytes = Payload7(out M6World w);
            Patch(bytes, 88, last);
            if (firstZero) Patch(bytes, 24, 0L);
            var error = Assert.Throws<SimSnapshotException>(() => w.System<ReactionFire>().LoadState(new SimStateReader(bytes)));
            Assert.That(error.Message, Is.EqualTo("AI burst last shot is outside its entry."));
        }

        [Test]
        public void Load_CountWithoutAShot_IsRefused()
        {
            byte[] bytes = Payload7(out M6World w);
            Patch(bytes, 96, 1);
            var error = Assert.Throws<SimSnapshotException>(() => w.System<ReactionFire>().LoadState(new SimStateReader(bytes)));
            Assert.That(error.Message, Is.EqualTo("AI burst last shot is outside its entry."));
        }
    }
}
