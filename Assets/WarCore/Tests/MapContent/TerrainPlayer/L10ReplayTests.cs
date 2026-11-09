using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.L10Fixture;

namespace WarCore.Tests
{
    // L10 (6): on the frozen twin, PlayerLog alone reproduces the recording, and so does a snapshot restored at three phases.
    public sealed class L10ReplayTests
    {
        private const int Ticks = 450;

        private static ulong[] Record(out SimSession session, int saveAt, out byte[] saved, out SimVec3 at)
        {
            var w = new M6World(Twin(), L6.MapBytes());
            session = w.Session;
            Queue(session, L10FlatLockTests.DemoScript);
            var hashes = new ulong[Ticks];
            saved = null; at = default;
            for (int tick = 1; tick <= Ticks; tick++)
            {
                session.StepOnce();
                hashes[tick - 1] = session.StateHash;
                if (tick == saveAt) { saved = w.Sim.SaveRetainedSnapshot(); at = At(w); }
            }
            return hashes;
        }

        [Test]
        public void L10Replay_FromPlayerLogOnly()
        {
            ulong[] original = Record(out SimSession source, 0, out _, out _);
            var log = source.PlayerLog;
            Assert.That(log.Count, Is.EqualTo(6));
            var replay = new M6World(Twin(), L6.MapBytes());
            foreach (var record in log) replay.Session.SetPlayerInput(record.AtTick, record.Input);
            for (int tick = 1; tick <= Ticks; tick++)
            {
                replay.Session.StepOnce();
                Assert.That(replay.Session.StateHash, Is.EqualTo(original[tick - 1]), "replay tick=" + tick);
            }
        }

        private const long M = SimFix.Scale;

        [TestCase(80, false), TestCase(80, true)]
        [TestCase(205, false), TestCase(205, true)]
        [TestCase(250, false), TestCase(250, true)]
        public void L10Replay_RestoredSnapshot(int saveAt, bool reverse)
        {
            ulong[] original = Record(out SimSession source, saveAt, out byte[] saved, out SimVec3 at);
            // The phase each snapshot is taken in.
            if (saveAt == 80) Assert.That(at.X.Raw > 10 * M && at.X.Raw < 19 * M + M / 2 && at.Y.Raw > 0, Is.True, "t80 up the slope");
            if (saveAt == 205) Assert.That(at.Z.Raw > 20 * M && at.Z.Raw < 21 * M + 7 * M / 10, Is.True, "t205 towards the cliff band");
            if (saveAt == 250)
                Assert.That(at.X.Raw > 10 * M && at.X.Raw < 19 * M + M / 2 && at.Z.Raw > 21 * M + 6 * M / 10 && at.Z.Raw < 21 * M + 7 * M / 10,
                    Is.True, "t250 down the slope under the cliff band");

            var restored = new M6World(Twin(), L6.MapBytes(), reverse);
            restored.Sim.RestoreRetainedSnapshot(saved, reverseAllocation: reverse);
            SimSession resumed = restored.Session;
            Assert.That(resumed.Tick, Is.EqualTo(saveAt));
            Assert.That(resumed.StateHash, Is.EqualTo(original[saveAt - 1]));
            PlayerInput held = default;
            var later = new List<PlayerInputRecord>();
            foreach (var record in source.PlayerLog)
                if (record.AtTick <= saveAt) held = record.Input; else later.Add(record);
            resumed.SetPlayerInput(saveAt + 1, held);
            if (reverse) later.Reverse();
            foreach (var record in later) resumed.SetPlayerInput(record.AtTick, record.Input);
            for (int tick = saveAt + 1; tick <= Ticks; tick++)
            {
                resumed.StepOnce();
                Assert.That(resumed.StateHash, Is.EqualTo(original[tick - 1]), "restored tick=" + tick);
            }
        }
    }
}
