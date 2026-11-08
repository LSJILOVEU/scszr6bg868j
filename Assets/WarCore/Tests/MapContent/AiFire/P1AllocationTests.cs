// Strict direct-byte proof needs the headless CoreCLR run (as the M0c2 allocation tests); Unity's counter cannot prove zero.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 9: Sense and TryAim do not allocate. A ReactionFire made here (R = 15) is driven by hand on A while B is
    // selectable. After one identical warm-up window and a positive control of the counter, three measured windows of 1000
    // calls each: Sense then TryAim per tick (a new clock, so both the delay and the hand-over run, counted in locals and
    // checked after the window), Sense alone, and TryAim alone on a tick past the delay (the hand-over path). Heap accounting
    // is settled before each window (AllocationMeter.Stabilize, as the M0c2 allocation tests do).
    public sealed class P1AllocationTests
    {
        [Test]
        public void SenseAndTryAim_DoNotAllocate()
        {
            M6World w = P1.World(P1.Cast(), null);
            for (long tick = 1; tick <= 45; tick++) w.Sim.Step(P1.Drive(w, tick, P1.Walk));
            Assert.That(P1.Selected(w, P1.KeyA), Is.EqualTo(P1.Id(w, P1.KeyB).Value));
            var fire = new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 15);
            CombatSystem combat = w.Combat;
            EntityId a = w.Unit(P1.KeyA);
            Combatant unit = w.Read<Combatant>(P1.KeyA);
            Position at = w.Read<Position>(P1.KeyA);
            WeaponDef weapon = combat.Pack.Weapons[unit.Weapon];
            Window(fire, combat, a, unit, at, weapon, 1000, out _, out _);

            long c0 = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - c0;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768), "the counter works");

            AllocationMeter.Stabilize();
            long both = Window(fire, combat, a, unit, at, weapon, 3000, out int passes, out int fires);
            AllocationMeter.Stabilize();
            long sense = SenseOnly(fire, combat, a, unit, at, 5000);
            AllocationMeter.Stabilize();
            long aim = AimOnly(fire, combat, a, unit, at, weapon, 5999);
            Assert.That(passes, Is.EqualTo(15), "the delay ran");
            Assert.That(fires, Is.EqualTo(985), "the hand-over ran");
            Assert.That(fire.TryAim(combat, a, in unit, in at, in weapon, 5999, out _), Is.EqualTo(AutoFireAction.Fire), "TryAim alone hands over");
            Assert.That(new[] { both, sense, aim }, Is.EqualTo(new long[] { 0, 0, 0 }), "bytes: Sense+TryAim, Sense, TryAim");
        }

        private static long Window(ReactionFire fire, CombatSystem combat, EntityId a, Combatant unit, Position at, WeaponDef weapon,
            long start, out int passes, out int fires)
        {
            passes = 0; fires = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (long t = start; t < start + 1000; t++)
            {
                fire.Sense(combat, a, in unit, in at, t);
                if (fire.TryAim(combat, a, in unit, in at, in weapon, t, out _) == AutoFireAction.Pass) passes++; else fires++;
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static long SenseOnly(ReactionFire fire, CombatSystem combat, EntityId a, Combatant unit, Position at, long start)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (long t = start; t < start + 1000; t++) fire.Sense(combat, a, in unit, in at, t);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static long AimOnly(ReactionFire fire, CombatSystem combat, EntityId a, Combatant unit, Position at, WeaponDef weapon, long t)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) fire.TryAim(combat, a, in unit, in at, in weapon, t, out _);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
#endif
