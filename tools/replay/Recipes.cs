using System;
using System.Collections.Generic;
using WarCore.Content.Rwr;
using WarCore.Presentation;

namespace WarCore.Replay
{
    // Named setups the exporter can run. Add one line per setup; nothing here changes game rules.
    public static class Recipes
    {
        public sealed class Entry
        {
            public readonly string Label;
            public readonly Func<WorldRecipe> Recipe;
            public readonly Func<byte[]> Map;
            public Entry(string label, Func<WorldRecipe> recipe, Func<byte[]> map) { Label = label; Recipe = recipe; Map = map; }
        }

        public static readonly IReadOnlyDictionary<string, Entry> All = new Dictionary<string, Entry>
        {
            ["base"] = new Entry("M7, default AI", () => DemoRecipes.M7(), DemoRecipes.M7MapBytes),
            ["playtest"] = new Entry("M7, rwr-m7-playtest AI rules",
                () => DemoRecipes.M7().WithAiRules(RwrPack.Load(DemoRecipes.PlaytestLayer)), DemoRecipes.M7MapBytes),
            // The recipe the MP playtest scene runs, with the player unit left idle (no input).
            ["mp-playtest"] = new Entry("MP playtest recipe, idle player", () => DemoRecipes.MpPlaytest(), DemoRecipes.M7MapBytes),
        };
    }
}
