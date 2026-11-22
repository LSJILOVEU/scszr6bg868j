using System;
using System.Linq;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // MAP-E1g: the committed map file, baked once, and the worlds the tests start from it.
    internal static class MapE1gWorld
    {
        internal static readonly ContentPack Pack = RwrPack.Load(MapE1gBuild.Pack);

        private static byte[] file;
        private static MapBakeResult baked;
        private static WcMap map;
        private static TerrainTables tables;

        internal static byte[] File => file ?? (file = System.IO.File.ReadAllBytes(MapE1gBuild.MapPath));
        internal static MapBakeResult Baked => baked ?? (baked = MapBake.Run(File));
        internal static WcMap Map => map ?? (map = WcMapReader.Read(Baked.WcMapBytes));
        internal static TerrainTables Tables => tables ?? (tables = TerrainDerivation.Derive(Map, Pack));

        // The recipe the runtime builds from the file.
        internal static WorldRecipe Recipe() => MapRecipe.From(Baked, Pack);

        internal static M6World World(WorldRecipe recipe = null) => new M6World(recipe ?? Recipe(), Baked.WcMapBytes);

        // Reserve soldiers added to a lone team. The world sizes its cell table from the number of entities (16 slots each, at
        // most half used): the 62 walls take 1108 cells, so 62 walls and 60 soldiers (a table of 2048 slots) is refused at build
        // time, and the full 62 + 120 (4096 slots) is not. The padding keeps the lone team's world the size of the full one.
        internal const int Padding = 20;

        // One team alone, on a match that never ends; same terrain rules, combat, match and crowd pass as the full recipe.
        // The other team's soldiers are left out. `padding` extra reserves of the same team (keys from 121) keep the table size.
        internal static WorldRecipe Alone(int team, int padding = Padding)
        {
            WorldRecipe full = Recipe();
            var mine = full.Units.ToArray().Where(u => u.Team == team).ToList();
            UnitSpawn reserve = mine.First(u => u.Reserve);
            for (int k = 0; k < padding; k++)
                mine.Add(new UnitSpawn(121UL + (ulong)k, reserve.Archetype, reserve.Squad, reserve.XMm, reserve.ZMm, reserve.SpeedMmPerSec,
                    reserve.Team, reserve.Weapon, true, reserve.Melee, reserve.Block, reserve.Layer));
            WorldRecipe recipe = new WorldRecipe(full.Seed, MoverKind.InterimCrowd, mine.ToArray(), Array.Empty<BridgeCommand>())
                .WithCombat(full.Combat).WithMatch(full.Match).WithCrowdPass(CrowdPassKind.TeamIdle);
            if (full.TerrainPack != null) recipe = recipe.WithTerrainRules(full.TerrainPack);
            return M6World.Endless(recipe);
        }

        // Cell of a millimetre position.
        internal static (int X, int Z) CellOf(long xMm, long zMm) => ((int)(xMm / 1000), (int)(zMm / 1000));

        internal static bool Walkable(long xMm, long zMm)
        {
            (int x, int z) = CellOf(xMm, zMm);
            return Tables.Nav.IsPassable(x, z);
        }
    }
}
