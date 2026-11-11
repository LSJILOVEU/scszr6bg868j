using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Rai01TimingTests
    {
        private const string Ai = "{ \"burstShots\": 3, \"burstPauseTicks\": 40, \"burstFollowsShooter\": true }";

        [Test]
        public void SwitchTargetKeepsPause()
        {
            var script = new[] { new P1.Move(20, 2, A2.Cell(8, 10)), new P1.Move(35, 3, A2.Cell(8, 4)) };
            P3.Trace r = P3.Run(P2.Cast(), script, Ai, 100, false, new P1.DestroyProbe(60, P3.KeyB));
            Assert.That(r.A.FirstSelect(r.Id(P3.KeyC)), Is.EqualTo(64L));
            Assert.That(r.A.Sel[60], Is.EqualTo(0UL));
            Assert.That(r.A.FireTicks(100), Is.EqualTo(new long[] { 40, 45, 50, 90, 95, 100 }));
            P3.NoFire(r.A, 51, 89, "the shooter keeps the pause");
            Assert.That(r.A.Due[89], Is.EqualTo(55000L));
            Assert.That(r.A.Fire[90], Is.True, "50 + Q");
            Assert.That(r.A.Target[90], Is.EqualTo(r.Id(P3.KeyC)));
        }

        [Test]
        public void LostTargetKeepsPause()
        {
            M6World w = P3.RunTo(Ai, 50);
            Assert.That(P1.Fired(w, P3.KeyA), Is.True);
            Position target = w.Read<Position>(P3.KeyB);
            for (long tick = 51; tick <= 90; tick++)
            {
                if (tick == 60 || tick == 61)
                {
                    w.Set(P3.KeyB, tick == 60 ? new Position(A2.Cell(0, 10)) : target);
                    w.Sim.CompleteSpatialBoundary();
                }
                w.Sim.Step(P2.Drive(w, tick, P1.Walk));
                if (tick == 60) Assert.That(P1.Selected(w, P3.KeyA), Is.EqualTo(0UL));
                if (tick == 61) Assert.That(P1.Selected(w, P3.KeyA), Is.EqualTo(P2.Id(w, P3.KeyB).Value));
                Assert.That(P1.Fired(w, P3.KeyA), Is.EqualTo(tick == 90), "tick " + tick);
            }
        }

        [Test]
        public void HeldShotDoesNotCount()
        {
            P3.Trace r = P3.Run(P2.Cast(P2.A2Behind), P1.Walk, Ai, 61);
            Assert.That(r.A.FireTicks(61), Is.EqualTo(new long[] { 40, 45, 50 }));
            Assert.That(r.A2.FirstSelect(r.Id(P3.KeyB)), Is.EqualTo(60L));
            Assert.That(r.A2.FireTicks(61), Is.Empty);
            Assert.That(r.A2.Due[60], Is.EqualTo(90000L));
            Assert.That(r.A2.Ammo[61], Is.EqualTo(P3.Magazine));
            Assert.That(Entries(r.W), Is.EqualTo(new[] { (r.Id(P3.KeyA), 3, 50L), (r.Id(P3.KeyA2), 0, 0L) }));
        }

        [TestCase(3, 20, 300)]
        [TestCase(5, 100, 400)]
        public void EmptyMagazineKeepsReloadRule(int burst, int pause, int last)
        {
            string ai = "{ \"burstShots\": " + burst + ", \"burstPauseTicks\": " + pause + ", \"burstFollowsShooter\": true }";
            P3.Trace r = P3.Run(P2.Cast(), P1.Walk, ai, last);
            Assert.That(r.A.FireTicks(last), Is.EqualTo(burst == 3 ? P3TimingTests.Burst3Pause20 : P3TimingTests.Burst5Pause100));
            int empty = burst == 3 ? 130 : 180;
            Assert.That(r.A.Ammo[empty - 1], Is.EqualTo(1));
            Assert.That(r.A.Ammo[empty], Is.EqualTo(P3.Magazine));
            Assert.That(r.A.Due[empty], Is.EqualTo((empty + P3.Reload) * 1000L));
            Assert.That(r.A.Fire[empty + P3.Reload], Is.True);
        }

        [Test]
        public void DeadShooterLosesPause()
        {
            M6World w = P3.RunTo(Ai, 59, false, P2.Cast(P2.A2Aside), new P2.KillProbe(60, P3.KeyA2));
            Assert.That(Entries(w), Is.EqualTo(new[] { (P2.Id(w, P3.KeyA).Value, 3, 50L), (P2.Id(w, P3.KeyA2).Value, 3, 52L) }));
            w.Sim.Step(P2.Drive(w, 60, P1.Walk));
            Assert.That(w.Dead(P3.KeyA2), Is.True);
            w.Sim.Step(P2.Drive(w, 61, P1.Walk));
            Assert.That(P1.Fired(w, P3.KeyA2), Is.False);
            Assert.That(Entries(w), Is.EqualTo(new[] { (P2.Id(w, P3.KeyA).Value, 3, 50L) }));
        }

        [Test]
        public void ReusedSlotDoesNotInheritPause()
        {
            M6World w = P3.RunTo(Ai, 59, false, P2.Cast(P2.A2Aside), new P1.DestroyProbe(60, P3.KeyA2));
            EntityId old = w.Unit(P3.KeyA2);
            StableId oldId = P2.Id(w, P3.KeyA2);
            Position position = w.Read<Position>(P3.KeyA2);
            Collider collider = w.Read<Collider>(P3.KeyA2);
            Assert.That(Entries(w), Is.EqualTo(new[] { (P2.Id(w, P3.KeyA).Value, 3, 50L), (oldId.Value, 3, 52L) }));
            w.Sim.Step(P2.Drive(w, 60, P1.Walk));
            EntityId fresh = (EntityId)w.Sim.Entities.Create(StableEntityKey.World(99, 60, 1));
            w.Sim.Entities.SynchronizeStableEntities();
            Assert.That(fresh.Id, Is.EqualTo(old.Id));
            StableId freshId = w.Sim.Entities.StableOf(fresh);
            Assert.That(freshId, Is.Not.EqualTo(oldId));
            var unit = new Combatant(1, 0, P3.Magazine, 0);
            w.Sim.Entities.Set(w.Sim.Entities.Resolve<Position>(), fresh, position);
            w.Sim.Entities.Set(w.Sim.Entities.Resolve<Collider>(), fresh, collider);
            w.Sim.Entities.Set(w.Sim.Entities.Resolve<Combatant>(), fresh, unit);
            w.Sim.CompleteSpatialBoundary();
            // Gameplay binding is initialization-only, so the replacement uses the registered fire decision directly.
            ReactionFire fire = w.System<ReactionFire>();
            WeaponDef weapon = w.Combat.Pack.Weapons[unit.Weapon];
            fire.Sense(w.Combat, fresh, in unit, in position, 61);
            Assert.That(fire.TryAim(w.Combat, fresh, in unit, in position, in weapon, 61, out _),
                Is.EqualTo(AutoFireAction.Fire), "the replacement can fire before the old pause expires at 92");
        }

        private static (ulong Shooter, int Shots, long Last)[] Entries(M6World w)
        {
            var writer = new SimStateWriter();
            w.System<BurstCadence>().SaveState(writer);
            var reader = new SimStateReader(writer.ToArray());
            Assert.That(reader.Int32(), Is.EqualTo(3));
            Assert.That(reader.Int32(), Is.EqualTo(40));
            var entries = new (ulong, int, long)[reader.Int32()];
            for (int i = 0; i < entries.Length; i++) entries[i] = (reader.UInt64(), reader.Int32(), reader.Int64());
            Assert.That(reader.Remaining, Is.EqualTo(0));
            return entries;
        }
    }
}
