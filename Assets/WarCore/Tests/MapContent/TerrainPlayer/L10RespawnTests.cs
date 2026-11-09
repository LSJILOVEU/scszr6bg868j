using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.L10Fixture;

namespace WarCore.Tests
{
    // L10 (7c): a frozen respawn fixture on the demo terrain. Team 1: the player on the slope, two reserves, its only base on the
    // slope. Team 2: armed units within range. Reinforcement and the time limit never act inside the test.
    public sealed class L10RespawnTests
    {
        private static M6World World()
        {
            var units = new[]
            {
                U(1, 16500, 15500), U(3, 0, 0, reserve: true), U(4, 0, 0, reserve: true),
                U(21, 20500, 13500, team: 2, weapon: 0), U(22, 20500, 15500, team: 2, weapon: 0), U(23, 20500, 17500, team: 2, weapon: 0),
                U(24, 21500, 14500, team: 2, weapon: 0), U(25, 21500, 16500, team: 2, weapon: 0)
            };
            MatchPack match = QuietMatch(new[] { new SpawnAnchor(1, SimVec3.Metres(14, 0, 8)), new SpawnAnchor(2, SimVec3.Metres(44, 0, 16)) }, 300);
            WorldRecipe recipe = M6World.RecipeOf(units, match, L6.Pack().BuildCombat()).WithTerrainRules(L6.Pack()).WithPlayer(1);
            return new M6World(recipe, L6.MapBytes());
        }

        [Test]
        public void L10Respawn_AfterThreeHundredTicks_OnTheGround()
        {
            M6World w = World();
            Assert.That(w.Recipe.Match.RespawnTicks, Is.EqualTo(300));
            StableEntityKey player = StableEntityKey.Placed(1, 1);
            while (!w.Dead(1) && w.Sim.Clock.Tick < 3000) w.Session.StepOnce();
            Assert.That(w.Dead(1), Is.True, "the player dies");
            long d = w.Sim.GameplayLife(player.Id).EnteredTick;
            Assert.That(w.Sim.GameplayLife(player.Id).State, Is.EqualTo(GameplayLifeState.Dead));
            while (w.Sim.Clock.Tick < d + 299) w.Session.StepOnce();
            Assert.That(w.Session.ControlledKey, Is.EqualTo((ulong?)1), "D+299 still the dead player");
            w.Session.StepOnce();
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(d + 300));
            ulong? now = w.Session.ControlledKey;
            Assert.That(now == 3 || now == 4, Is.True, "D+300 a reserve, was " + now);
            SimVec3 p = At(w, now.Value);
            Assert.That(OnGround(w, p), Is.True, "respawned on the ground");
            Assert.That(p.Y.Raw, Is.GreaterThan(0), "on the slope");
            Assert.That(w.Match.Winner, Is.EqualTo(0));
        }
    }
}
