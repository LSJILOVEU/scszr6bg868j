using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    public sealed class Wp16BridgeInputTests
    {
        [Test]
        public void ScheduledInput_HoldsUntilRelease_RecordsOnlyEffectiveChanges()
        {
            var w = new M6World(Recipe());
            var s = w.Session;
            var held = new PlayerInput(500, -250);
            // Intentionally insert out of order, including duplicates of the held value.
            s.SetPlayerInput(8, default);
            s.SetPlayerInput(5, held);
            s.SetPlayerInput(3, held);
            s.SetPlayerInput(1, default);
            var log = s.PlayerLog;
            Assert.That(log, Is.Empty, "scheduling is not consumption");
            SimVec3 start = Point(w);
            SimFix speed = w.Read<MoveIntent>(1).SpeedPerTick;
            var delta = new SimVec3(speed * SimFix.Ratio(1, 2), default, speed * SimFix.Ratio(-1, 4));
            SimVec3 expected = start;
            for (int tick = 1; tick <= 20; tick++)
            {
                s.StepOnce();
                if (tick >= 3 && tick < 8) expected += delta;
                Assert.That(s.Tick, Is.EqualTo(tick));
                Assert.That(Point(w), Is.EqualTo(expected), "tick=" + tick);
                Assert.That(log.Count, Is.EqualTo(tick < 3 ? 0 : tick < 8 ? 1 : 2));
                if (tick == 3)
                {
                    Assert.That(Point(w).X.Raw, Is.GreaterThan(start.X.Raw));
                    Assert.That(Point(w).Z.Raw, Is.LessThan(start.Z.Raw));
                }
            }
            Assert.That(log[0].AtTick, Is.EqualTo(3)); Assert.That(log[0].Input, Is.EqualTo(held));
            Assert.That(log[1].AtTick, Is.EqualTo(8)); Assert.That(log[1].Input, Is.EqualTo(default(PlayerInput)));
            Assert.That(s.CommandLog, Is.Empty);
            Assert.Throws<NotSupportedException>(() => ((IList<PlayerInputRecord>)log).Clear());
        }

        [Test]
        public void SameTick_LastPlayerInputWins_AlongsideUnchangedSquadQueue()
        {
            var w = new M6World(Recipe(new[] { U(1), U(2, 14000), U(3, 70000, 35000, team: 2, squad: 2) }));
            var s = w.Session;
            var command = BridgeCommand.MoveTo(2, 1, 40000, 10000);
            s.SetPlayerInput(2, new PlayerInput(-1000, 0));
            s.Enqueue(command);
            s.Enqueue(BridgeCommand.MoveTo(2, 1, 40000, 20000)); // Squad queue still keeps the first.
            var last = new PlayerInput(0, 1000);
            s.SetPlayerInput(2, last);
            SimVec3 start = Point(w), ally = Point(w, 2);
            s.StepOnce(); Assert.That(Point(w), Is.EqualTo(start));
            s.StepOnce();
            Assert.That(Point(w), Is.EqualTo(start + new SimVec3(default, default, w.Read<MoveIntent>(1).SpeedPerTick)));
            Assert.That(Point(w, 2).X.Raw, Is.GreaterThan(ally.X.Raw), "MoveTo must also be consumed");
            Assert.That(w.Read<MoveIntent>(2).Target, Is.EqualTo(SimVec3.Metres(40, 0, 10)));
            Idle(w, 1);
            Assert.That(s.PlayerLog.Count, Is.EqualTo(1)); Assert.That(s.PlayerLog[0].Input, Is.EqualTo(last));
            Assert.That(s.PlayerLog[0].AtTick, Is.EqualTo(2));
            Assert.That(s.CommandLog, Is.EqualTo(new[] { command }));
        }

        [TestCase(-1001, 0)]
        [TestCase(1001, 0)]
        [TestCase(0, -1001)]
        [TestCase(0, 1001)]
        [TestCase(int.MinValue, 0)]
        [TestCase(0, int.MaxValue)]
        public void MovementComponentOutsideRange_Throws(int x, int z) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInput(x, z));

        [Test]
        public void AimBounds_MatchWorldLimit_IncludingExtremeIntegers()
        {
            long limit = SimVec3.WorldLimitMetres * 1000L;
            foreach (long value in new[] { -limit, limit })
            {
                Assert.DoesNotThrow(() => new PlayerInput(-1000, 1000, value, -value));
                var w = new M6World(Recipe());
                w.Session.SetPlayerInput(1, new PlayerInput(0, 0, value, -value));
                w.Session.StepOnce();
                Assert.That(w.Sim.Context.Input.Aim,
                    Is.EqualTo(new SimVec3(SimFix.Ratio(value, 1000), default, SimFix.Ratio(-value, 1000))));
            }
            foreach (long value in new[] { -limit - 1, limit + 1, long.MinValue, long.MaxValue })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInput(0, 0, value, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerInput(0, 0, 0, value));
            }
        }

        [Test]
        public void PastOrCurrentTick_Throws_WithoutReplacingFutureInput()
        {
            var w = new M6World(Recipe());
            foreach (long tick in new[] { long.MinValue, -1L, 0L })
                Assert.Throws<ArgumentOutOfRangeException>(() => w.Session.SetPlayerInput(tick, default));
            w.Session.SetPlayerInput(2, new PlayerInput(1000, 0));
            w.Session.StepOnce();
            Assert.Throws<ArgumentOutOfRangeException>(() => w.Session.SetPlayerInput(1, default));
            SimVec3 start = Point(w);
            w.Session.StepOnce();
            Assert.That(Point(w).X.Raw, Is.GreaterThan(start.X.Raw));
            Assert.That(w.Session.PlayerLog[0].AtTick, Is.EqualTo(2));
        }

        [Test]
        public void FramePassesRawDiagonal_WorldAimAtGroundHeight_AndPrimary()
        {
            var map = RaisedMap();
            var w = new M6World(Recipe(), WcMapWriter.Write(map));
            var input = new PlayerInput(1000, -1000, -12345, 23456, true);
            w.Session.SetPlayerInput(1, input);
            SimVec3 start = Point(w);
            w.Session.StepOnce();
            InputFrame frame = w.Sim.Context.Input;
            Assert.That(frame.Move, Is.EqualTo(SimVec3.Metres(1, 0, -1)), "bridge must not normalize");
            Assert.That(frame.Aim, Is.EqualTo(new SimVec3(SimFix.Ratio(-12345, 1000), map.FlatHeight, SimFix.Ratio(23456, 1000))));
            Assert.That(frame.PrimaryAttack, Is.True);
            SimVec3 delta = Point(w) - start;
            Assert.That(delta.X.Raw, Is.GreaterThan(0)); Assert.That(delta.Z.Raw, Is.LessThan(0));
            Assert.That(delta.Y.Raw, Is.Zero);
            SimFix speed = w.Read<MoveIntent>(1).SpeedPerTick;
            Assert.That(delta.LengthSquared <= new SimVec3(speed, default, default).LengthSquared, Is.True);
            Assert.That(w.Session.PlayerLog[0].Input, Is.EqualTo(input));
        }

        [Test]
        public void EachFieldAloneCanChangeTheHeldValue()
        {
            var w = new M6World(Recipe());
            var inputs = new[] {
                new PlayerInput(1, 0), new PlayerInput(1, -1), new PlayerInput(1, -1, 123),
                new PlayerInput(1, -1, 123, -456), new PlayerInput(1, -1, 123, -456, true),
                new PlayerInput(1, -1, 123, -456, false), default(PlayerInput)
            };
            for (int i = 0; i < inputs.Length; i++)
            {
                long tick = 2L * i + 1;
                w.Session.SetPlayerInput(tick, inputs[i]);
                w.Session.SetPlayerInput(tick + 1, inputs[i]);
                w.Session.StepOnce(); w.Session.StepOnce();
                Assert.That(w.Session.PlayerLog.Count, Is.EqualTo(i + 1));
                Assert.That(w.Session.PlayerLog[i].AtTick, Is.EqualTo(tick));
                Assert.That(w.Session.PlayerLog[i].Input, Is.EqualTo(inputs[i]));
                InputFrame frame = w.Sim.Context.Input;
                Assert.That(frame.Move, Is.EqualTo(new SimVec3(SimFix.Ratio(inputs[i].MoveXMilli, 1000), default, SimFix.Ratio(inputs[i].MoveZMilli, 1000))));
                Assert.That(frame.Aim, Is.EqualTo(new SimVec3(SimFix.Ratio(inputs[i].AimXMm, 1000), default, SimFix.Ratio(inputs[i].AimZMm, 1000))));
                Assert.That(frame.PrimaryAttack, Is.EqualTo(inputs[i].Primary));
            }
        }

        [Test]
        public void NoPlayer_RejectsInput_LeavesPlayerFieldsAndLogEmpty_EvenAboveZeroGround()
        {
            var recipe = M6World.RecipeOf(new[] { U(1), U(2, 70000, 35000, team: 2, squad: 2) }, Match());
            var w = new M6World(recipe, WcMapWriter.Write(RaisedMap()));
            Assert.That(w.Session.ControlledKey, Is.Null);
            ulong hash = w.Hash;
            Assert.Throws<InvalidOperationException>(() => w.Session.SetPlayerInput(1, new PlayerInput(1000, 0)));
            Assert.That(w.Hash, Is.EqualTo(hash));
            w.Session.Enqueue(BridgeCommand.MoveTo(1, 1, 20000, 10000));
            for (int i = 0; i < 10; i++)
            {
                w.Session.StepOnce();
                Assert.That(w.Sim.Context.Input.Move, Is.EqualTo(default(SimVec3)));
                Assert.That(w.Sim.Context.Input.Aim, Is.EqualTo(default(SimVec3)));
                Assert.That(w.Sim.Context.Input.PrimaryAttack, Is.False);
                Assert.That(w.Session.PlayerLog, Is.Empty);
            }
            Assert.That(w.Session.CommandLog.Count, Is.EqualTo(1));
            Assert.That(Point(w).X.Raw, Is.GreaterThan(SimFix.FromInt(10).Raw));
        }

        private static WcMap RaisedMap()
        {
            var source = M3TestWorld.Boxes(80, 40);
            return new WcMap(source.MapId, source.ContentId, SimVec3.Metres(0, 2, 0), SimVec3.Metres(80, 6, 40),
                source.ChunkSize, SimFix.FromInt(2), source.Width, source.Depth,
                new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), new List<byte>(source.Passability).ToArray());
        }
    }
}
