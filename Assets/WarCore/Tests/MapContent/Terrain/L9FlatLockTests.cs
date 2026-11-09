using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    // L9 acceptance 6: recorded on the code before L9 and unchanged after it. Two height-field worlds in which terrain can never
    // block anything: (a) an all-zero field with a wall beside the line of fire, (b) everyone on a 3 m plateau. The fold of the per-tick state
    // hashes and the fold of every fire record (tick, shooter, target, origin, end, distance, outcome) are pinned, and each
    // world is checked to really take the branches the lock is meant to cover (hits, misses, spread, the wall).
    public sealed class L9FlatLockTests
    {
        private const int Ticks = 600;
        private const int Spread = 6000;

        // Recorded values (see the PR for the base commit they were recorded on).
        private const ulong DuelHash = 0x7D934E199E729DDDUL, DuelFires = 0xD90E8BF18C2EDC45UL;
        private const ulong PlateauHash = 0x182E7897702788E8UL, PlateauFires = 0xD89A2331FBA86BF1UL;

        [Test]
        public void L9FlatLock_ZeroFieldDuelIsUnchanged()
        {
            var w = L9.World(L9.DuelMap(), L9.DuelUnits(), Spread);
            var log = new List<(int, FireRecord)>();
            ulong hash = L9.Run(w, Ticks, log);
            TestContext.Progress.WriteLine($"L9FlatLock duel hash=0x{hash:X16} fires=0x{L9.FoldFires(log):X16} n={log.Count}");
            int hit = 0, open = 0, wall = 0, off = 0;
            foreach (var (_, f) in log)
            {
                if (f.Outcome == FireOutcome.Hit) hit++;
                if (f.Outcome == FireOutcome.Open) open++;
                if (f.Outcome == FireOutcome.WallBlocked)
                {
                    wall++;
                    // The wall is the box x 12..13 m, z 6..7 m; a blocked shot ends on its boundary (within 32 raw: origin + unit direction * distance rounds by about 1 raw per metre).
                    Assert.That(OutsideBy(f.End.X.Raw, 12 * L9.S, 13 * L9.S), Is.LessThanOrEqualTo(32), "x of a wall-blocked end");
                    Assert.That(OutsideBy(f.End.Z.Raw, 6 * L9.S, 7 * L9.S), Is.LessThanOrEqualTo(32), "z of a wall-blocked end");
                    Assert.That(ToFace(f.End.X.Raw, f.End.Z.Raw), Is.LessThanOrEqualTo(32), "a wall-blocked end lies on a face, not inside");
                }
                // Every pair of opponents stands on one line of constant z, so a shot that leaves that line was turned by spread.
                if (f.End.Z.Raw != f.Origin.Z.Raw) off++;
            }
            Assert.That(log.Count, Is.GreaterThanOrEqualTo(10));
            Assert.That(hit, Is.GreaterThanOrEqualTo(1));
            Assert.That(open, Is.GreaterThanOrEqualTo(1));
            Assert.That(wall, Is.GreaterThanOrEqualTo(1));
            Assert.That(off, Is.GreaterThanOrEqualTo(1));
            Assert.That(hash, Is.EqualTo(DuelHash));
            Assert.That(L9.FoldFires(log), Is.EqualTo(DuelFires));
        }

        private static long OutsideBy(long v, long lo, long hi) => v < lo ? lo - v : v > hi ? v - hi : 0;
        // Distance to the nearest of the four faces x = 12, x = 13, z = 6, z = 7 (m).
        private static long ToFace(long x, long z) =>
            Min(Min(Abs(x - 12 * L9.S), Abs(x - 13 * L9.S)), Min(Abs(z - 6 * L9.S), Abs(z - 7 * L9.S)));
        private static long Abs(long v) => v < 0 ? -v : v;
        private static long Min(long a, long b) => a < b ? a : b;

        [Test]
        public void L9FlatLock_PlateauIsUnchanged()
        {
            var w = L9.World(L9.PlateauMap(), L9.PlateauUnits(), Spread);
            var log = new List<(int, FireRecord)>();
            ulong hash = L9.Run(w, Ticks, log);
            TestContext.Progress.WriteLine($"L9FlatLock plateau hash=0x{hash:X16} fires=0x{L9.FoldFires(log):X16} n={log.Count}");
            for (ulong key = 1; key <= 4; key++) Assert.That(w.Read<Position>(key).Value.Y.Raw, Is.EqualTo(3 * L9.S), "unit " + key);
            int hit = 0, off = 0;
            foreach (var (_, f) in log)
            {
                if (f.Outcome == FireOutcome.Hit) hit++;
                if (f.End.Z.Raw != f.Origin.Z.Raw) off++;
                Assert.That(f.Origin.Y.Raw, Is.EqualTo(3 * L9.S));
            }
            Assert.That(log.Count, Is.GreaterThanOrEqualTo(10));
            Assert.That(hit, Is.GreaterThanOrEqualTo(1));
            Assert.That(off, Is.GreaterThanOrEqualTo(1));
            Assert.That(hash, Is.EqualTo(PlateauHash));
            Assert.That(L9.FoldFires(log), Is.EqualTo(PlateauFires));
        }
    }
}
