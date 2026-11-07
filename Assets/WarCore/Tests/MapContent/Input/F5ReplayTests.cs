using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // F5 (3): new fields enter PlayerLog and reproduce the original recording, from the log alone
    // and after a snapshot taken while block is held.
    public sealed class F5ReplayTests
    {
        private const int Ticks = 60;

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

        private static ulong[] Record(out SimSession session, out byte[] saved, int saveAt = 20)
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
                if (tick == saveAt) saved = w.Sim.SaveRetainedSnapshot();
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

        // 20: block held. 25: throw held. 32: skill slot 2 held.
        [TestCase(20, false)]
        [TestCase(20, true)]
        [TestCase(25, false)]
        [TestCase(25, true)]
        [TestCase(32, false)]
        [TestCase(32, true)]
        public void RestoreWhileHeld_RequeuedFromPlayerLog_MatchesTheOriginalRecording(int saveAt, bool reverse)
        {
            ulong[] original = Record(out SimSession source, out byte[] saved, saveAt);
            var restored = F5World.World(reverse: reverse);
            restored.Sim.RestoreRetainedSnapshot(saved, reverseAllocation: reverse);
            var resumed = restored.Session;
            Assert.That(resumed.Tick, Is.EqualTo(saveAt));
            Assert.That(resumed.StateHash, Is.EqualTo(original[saveAt - 1]));
            PlayerInput held = default;
            foreach (var record in source.PlayerLog)
                if (record.AtTick <= saveAt) held = record.Input;
            Assert.That(held.Block, Is.True, "save tick is inside the held block");
            Assert.That(held.Throw, Is.EqualTo(saveAt == 25));
            Assert.That(held.SkillSlot, Is.EqualTo(saveAt == 32 ? 2 : 0));
            resumed.SetPlayerInput(saveAt + 1, held);
            var later = new System.Collections.Generic.List<PlayerInputRecord>();
            foreach (var record in source.PlayerLog)
                if (record.AtTick > saveAt) later.Add(record);
            Assert.That(later.Count, Is.EqualTo(saveAt == 20 ? 5 : saveAt == 25 ? 4 : 2));
            if (reverse) later.Reverse();
            foreach (var record in later) resumed.SetPlayerInput(record.AtTick, record.Input);
            for (int tick = saveAt + 1; tick <= Ticks; tick++)
            {
                resumed.StepOnce();
                if (tick == saveAt + 1)
                {
                    Assert.That(restored.Sim.Context.Input.Block, Is.True, "first resumed tick block");
                    Assert.That(restored.Sim.Context.Input.SkillSlot, Is.EqualTo(saveAt == 32 ? 2 : 0), "first resumed tick skill");
                }
                Assert.That(resumed.StateHash, Is.EqualTo(original[tick - 1]), "restored tick=" + tick);
            }
        }
    }
}
