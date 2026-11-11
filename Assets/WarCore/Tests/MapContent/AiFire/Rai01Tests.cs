using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Rai01Tests
    {
        private const string Enabled = "{ \"burstShots\": 3, \"burstPauseTicks\": 40, \"burstPauseFollowsShooter\": true }";
        private static readonly P1.Move[] Switch = { new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(35, 3, A2.Cell(8, 4)) };

        private static string Enable(string ai, bool enabled = true) =>
            ai.Substring(0, ai.LastIndexOf('}')) + ", \"burstPauseFollowsShooter\": " + (enabled ? "true" : "false") + " }";

        private static byte[] Payload(BurstCadence cadence)
        {
            var writer = new SimStateWriter();
            cadence.SaveState(writer);
            return writer.ToArray();
        }

        private static (ulong Shooter, long Until)[] Entries(BurstCadence cadence, int pause = 40)
        {
            var reader = new SimStateReader(Payload(cadence));
            Assert.That(reader.Int32(), Is.EqualTo(pause));
            int count = reader.Int32();
            var entries = new (ulong, long)[count];
            for (int i = 0; i < count; i++) entries[i] = (reader.UInt64(), reader.Int64());
            Assert.That(reader.Remaining, Is.EqualTo(0));
            return entries;
        }

        [Test]
        public void ContentDefaultsAndRegistration()
        {
            Assert.That(A3.Load(P1.PackJson(null)).Section<AiRules>().BurstPauseFollowsShooter, Is.False);
            Assert.That(A3.Load(P1.PackJson("{ }")).Section<AiRules>().BurstPauseFollowsShooter, Is.False);
            Assert.That(A3.Load(P1.PackJson(Enabled)).Section<AiRules>().BurstPauseFollowsShooter, Is.True);
            M6World on = P3.World(P2.Cast(), Enabled);
            Assert.That(P3.Systems(on), Is.EqualTo(P2BaselineTests.Systems.Replace("battle.ai-fire|0|14;",
                "battle.ai-fire|0|14;battle.ai-burst-cadence|0|15;")));
            Assert.That(on.System<BurstCadence>().StateVersion, Is.EqualTo(1));
            M6World zero = P3.World(P2.Cast(), "{ \"burstPauseFollowsShooter\": true }");
            Assert.That(P3.Systems(zero), Is.EqualTo(P1BaselineTests.Systems));
            Assert.That(zero.System<BurstCadence>(), Is.Null);
        }

        [TestCase("1")]
        [TestCase("\"true\"")]
        public void ContentRejectsNonBoolean(string value)
        {
            var error = Assert.Throws<ContentException>(() => A3.Load(P1.PackJson("{ \"burstPauseFollowsShooter\": " + value + " }")));
            Assert.That(error.Message, Is.EqualTo("content pack 'a2-test' layer 'a2-test' ai field 'burstPauseFollowsShooter': must be true or false"));
        }

        [TestCase(0, 1, 0)]
        [TestCase(15, 10, 3)]
        [TestCase(0, 1, 3)]
        public void DisabledKeepsHashesSnapshotsAndSystems(int reaction, int sense, int burst)
        {
            string ai = P3.Ai(reaction, sense, burst, 40);
            M6World absent = P3.World(P2.Cast(), ai, false, new P1.DestroyProbe(60, P3.KeyB));
            M6World off = P3.World(P2.Cast(), Enable(ai, false), false, new P1.DestroyProbe(60, P3.KeyB));
            Assert.That(P3.Systems(off), Is.EqualTo(P3.Systems(absent)));
            Assert.That(off.System<BurstCadence>(), Is.Null);
            var fires = new List<long>();
            for (long tick = 0; tick <= 200; tick++)
            {
                if (tick > 0)
                {
                    absent.Sim.Step(P2.Drive(absent, tick, Switch));
                    off.Sim.Step(P2.Drive(off, tick, Switch));
                    Assert.That(P1.Fired(off, P3.KeyA), Is.EqualTo(P1.Fired(absent, P3.KeyA)), "shot, tick " + tick);
                    if (tick <= 120 && P1.Fired(off, P3.KeyA)) fires.Add(tick);
                }
                Assert.That(off.Hash, Is.EqualTo(absent.Hash), "hash, tick " + tick);
                Assert.That(off.Sim.SaveRetainedSnapshot(), Is.EqualTo(absent.Sim.SaveRetainedSnapshot()), "snapshot, tick " + tick);
            }
            if (reaction == 0 && burst == 3)
                Assert.That(fires, Is.EqualTo(new long[] { 40, 45, 50, 64, 69, 74, 114, 119 }));
        }

        [Test]
        public void SwitchTargetKeepsPause()
        {
            P3.Trace r = P3.Run(P2.Cast(), Switch, Enabled, 150, false, new P1.DestroyProbe(60, P3.KeyB));
            Assert.That(r.A.FireTicks(50), Is.EqualTo(new long[] { 40, 45, 50 }));
            Assert.That(r.A.FirstSelect(r.Id(P3.KeyC)), Is.EqualTo(64L));
            Assert.That(r.A.Sel[60], Is.EqualTo(0UL));
            Assert.That(r.A.Due[89], Is.EqualTo(55000L));
            P3.NoFire(r.A, 51, 89, "the pause follows the shooter");
            Assert.That(r.A.Fire[90], Is.True);
            Assert.That(r.A.Target[90], Is.EqualTo(r.Id(P3.KeyC)));
            Assert.That(r.A.FireTicks(150), Is.EqualTo(new long[] { 40, 45, 50, 90, 95, 100, 140, 145, 150 }));
        }

        [Test]
        public void SwitchDuringPartialBurstKeepsExistingReset()
        {
            P3.Trace r = P3.Run(P2.Cast(), Switch, Enabled, 120, false, new P1.DestroyProbe(46, P3.KeyB));
            Assert.That(r.A.FirstSelect(r.Id(P3.KeyC)), Is.EqualTo(64L));
            Assert.That(r.A.FireTicks(120), Is.EqualTo(new long[] { 40, 45, 64, 69, 74, 114, 119 }));
        }

        [Test]
        public void LosingAndRecoveringSameTargetKeepsPause()
        {
            M6World w = P3.RunTo(Enabled, 50);
            Position visible = w.Read<Position>(P3.KeyB);
            w.Set(P3.KeyB, new Position(A2.Cell(0, 10)));
            w.Sim.CompleteSpatialBoundary();
            w.Sim.Step(P1.Command(SquadCommandKind.Hold, 2));
            Assert.That(P1.Selected(w, P3.KeyA), Is.EqualTo(0UL));
            for (long tick = 52; tick <= 59; tick++) w.Sim.Step(default);
            w.Set(P3.KeyB, visible);
            w.Sim.CompleteSpatialBoundary();
            var fires = new List<long>();
            for (long tick = 60; tick <= 95; tick++)
            {
                w.Sim.Step(default);
                if (P1.Fired(w, P3.KeyA)) fires.Add(tick);
            }
            Assert.That(fires, Is.EqualTo(new long[] { 90, 95 }));
        }

        [Test]
        public void HeldShotDoesNotCount()
        {
            P3.Trace r = P3.Run(P2.Cast(P2.A2Behind), P1.Walk, Enabled, 61);
            Assert.That(r.A2.FirstSelect(r.Id(P3.KeyB)), Is.EqualTo(60L));
            Assert.That(r.A2.FireTicks(61), Is.Empty);
            Assert.That(r.A2.Due[60], Is.EqualTo(90000L));
            Assert.That(P3.Parse(P3.Payload(r.W.System<ReactionFire>())).Bursts, Is.EqualTo(new[] { (3, 50L), (0, 0L) }));
            Assert.That(Entries(r.W.System<BurstCadence>()), Is.EqualTo(new[]
            {
                (r.Id(P3.KeyA), 90L), (r.Id(P3.KeyA2), 0L),
            }));
        }

        [TestCase(3, 20)]
        [TestCase(5, 100)]
        public void EmptyMagazineKeepsExistingReload(int burst, int pause)
        {
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, Enable(P3.Ai(0, null, burst, pause)), 400);
            Assert.That(r.A.FireTicks(400), Is.EqualTo(P3.Expect(P1.T, burst, pause, 400)));
            long empty = burst == 3 ? 130 : 180;
            Assert.That(r.A.Ammo[(int)empty - 1], Is.EqualTo(1));
            Assert.That(r.A.Due[(int)empty], Is.EqualTo((empty + P3.Reload) * 1000));
            Assert.That(r.A.Fire[(int)empty + P3.Reload], Is.True);
        }

        [Test]
        public void DeathClearsPauseBeforeSave()
        {
            M6World w = P3.RunTo(Enabled, 59, false, null, new P2.KillProbe(60, P3.KeyA));
            Assert.That(Entries(w.System<BurstCadence>()), Is.EqualTo(new[] { (P2.Id(w, P3.KeyA).Value, 90L) }));
            w.Sim.Step(P2.Drive(w, 60, P1.Walk));
            Assert.That(w.Dead(P3.KeyA), Is.True);
            Assert.That(Entries(w.System<BurstCadence>()), Is.Empty);
            w.Sim.Step(P2.Drive(w, 61, P1.Walk));
            Assert.That(Entries(w.System<BurstCadence>()), Is.Empty);
        }

        [Test]
        public void ReusedSlotDoesNotInheritPause()
        {
            M6World w = P3.World(P2.Cast(bAt: A2.Cell(8, 10)), Enabled);
            EntityId old = w.Unit(P3.KeyA);
            StableId oldId = P2.Id(w, P3.KeyA);
            Position at = w.Read<Position>(P3.KeyA);
            Collider collider = w.Read<Collider>(P3.KeyA);
            Combatant unit = w.Read<Combatant>(P3.KeyA);
            ReactionFire fire = w.System<ReactionFire>();
            WeaponDef weapon = w.Combat.Pack.Weapons[0];
            for (long tick = 40; tick <= 50; tick++)
            {
                fire.Sense(w.Combat, old, in unit, in at, tick);
                if (tick % 5 == 0)
                    Assert.That(fire.TryAim(w.Combat, old, in unit, in at, in weapon, tick, out _), Is.EqualTo(AutoFireAction.Fire));
            }
            Assert.That(Entries(w.System<BurstCadence>()), Is.EqualTo(new[] { (oldId.Value, 90L) }));
            w.Sim.Entities.Destroy(old);
            w.Sim.CompleteSpatialBoundary();
            EntityId fresh = (EntityId)w.Sim.Entities.Create(StableEntityKey.World(99, 0, 1));
            w.Sim.Entities.Set(fresh, at);
            w.Sim.Entities.Set(fresh, collider);
            w.Sim.Entities.Set(fresh, unit);
            w.Sim.BindGameplay(fresh, 1);
            w.Sim.Entities.SynchronizeStableEntities();
            w.Sim.CompleteSpatialBoundary();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(0L));
            Assert.That(fresh.Id, Is.EqualTo(old.Id));
            Assert.That(w.Sim.Entities.StableOf(fresh), Is.Not.EqualTo(oldId));
            fire.Sense(w.Combat, fresh, in unit, in at, 61);
            Assert.That(fire.TryAim(w.Combat, fresh, in unit, in at, in weapon, 61, out _), Is.EqualTo(AutoFireAction.Fire));
            Assert.That(Entries(w.System<BurstCadence>()), Is.EqualTo(new[] { (w.Sim.Entities.StableOf(fresh).Value, 0L) }));
        }

        [Test]
        public void StableOrderRerunAndRestoreKeepPause()
        {
            const int Save = 62, Last = 160;
            UnitSpawn[] units = P2.Cast(P2.A2Aside);
            P3.Trace forward = P3.Run(units, Switch, Enabled, Last, false, new P1.DestroyProbe(60, P3.KeyB));
            P3.Trace repeat = P3.Run(units, Switch, Enabled, Last, false, new P1.DestroyProbe(60, P3.KeyB));
            P3.Trace reverse = P3.Run(units, Switch, Enabled, Last, true, new P1.DestroyProbe(60, P3.KeyB));
            Assert.That(reverse.W.Unit(P3.KeyA).Id, Is.Not.EqualTo(forward.W.Unit(P3.KeyA).Id));
            Assert.That(repeat.Hashes, Is.EqualTo(forward.Hashes));
            Assert.That(reverse.Hashes, Is.EqualTo(forward.Hashes));
            Assert.That(reverse.A.FireTicks(Last), Is.EqualTo(forward.A.FireTicks(Last)));
            Assert.That(reverse.W.Sim.SaveRetainedSnapshot(), Is.EqualTo(forward.W.Sim.SaveRetainedSnapshot()));
            M6World saved = P3.World(units, Enabled, false, new P1.DestroyProbe(60, P3.KeyB));
            for (long tick = 1; tick <= Save; tick++) saved.Sim.Step(P2.Drive(saved, tick, Switch));
            Assert.That(P1.Selected(saved, P3.KeyA), Is.EqualTo(0UL));
            M6World restored = P3.World(units, Enabled, true, new P1.DestroyProbe(60, P3.KeyB));
            restored.Sim.RestoreRetainedSnapshot(saved.Sim.SaveRetainedSnapshot(), reverseAllocation: true);
            Assert.That(restored.Unit(P3.KeyA).Id, Is.Not.EqualTo(saved.Unit(P3.KeyA).Id));
            Assert.That(restored.Hash, Is.EqualTo(saved.Hash));
            Assert.That(Payload(restored.System<BurstCadence>()), Is.EqualTo(Payload(saved.System<BurstCadence>())));
            var fires = new List<long>();
            for (long tick = Save + 1; tick <= Last; tick++)
            {
                restored.Sim.Step(P2.Drive(restored, tick, Switch));
                Assert.That(restored.Hash, Is.EqualTo(forward.Hashes[(int)tick]), "tick " + tick);
                if (P1.Fired(restored, P3.KeyA)) fires.Add(tick);
            }
            Assert.That(fires, Is.EqualTo(new long[] { 90, 95, 100, 140, 145, 150 }));
        }

        [Test]
        public void CadencePayloadAndHashIncludeDeadline()
        {
            M6World w = P3.RunTo(Enabled, 50);
            BurstCadence cadence = w.System<BurstCadence>();
            ulong shooter = P2.Id(w, P3.KeyA).Value;
            Assert.That(Entries(cadence), Is.EqualTo(new[] { (shooter, 90L) }));
            var expected = new StateHash();
            expected.Add("battle.ai-burst-cadence"); expected.Add(1); expected.Add(40); expected.Add(1);
            expected.Add(shooter); expected.Add(90L);
            var actual = new StateHash();
            cadence.WriteState(actual);
            Assert.That(actual.Value, Is.EqualTo(expected.Value));
            M6World off = P3.World(P2.Cast(), P3.Ai(0, null, 3, 40));
            Assert.Throws<SimSnapshotException>(() => off.Sim.RestoreRetainedSnapshot(w.Sim.SaveRetainedSnapshot()));
        }

        [Test]
        public void RestoreRejectsDifferentPauseAndClearsOldEntries()
        {
            M6World w = P3.RunTo(Enabled, 50);
            byte[] snapshot = w.Sim.SaveRetainedSnapshot();
            M6World different = P3.World(P2.Cast(), Enable(P3.Ai(0, null, 3, 41)));
            Assert.Throws<SimSnapshotException>(() => different.System<BurstCadence>().LoadState(new SimStateReader(Payload(w.System<BurstCadence>()))));
            Assert.Throws<SimSnapshotException>(() => different.Sim.RestoreRetainedSnapshot(snapshot));
            byte[] empty = P3.World(P2.Cast(), Enabled).Sim.SaveRetainedSnapshot();
            w.Sim.RestoreRetainedSnapshot(empty);
            Assert.That(Entries(w.System<BurstCadence>()), Is.Empty);
        }

        [TestCase(-1L, false, false, "AI burst deadline is negative.")]
        [TestCase(90L, true, false, "AI burst shooter does not exist.")]
        [TestCase(90L, false, true, "AI burst shooters are not in strictly increasing order.")]
        public void LoadRejectsInvalidEntries(long until, bool missing, bool duplicate, string message)
        {
            M6World w = P3.RunTo(Enabled, 50);
            ulong shooter = missing ? ulong.MaxValue : P2.Id(w, P3.KeyA).Value;
            var writer = new SimStateWriter();
            writer.Int32(40); writer.Int32(duplicate ? 2 : 1);
            writer.UInt64(shooter); writer.Int64(until);
            if (duplicate) { writer.UInt64(shooter); writer.Int64(until); }
            var error = Assert.Throws<SimSnapshotException>(() => w.System<BurstCadence>().LoadState(new SimStateReader(writer.ToArray())));
            Assert.That(error.Message, Is.EqualTo(message));
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        [Test]
        public void FullStepFirstShotAndRepeatedPausesAllocateZero()
        {
            P3.Run(P2.Cast(), Switch, Enabled, 200, false, new P1.DestroyProbe(60, P3.KeyB));
            M6World w = P3.World(P2.Cast(), Enabled, false, new P1.DestroyProbe(60, P3.KeyB));
            w.Sim.CompleteWarmup();
            // Warm the existing movement caches before the first cadence entry at tick 40.
            for (long tick = 1; tick < P1.T; tick++)
            {
                w.Sim.Step(P2.Drive(w, tick, Switch));
                Assert.That(P1.Fired(w, P3.KeyA), Is.False);
            }
            Assert.That(Entries(w.System<BurstCadence>()), Is.Empty);
            var fired = new bool[201];
            long controlBefore = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - controlBefore;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768));
            AllocationMeter.Stabilize();
            long bytes = 0;
            for (int tick = (int)P1.T; tick <= 200; tick++)
            {
                InputFrame input = P2.Drive(w, tick, Switch);
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(input);
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
                fired[tick] = P1.Fired(w, P3.KeyA);
            }
            var fires = new List<long>();
            for (int tick = 1; tick <= 200; tick++) if (fired[tick]) fires.Add(tick);
            Assert.That(fires, Is.EqualTo(new long[] { 40, 45, 50, 90, 95, 100, 140, 145, 150, 190 }));
            Assert.That(bytes, Is.EqualTo(0L));
        }
#endif
    }
}
