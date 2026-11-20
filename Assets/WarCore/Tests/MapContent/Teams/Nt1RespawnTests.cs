using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;
using static WarCore.Tests.C1bAnchorTests;
using static WarCore.Tests.RespawnWorld;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // NT-1 A8: in a three-team world any living unit of another team (1..TeamCount) contests the player's spawn point; a unit
    // of no team does not. The player (team 1) dies at K; its only point A (24, 20) has a spawn; the own base is at (5, 38).
    public sealed class Nt1RespawnTests
    {
        private static MatchPack ThreeTeamPack() =>
            Pack(Points(Spawn(1, 24, 20)), Bases(new SpawnAnchor(1, P(5, 38)), new SpawnAnchor(3, P(40, 2))), D);

        private static void InA(M6World w)
        {
            Assert.That(InsideA(w, Other), Is.True); Assert.That(w.Dead(Other), Is.False);
            Assert.That(w.Recipe.Match.TeamCount, Is.EqualTo(3));
        }

        [Test]
        public void Nt1Respawn_ThirdTeamUnitInThePoint_FallsBackToTheBase() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 3)), ThreeTeamPack()), InA),
                Is.EqualTo(P(4, 37)));

        [Test]
        public void Nt1Respawn_SecondTeamUnitInThePoint_StillContests() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 2)), ThreeTeamPack()), InA),
                Is.EqualTo(P(4, 37)));

        [Test]
        public void Nt1Respawn_TeamZeroUnitInThePoint_UsesThePoint() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 0)), ThreeTeamPack()), InA),
                Is.EqualTo(P(23, 19)));

        // Team 9 is outside 1..TeamCount, like team 0.
        [Test]
        public void Nt1Respawn_TeamNineUnitInThePoint_UsesThePoint() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 9)), ThreeTeamPack()), InA),
                Is.EqualTo(P(23, 19)));

        [Test]
        public void Nt1Respawn_OwnTeamUnitInThePoint_UsesThePoint() =>
            Assert.That(Respawned(World(Units(U(Reserve, 0, 0, reserve: true), U(Other, 24, 21, team: 1)), ThreeTeamPack()), InA),
                Is.EqualTo(P(23, 19)));
    }
}
