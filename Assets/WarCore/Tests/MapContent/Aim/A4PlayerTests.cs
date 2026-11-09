using NUnit.Framework;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // A4 acceptance 2: the player's input carries the aim height. Flat M2b map (as Wp18FireTests.cs:81-86): shooter key 1 at
    // (20, 2) m, target key 2 at (22, 2) m, no spread; one tick.
    public sealed class A4PlayerTests
    {
        private static (M6World w, FireRecord f) Fire(long? yMm)
        {
            var w = new M6World(Wp18TestWorld.Recipe(M5TestWorld.NoSpread(WarCore.Content.Rwr.RwrPack.Create()), new[] {
                Wp16TestWorld.U(1, 20000, 2000, weapon: 0), Wp16TestWorld.U(2, 22000, 2000, team: 2, squad: 2) }), DemoRecipes.M2bMapBytes());
            w.Session.SetPlayerInput(1, new PlayerInput(0, 0, 22000, 2000, true, aimYMm: yMm));
            w.Session.StepOnce();
            Assert.That(w.Combat.Fires.Length, Is.EqualTo(1));
            Assert.That(w.Read<Position>(1).Value.Y.Raw, Is.EqualTo(0), "shooter Y = the map's ground height");
            return (w, w.Combat.Fires[0]);
        }

        private static void End(FireRecord f, long x, long y, long z)
        {
            Assert.That(f.End.X.Raw, Is.EqualTo(x), "End.X"); Assert.That(f.End.Y.Raw, Is.EqualTo(y), "End.Y"); Assert.That(f.End.Z.Raw, Is.EqualTo(z), "End.Z");
        }

        [Test]
        public void A4Player_NoHeight_IsTheFlatShot()
        {
            var (_, f) = Fire(null);
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit));
            End(f, 1425408, 0, 131072);
            Assert.That(f.Distance.Raw, Is.EqualTo(131072));
        }

        [Test]
        public void A4Player_HeightZero_IsTheSameAsNoHeight()
        {
            var (_, a) = Fire(null);
            var (_, b) = Fire(0);
            Assert.That(b.Outcome, Is.EqualTo(a.Outcome)); Assert.That(b.Target, Is.EqualTo(a.Target));
            Assert.That(b.Origin, Is.EqualTo(a.Origin)); Assert.That(b.End, Is.EqualTo(a.End));
            Assert.That(b.Distance, Is.EqualTo(a.Distance)); Assert.That(b.DamageRaw, Is.EqualTo(a.DamageRaw));
        }

        [Test]
        public void A4Player_Height200_HitsAlongTheRisingLine()
        {
            var (_, f) = Fire(200);
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Hit));
            Assert.That(f.Target, Is.EqualTo(Wp18TestWorld.Id(2)));
            End(f, 1425405, 11466, 131072);
        }

        [Test]
        public void A4Player_Height3000_PassesOver()
        {
            var (_, f) = Fire(3000);
            Assert.That(f.Outcome, Is.EqualTo(FireOutcome.Open));
            Assert.That(f.Target, Is.EqualTo(default(StableId)));
            End(f, 3746304, 3653443, 131072);
            Assert.That(f.Distance.Raw, Is.EqualTo(2435584));
        }
    }
}
