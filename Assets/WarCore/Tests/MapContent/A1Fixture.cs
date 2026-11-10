using System;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    internal static class A1
    {
        internal const int Think = 10, MaxTicks = 9000;
        internal static ContentPack Pack(string ai) => L4.Pack(ai);
        internal static UnitSpawn Unit(ulong key, int team, int x, int z, int weapon = -1, int melee = 0, long speed = 3000) =>
            new UnitSpawn(key, 0, team, x * 1000, z * 1000, speed, team, weapon, false, melee);

        internal static UnitSpawn[] Armies()
        {
            var units = new UnitSpawn[20];
            for (int i = 0; i < 10; i++)
            {
                units[i] = Unit((ulong)i + 1, 1, 10, 11 + 2 * i);
                units[i + 10] = Unit((ulong)i + 11, 2, 70, 11 + 2 * i);
            }
            return units;
        }

        internal static WorldRecipe Recipe(string ai, UnitSpawn[] units = null, bool endless = false)
        {
            var match = new MatchPack(new[] { new ControlPointDef(1, SimVec3.Metres(40, 0, 20), SimFix.FromInt(8)) },
                new[] { new SpawnAnchor(1, SimVec3.Metres(4, 0, 20)), new SpawnAnchor(2, SimVec3.Metres(76, 0, 20)) },
                2, 1, false, 100000, InterruptMode.Reset, 1, 90, Think, MaxTicks, SimFix.FromInt(80), 4, 4, SimFix.Ratio(3, 2));
            var combat = MeleeWorld.Pack(new[] { MeleeWorld.Spec(maxTargets: 1) }, new[] { A2.Rifle(0) });
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units ?? Armies(), Array.Empty<BridgeCommand>())
                .WithMatch(match).WithCombat(combat).WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(Pack(ai));
            return endless ? recipe.WithRuleSet(MatchMode.TestNeverEndsId) : recipe;
        }

        internal static byte[] Map((int, int, int, int)[] walls = null) => WcMapWriter.Write(Wp23.Make(80, 40, 0, 4 * SimFix.Scale,
            walls ?? Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, (x, z) => 0, new[] { "grass" }, (x, z) => 0));
        internal static M6World World(string ai, UnitSpawn[] units = null, (int, int, int, int)[] walls = null, bool endless = false) =>
            new M6World(Recipe(ai, units, endless), Map(walls));

        internal static int Deaths(M6World w, int team)
        {
            int count = 0;
            foreach (UnitSpawn unit in w.Recipe.Units)
                if (unit.Team == team && w.Dead(unit.Key)) count++;
            return count;
        }
    }
}
