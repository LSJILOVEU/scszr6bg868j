using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // K + Match + H1 together: a rifle-and-sword or sword-only team against a sword-only team, both marching on one control point with guard slots and the friendly pass rule.
    public sealed class Wp21MeleeMatchTests
    {
        private const int PerTeam = 80;
        private static WorldRecipe Recipe(string team1Type)
        {
            var content = RwrPack.Load("rwr-melee");
            int gun = content.WeaponOfUnitType(team1Type), sword = content.MeleeOfUnitType(team1Type);
            Assert.That(sword, Is.EqualTo(0));
            var units = new UnitSpawn[PerTeam * 2];
            for (int i = 0; i < units.Length; i++)
            {
                int team = i < PerTeam ? 1 : 2, rank = i % PerTeam;
                long x = team == 1 ? 26000 + rank % 8 * 1500 : 54000 - rank % 8 * 1500, z = 12000 + rank / 8 * 1600;
                units[i] = team == 1 ? new UnitSpawn((ulong)i + 1, 0, team, x, z, 6000, team, gun, false, sword)
                    : new UnitSpawn((ulong)i + 1, 0, team, x, z, 6000, team, content.WeaponOfUnitType("rwr.swordsman"), false, content.MeleeOfUnitType("rwr.swordsman"));
            }
            var match = M6World.Pack(new[] { new ControlPointDef(7, M6World.P(40), SimFix.FromInt(6)) }, think: 30);
            return M6World.Endless(new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(content.BuildCombat()).WithMatch(match).WithCrowdPass(CrowdPassKind.TeamIdle));
        }

        [TestCase("rwr.rifle-sword", false)]
        [TestCase("rwr.swordsman", true)]
        public void MeleeTeamsGarrisoningAPointStayDeterministicAndNeverStackExactly(string team1Type, bool swordsOnly)
        {
            var recipe = Recipe(team1Type);
            var a = new M6World(recipe); var b = new M6World(recipe); var reverse = new M6World(recipe, reverse: true);
            int strikes = 0, fires = 0;
            for (int tick = 1; tick <= 900; tick++)
            {
                a.Step(); strikes += a.Combat.Strikes.Length; fires += a.Combat.Fires.Length; b.Step(); reverse.Step();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "repeat tick=" + tick);
                Assert.That(reverse.Hash, Is.EqualTo(a.Hash), "reverse tick=" + tick);
            }
            int alive = 0, fought = 0;
            var seen = new List<(int Team, SimVec3 Point)>();
            for (ulong key = 1; key <= PerTeam * 2; key++)
            {
                if (a.Dead(key)) { fought++; continue; }
                alive++;
                var p = a.Read<Position>(key).Value; int team = key <= PerTeam ? 1 : 2;
                foreach (var other in seen)
                    if (other.Team == team) Assert.That(CombatSystem.Horizontal(other.Point - p).LengthSquared.Sign, Is.Positive, "exact stack of team " + team);
                seen.Add((team, p));
            }
            double minEnemy = 1e9;
            for (ulong i = 1; i <= PerTeam; i++) for (ulong j = PerTeam + 1; j <= PerTeam * 2; j++)
                minEnemy = Math.Min(minEnemy, CombatSystem.Horizontal(a.Read<Position>(i).Value - a.Read<Position>(j).Value).LengthSquared.Sqrt().Raw / 65536.0);
            TestContext.Out.WriteLine($"minEnemy={minEnemy:F2} strikes={strikes} fires={fires}");
            TestContext.Out.WriteLine($"WP21_MELEE_MATCH tick={a.Sim.Clock.Tick} alive={alive} dead={fought} owner={a.Capture.Points[0].Owner} hash={a.Hash:X16}");
            Assert.That(alive, Is.GreaterThan(0));
            if (swordsOnly) Assert.That(strikes, Is.GreaterThan(0), "swords must actually be swung");
        }
    }
}
