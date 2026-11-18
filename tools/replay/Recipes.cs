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
            // Width of the guard-slot ring the setup's AI rules set (millimetres); idleOutside counts units beyond radius + ring.
            public readonly int RingMm;
            public Entry(string label, Func<WorldRecipe> recipe, Func<byte[]> map, int ringMm = 0) { Label = label; Recipe = recipe; Map = map; RingMm = ringMm; }
        }

        public static readonly IReadOnlyDictionary<string, Entry> All = new Dictionary<string, Entry>
        {
            ["base"] = new Entry("M7, default AI", () => DemoRecipes.M7(), DemoRecipes.M7MapBytes),
            ["playtest"] = new Entry("M7, rwr-m7-playtest AI rules",
                () => DemoRecipes.M7().WithAiRules(RwrPack.Load(DemoRecipes.PlaytestLayer)), DemoRecipes.M7MapBytes),
            // The recipe the MP playtest scene runs, with the player unit left idle (no input).
            ["mp-playtest"] = new Entry("MP playtest recipe, idle player", () => DemoRecipes.MpPlaytest(), DemoRecipes.M7MapBytes),
            // MAP-P1 three-lane map, seed and AI rules as baked (default AI).
            ["lanes"] = new Entry("Three-lane map, default AI", () => Lanes(null), () => LanesBake().WcMapBytes),
            // AI-P1 (values fixed by the task, PLACEHOLDER): 2 m guard slots, a 6 m slot ring (the point radius) and the
            // scatter placement; -p1 scatters over the point radius (6 m), -p1w over radius + ring (12 m).
            ["lanes-p1"] = new Entry("Three-lane map, 2 m slots + 6 m ring + scatter 6 m",
                () => Lanes(Layer("replay-lanes-p1", "rwr-m6", P1Ai(6000))), () => LanesBake().WcMapBytes, P1RingMm),
            ["lanes-p1w"] = new Entry("Three-lane map, 2 m slots + 6 m ring + scatter 12 m",
                () => Lanes(Layer("replay-lanes-p1w", "rwr-m6", P1Ai(12000))), () => LanesBake().WcMapBytes, P1RingMm),
            ["playtest-p1"] = new Entry("M7, rwr-m7-playtest AI rules + 2 m slots + 6 m ring + scatter 6 m",
                () => DemoRecipes.M7().WithAiRules(Layer("replay-playtest-p1", DemoRecipes.PlaytestLayer, P1Ai(6000))), DemoRecipes.M7MapBytes, P1RingMm),
            ["playtest-p1w"] = new Entry("M7, rwr-m7-playtest AI rules + 2 m slots + 6 m ring + scatter 12 m",
                () => DemoRecipes.M7().WithAiRules(Layer("replay-playtest-p1w", DemoRecipes.PlaytestLayer, P1Ai(12000))), DemoRecipes.M7MapBytes, P1RingMm),
            // AI-P2 (PLACEHOLDER values of the task): 2 m slots, 6 m ring and the assault-roles placement. The anchor sits on the
            // capture circle (6 m): at 10 m every role's slots lay in the ring outside the circle and no point was ever taken.
            ["lanes-p2"] = new Entry("Three-lane map, 2 m slots + 6 m ring + assault-roles",
                () => Lanes(Layer("replay-lanes-p2", "rwr-m6", P2Ai)), () => LanesBake().WcMapBytes, P1RingMm),
            // AI-S1 (values fixed by the task, PLACEHOLDER): the engage part "bound" on top of the setup's own AI rules.
            ["playtest-s1"] = new Entry("M7, rwr-m7-playtest AI rules + engage bound",
                () => DemoRecipes.M7().WithAiRules(Layer("replay-playtest-s1", DemoRecipes.PlaytestLayer, S1Ai)), DemoRecipes.M7MapBytes),
            ["lanes-s1"] = new Entry("Three-lane map, default AI + engage bound",
                () => Lanes(Layer("replay-lanes-s1", LanesBake().PackId, S1Ai)), () => LanesBake().WcMapBytes),
        };

        private const string S1Ai = "{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000 }";
        private const string LanesPath = "Assets/WarCore/Maps/lanes-playtest.map.json";
        private const int P1RingMm = 6000;
        private static string P1Ai(int scatterMm) =>
            "{ \"slotSpacingMm\": 2000, \"slotRingMm\": " + P1RingMm + ", \"placement\": \"scatter\", \"scatterMm\": " + scatterMm + " }";
        private const string P2Ai = "{ \"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"assault-roles\", "
            + "\"frontPermille\": 425, \"flankPermille\": 375, \"rearPermille\": 200, \"frontArcMm\": 14000, \"flankSideMm\": 35000, "
            + "\"rearOutMm\": 12000, \"flankMinMm\": 60000, \"anchorMm\": 6000, \"arriveMm\": 3000 }";
        private static MapBakeResult LanesBake() => MapBake.Run(File.ReadAllBytes(LanesPath));
        private static WorldRecipe Lanes(ContentPack ai)
        {
            MapBakeResult bake = LanesBake();
            WorldRecipe recipe = MapRecipe.From(bake, RwrPack.Load(bake.PackId));
            return ai == null ? recipe : recipe.WithAiRules(ai);
        }

        // A content layer that exists only in the replay tool: extends a shipped layer and sets its ai section.
        private static ContentPack Layer(string id, string extends, string ai) => ContentLoader.Load(
            new OneLayer(id, "{ \"id\": \"" + id + "\", \"extends\": \"" + extends + "\", \"ai\": " + ai + " }"), id);
        private sealed class OneLayer : IContentSource
        {
            private readonly string id, text;
            private readonly IContentSource rest = FileContentSource.Rwr();
            public OneLayer(string id, string text) { this.id = id; this.text = text; }
            public bool TryRead(string layerId, out string json, out string location)
            {
                if (layerId != id) return rest.TryRead(layerId, out json, out location);
                json = text; location = "replay:" + id; return true;
            }
        }
    }
}
