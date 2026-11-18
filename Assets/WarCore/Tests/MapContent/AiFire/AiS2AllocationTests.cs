// Strict direct-byte proof needs the headless CoreCLR run (as the M0c2 allocation tests); Unity's counter cannot prove zero.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-S2: Sense and TryAim do not allocate with a trait spread. P3's allocation drive (P3AllocationTests.cs) with
    // R 0, K 1, B 3, Q 20 and s 300: A (key 1) has its own burst 2 and pause 22, so two shots then 21 passes every 23
    // ticks: 43 cycles and 11 more calls in a window of 1000, 88 shots and 912 passes.
    public sealed class AiS2AllocationTests
    {
        private const int Fires = 88, Passes = 912;

        [Test]
        public void SenseAndTryAim_DoNotAllocate()
        {
            M6World w = P3.World(P2.Cast(), null);
            for (long tick = 1; tick <= 100; tick++) w.Sim.Step(P2.Drive(w, tick, P1.Walk));
            Assert.That(P1.Selected(w, P3.KeyA), Is.EqualTo(P2.Id(w, P3.KeyB).Value));
            var fire = new ReactionFire(w.Combat, w.Combat.BuiltInAutoFire, 0, 1, 3, 20, null, 300);
            CombatSystem combat = w.Combat;
            EntityId a = w.Unit(P3.KeyA);
            Assert.That((fire.BurstOf(P2.Id(w, P3.KeyA)), fire.PauseOf(P2.Id(w, P3.KeyA))), Is.EqualTo((2, 22)));
            Combatant unit = w.Read<Combatant>(P3.KeyA);
            Position at = w.Read<Position>(P3.KeyA);
            WeaponDef weapon = combat.Pack.Weapons[unit.Weapon];
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
