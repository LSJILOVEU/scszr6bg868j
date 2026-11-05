using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    public sealed class Wp16BridgeReplayTests
    {
        [Test]
        public void RestoredReverseWorld_ReloadsHeldInputAndFutureMixedQueues_MatchesEveryBridgeTick()
        {
            var recipe = Recipe(new[] { U(1), U(2, 14000), U(3, 70000, 35000, team: 2, squad: 2) });
            var original = new M6World(recipe);
            var source = original.Session;
            // Keep future schedules separately: PlayerLog records consumed changes only.
            var futureInputs = new (long AtTick, PlayerInput Input)[] {
                (9, new PlayerInput(-600, -800, 18000, 16000, true)),
                (14, new PlayerInput(500, 500)),
                (19, default)
            };
            var futureCommands = new[] {
                BridgeCommand.MoveTo(9, 1, 40000, 20000),
                BridgeCommand.MoveTo(14, 1, 45000, 25000)
            };
            source.SetPlayerInput(1, new PlayerInput(1000, 0));
            source.SetPlayerInput(3, new PlayerInput(0, 1000, 20000, 15000, true));
            source.Enqueue(BridgeCommand.MoveTo(3, 1, 40000, 10000));
            foreach (var input in futureInputs) source.SetPlayerInput(input.AtTick, input.Input);
            foreach (var command in futureCommands) source.Enqueue(command);
            const int saveAt = 5;
            for (int tick = 1; tick <= saveAt; tick++) source.StepOnce();
            byte[] saved = original.Sim.SaveRetainedSnapshot();
            Assert.That(source.PlayerLog.Count, Is.EqualTo(2));

            var restored = new M6World(recipe, reverse: true);
            restored.Sim.RestoreRetainedSnapshot(saved, reverseAllocation: true);
            var resumed = restored.Session;
            Assert.That(restored.Unit(1), Is.Not.EqualTo(original.Unit(1)));
            Assert.That(restored.Sim.SaveRetainedSnapshot(), Is.EqualTo(saved));
            Assert.That(resumed.StateHash, Is.EqualTo(source.StateHash));
            Assert.That(resumed.PlayerLog.Count, Is.EqualTo(0));
            PlayerInput held = default;
            foreach (var record in source.PlayerLog)
                if (record.AtTick <= saveAt) held = record.Input;
            resumed.SetPlayerInput(saveAt + 1, held);
            // Reverse queue load order as well as entity creation order.
            for (int i = futureInputs.Length - 1; i >= 0; i--)
                resumed.SetPlayerInput(futureInputs[i].AtTick, futureInputs[i].Input);
            for (int i = futureCommands.Length - 1; i >= 0; i--)
                resumed.Enqueue(futureCommands[i]);

            // A new bridge starts with zero wall-time remainder. This contract compares ticks
            // via StepOnce; it does not claim to restore frame interpolation or Advance timing.
            for (int tick = saveAt + 1; tick <= 30; tick++)
            {
                var before = Point(restored);
                var allyBefore = Point(restored, 2);
                source.StepOnce(); resumed.StepOnce();
                Assert.That(resumed.Tick, Is.EqualTo(tick));
                Assert.That(resumed.StateHash, Is.EqualTo(source.StateHash), "restored bridge tick=" + tick);
                SameEvents(original, restored);
                if (tick < 9)
                {
                    Assert.That((Point(restored) - before).X.Raw, Is.EqualTo(0L));
                    Assert.That((Point(restored) - before).Z.Raw, Is.EqualTo(13107L));
                }
                if (tick == 9)
                {
                    Assert.That((Point(restored) - before).X.Raw, Is.EqualTo(-7864L));
                    Assert.That((Point(restored) - before).Z.Raw, Is.EqualTo(-10485L));
                    Assert.That(Point(restored, 2), Is.Not.EqualTo(allyBefore));
                    Assert.That(restored.Read<MoveIntent>(2).Target.X.Raw, Is.EqualTo(2621440L));
                    Assert.That(restored.Read<MoveIntent>(2).Target.Z.Raw, Is.EqualTo(1310720L));
                    Idle(restored, 1);
                }
                if (tick >= 19)
                {
                    Assert.That((Point(restored) - before).X.Raw, Is.EqualTo(0L));
                    Assert.That((Point(restored) - before).Z.Raw, Is.EqualTo(0L));
                }
            }
            Assert.That(resumed.PlayerLog.Count, Is.EqualTo(4));
            Assert.That(resumed.PlayerLog[0].AtTick, Is.EqualTo(6L));
            Assert.That(resumed.PlayerLog[0].Input, Is.EqualTo(held));
            for (int i = 0; i < futureInputs.Length; i++)
            {
                Assert.That(resumed.PlayerLog[i + 1].AtTick, Is.EqualTo(futureInputs[i].AtTick));
                Assert.That(resumed.PlayerLog[i + 1].Input, Is.EqualTo(futureInputs[i].Input));
            }
            Assert.That(resumed.CommandLog, Is.EqualTo(futureCommands));
        }

        [Test]
        public void MixedLogs_ReplayTwice_AndReverseAllocation_MatchEveryTick()
        {
            var recipe = Recipe(new[] { U(1), U(2, 14000), U(3, 70000, 35000, team: 2, squad: 2) });
            var original = new M6World(recipe);
            var s = original.Session;
            // Mix both channels at tick 3, and insert future input out of order.
            s.SetPlayerInput(18, default);
            s.SetPlayerInput(3, new PlayerInput(-1000, 0));
            s.Enqueue(BridgeCommand.MoveTo(3, 1, 40000, 10000));
            s.SetPlayerInput(3, new PlayerInput(0, 1000, 20000, 15000, true));
            s.SetPlayerInput(7, new PlayerInput(0, 1000, 20000, 15000, true)); // No extra log entry.
            s.SetPlayerInput(10, new PlayerInput(-500, 0, 18000, 16000));
            s.SetPlayerInput(23, new PlayerInput(0, 0, 10000, 10000, true)); // Aim/trigger-only change.
            s.Enqueue(BridgeCommand.MoveTo(12, 1, 40000, 20000));
            const int ticks = 40;
            var hashes = new ulong[ticks + 1]; hashes[0] = s.StateHash;
            SimVec3 playerStart = Point(original), allyStart = Point(original, 2);
            SimVec3 stopped = default;
            for (int tick = 1; tick <= ticks; tick++)
            {
                s.StepOnce(); hashes[tick] = s.StateHash;
                if (tick < 3) Assert.That(Point(original), Is.EqualTo(playerStart));
                if (tick == 3)
                {
                    Assert.That(Point(original).Z.Raw, Is.GreaterThan(playerStart.Z.Raw));
                    Assert.That(Point(original).X, Is.EqualTo(playerStart.X));
                    Assert.That(Point(original, 2).X.Raw, Is.GreaterThan(allyStart.X.Raw));
                    Idle(original, 1);
                }
                if (tick == 10) Assert.That(Point(original).X.Raw, Is.LessThan(playerStart.X.Raw));
                if (tick == 18) stopped = Point(original);
                if (tick >= 18) Assert.That(Point(original), Is.EqualTo(stopped));
            }
            Assert.That(s.PlayerLog.Count, Is.EqualTo(4));
            Assert.That(s.CommandLog.Count, Is.EqualTo(2));
            Assert.That(new[] { s.PlayerLog[0].AtTick, s.PlayerLog[1].AtTick, s.PlayerLog[2].AtTick, s.PlayerLog[3].AtTick },
                Is.EqualTo(new long[] { 3, 10, 18, 23 }));

            foreach (bool reverse in new[] { false, false, true })
            {
                var replay = new M6World(recipe, reverse: reverse);
                Assert.That(replay.Session.StateHash, Is.EqualTo(hashes[0]));
                // Load only the public recordings. Reverse load order also exercises scheduling order.
                for (int i = s.PlayerLog.Count - 1; i >= 0; i--)
                    replay.Session.SetPlayerInput(s.PlayerLog[i].AtTick, s.PlayerLog[i].Input);
                for (int i = s.CommandLog.Count - 1; i >= 0; i--)
                    replay.Session.Enqueue(s.CommandLog[i]);
                for (int tick = 1; tick <= ticks; tick++)
                {
                    replay.Session.StepOnce();
                    Assert.That(replay.Session.StateHash, Is.EqualTo(hashes[tick]), $"reverse={reverse} tick={tick}");
                    if (tick == 3)
                    {
                        Assert.That(Point(replay).Z.Raw, Is.GreaterThan(playerStart.Z.Raw));
                        Assert.That(Point(replay).X, Is.EqualTo(playerStart.X));
                        Assert.That(Point(replay, 2).X.Raw, Is.GreaterThan(allyStart.X.Raw));
                    }
                }
                Assert.That(replay.Session.PlayerLog, Is.EqualTo(s.PlayerLog));
                Assert.That(replay.Session.CommandLog, Is.EqualTo(s.CommandLog));
            }
        }

        [Test]
        public void Advance_CapsAtFive_UsesTickSchedule_NotElapsedWallTime()
        {
            var step = new M6World(Recipe());
            var advance = new M6World(Recipe());
            foreach (var w in new[] { step, advance })
            {
                w.Session.SetPlayerInput(14, default);
                w.Session.SetPlayerInput(3, new PlayerInput(1000, 0));
                w.Session.SetPlayerInput(5, new PlayerInput(0, 1000));
                w.Session.SetPlayerInput(6, new PlayerInput(-1000, 0));
                w.Session.SetPlayerInput(9, default);
                w.Session.SetPlayerInput(11, new PlayerInput(0, 1000));
                w.Session.Enqueue(BridgeCommand.MoveTo(5, 2, 60000, 35000));
            }
            SimVec3 expected = Point(step);
            SimFix speed = step.Read<MoveIntent>(1).SpeedPerTick;
            for (int batch = 0; batch < 3; batch++)
            {
                for (int i = 0; i < 5; i++)
                {
                    long tick = step.Session.Tick + 1;
                    step.Session.StepOnce();
                    if (tick == 3 || tick == 4) expected += new SimVec3(speed, default, default);
                    if (tick == 5 || tick >= 11 && tick < 14) expected += new SimVec3(default, default, speed);
                    if (tick >= 6 && tick < 9) expected += new SimVec3(-speed, default, default);
                    Assert.That(Point(step), Is.EqualTo(expected), "tick=" + tick);
                }
                Assert.That(advance.Session.Advance(1000000), Is.EqualTo(5));
                Assert.That(advance.Session.Tick, Is.EqualTo((batch + 1) * 5));
                Assert.That(advance.Session.StateHash, Is.EqualTo(step.Session.StateHash));
                Assert.That(Point(advance), Is.EqualTo(expected));
                Assert.That(advance.Session.PlayerLog, Is.EqualTo(step.Session.PlayerLog));
                Assert.That(advance.Session.CommandLog, Is.EqualTo(step.Session.CommandLog));
                Assert.That(advance.Session.Advance(0), Is.Zero, "capped time must not drain more input");
            }
            Assert.That(advance.Session.PlayerLog.Count, Is.EqualTo(6));
        }

        [Test]
        public void Advance_BelowOneTick_DoesNotConsumeInput()
        {
            var w = new M6World(Recipe());
            var s = w.Session;
            s.SetPlayerInput(1, new PlayerInput(1000, 0));
            SimVec3 start = Point(w);
            Assert.That(s.Advance(0), Is.Zero);
            Assert.That(s.Advance(1), Is.Zero);
            Assert.That(s.Tick, Is.Zero); Assert.That(Point(w), Is.EqualTo(start));
            Assert.That(s.PlayerLog, Is.Empty); Assert.That(PendingCount(s), Is.EqualTo(1));
            s.SetPlayerInput(1, new PlayerInput(-1000, 0));
            Assert.That(s.Advance(33333), Is.EqualTo(1));
            Assert.That(Point(w).X.Raw, Is.LessThan(start.X.Raw));
            Assert.That(s.PlayerLog.Count, Is.EqualTo(1)); Assert.That(s.PlayerLog[0].AtTick, Is.EqualTo(1));
            Assert.That(s.PlayerLog[0].Input.MoveXMilli, Is.EqualTo(-1000));
        }

        [Test]
        public void EndedMatch_DoesNotConsumeFutureInput_OrAppendLogs()
        {
            var w = new M6World(Recipe(match: Match(max: 1)));
            var s = w.Session;
            s.SetPlayerInput(1, new PlayerInput(1000, 0));
            s.SetPlayerInput(2, new PlayerInput(0, 1000));
            s.Enqueue(BridgeCommand.MoveTo(2, 2, 60000, 35000));
            Assert.That(s.Advance(1000000), Is.EqualTo(1));
            Assert.That(s.Winner, Is.Not.Zero);
            ulong hash = s.StateHash;
            for (int i = 0; i < 3; i++)
            {
                s.StepOnce(); Assert.That(s.Advance(1000000), Is.Zero);
                Assert.That(s.Tick, Is.EqualTo(1)); Assert.That(s.StateHash, Is.EqualTo(hash));
                Assert.That(s.PlayerLog.Count, Is.EqualTo(1));
                Assert.That(PendingCount(s), Is.EqualTo(1));
                Assert.That(s.CommandLog, Is.Empty);
            }
        }

        [Test]
        public void FailedStep_DoesNotCommitPlayerLogOrConsumeFutureInput()
        {
            var w = new M6World(Recipe());
            w.Sim.RegisterGameplay("test.wp16-bridge-failure", new FailStep(), SimSystemPhase.Requests);
            w.Session.SetPlayerInput(1, new PlayerInput(1000, 0));
            Assert.Throws<InvalidOperationException>(() => w.Session.StepOnce());
            Assert.That(w.Session.PlayerLog, Is.Empty);
            Assert.That(PendingCount(w.Session), Is.EqualTo(1));
        }

        // After failure/terminal state there is no next successful step to observe the queue.
        private static int PendingCount(SimSession session) => ((ICollection)typeof(SimSession)
            .GetField("pendingPlayer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(session)).Count;

        private sealed class FailStep : IRetainedGameplaySystem
        {
            public int Order => 4;
            public string StateTypeId => "test.wp16-bridge-failure";
            public int StateVersion => 1;
            public void Step(SimContext context) => throw new InvalidOperationException("Injected bridge test failure.");
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { }
        }
    }
}
