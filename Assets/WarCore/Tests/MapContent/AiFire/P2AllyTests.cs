using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 9: the kept target is aimed at through DefaultAutoFire.AimAt, so the ally check still holds the fire. The
    // fixture's combat pack writes allyCheckMissRaw 0 (the roll never lets a blocked shot through) and allyHoldMilli 30000.
    public sealed class P2AllyTests
    {
        private const int K = 10;
        // A2 stands 2 m east of A, on A2's line to B; A2 first picks B on 60 (B is 2 m further from A2) and its first
        // sample tick from there is f = 64 (phase 6).
        private const long SelA2 = 60, F = 64;

        private static void PackIsExplicit(M6World w) =>
            Assert.That((w.Combat.Pack.AllyCheckMissRaw, w.Combat.Pack.AllyHoldMilli), Is.EqualTo((P2.AllyMissRaw, P2.AllyHoldMilli)));

        // World level, K = 10, R = 0: A2 is ready on f, fires nothing on f and its DueMilli jumps to f * 1000 + the hold; it
        // never fires in 300 ticks (every attempt is blocked by A), while A fires on its sample ticks as usual.
        [Test]
        public void BlockedShooter_NeverFires_AndTheHoldPushesDue()
        {
            P2.Trace r = P2.Run(P2.Cast(P2.A2Behind), P1.Walk, P2.Ai(0, K), 300);
            PackIsExplicit(r.W);
            ulong b = r.Id(P2.KeyB);
            Assert.That(r.A2.FirstSelect(b), Is.EqualTo(SelA2));
            long f = P2.FirstSample(r.Id(P2.KeyA2), SelA2, K);
            Assert.That(f, Is.EqualTo(F));
            Assert.That(r.A2.Due[(int)F - 1], Is.LessThanOrEqualTo(F * 1000), "A2 is ready on f");
            Assert.That(r.A2.Fire[(int)F], Is.False);
            Assert.That(r.A2.Due[(int)F], Is.EqualTo(F * 1000 + P2.AllyHoldMilli), "the hold");
            Assert.That(r.A2.FireTicks(300), Is.Empty, "A2 never fires");
            Assert.That(r.A.FireTicks(120), Is.EqualTo(new long[] { 46, 76, 106 }), "A fires as usual");
        }

        // AimAt directly, on the world after tick 63 (B picked by both): A2 -> B is held (A is first on the line), A -> B
        // fires with the horizontal heading to B.
        [Test]
        public void AimAt_BlockedByTeammate_HoldsFire()
        {
            M6World w = P2.World(P2.Cast(P2.A2Behind), P2.Ai(0, K));
            PackIsExplicit(w);
            for (long tick = 1; tick <= F - 1; tick++) w.Sim.Step(P2.Drive(w, tick, P1.Walk));
            Assert.That(P1.Selected(w, P2.KeyA2), Is.EqualTo(P2.Id(w, P2.KeyB).Value));
            Assert.That(P1.Selected(w, P2.KeyA), Is.EqualTo(P2.Id(w, P2.KeyB).Value));
            var builtIn = (DefaultAutoFire)w.Combat.BuiltInAutoFire;
            EntityId a = w.Unit(P2.KeyA), a2 = w.Unit(P2.KeyA2), b = w.Unit(P2.KeyB);
            Position atA = w.Read<Position>(P2.KeyA), atA2 = w.Read<Position>(P2.KeyA2);
            Assert.That(builtIn.AimAt(w.Combat, a2, in atA2, b, out _), Is.EqualTo(AutoFireAction.HoldFire));
            Assert.That(builtIn.AimAt(w.Combat, a, in atA, b, out SimVec3 direction), Is.EqualTo(AutoFireAction.Fire));
            Assert.That(direction, Is.EqualTo(CombatSystem.Horizontal(w.Combat.Point(b) - w.Combat.Point(a))));
        }
    }
}
