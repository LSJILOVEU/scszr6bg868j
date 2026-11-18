using System;
using System.Collections.Generic;
using System.IO;
using WarCore.Content;
using WarCore.Content.MapTools;
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
            // MAP-P1 three-lane map, its own pack's AI rules.
            ["lanes"] = new Entry("three lanes, default AI", () => MapRecipe.From(Lanes.Value, RwrPack.Load(Lanes.Value.PackId)), () => Lanes.Value.WcMapBytes),
            // The same with the assault-roles placement (AI-P2 PLACEHOLDER values).
            ["lanes-p2"] = new Entry("three lanes, assault-roles", () => MapRecipe.From(Lanes.Value, RwrPack.Load(Lanes.Value.PackId))
                .WithAiRules(Layer(Lanes.Value.PackId, AssaultRolesP2)), () => Lanes.Value.WcMapBytes),
        };

        private static readonly Lazy<MapBakeResult> Lanes = new Lazy<MapBakeResult>(() => MapBake.Run(File.ReadAllBytes("Assets/WarCore/Maps/lanes-playtest.map.json")));
        private const string AssaultRolesP2 = "{ \"placement\": \"assault-roles\", \"frontPermille\": 425, \"flankPermille\": 375, \"rearPermille\": 200, "
            + "\"frontArcMm\": 14000, \"flankSideMm\": 35000, \"rearOutMm\": 12000, \"flankMinMm\": 60000, \"anchorMm\": 10000, \"arriveMm\": 3000 }";

        // A replay-only layer over a shipped pack that sets the ai section; nothing is written to the content folder.
        private static ContentPack Layer(string extends, string ai)
        {
            const string id = "replay-layer";
            return ContentLoader.Load(new OverlaySource(id, "{ \"id\": \"" + id + "\", \"extends\": \"" + extends + "\", \"ai\": " + ai + " }"), id);
        }
        private sealed class OverlaySource : IContentSource
        {
            private readonly string id, text;
            private readonly IContentSource files = FileContentSource.Rwr();
            internal OverlaySource(string id, string text) { this.id = id; this.text = text; }
            public bool TryRead(string layerId, out string text, out string location)
            {
                if (layerId != id) return files.TryRead(layerId, out text, out location);
                text = this.text; location = "replay:" + id; return true;
            }
        }
    }
}
