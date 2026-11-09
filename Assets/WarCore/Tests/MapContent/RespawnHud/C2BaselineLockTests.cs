using NUnit.Framework;
using WarCore.Presentation;
using static WarCore.Tests.RespawnWorld;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // C2 recorded on the base code before any C2 change: the MP block demo has no respawn, and the full HUD text
    // of a respawn world while the player lies dead. C2 adds lines beside Build, never inside it.
    public sealed class C2BaselineLockTests
    {
        private const ulong BlockDemoMatchDigest = 0x582B535D9D24F6EBUL;
        private const string DeadHud = "Tick 3\nStateHash F1803B11B79B1590\nFollow #1  HP 0/100  Ammo 0\nPoints  P1 T1  P90 T2  P91 -\nGround (1.0, 2.0)";

        [Test]
        public void C2MpBlockDemo_HasNoRespawn_AndKeepsItsMatchDigest()
        {
            var match = DemoRecipes.MpBlockDemo().Match;
            Assert.That(match.RespawnTicks, Is.Zero);
            Assert.That(match.PolicyDigest, Is.EqualTo(BlockDemoMatchDigest));
        }

        [Test]
        public void C2BuildText_WhileThePlayerIsDead_IsUnchanged()
        {
            var w = World(Units(U(3, 0, 0, reserve: true)), Pack(Points(Spawn(1, 30, 20)), Bases(new WarCore.Battle.SpawnAnchor(1, P(5, 38))), 45));
            KillAt(w, 1, Player);
            for (int i = 0; i < 3; i++) w.Session.StepOnce();
            Assert.That(w.Session.Current.Tick, Is.EqualTo(3));
            Assert.That(w.Dead(Player), Is.True);
            Assert.That(w.Session.TryUnitStatus(Player, out UnitStatus status), Is.True);
            string text = HudLines.Build(w.Session.Current, w.Session.StateHash, w.Session.ControlledKey, status, new GroundPick(1f, 2f));
            Assert.That(text, Is.EqualTo(DeadHud));
        }
    }
}
