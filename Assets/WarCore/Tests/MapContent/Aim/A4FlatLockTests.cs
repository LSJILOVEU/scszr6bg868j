using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // A4 acceptance 4: recorded on the code before A4 and unchanged after it (only interfaces that existed before A4 are used).
    // Three flat player worlds; every tick the controlled unit is alive, its Position.Y equals the map's ground height, so a
    // player aim with no height (Aim.Y = ground) is the old (Aim.X, Position.Y, Aim.Z) bit for bit. These are behaviour
    // baselines: a package that changes play may re-record them under the rules for baselines.
    public sealed class A4FlatLockTests
    {
        // Recorded values (see the PR for the base commit they were recorded on).
        private const ulong WalkHash = 0x270B175D7AA026D7UL, WalkFires = 0x005F344E51360E1DUL;
        private const ulong RespawnHash = 0x63183215151A8738UL, RespawnFires = 0x3607050946E87BE3UL;
        private const ulong LiftedHash = 0x240307C3F07ED4C2UL, LiftedFires = 0x37EC6CA8DA130E1DUL;
        private const int RespawnTicks = 3000;

        private static PlayerInput? Phases(long tick) => (tick - 1) % 150 == 0 ? A4.FlatScript((int)((tick - 1) / 150)) : (PlayerInput?)null;

        [Test]
        public void A4FlatLock_M2bWalkAimPushIsUnchanged()
        {
            var w = new M6World(A4.FlatRecipe(), DemoRecipes.M2bMapBytes());
            var (hash, fires, s) = A4.RunPlayer(w, A4.FlatTicks, Phases, 0);
            Report("walk", hash, fires, s);
            CheckCoverage(s);
            Assert.That(s.TravelRaw, Is.GreaterThanOrEqualTo(5 * A4.S), "player travel");
            Assert.That(s.BlockedTicks, Is.GreaterThanOrEqualTo(1), "a tick the player was held by the crowd");
            Assert.That(hash, Is.EqualTo(WalkHash));
            Assert.That(fires, Is.EqualTo(WalkFires));
        }

        [Test]
        public void A4FlatLock_MpRespawnIsUnchanged()
        {
            WorldRecipe recipe = DemoRecipes.MpRespawnDemo();
            A4.AssertRespawnRecipe(recipe);
            var w = new M6World(recipe, DemoRecipes.M7MapBytes());
            // Walk east into the enemy army and keep firing towards its base.
            var (hash, fires, s) = A4.RunPlayer(w, RespawnTicks,
                tick => tick == 1 ? new PlayerInput(1000, 0, 150000, 50000, true) : (PlayerInput?)null, 0);
            Report("respawn", hash, fires, s);
            Assert.That(s.Fires, Is.GreaterThanOrEqualTo(10));
            Assert.That(s.Hit, Is.GreaterThanOrEqualTo(1));
            Assert.That(s.Deaths, Is.GreaterThanOrEqualTo(1), "player deaths");
            Assert.That(s.Respawns, Is.GreaterThanOrEqualTo(1), "player respawns");
            Assert.That(hash, Is.EqualTo(RespawnHash));
            Assert.That(fires, Is.EqualTo(RespawnFires));
        }

        [Test]
        public void A4FlatLock_LiftedFlatMapIsUnchanged()
        {
            var w = new M6World(A4.FlatRecipe(), A4.LiftedM2bBytes());
            var (hash, fires, s) = A4.RunPlayer(w, A4.FlatTicks, Phases, A4.LiftedGroundRaw);
            Report("lifted", hash, fires, s);
            CheckCoverage(s);
            Assert.That(s.TravelRaw, Is.GreaterThanOrEqualTo(5 * A4.S), "player travel");
            Assert.That(hash, Is.EqualTo(LiftedHash));
            Assert.That(fires, Is.EqualTo(LiftedFires));
        }

        private static void CheckCoverage(A4.FlatStats s)
        {
            Assert.That(s.Fires, Is.GreaterThanOrEqualTo(10));
            Assert.That(s.Hit, Is.GreaterThanOrEqualTo(1));
            Assert.That(s.Wall, Is.GreaterThanOrEqualTo(1));
        }

        private static void Report(string name, ulong hash, ulong fires, A4.FlatStats s) =>
            TestContext.Progress.WriteLine($"A4FlatLock {name} hash=0x{hash:X16} fires=0x{fires:X16} n={s.Fires} hit={s.Hit} wall={s.Wall} "
                + $"open={s.Open} deaths={s.Deaths} respawns={s.Respawns} pushed={s.PushedTicks} blocked={s.BlockedTicks} travel={s.TravelRaw}");
    }
}
