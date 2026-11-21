using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Battle;
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
            // AI-N1: the same AI rules with the playtest layer's weapons too (the combat pack the MP playtest recipe now runs).
            // "playtest" keeps the M6 weapons so that the -p1, -s1 setups and their reference values stay comparable.
            ["playtest-n1"] = new Entry("M7, rwr-m7-playtest weapons and AI rules",
                () => DemoRecipes.M7(RwrPack.Load(DemoRecipes.PlaytestLayer)).WithAiRules(RwrPack.Load(DemoRecipes.PlaytestLayer)), DemoRecipes.M7MapBytes),
            // The recipe the MP playtest scene runs, with the player unit left idle (no input).
            ["mp-playtest"] = new Entry("MP playtest recipe, idle player", () => DemoRecipes.MpPlaytest(), DemoRecipes.M7MapBytes),
            // MAP-P1 three-lane map, seed and AI rules as baked (default AI).
            ["lanes"] = new Entry("Three-lane map, default AI", () => Lanes(null), () => LanesBake().WcMapBytes),
            // BODY-1: the same map and default AI with the pack "rwr-body" (soldier body 1.75 m, eye and muzzle 1.5 m); only the pack line is swapped.
            ["lanes-body"] = new Entry("Three-lane map, default AI, rwr-body pack", LanesBody, () => LanesBodyBake().WcMapBytes),
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
            // AI-S2 (value fixed by the task, PLACEHOLDER): each shooter's own reaction, burst and pause, spread 300 permille.
            ["playtest-s2"] = new Entry("M7, rwr-m7-playtest AI rules + trait spread 300",
                () => DemoRecipes.M7().WithAiRules(Layer("replay-playtest-s2", DemoRecipes.PlaytestLayer, "{ \"traitSpreadPermille\": 300 }")), DemoRecipes.M7MapBytes),
            // AI-S3 (values fixed by the task, PLACEHOLDER): the pressure system on top of the S1 setups.
            ["playtest-s3"] = new Entry("M7, rwr-m7-playtest AI rules + engage bound + pressure",
                () => DemoRecipes.M7().WithAiRules(Layer("replay-playtest-s3", DemoRecipes.PlaytestLayer, S3Ai)), DemoRecipes.M7MapBytes),
            ["lanes-s3"] = new Entry("Three-lane map, default AI + engage bound + pressure",
                () => Lanes(Layer("replay-lanes-s3", LanesBake().PackId, S3Ai)), () => LanesBake().WcMapBytes),
            // AI-S4 (PLACEHOLDER values): cover on top of the S3 setups.
            ["playtest-s4"] = new Entry("M7, rwr-m7-playtest AI rules + engage bound + pressure + cover",
                () => DemoRecipes.M7().WithAiRules(Layer("replay-playtest-s4", DemoRecipes.PlaytestLayer, S4Ai)), DemoRecipes.M7MapBytes),
            ["lanes-s4"] = new Entry("Three-lane map, default AI + engage bound + pressure + cover",
                () => Lanes(Layer("replay-lanes-s4", LanesBake().PackId, S4Ai)), () => LanesBake().WcMapBytes),
            // Everything finished in AI so far switched on at once (playtest fields, hold share and attack spread, slots + ring,
            // bound, pressure, cover), with scatter or assault-roles as the placement; and the same with squads (AI-P5a,
            // PLACEHOLDER values).
            ["lanes-allon"] = new Entry("Three-lane map, all finished AI on (scatter)",
                () => Lanes(Layer("replay-lanes-allon", LanesBake().PackId, AllOn(ScatterPart))), () => LanesBake().WcMapBytes, P1RingMm),
            ["lanes-allon-roles"] = new Entry("Three-lane map, all finished AI on (assault-roles)",
                () => Lanes(Layer("replay-lanes-allon-roles", LanesBake().PackId, AllOn(RolesPart))), () => LanesBake().WcMapBytes, P1RingMm),
            ["lanes-allon-squads"] = new Entry("Three-lane map, all finished AI on (scatter) + squads",
                () => Lanes(Layer("replay-lanes-allon-squads", LanesBake().PackId, WithSquads(AllOn(ScatterPart)))), () => LanesBake().WcMapBytes, P1RingMm),
            ["lanes-allon-roles-squads"] = new Entry("Three-lane map, all finished AI on (assault-roles) + squads",
                () => Lanes(Layer("replay-lanes-allon-roles-squads", LanesBake().PackId, WithSquads(AllOn(RolesPart)))), () => LanesBake().WcMapBytes, P1RingMm),
            // ANC-2: ancient-field layer (spear), 60 against 60 on a flat 96 x 64 m field; the same layout as Tests/MapContent/Ancient.
            ["ancient-field"] = new Entry("Ancient field, 60 v 60 with spears", AncientField, AncientFieldMap),
            // STANCE-1: a 1 m wall; squad 2 behind it crouches at tick 60 and stands up at tick 240 (StanceDemo).
            ["stance-wall"] = new Entry("1 m wall, team 2 crouches at 60 and stands at 240", StanceDemo.Recipe, StanceDemo.MapBytes),
        };

        private const string SquadAi = "\"squadMinSize\": 4, \"squadMaxSize\": 9, \"squadSkirmisherPermille\": 300, \"squadRecruitRadiusMm\": 30000, "
            + "\"squadFormTicks\": 15, \"squadSpreadMinMm\": 1500, \"squadSpreadMaxMm\": 6000";
        private static string WithSquads(string ai) => ai.Substring(0, ai.Length - 2) + ", " + SquadAi + " }";
        private const string PlaytestFields = "\"failedSlotTicks\": 90, \"targetMemoryTicks\": 300, \"searchTicks\": 90, \"reactionTicks\": 15, "
            + "\"senseTicks\": 6, \"burstShots\": 3, \"burstPauseTicks\": 30, \"suppressTicks\": 90, \"meleeChaseRange\": 12, "
            + "\"holdSharePermille\": 300, \"attackSpreadPoints\": 3";
        private const string ScatterPart = "\"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"scatter\", \"scatterMm\": 6000";
        private static readonly string RolesPart = P2Ai.Substring(2, P2Ai.Length - 4);
        private static string AllOn(string placement) => "{ " + PlaytestFields + ", " + placement + ", " + S4Ai.Substring(2);
        private const string CoverAi = "\"coverRadiusMm\": 9000, \"coverStepMm\": 1000, \"coverDepthMm\": 2000";
        private static readonly string S4Ai = S3Ai.Substring(0, S3Ai.Length - 2) + ", " + CoverAi + " }";
        private const string S3Ai = "{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000, "
            + "\"pressureDeathRadiusMm\": 3000, \"pressureDeathShock\": 2000, \"pressureNearRadiusMm\": 3000, \"pressureNearShock\": 1000, \"pressureDecayPermille\": 12, "
            + "\"pressureBrokenAt\": 7000, \"pressureRecoverAt\": 1000, \"pressurePinnedAt\": 2000, \"pressureSpreadPermille\": 500, \"pressureBreakSharePermille\": 300 }";
        private const string S1Ai = "{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000 }";
        private const string LanesPath = "Assets/WarCore/Maps/lanes-playtest.map.json";
        private const int P1RingMm = 6000;
        private static string P1Ai(int scatterMm) =>
            "{ \"slotSpacingMm\": 2000, \"slotRingMm\": " + P1RingMm + ", \"placement\": \"scatter\", \"scatterMm\": " + scatterMm + " }";
        private const string P2Ai = "{ \"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"assault-roles\", "
            + "\"frontPermille\": 425, \"flankPermille\": 375, \"rearPermille\": 200, \"frontArcMm\": 14000, \"flankSideMm\": 35000, "
            + "\"rearOutMm\": 12000, \"flankMinMm\": 60000, \"anchorMm\": 6000, \"arriveMm\": 3000 }";
        // The three-lane setups run with the map's own seed (41) unless the command line names another.
        public static int? LanesSeed;
        private const string BakedSeed = "\"seed\": 41";
        private static MapBakeResult LanesBake()
        {
            if (LanesSeed == null) return MapBake.Run(File.ReadAllBytes(LanesPath));
            string text = File.ReadAllText(LanesPath);
            if (text.IndexOf(BakedSeed, StringComparison.Ordinal) < 0) throw new InvalidOperationException("The three-lane map no longer says " + BakedSeed + ".");
            return MapBake.Run(System.Text.Encoding.UTF8.GetBytes(text.Replace(BakedSeed, "\"seed\": " + LanesSeed.Value)));
        }
        // The three-lane map with its pack line swapped in memory (the file is not touched); the seed follows LanesSeed as for "lanes".
        private const string BakedPack = "\"pack\": \"rwr-m6\"";
        private static MapBakeResult LanesBodyBake()
        {
            string text = File.ReadAllText(LanesPath);
            if (text.IndexOf(BakedPack, StringComparison.Ordinal) < 0) throw new InvalidOperationException("The three-lane map no longer says " + BakedPack + ".");
            text = text.Replace(BakedPack, "\"pack\": \"rwr-body\"");
            if (LanesSeed != null)
            {
                if (text.IndexOf(BakedSeed, StringComparison.Ordinal) < 0) throw new InvalidOperationException("The three-lane map no longer says " + BakedSeed + ".");
                text = text.Replace(BakedSeed, "\"seed\": " + LanesSeed.Value);
            }
            return MapBake.Run(System.Text.Encoding.UTF8.GetBytes(text));
        }
        private static WorldRecipe LanesBody()
        {
            MapBakeResult bake = LanesBodyBake();
            return MapRecipe.From(bake, RwrPack.Load(bake.PackId));
        }
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
        private const string AncientDir = "Assets/WarCore/Content/Ancient/Data";
        private sealed class AncientSource : IContentSource
        {
            public bool TryRead(string layerId, out string text, out string location) =>
                new FileContentSource(AncientDir).TryRead(layerId, out text, out location);
        }
        private static byte[] AncientFieldMap()
        {
            var bits = new byte[96 * 64 / 8]; Array.Fill(bits, (byte)255);
            var points = new int[97 * 65];
            var min = new WarCore.Sim.SimVec3(default, default, default);
            var max = new WarCore.Sim.SimVec3(WarCore.Sim.SimFix.FromInt(96), WarCore.Sim.SimFix.FromInt(4), WarCore.Sim.SimFix.FromInt(64));
            return WcMapWriter.Write(new WcMap("ancient-field", "none", min, max, WarCore.Sim.SimFix.FromInt(96), default, 96, 64,
                new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), bits, points, new[] { "ancient.ground" }, new byte[96 * 64]));
        }
        private static WorldRecipe AncientField()
        {
            ContentPack pack = ContentLoader.Load(new AncientSource(), "ancient-field");
            string[] rank = { "ancient.guard", "ancient.guard", "ancient.spearman", "ancient.spearman", "ancient.swordsman", "ancient.swordsman" };
            var units = new UnitSpawn[120];
            for (int i = 0; i < 60; i++)
            {
                units[i] = UnitSpawn.OfType((ulong)i + 1, pack, rank[i % 6], 1, 1, 30000 - 1500 * (i % 6), 22000 + 2000 * (i / 6));
                units[i + 60] = UnitSpawn.OfType((ulong)i + 61, pack, rank[i % 6], 2, 2, 66000 + 1500 * (i % 6), 22000 + 2000 * (i / 6));
            }
            var pts = new[] { 12, 32, 52 }.Select((z, n) => new ControlPointDef(n + 1, WarCore.Sim.SimVec3.Metres(48, 0, z), WarCore.Sim.SimFix.FromInt(4))).ToArray();
            var bases = new[] { new SpawnAnchor(1, WarCore.Sim.SimVec3.Metres(4, 0, 32)), new SpawnAnchor(2, WarCore.Sim.SimVec3.Metres(92, 0, 32)) };
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(pack.BuildCombat())
                .WithMatch(pack.BuildMatch(pts, bases)).WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(pack).WithFacing().WithBlockAi();
        }
    }
}
