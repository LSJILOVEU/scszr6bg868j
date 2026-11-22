using System;
using System.Collections.Generic;
using System.IO;
using WarCore.Content;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // MAP-BL0: the walled ward as a showcase stage set of its own (ShowcaseSets "ward"), played from its own menu item. One
    // stage: the committed map file baked, the open battle's recipe (unit types, AI rules, facing) and team 1's first
    // deployed soldier (key 1) as the player. PLACEHOLDER values come from the content layer "rwr-showcase".
    public static class WardDemo
    {
        public const string SetId = "ward", ShotPrefix = "WARD", StageId = "ward";
        public const string MapPath = "Assets/WarCore/Maps/walled-ward-160.map.json";
        public const ulong PlayerKey = 1;

        public static IReadOnlyList<ShowcaseStage> Stages { get; } = Array.AsReadOnly(new[]
        {
            new ShowcaseStage(StageId, "Walled ward", 20f, WardLayout.Pack, new[]
            {
                "160 m square ward: a 3 m wall with four gates, a cross of main streets and a square, all 1 m cubes",
                "Twenty shops with a door and a window, solid houses, stalls, carts, wells and trees",
                "Two points (crossroads, square); 32 a side on the field and 16 in reserve at the bases outside the gates",
                "AI, player keys and respawn as in the open battle",
            }, () => Baked().WcMapBytes, () => Recipe(Baked()), Script),
        });

        // The committed map file, found upwards from the working directory.
        public static byte[] MapFileBytes() => File.ReadAllBytes(ProjectFile(MapPath));

        public static MapBakeResult Baked() => MapBake.Run(MapFileBytes());

        // The open battle's recipe on this map, without a player.
        public static WorldRecipe AiRecipe(MapBakeResult baked)
        {
            if (baked == null) throw new ArgumentNullException(nameof(baked));
            ContentPack pack = ContentCatalog.Find().Load(baked.PackId);
            return MapRecipe.From(baked, pack, useUnitTypes: true).WithAiRules(pack).WithFacing();
        }

        public static WorldRecipe Recipe(MapBakeResult baked) => AiRecipe(baked).WithPlayer(PlayerKey);

        // East along the east-west street towards the crossroads, fire, dash, hold a block, fire again.
        public static (long AtTick, PlayerInput Input)[] Script() => new[]
        {
            (1L, new PlayerInput(1000, 0, 80000, 80000)),
            (120L, new PlayerInput(0, 0, 80000, 80000, primary: true)),
            (180L, new PlayerInput(1000, 0, 80000, 80000, dash: true)),
            (200L, new PlayerInput(1000, 0, 80000, 80000)),
            (300L, new PlayerInput(0, 0, 80000, 80000, block: true)),
            (360L, new PlayerInput(0, 0, 80000, 80000, primary: true)),
            (480L, new PlayerInput(1000, 0, 80000, 80000)),
        };

        private static string ProjectFile(string relative)
        {
            for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("Ward file " + relative + " not found (searched upwards from " + Directory.GetCurrentDirectory() + ").");
        }
    }
}
