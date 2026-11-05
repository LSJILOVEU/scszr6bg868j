using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M1BridgeTests
    {
        [Test]
        public void M1_SameMultiUnitRecipeAndCommands_MatchAll600Hashes()
        {
            SimSession first = SimSession.Create(M1TestWorld.Recipe());
            SimSession second = SimSession.Create(M1TestWorld.Recipe());
            first.Enqueue(BridgeCommand.MoveTo(310, 2, 4000, -7000));
            second.Enqueue(BridgeCommand.MoveTo(310, 2, 4000, -7000));
            Assert.That(first.StateHash, Is.EqualTo(second.StateHash));
            for (int tick = 1; tick <= 600; tick++)
            {
                first.StepOnce();
                second.StepOnce();
                Assert.That(first.Tick, Is.EqualTo(tick));
                Assert.That(second.StateHash, Is.EqualTo(first.StateHash), "Tick " + tick);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void M1_Advance_FrameSequencesMatchTickHashesAndCommands(int sequence)
        {
            SimSession reference = SimSession.Create(M1TestWorld.Recipe());
            var hashes = new ulong[601];
            hashes[0] = reference.StateHash;
            for (int tick = 1; tick <= 600; tick++)
            {
                reference.StepOnce();
                hashes[tick] = reference.StateHash;
            }
            SimSession session = SimSession.Create(M1TestWorld.Recipe());
            uint state = 12345;
            int frame = 0;
            int advanceTicks = 0;
            while (session.Tick <= 600 - SimSession.MaxTicksPerFrame)
            {
                long elapsed = FrameMicros(sequence, frame++, ref state);
                long before = session.Tick;
                int count = session.Advance(elapsed);
                advanceTicks += count;
                Assert.That(count, Is.InRange(0, SimSession.MaxTicksPerFrame));
                Assert.That(session.Tick, Is.EqualTo(before + count));
                Assert.That(session.StateHash, Is.EqualTo(hashes[session.Tick]), "Frame " + frame);
                AssertViews(session);
                Assert.That(frame, Is.LessThan(2000));
            }
            while (session.Tick < 600)
            {
                session.StepOnce();
                Assert.That(session.StateHash, Is.EqualTo(hashes[session.Tick]));
                AssertViews(session);
            }
            Assert.That(advanceTicks, Is.GreaterThanOrEqualTo(595));
            Assert.That(session.Tick, Is.EqualTo(600));
            Assert.That(session.CommandLog, Is.EqualTo(reference.CommandLog));
            Assert.That(session.CommandLog.Select(c => c.AtTick), Is.EqualTo(new long[] { 1, 6, 7, 150, 599, 600 }));
        }

        [Test]
        public void M1_CommandLog_ReplaysOnlyExecutedCommandsWithScriptDisabled()
        {
            SimSession original = SimSession.Create(M1TestWorld.Recipe());
            original.Enqueue(BridgeCommand.MoveTo(150, 2, 0, 0));
            original.Enqueue(BridgeCommand.MoveTo(310, 2, 4000, -7000));
            original.Enqueue(BridgeCommand.MoveTo(601, 1, 0, 0));
            var hashes = new ulong[600];
            for (int i = 0; i < hashes.Length; i++)
            {
                original.StepOnce();
                hashes[i] = original.StateHash;
            }
            Assert.That(original.CommandLog.Count, Is.EqualTo(7));
            Assert.That(original.CommandLog.Single(c => c.AtTick == 150).Squad, Is.EqualTo(1));
            Assert.That(original.CommandLog.Any(c => c.AtTick == 601), Is.False);
            WorldRecipe replayRecipe = M1TestWorld.Recipe(false);
            Assert.That(replayRecipe.Script.Length, Is.Zero);
            SimSession replay = SimSession.Create(replayRecipe);
            foreach (BridgeCommand command in original.CommandLog)
                replay.Enqueue(command);
            for (int i = 0; i < hashes.Length; i++)
            {
                replay.StepOnce();
                Assert.That(replay.StateHash, Is.EqualTo(hashes[i]), "Tick " + (i + 1));
            }
            Assert.That(replay.CommandLog, Is.EqualTo(original.CommandLog));
            var writable = (IList<BridgeCommand>)original.CommandLog;
            Assert.Throws<NotSupportedException>(() => writable.Clear());
        }

        [TestCase(10000, 0, 3000)]
        [TestCase(-10001, 7333, 3000)]
        [TestCase(1, -1, 1)]
        [TestCase(-1, -1, 3000)]
        [TestCase(100, 100, 4243)]
        public void M1_Movement_UsesUntruncatedSquaredDistanceAndArrivesExactly(long xMm, long zMm, long speed)
        {
            var recipe = new WorldRecipe(1, MoverKind.Interim,
                new[] { new UnitSpawn(1, 0, 1, 0, 0, speed) },
                new[] { BridgeCommand.MoveTo(1, 1, xMm, zMm) });
            SimSession session = SimSession.Create(recipe);
            SimVec3 target = new SimVec3(SimFix.Ratio(xMm, 1000), default, SimFix.Ratio(zMm, 1000));
            SimFix step = SimFix.Ratio(speed, 1000 * SimClock.TickRate);
            SimWide limit = new SimVec3(step, default, default).LengthSquared;
            SimVec3 previous = M1TestWorld.PositionOf(session);
            bool arrived = false;
            for (int i = 0; i < 600; i++)
            {
                session.StepOnce();
                SimVec3 current = M1TestWorld.PositionOf(session);
                Assert.That((current - previous).LengthSquared <= limit, Is.True, "Tick " + session.Tick);
                if (arrived || (target - previous).LengthSquared <= limit)
                    Assert.That(current, Is.EqualTo(target), "Final step must snap to the exact raw target.");
                arrived |= current.Equals(target);
                previous = current;
            }
            Assert.That(arrived, Is.True);
            Assert.That(previous.X.Raw, Is.EqualTo(target.X.Raw));
            Assert.That(previous.Y.Raw, Is.EqualTo(target.Y.Raw));
            Assert.That(previous.Z.Raw, Is.EqualTo(target.Z.Raw));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void M1_IntegerMicros_MatchTickAccumulatorAndFormulaEveryFrame(int sequence)
        {
            SimSession session = SimSession.Create(M1TestWorld.Probe());
            var accumulator = new TickAccumulator(1000000);
            long total = 0;
            uint state = 12345;
            for (int frame = 0; frame < 800; frame++)
            {
                long micros = frame % 17 == 0 ? 0 : FrameMicros(sequence, frame, ref state);
                total += micros;
                long expected = accumulator.AddElapsed(micros);
                Assert.That(expected, Is.LessThanOrEqualTo(SimSession.MaxTicksPerFrame));
                Assert.That(session.Advance(micros), Is.EqualTo(expected));
                Assert.That(session.Tick, Is.EqualTo(total * 30 / 1000000));
                Assert.That(session.Alpha, Is.EqualTo((float)(total * 30 % 1000000) / 1000000));
                AssertViews(session);
            }
        }

        [Test]
        public void M1_Stall_DropsOnlyWholeTickQuotaAndRetainsFraction()
        {
            SimSession session = SimSession.Create(M1TestWorld.Recipe());
            Assert.That(session.Advance(250000), Is.EqualTo(5));
            Assert.That(session.Tick, Is.EqualTo(5));
            Assert.That(session.Alpha, Is.EqualTo(0.5f));
            Assert.That(session.CommandLog.Select(c => c.AtTick), Is.EqualTo(new long[] { 1 }));
            Assert.That(session.Advance(0), Is.Zero);
            Assert.That(session.Advance(16666), Is.Zero);
            Assert.That(session.Advance(1), Is.EqualTo(1));
            Assert.That(session.Tick, Is.EqualTo(6));
            Assert.That(session.CommandLog.Last().AtTick, Is.EqualTo(6));
            session.StepOnce();
            Assert.That(session.CommandLog.Last().AtTick, Is.EqualTo(7));
            AssertViews(session);
        }

        [Test]
        public void M1_Snapshots_RefreshPerTickAndRemainAdjacentAfterMultipleSteps()
        {
            SimSession session = SimSession.Create(M1TestWorld.Recipe());
            FrameView initial = session.Current;
            Assert.That(session.Previous, Is.SameAs(initial));
            Assert.That(session.Advance(16666), Is.Zero);
            Assert.That(session.Current, Is.SameAs(initial));
            Assert.That(session.Previous, Is.SameAs(initial));
            session.StepOnce();
            Assert.That(session.Previous, Is.SameAs(initial));
            Assert.That(session.Current, Is.Not.SameAs(initial));
            Assert.That(session.Advance(100000), Is.EqualTo(3));
            Assert.That(session.Previous.Tick, Is.EqualTo(3));
            Assert.That(session.Current.Tick, Is.EqualTo(4));
            FrameView previous = session.Previous;
            FrameView current = session.Current;
            Assert.That(session.Advance(0), Is.Zero);
            Assert.That(session.Previous, Is.SameAs(previous));
            Assert.That(session.Current, Is.SameAs(current));
            AssertViews(session);
        }

        [Test]
        public void M1_MultipleUnits_PreserveKeysArchetypesSpeedsAndSquadSelection()
        {
            SimSession session = SimSession.Create(new WorldRecipe(1, MoverKind.Interim,
                new[] { new UnitSpawn(90, 3, 2, -2000, 7000, 6000), new UnitSpawn(4, 7, 1, 0, 0, 3000) },
                new[] { BridgeCommand.MoveTo(1, 2, 10000, 7000) }));
            session.StepOnce();
            Assert.That(session.Current.Entities.ToArray().Select(e => e.Key), Is.EqualTo(new ulong[] { 4, 90 }));
            Assert.That(session.Current.TryGet(4, out ViewEntity idle), Is.True);
            Assert.That(idle.Archetype, Is.EqualTo(7));
            Assert.That(idle.X, Is.Zero);
            Assert.That(session.Current.TryGet(90, out ViewEntity moving), Is.True);
            Assert.That(moving.Archetype, Is.EqualTo(3));
            Assert.That(M1TestWorld.PositionOf(session, 90).X.Raw,
                Is.EqualTo(SimFix.FromInt(-2).Raw + SimFix.Ratio(6000, 30000).Raw));
            Assert.That(moving.X, Is.EqualTo((float)M1TestWorld.PositionOf(session, 90).X.Raw / SimFix.Scale));
            Assert.That(session.Current.TryGet(999, out _), Is.False);
        }

        [Test]
        public void M1_Recipe_CopiesInputArraysAndNormalizesUnitOrder()
        {
            var units = M1TestWorld.Recipe().Units.ToArray();
            var script = M1TestWorld.Recipe().Script.ToArray();
            var recipe = new WorldRecipe(41, MoverKind.Interim, units, script);
            units[0] = new UnitSpawn(999, 0, 9, 0, 0, 1);
            script[0] = BridgeCommand.MoveTo(2, 9, 0, 0);
            Array.Reverse(units = recipe.Units.ToArray());
            SimSession first = SimSession.Create(recipe);
            SimSession second = SimSession.Create(new WorldRecipe(41, MoverKind.Interim, units, recipe.Script.ToArray()));
            for (int tick = 0; tick < 10; tick++)
            {
                first.StepOnce();
                second.StepOnce();
                Assert.That(first.StateHash, Is.EqualTo(second.StateHash));
            }
            Assert.That(first.Current.TryGet(999, out _), Is.False);
            Assert.That(first.CommandLog[0].AtTick, Is.EqualTo(1));
        }

        [Test]
        public void M1_InvalidInputsFailWithoutAdvancingOrConsumingCommands()
        {
            SimSession session = SimSession.Create(M1TestWorld.Recipe());
            ulong hash = session.StateHash;
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Advance(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Enqueue(default));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Enqueue(BridgeCommand.MoveTo(2, 1, long.MaxValue, 0)));
            Assert.That(session.StateHash, Is.EqualTo(hash));
            Assert.That(session.Tick, Is.Zero);
            Assert.That(session.CommandLog, Is.Empty);
            Assert.That(session.Advance(long.MaxValue), Is.EqualTo(5));
            AssertViews(session);
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Enqueue(BridgeCommand.MoveTo(5, 1, 0, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => SimSession.Create(new WorldRecipe(1, (MoverKind)99,
                Array.Empty<UnitSpawn>(), Array.Empty<BridgeCommand>())));
            Assert.Throws<ArgumentException>(() => new WorldRecipe(1, MoverKind.Interim,
                new[] { new UnitSpawn(1, 0, 1, 0, 0, 1), new UnitSpawn(1, 0, 1, 0, 0, 1) }, Array.Empty<BridgeCommand>()));
            Assert.Throws<ArgumentOutOfRangeException>(() => SimSession.Create(new WorldRecipe(1, MoverKind.Interim,
                new[] { default(UnitSpawn) }, Array.Empty<BridgeCommand>())));
        }

        private static long FrameMicros(int sequence, int frame, ref uint state)
        {
            if (sequence == 0)
                return 16600;
            if (sequence == 1)
                return 33300;
            if (sequence == 3)
                return frame % 11 == 0 ? 250000 : 16600;
            state = unchecked(state * 1664525u + 1013904223u);
            return 1000 + state % 65000;
        }

        private static void AssertViews(SimSession session)
        {
            Assert.That(session.Alpha, Is.GreaterThanOrEqualTo(0).And.LessThan(1));
            Assert.That(session.Current.Tick, Is.EqualTo(session.Tick));
            Assert.That(session.Previous.Tick, Is.EqualTo(Math.Max(0, session.Tick - 1)));
            FrameView view = FrameView.Interpolate(session.Previous, session.Current, session.Alpha);
            foreach (ViewEntity entity in view.Entities)
            {
                Assert.That(session.Previous.TryGet(entity.Key, out ViewEntity previous), Is.True);
                Assert.That(session.Current.TryGet(entity.Key, out ViewEntity current), Is.True);
                Assert.That(entity.X, Is.InRange(Math.Min(previous.X, current.X), Math.Max(previous.X, current.X)));
                Assert.That(entity.Y, Is.InRange(Math.Min(previous.Y, current.Y), Math.Max(previous.Y, current.Y)));
                Assert.That(entity.Z, Is.InRange(Math.Min(previous.Z, current.Z), Math.Max(previous.Z, current.Z)));
            }
        }
    }
}
