using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // F5 (3): new fields enter PlayerLog and reproduce the original recording, from the log alone
    // and after a snapshot taken while block is held.
    public sealed class F5ReplayTests
    {
        private const int Ticks = 60;
        private const int SaveAt = 20;

        // Block held 10..40, throw on tick 25 only, skill slot 2 on 30..35; movement held throughout.
        private static readonly (long AtTick, PlayerInput Input)[] Script = {
            (1, new PlayerInput(500, 0, 20000, 15000)),
            (10, new PlayerInput(500, 0, 20000, 15000, block: true)),
            (25, new PlayerInput(500, 0, 20000, 15000, block: true, @throw: true)),
            (26, new PlayerInput(500, 0, 20000, 15000, block: true)),
            (30, new PlayerInput(500, 0, 20000, 15000, block: true, skillSlot: 2)),
            (36, new PlayerInput(500, 0, 20000, 15000, block: true)),
            (41, new PlayerInput(500, 0, 20000, 15000))
        };

        private static ulong[] Record(out SimSession session, out byte[] saved)
        {
            var w = F5World.World();
            session = w.Session;
            foreach (var item in Script) session.SetPlayerInput(item.AtTick, item.Input);
            var hashes = new ulong[Ticks];
            saved = null;
            for (int tick = 1; tick <= Ticks; tick++)
            {
                session.StepOnce();
                hashes[tick - 1] = session.StateHash;
                if (tick == SaveAt) saved = w.Sim.SaveRetainedSnapshot();
            }
            return hashes;
        }

        [Test]
        public void ReplayFromPlayerLogOnly_MatchesTheOriginalRecording()
        {
            ulong[] original = Record(out SimSession source, out _);
            var log = source.PlayerLog;
            Assert.That(log.Count, Is.EqualTo(Script.Length));
            for (int i = 0; i < Script.Length; i++)
            {
                Assert.That(log[i].AtTick, Is.EqualTo(Script[i].AtTick), "log " + i);
                Assert.That(log[i].Input, Is.EqualTo(Script[i].Input), "log " + i);
            }
            Assert.That(log[0].Input.Block, Is.False);
            Assert.That((log[1].AtTick, log[1].Input.Block), Is.EqualTo((10L, true)), "block starts");
            Assert.That((log[6].AtTick, log[6].Input.Block), Is.EqualTo((41L, false)), "block ends");

            var replay = F5World.World();
            foreach (var record in log) replay.Session.SetPlayerInput(record.AtTick, record.Input);
            for (int tick = 1; tick <= Ticks; tick++)
            {
                replay.Session.StepOnce();
                Assert.That(replay.Session.StateHash, Is.EqualTo(original[tick - 1]), "replay tick=" + tick);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RestoreWhileBlockHeld_RequeuedFromPlayerLog_MatchesTheOriginalRecording(bool reverse)
        {
            ulong[] original = Record(out SimSession source, out byte[] saved);
            var restored = F5World.World(reverse: reverse);
            restored.Sim.RestoreRetainedSnapshot(saved, reverseAllocation: reverse);
            var resumed = restored.Session;
            Assert.That(resumed.Tick, Is.EqualTo(SaveAt));
            Assert.That(resumed.StateHash, Is.EqualTo(original[SaveAt - 1]));
            PlayerInput held = default;
            foreach (var record in source.PlayerLog)
                if (record.AtTick <= SaveAt) held = record.Input;
            Assert.That(held.Block, Is.True, "tick 20 is inside the held block");
            resumed.SetPlayerInput(SaveAt + 1, held);
            var later = new System.Collections.Generic.List<PlayerInputRecord>();
            foreach (var record in source.PlayerLog)
                if (record.AtTick > SaveAt) later.Add(record);
            Assert.That(later.Count, Is.EqualTo(5));
            if (reverse) later.Reverse();
            foreach (var record in later) resumed.SetPlayerInput(record.AtTick, record.Input);
            for (int tick = SaveAt + 1; tick <= Ticks; tick++)
            {
                resumed.StepOnce();
                if (tick == SaveAt + 1) Assert.That(restored.Sim.Context.Input.Block, Is.True, "tick 21 block");
                Assert.That(resumed.StateHash, Is.EqualTo(original[tick - 1]), "restored tick=" + tick);
            }
        }
    }
}
