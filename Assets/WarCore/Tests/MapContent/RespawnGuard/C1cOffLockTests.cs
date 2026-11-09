using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // C1c (7): recorded on the code before the respawn guard existed. With no guard (the default), a respawn world keeps its
    // per-tick hashes, fire records, pack digest and snapshot bytes. Old constructors only; never edit after the first commit.
    public sealed class C1cOffLockTests
    {
        private const ulong ShooterHash = 0x67C4C7728B3CA425UL, ShooterFires = 0xA7C56B6B2790C5ACUL, ShooterDigest = 17219779227557085662UL;
        private const string ShooterSnapshot = "0EE655C02A9A588C4D5AA2FDD156645350C43EF5088CEFF443AAAC7EC8CB3315";
        // Nobody in the C1 world carries a gun, so its fire fold stays at the FNV offset basis.
        private const ulong RespawnHash = 0x3D95D41078471324UL, RespawnFires = 0xCBF29CE484222325UL, RespawnDigest = 17219779227557085662UL;
        private const string RespawnSnapshot = "61BA9BD0C225414EBE351ABE3521C3A87E32586D246B4C7E15311477B7B2B929";

        private static MatchPack ShooterPack() => new MatchPack(C1c.PointA(), C1c.BaseA(), 2, 1, false, 8000, InterruptMode.Reset, 1,
            1000000, 1000000, 100000, SimFix.FromInt(35), 2, 2, SimFix.FromInt(2), C1c.D);

        private static (ulong hash, ulong fires, string snapshot) Run(M6World w)
        {
            ulong hash = 14695981039346656037UL, fires = 14695981039346656037UL; string snapshot = null;
            for (int tick = 1; tick <= 40; tick++)
            {
                w.Step();
                hash = C1c.Fnv(hash, w.Hash);
                fires = C1c.FoldFires(fires, w);
                if (tick == 20) snapshot = C1c.Sha256(w.Sim.SaveRetainedSnapshot());
            }
            return (hash, fires, snapshot);
        }

        [Test]
        public void C1cOffLock_ShooterWorldIsUnchanged()
        {
            MatchPack pack = ShooterPack();
            var w = C1c.World(C1c.ShooterUnits(), pack);
            var (hash, fires, snapshot) = Run(w);
            Assert.That(w.Dead(C1c.Reserve), Is.True, "the new unit was shot (no guard)");
            Report("shooter", hash, fires, pack.PolicyDigest, snapshot);
            Assert.That(pack.PolicyDigest, Is.EqualTo(ShooterDigest));
            Assert.That(hash, Is.EqualTo(ShooterHash));
            Assert.That(fires, Is.EqualTo(ShooterFires));
            Assert.That(snapshot, Is.EqualTo(ShooterSnapshot));
        }

        [Test]
        public void C1cOffLock_C1RespawnWorldIsUnchanged()
        {
            MatchPack pack = Pack(C1c.PointA(), C1c.BaseA(), C1c.D);
            var w = RespawnWorld.World(Units(U(C1c.Reserve, 0, 0, reserve: true), U(C1c.Reserve2, 0, 0, reserve: true)), pack, kills: new long[] { 1, 8 });
            var (hash, fires, snapshot) = Run(w);
            Assert.That(Controlled(w), Is.EqualTo(C1c.Reserve2), "two switches happened");
            Report("c1", hash, fires, pack.PolicyDigest, snapshot);
            Assert.That(pack.PolicyDigest, Is.EqualTo(RespawnDigest));
            Assert.That(hash, Is.EqualTo(RespawnHash));
            Assert.That(fires, Is.EqualTo(RespawnFires));
            Assert.That(snapshot, Is.EqualTo(RespawnSnapshot));
        }

        private static void Report(string name, ulong hash, ulong fires, ulong digest, string snapshot) =>
            TestContext.Out.WriteLine($"{name}: hash 0x{hash:X16}UL fires 0x{fires:X16}UL digest {digest}UL snapshot {snapshot}");
    }
}
