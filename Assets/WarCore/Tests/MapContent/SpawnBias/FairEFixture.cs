using System;
using System.Collections.Generic;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    internal static class FairEFixture
    {
        internal static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(
            new Dictionary<string, string> { ["fair-e"] = "{ \"id\": \"fair-e\", \"extends\": \"rwr-m6\""
                + (ai == null ? "" : ", \"ai\": " + ai) + " }" }, FileContentSource.Rwr()), "fair-e");

        internal static WorldRecipe Recipe(string layout, ulong seed, bool? fairDecisions)
        {
            ContentPack content = RwrPack.Load(RwrPack.M6Layer);
            WorldRecipe recipe;
            if (layout == "M6")
            {
                WorldRecipe demo = DemoRecipes.M6(content);
                recipe = new WorldRecipe(seed, demo.Mover, demo.Units.ToArray(), demo.Script.ToArray())
                    .WithCombat(demo.Combat).WithMatch(demo.Match).WithCrowdPass(demo.CrowdPass);
            }
            else
            {
                int[] weapons = { content.WeaponOfUnitType(RwrPack.SoldierTeam1), content.WeaponOfUnitType(RwrPack.SoldierTeam2) };
                var units = new UnitSpawn[300];
                for (int i = 0; i < units.Length; i++)
                {
                    int team = i < 150 ? 1 : 2, rank = i % 150;
                    int x = team == 1 ? 6000 + rank % 10 * 2000 : 154000 - rank % 10 * 2000;
                    units[i] = new UnitSpawn((ulong)i + 1, 0, team, x, 41000 + rank % 100 / 10 * 2000,
                        6000, team, weapons[team - 1], rank >= 100);
                }
                var bases = new[] { new WarCore.Battle.SpawnAnchor(1, SimVec3.Metres(6, 0, 50)),
                    new WarCore.Battle.SpawnAnchor(2, SimVec3.Metres(154, 0, 50)) };
                recipe = new WorldRecipe(seed, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                    .WithCombat(content.BuildCombat()).WithMatch(content.BuildMatch(RwrMatch.CreateM7(content).Points.ToArray(), bases))
                    .WithCrowdPass(CrowdPassKind.TeamIdle);
            }
            return fairDecisions.HasValue
                ? recipe.WithTerrainRules(Pack("{ \"fairDecisions\": " + (fairDecisions.Value ? "true" : "false") + " }")) : recipe;
        }

        internal static byte[] Map(string layout) => layout == "M6" ? DemoRecipes.M6MapBytes() : WcMapWriter.Write(M3TestWorld.Boxes(160, 100));
        internal static M6World Create(string layout, ulong seed, bool? fairDecisions) =>
            new M6World(Recipe(layout, seed, fairDecisions), Map(layout));
    }
}
