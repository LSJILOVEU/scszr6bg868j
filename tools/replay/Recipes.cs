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
            ["playtest"] = new Entry("M7, rwr-ai-playtest AI values",
                () => DemoRecipes.M7().WithAiRules(RwrPack.Load("rwr-ai-playtest")), DemoRecipes.M7MapBytes),
        };
    }
}
