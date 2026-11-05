using System;
using NUnit.Framework;
using WarCore.Control;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp18TestWorld;

namespace WarCore.Tests
{
    public sealed class Wp18StateTests
    {
        [Test]
        public void ActualLivePlayerLog_ReplaysEveryTickHash_AlsoWithReverseCreation()
        {
            var recipe = Recipe(); var source = new M6World(recipe); var live = new LivePlayerInput();
            var hashes = new ulong[240]; int shots = 0;
            for (int tick = 1; tick <= hashes.Length; tick++)
            {
                bool held = tick >= 10 && tick <= 170;
                bool edge = tick == 1 || tick == 10 || tick == 180;
                // Include miss, hit, zero direction, release, and movement in the consumed log.
                long aimX = tick >= 40 && tick < 45 ? 10000 : 20000;
                long aimZ = tick >= 50 && tick < 80 ? 20000 : 10000;
                var input = new PlayerInput(tick >= 190 && tick < 200 ? 1000 : 0, 0, aimX, aimZ, held);
                live.Apply(source.Session, input, edge);
                Assert.That(source.Session.Advance(0), Is.Zero);
                live.Apply(source.Session, input, false);
                source.Session.StepOnce();
                hashes[tick - 1] = source.Hash; shots += Shots(source);
            }
            Assert.That(shots, Is.GreaterThan(30));
            Assert.That(source.Session.PlayerLog.Count, Is.GreaterThan(5));
            var replay = new M6World(recipe); var reverse = new M6World(recipe, reverse: true);
            Assert.That(reverse.Unit(1), Is.Not.EqualTo(source.Unit(1)));
            foreach (var record in source.Session.PlayerLog) replay.Session.SetPlayerInput(record.AtTick, record.Input);
            // Out-of-order queue loading also keeps the original log contract.
            for (int i = source.Session.PlayerLog.Count - 1; i >= 0; i--)
            {
                var record = source.Session.PlayerLog[i]; reverse.Session.SetPlayerInput(record.AtTick, record.Input);
            }
            int replayShots = 0;
            for (int tick = 1; tick <= hashes.Length; tick++)
            {
                replay.Session.StepOnce(); reverse.Session.StepOnce(); replayShots += Shots(replay);
                Assert.That(replay.Hash, Is.EqualTo(hashes[tick - 1]), "replay tick=" + tick);
                Assert.That(reverse.Hash, Is.EqualTo(hashes[tick - 1]), "reverse tick=" + tick);
            }
            Assert.That(replayShots, Is.EqualTo(shots));
            Assert.That(replay.Session.PlayerLog, Is.EqualTo(source.Session.PlayerLog));
            Assert.That(reverse.Session.PlayerLog, Is.EqualTo(source.Session.PlayerLog));
        }

        [Test]
        public void Producer_EmptyRetainedState_AndSnapshotRestoresDuringReload()
        {
            var recipe = Recipe(); var w = new M6World(recipe);
            var producer = w.System<PlayerFire>();
            Assert.That(producer.StateTypeId, Is.EqualTo("control.player-fire"));
            Assert.That(producer.StateVersion, Is.EqualTo(1));
            var writer = new SimStateWriter(); producer.SaveState(writer);
            Assert.That(writer.ToArray(), Is.Empty);
            var reader = new SimStateReader(writer.ToArray()); producer.LoadState(reader); reader.End();
            w.Session.SetPlayerInput(1, Aim());
            for (int tick = 1; tick <= 100; tick++) w.Session.StepOnce();
            Assert.That(Status(w).Reloading, Is.True);
            var restored = new M6World(recipe, reverse: true);
            restored.Sim.RestoreRetainedSnapshot(w.Sim.SaveRetainedSnapshot(), reverseAllocation: true);
            restored.Session.SetPlayerInput(101, w.Session.PlayerLog[0].Input);
            Assert.That(restored.Hash, Is.EqualTo(w.Hash));
            for (int tick = 101; tick <= 200; tick++)
            {
                w.Session.StepOnce(); restored.Session.StepOnce();
                Assert.That(restored.Hash, Is.EqualTo(w.Hash), "restore tick=" + tick);
                Assert.That(Status(restored).WaitTicksLeft, Is.EqualTo(Status(w).WaitTicksLeft));
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void WarmPlayerSimStep_HitOrMiss_AllocatesZero(bool hit)
        {
            var w = new M6World(Recipe());
            var input = new InputFrame(default, SimVec3.Metres(20, 0, hit ? 10 : 20), primaryAttack: true);
            for (int tick = 0; tick < 100; tick++) w.Sim.Step(input);
            int shots = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int tick = 0; tick < 600; tick++)
            {
                w.Sim.Step(input); shots += w.Combat.Fires.Length;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
            Assert.That(shots, Is.GreaterThan(90));
            Assert.That(w.Sim.Events.EventsDroppedTotal, Is.Zero);
        }

        [Test]
        public void MpDemo_OnlyAddsSmallestDeployedTeamOnePlayer_M7IsUnchanged()
        {
            WorldRecipe m7 = DemoRecipes.M7(), mp = DemoRecipes.MpDemo();
            Assert.That(m7.PlayerKey, Is.Null);
            ulong smallest = ulong.MaxValue;
            foreach (UnitSpawn unit in m7.Units)
                if (unit.Team == 1 && !unit.Reserve && unit.Key < smallest) smallest = unit.Key;
            Assert.That(mp.PlayerKey, Is.EqualTo(smallest));
            Assert.That(mp.Seed, Is.EqualTo(m7.Seed)); Assert.That(mp.Mover, Is.EqualTo(m7.Mover));
            Assert.That(mp.Units.ToArray(), Is.EqualTo(m7.Units.ToArray()));
            Assert.That(mp.Script.ToArray(), Is.EqualTo(m7.Script.ToArray()));
            Assert.That(mp.Combat.PolicyDigest, Is.EqualTo(m7.Combat.PolicyDigest));
            Assert.That(mp.Match.PolicyDigest, Is.EqualTo(m7.Match.PolicyDigest));
            var session = WorldBuilder.Create(mp, DemoRecipes.M7MapBytes());
            Assert.That(session.ControlledKey, Is.EqualTo(smallest));
            Assert.That(session.TryUnitStatus(smallest, out UnitStatus status), Is.True);
            Assert.That(status.Alive && status.HasWeapon, Is.True);
        }
    }
}
