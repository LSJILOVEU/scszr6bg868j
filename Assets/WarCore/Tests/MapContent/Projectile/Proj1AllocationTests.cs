// Strict direct-byte proof needs the headless CoreCLR run (as the M0c2 allocation tests); Unity's counter cannot prove zero.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests.Projectile
{
    // PROJ-1 A15: Sim.Step does not allocate with projectiles in flight. Two units fire every tick at points out of range, so
    // about 30 projectiles each are always flying, one ends Open and one is fired per unit per tick; a third unit stands where
    // some land. 60 ticks warm up, then four measured windows of 100 ticks, each after a collection, after a positive control of
    // the counter.
    public sealed class Proj1AllocationTests
    {
        private const long M = Proj1World.M;

        [Test]
        public void SimStep_DoesNotAllocateWithProjectilesInFlight()
        {
            var gun = Proj1World.Weapon(30, 1000, magazine: 100000);
            var w = new Proj1World(Proj1World.Pack(30000, gun), new[]
            {
                Proj1World.U(0, 0, 1), Proj1World.U(0, 10 * M, 1), Proj1World.U(20 * M, 10 * M, 2, -1),
            });
            SimVec3 far = Proj1World.Raw(80 * M, 0, 0), farther = Proj1World.Raw(80 * M, 0, 10 * M);
            w.Produce = c =>
            {
                w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), far));
                w.Combat.Submit(c, FireIntent.Aimed(w.Id(1), farther));
            };
            for (int i = 0; i < 60; i++) w.Step();
            Assert.That(w.Projectiles.Count, Is.GreaterThanOrEqualTo(20), "at least 20 in flight");

            long c0 = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - c0;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768), "the counter works");

            long[] windows = new long[4];
            int landed = 0;
            for (int r = 0; r < 4; r++)
            {
                AllocationMeter.Stabilize();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) { w.Step(); landed += w.Projectiles.Impacts.Length; }
                windows[r] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Assert.That(landed, Is.GreaterThanOrEqualTo(400), "projectiles land during the windows");
            Assert.That(windows, Is.EqualTo(new long[4]), "bytes in each 100-tick window with projectiles in flight");
        }
    }
}
#endif
