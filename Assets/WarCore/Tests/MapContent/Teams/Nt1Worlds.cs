using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // NT-1 worlds with three teams, built through the same WorldBuilder as every recipe. Team t's units start in a column at
    // x = 10 + 25 (t - 1), its base below them; one neutral point (id 7) at (35, 28), radius 4, taken in 120 ticks. The AI thinks every 30 ticks
    // (it leads teams 1 and 2 only); real M6 guns. One unit of team 9 (outside every team) stands at (35, 38).
    internal static class Nt1Worlds
    {
        internal const ulong TeamNine = 900;

        internal static WorldRecipe ThreeTeams(int alive, int reserves, int interval, bool endless, int max = 9000)
        {
            var units = new List<UnitSpawn>();
            var bases = new List<SpawnAnchor>();
            for (int t = 1; t <= 3; t++)
            {
                int x = 10 + 25 * (t - 1);
                bases.Add(new SpawnAnchor(t, M6World.P(x, 4)));
                for (int i = 0; i < alive; i++) units.Add(M6World.U((ulong)(100 * t + i), t, x + i % 4, 10 + i / 4 * 2, weapon: 0, speed: 6000));
                for (int i = 0; i < reserves; i++) units.Add(M6World.U((ulong)(100 * t + 50 + i), t, reserve: true, weapon: 0, speed: 6000));
            }
            units.Add(M6World.U(TeamNine, 9, 35, 38));
            var points = new[] { new ControlPointDef(7, M6World.P(35, 28), SimFix.FromInt(4)) };
            var pack = M6World.Pack(points, ticks: 120, interval: interval, think: 30, max: max, bases: bases.ToArray());
            WorldRecipe recipe = M6World.RecipeOf(units.ToArray(), pack, RwrPack.CreateM6());
            return endless ? M6World.Endless(recipe) : recipe;
        }
    }
}
