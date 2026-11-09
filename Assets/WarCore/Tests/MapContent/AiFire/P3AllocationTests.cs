// Strict direct-byte proof needs the headless CoreCLR run (as the M0c2 allocation tests); Unity's counter cannot prove zero.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 12: Sense and TryAim do not allocate with a burst. A ReactionFire made here (R = 0, K = 1, B = 3, Q = 20) is
    // driven by hand on A while B is selectable, as P2's test does: warm-up, a positive control of the counter, then a
    // measured window of 1000 calls (Sense then TryAim per tick). The hand-driven unit copy keeps 10 rounds, so no reload
    // resets the count: three shots then 19 passes every 22 ticks, 138 shots and 862 passes in a window.
    public sealed class P3AllocationTests
    {
        private const int Fires = 138, Passes = 862;

        [Test]
        public void SenseAndTryAim_DoNotAllocate()
        {
            M6World w = P3.World(P2.Cast(), null);
            for (long tick = 1; tick <= 100; tick++) w.Sim.Step(P2.Drive(w, tick, P1.Walk));
            Assert.That(P1.Selected(w, P3.KeyA), Is.EqualTo(P2.Id(w, P3.KeyB).Value));
            var fire = new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1, 3, 20);
            CombatSystem combat = w.Combat;
            EntityId a = w.Unit(P3.KeyA);
            Combatant unit = w.Read<Combatant>(P3.KeyA);
            Position at = w.Read<Position>(P3.KeyA);
            WeaponDef weapon = combat.Pack.Weapons[unit.Weapon];
            Assert.That(unit.Ammo, Is.EqualTo(P3.Magazine));
            Window(fire, combat, a, unit, at, weapon, 1000, out int warmPasses, out int warmFires);
            Assert.That((warmFires, warmPasses), Is.EqualTo((Fires, Passes)), "the warm-up window");

            long c0 = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - c0;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768), "the counter works");

            AllocationMeter.Stabilize();
            long bytes = Window(fire, combat, a, unit, at, weapon, 3000, out int passes, out int fires);
            Assert.That((fires, passes), Is.EqualTo((Fires, Passes)), "the bursts and pauses ran");
            Assert.That(bytes, Is.EqualTo(0L), "bytes: Sense + TryAim");
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
    }
}
#endif
