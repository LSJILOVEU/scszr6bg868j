using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Rai01TimingTests
    {
        internal static string Ai(int burst = 3, int pause = 40) =>
            "{ \"burstShots\": " + burst + ", \"burstPauseTicks\": " + pause + ", \"burstPauseFollowsShooter\": true }";
        internal static readonly P1.Move[] SwitchScript =
        {
            new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(35, 3, A2.Cell(8, 4)),
        };

        [Test]
        public void SwitchTargetKeepsPause()
        {
            P3.Trace r = P3.Run(P2.Cast(), SwitchScript, Ai(), 150, false, new P1.DestroyProbe(60, P3.KeyB));
            Assert.That(r.A.FireTicks(50), Is.EqualTo(new long[] { 40, 45, 50 }));
            Assert.That(r.Counts[60], Is.EqualTo(0));
            Assert.That(r.A.Sel[60], Is.EqualTo(0UL));
            Assert.That(r.A.FirstSelect(r.Id(P3.KeyC)), Is.EqualTo(64L));
            Assert.That(r.A.Due[89], Is.EqualTo(55000L));
            P3.NoFire(r.A, 51, 89, "the shooter's pause survives the target gap");
            Assert.That(r.A.Fire[90], Is.True, "50 + Q is the first allowed tick");
            Assert.That(r.A.Target[90], Is.EqualTo(r.Id(P3.KeyC)));
            Assert.That(r.A.FireTicks(150), Is.EqualTo(new long[] { 40, 45, 50, 90, 95, 100, 140, 145, 150 }));
        }

        [Test]
        public void SwitchWithoutGapKeepsPause()
        {
            M6World w = P3.RunTo(Ai(), 55);
            Assert.That(P1.Selected(w, P3.KeyA), Is.EqualTo(P2.Id(w, P3.KeyB).Value));
            w.Set(P3.KeyC, new Position(A2.Cell(16, 10)));
            w.Sim.CompleteSpatialBoundary();
            Assert.That(P1.Selected(w, P3.KeyA), Is.EqualTo(P2.Id(w, P3.KeyC).Value));
            var fires = new List<long>();
            for (long tick = 56; tick <= 100; tick++)
            {
                w.Sim.Step(P2.Drive(w, tick, P1.Walk));
                if (P1.Fired(w, P3.KeyA)) fires.Add(tick);
            }
            Assert.That(fires, Is.EqualTo(new long[] { 90, 95, 100 }));
        }

        [Test]
        public void HeldShotDoesNotCount()
        {
            M6World w = P3.RunTo(Ai(1), 60, false, P2.Cast(P2.A2Behind));
            Assert.That(P1.Fired(w, P3.KeyA2), Is.False);
            Assert.That(w.Read<Combatant>(P3.KeyA2).DueMilli, Is.EqualTo(90000L));
            Assert.That(w.Read<Combatant>(P3.KeyA2).Ammo, Is.EqualTo(10));
            var reader = new SimStateReader(Rai01StateTests.Payload(w.System<BurstCadence>()));
            Assert.That(reader.Int32(), Is.EqualTo(1));
            Assert.That(reader.UInt64(), Is.EqualTo(P2.Id(w, P3.KeyA).Value));
            Assert.That(reader.Int64(), Is.EqualTo(80L));
            Assert.That(reader.Remaining, Is.EqualTo(0));
            w.Set(P3.KeyA2, new Position(P2.A2Aside));
            w.Sim.CompleteSpatialBoundary();
            for (long tick = 61; tick <= 90; tick++)
            {
                w.Sim.Step(P2.Drive(w, tick, P1.Walk));
                Assert.That(P1.Fired(w, P3.KeyA2), Is.EqualTo(tick == 90), "tick " + tick);
            }
            Assert.That(w.Read<Combatant>(P3.KeyA2).Ammo, Is.EqualTo(9));
        }

        [TestCase(3, 20)]
        [TestCase(5, 100)]
        public void ReloadKeepsExistingRhythm(int burst, int pause)
        {
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, Ai(burst, pause), 400);
            long[] expected = burst == 3 ? P3TimingTests.Burst3Pause20 : P3TimingTests.Burst5Pause100;
            Assert.That(r.A.FireTicks(burst == 3 ? 300 : 400), Is.EqualTo(expected));
            long empty = burst == 3 ? 130 : 180, ready = empty + 60;
            Assert.That(r.A.Ammo[(int)empty - 1], Is.EqualTo(1));
            Assert.That(r.A.Ammo[(int)empty], Is.EqualTo(10));
            Assert.That(r.A.Due[(int)empty], Is.EqualTo(ready * 1000));
            P3.NoFire(r.A, empty + 1, ready - 1, "reload");
            Assert.That(r.A.Fire[(int)ready], Is.True);
        }

        [Test]
        public void DeathDropsPause()
        {
            M6World w = P3.RunTo(Ai(), 59, false, null, new P2.KillProbe(60, P3.KeyA));
            Assert.That(Rai01StateTests.Payload(w.System<BurstCadence>()).Length, Is.EqualTo(20));
            w.Sim.Step(P2.Drive(w, 60, P1.Walk));
            Assert.That(w.Dead(P3.KeyA), Is.True);
            Assert.That(Rai01StateTests.Payload(w.System<BurstCadence>()), Is.EqualTo(new byte[4]));
            w.Sim.Step(P2.Drive(w, 61, P1.Walk));
            Assert.That(P1.Fired(w, P3.KeyA), Is.False);
            Assert.That(Rai01StateTests.Payload(w.System<BurstCadence>()), Is.EqualTo(new byte[4]));
        }

        [Test]
        public void ReusedSlotDoesNotInheritPause()
        {
            M6World w = P3.RunTo(Ai(), 59, false, null, new P1.DestroyProbe(60, P3.KeyA));
            EntityId old = w.Unit(P3.KeyA);
            StableId oldId = P2.Id(w, P3.KeyA);
            Position at = w.Read<Position>(P3.KeyA);
            var unit = new Combatant(1, 0, 10, 0);
            Assert.That(Rai01StateTests.Payload(w.System<BurstCadence>()).Length, Is.EqualTo(20));
            w.Sim.Step(P2.Drive(w, 60, P1.Walk));
            Assert.That(P2.Present(w, P3.KeyA), Is.False);
            EntityId fresh = w.Sim.Entities.Create(StableEntityKey.World(99, 60, 1));
            w.Sim.Entities.Set(fresh, at);
            w.Sim.Entities.Set(fresh, unit);
            w.Sim.Entities.SynchronizeStableEntities();
            Assert.That(fresh.Id, Is.EqualTo(old.Id));
            Assert.That(w.Sim.Entities.StableOf(fresh), Is.Not.EqualTo(oldId));
            Assert.That(Rai01StateTests.Payload(w.System<BurstCadence>()), Is.EqualTo(new byte[4]));
            ReactionFire fire = w.System<ReactionFire>();
            WeaponDef weapon = w.Combat.Pack.Weapons[0];
            fire.Sense(w.Combat, fresh, in unit, in at, 61);
            Assert.That(fire.TryAim(w.Combat, fresh, in unit, in at, in weapon, 61, out _), Is.EqualTo(AutoFireAction.Fire));
        }
    }
}
