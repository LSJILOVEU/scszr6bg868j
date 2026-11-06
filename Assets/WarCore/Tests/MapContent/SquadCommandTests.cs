using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // C10: hold and follow reach the command stream next to move-to, and a logged stream replays exactly.
    public sealed class SquadCommandTests
    {
        // Two squads of eight (keys 1..8 and 9..16) plus one enemy.
        private static M6World World()
        {
            var units = new List<UnitSpawn>();
            for (ulong key = 1; key <= 16; key++)
                units.Add(U(key, 10000 + (long)key * 1500, 10000, team: 1, squad: key <= 8 ? 1 : 2));
            units.Add(U(17, 70000, 35000, team: 2, squad: 3));
            return new M6World(M6World.RecipeOf(units.ToArray(), Match()));
        }

        private static SquadCommand? Seen(M6World w) => w.Sim.Context.Input.Squad;

        [Test]
        public void Hold_ReachesTheSimOnItsTick_WithNoPointAndNoTarget()
        {
            var w = World(); w.Session.Enqueue(BridgeCommand.Hold(3, 2));
            w.Session.StepOnce(); w.Session.StepOnce(); Assert.That(Seen(w), Is.Null);
            w.Session.StepOnce();
            SquadCommand c = Seen(w).Value;
            Assert.That((c.Kind, c.Squad, c.TargetPoint, c.TargetEntity), Is.EqualTo((SquadCommandKind.Hold, 2, default(SimVec3), default(EntityId))));
            w.Session.StepOnce(); Assert.That(Seen(w), Is.Null, "a command lasts one tick");
        }

        [Test]
        public void Follow_CarriesTheTargetUnitAndNoPoint()
        {
            var w = World(); w.Session.Enqueue(BridgeCommand.Follow(2, 1, 12));
            w.Session.StepOnce(); w.Session.StepOnce();
            SquadCommand c = Seen(w).Value;
            Assert.That(c.Kind, Is.EqualTo(SquadCommandKind.Follow));
            Assert.That(c.Squad, Is.EqualTo(1));
            Assert.That(c.TargetEntity, Is.EqualTo(w.Unit(12)));
            Assert.That(c.TargetPoint, Is.EqualTo(default(SimVec3)));
        }

        [Test]
        public void MoveTo_StillCarriesItsPoint()
        {
            var w = World(); w.Session.Enqueue(BridgeCommand.MoveTo(1, 1, 40000, 20000));
            w.Session.StepOnce();
            SquadCommand c = Seen(w).Value;
            Assert.That(c.Kind, Is.EqualTo(SquadCommandKind.MoveTo));
            Assert.That(c.TargetPoint.X, Is.EqualTo(SimFix.FromInt(40)));
            Assert.That(c.TargetPoint.Z, Is.EqualTo(SimFix.FromInt(20)));
            Assert.That(c.TargetEntity, Is.EqualTo(default(EntityId)));
        }

        [Test]
        public void MixedStream_IsLoggedWithKinds_AndReplaysEveryTick()
        {
            var first = World();
            foreach (var c in new[] { BridgeCommand.Hold(3, 1), BridgeCommand.Follow(6, 2, 4), BridgeCommand.MoveTo(9, 1, 45000, 25000), BridgeCommand.Hold(12, 2) })
                first.Session.Enqueue(c);
            var seenFirst = new List<(SquadCommandKind, int, EntityId)?>(); var hashFirst = new List<ulong>();
            for (int i = 0; i < 15; i++)
            {
                first.Session.StepOnce(); hashFirst.Add(first.Session.StateHash);
                seenFirst.Add(Seen(first) is SquadCommand s ? (s.Kind, s.Squad, s.TargetEntity) : ((SquadCommandKind, int, EntityId)?)null);
            }
            var log = first.Session.CommandLog;
            Assert.That(log.Count, Is.EqualTo(4));
            Assert.That(new[] { log[0].Kind, log[1].Kind, log[2].Kind, log[3].Kind },
                Is.EqualTo(new[] { SquadCommandKind.Hold, SquadCommandKind.Follow, SquadCommandKind.MoveTo, SquadCommandKind.Hold }));
            Assert.That(log[1].TargetKey, Is.EqualTo(4UL));

            var replay = World();
            foreach (var c in log) replay.Session.Enqueue(c);
            for (int i = 0; i < 15; i++)
            {
                replay.Session.StepOnce();
                Assert.That(replay.Session.StateHash, Is.EqualTo(hashFirst[i]), "tick " + (i + 1));
                var seen = Seen(replay) is SquadCommand s ? (s.Kind, s.Squad, s.TargetEntity) : ((SquadCommandKind, int, EntityId)?)null;
                Assert.That(seen, Is.EqualTo(seenFirst[i]), "tick " + (i + 1));
            }
        }

        [Test]
        public void FollowOfAnUnknownUnit_IsRefusedAtEnqueue()
        {
            var w = World();
            Assert.Throws<ArgumentException>(() => w.Session.Enqueue(BridgeCommand.Follow(2, 1, 999)));
            Assert.That(w.Session.CommandLog.Count, Is.EqualTo(0));
        }

        [Test]
        public void MoveToOffTheWorld_IsStillRefused_WhileHoldNeedsNoPoint()
        {
            var w = World();
            Assert.Throws<ArgumentOutOfRangeException>(() => w.Session.Enqueue(BridgeCommand.MoveTo(2, 1, long.MaxValue, 0)));
            Assert.DoesNotThrow(() => w.Session.Enqueue(BridgeCommand.Hold(2, 1)));
        }

        [Test]
        public void TwoCommandsOnOneTick_KeepTheFirstOfAnyKind()
        {
            var w = World();
            w.Session.Enqueue(BridgeCommand.Hold(2, 1)); w.Session.Enqueue(BridgeCommand.MoveTo(2, 1, 40000, 20000)); w.Session.Enqueue(BridgeCommand.Follow(2, 1, 3));
            w.Session.StepOnce(); w.Session.StepOnce();
            Assert.That(Seen(w).Value.Kind, Is.EqualTo(SquadCommandKind.Hold));
            Assert.That(w.Session.CommandLog.Count, Is.EqualTo(1));
        }
    }
}
