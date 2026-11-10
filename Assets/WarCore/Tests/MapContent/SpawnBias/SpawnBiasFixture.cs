using System;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    internal static class SpawnBiasFixture
    {
        internal static (int winner, long tick, ulong hash) Run(string combination, ulong seed)
        {
            ContentPack content = RwrPack.Load(RwrPack.M6Layer);
            int[] weapons = { content.WeaponOfUnitType(RwrPack.SoldierTeam1), content.WeaponOfUnitType(RwrPack.SoldierTeam2) };
            var units = new UnitSpawn[300];
            for (int i = 0; i < units.Length; i++)
            {
                int team = i < 150 ? 1 : 2, rank = i % 150;
                int side = combination == "sides" ? 3 - team : team;
                int keyTeam = combination == "keys" ? 3 - team : team;
                int weaponTeam = combination == "weapons" ? 3 - team : combination == "same-weapon" ? 1 : team;
                int x = side == 1 ? 6000 + rank % 10 * 2000 : 154000 - rank % 10 * 2000;
                units[i] = new UnitSpawn((ulong)((keyTeam - 1) * 150 + rank + 1), 0, team,
                    x, 41000 + rank % 100 / 10 * 2000, 6000, team, weapons[weaponTeam - 1], rank >= 100);
            }
            // Team bases follow the side swap; neutral point IDs stay at their fixed map positions.
            var bases = new[] {
                new SpawnAnchor(1, SimVec3.Metres(combination == "sides" ? 154 : 6, 0, 50)),
                new SpawnAnchor(2, SimVec3.Metres(combination == "sides" ? 6 : 154, 0, 50))
            };
            MatchPack match = content.BuildMatch(RwrMatch.CreateM7(content).Points.ToArray(), bases);
            var recipe = new WorldRecipe(seed, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(content.BuildCombat()).WithMatch(match).WithCrowdPass(CrowdPassKind.TeamIdle);
            var w = new M6World(recipe, WcMapWriter.Write(M3TestWorld.Boxes(160, 100)));
            while (!w.Sim.IsFinished && w.Sim.Clock.Tick < match.MaxTicks) w.Step();
            return (w.Match.Winner, w.Match.EndTick, w.Hash);
        }
    }
}
