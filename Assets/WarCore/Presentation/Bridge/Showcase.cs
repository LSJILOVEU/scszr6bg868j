using System;
using System.Collections.Generic;
using System.IO;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // One playable setup of the showcase: a map, a recipe with a player, and the lines the HUD shows about it.
    public sealed class ShowcaseStage
    {
        private readonly Func<byte[]> map;
        private readonly Func<WorldRecipe> recipe;
        private readonly Func<(long AtTick, PlayerInput Input)[]> script;

        private readonly string paletteLayer;

        public string Id { get; }
        public string Title { get; }
        // What is switched on in this stage, one short line each.
        public IReadOnlyList<string> Shows { get; }
        // Half the height of the picture in metres when the stage starts.
        public float Zoom { get; }

        internal ShowcaseStage(string id, string title, float zoom, string paletteLayer, string[] shows, Func<byte[]> map, Func<WorldRecipe> recipe,
            Func<(long AtTick, PlayerInput Input)[]> script)
        {
            Id = id; Title = title; Zoom = zoom; Shows = Array.AsReadOnly(shows); this.map = map; this.recipe = recipe; this.script = script;
            this.paletteLayer = paletteLayer;
        }

        // The colours of the cartridge that holds the stage's layer (its palette.json, or the built-in ones), for this map.
        public ShowcaseColors Colors(ViewMap drawn)
        {
            if (drawn == null) throw new ArgumentNullException(nameof(drawn));
            MapPalette palette = MapPalette.ForLayer(ContentCatalog.Find(), paletteLayer);
            var surfaces = new int[drawn.SurfaceCount];
            for (int i = 0; i < surfaces.Length; i++) surfaces[i] = palette.GroundColor(drawn.SurfaceName(i), out _);
            var teams = new int[palette.Team.Count];
            for (int i = 0; i < teams.Length; i++) teams[i] = palette.Team[i];
            return new ShowcaseColors(teams, palette.Neutral, palette.NoGround, palette.StructSide, palette.StructTop, surfaces);
        }

        public byte[] MapBytes() => map();
        public WorldRecipe Recipe() => recipe();
        // A new session at tick 0. Every call builds the same world.
        public SimSession Start() => SimSession.Create(recipe(), map());
        // The scripted player for pictures taken without a person at the keys. A new array each call.
        public (long AtTick, PlayerInput Input)[] Script() => script();
    }

    // Colours as 0xRRGGBB. Team[k] is team k + 1; Surface[i] is the map's surface i (empty for a map without a surface table).
    public sealed class ShowcaseColors
    {
        public IReadOnlyList<int> Team { get; }
        public int Neutral { get; }
        public int Ground { get; }
        public int Wall { get; }
        public int Floor { get; }
        public IReadOnlyList<int> Surface { get; }

        internal ShowcaseColors(int[] team, int neutral, int ground, int wall, int floor, int[] surface)
        {
            Team = Array.AsReadOnly(team); Neutral = neutral; Ground = ground; Wall = wall; Floor = floor; Surface = Array.AsReadOnly(surface);
        }
    }

    // SHOW-1: every finished feature switched on at once, in a few stages a person can play. Nothing here is a rule of the
    // game: each stage only combines maps, content layers and recipe switches that already exist. PLACEHOLDER values throughout
    // (they come from the content layers "rwr-showcase", "rwr-showcase-bullets", "rwr-ai-playtest" and "ancient-field").
    public static class Showcase
    {
        public const string BattleLayer = "rwr-showcase", BulletsLayer = "rwr-showcase-bullets";
        public const string HouseAiLayer = "rwr-ai-playtest", TerrainLayer = "rwr-ai-playtest", AncientLayer = "ancient-field";
        private const string LanesMap = "Assets/WarCore/Maps/lanes-playtest.map.json";
        private const string LanesPack = "\"pack\": \"rwr-m6\"";
        private const int AncientWidth = 96, AncientDepth = 64;
        // The speed the map-file route gives a soldier whose unit type names none (MapRecipe).
        private const long SoldierSpeedMmPerSec = 6000;

        public static IReadOnlyList<ShowcaseStage> Stages { get; } = Array.AsReadOnly(new[]
        {
            new ShowcaseStage("battle", "Open battle", 22f, BattleLayer, new[]
            {
                "Three-lane map, 7 capture points, 100 + 50 reserves a side",
                "AI: squads, spread attack, bounding, pressure (pinned / broken), cover, bursts, memory",
                "Player: rifle, dash, block, respawn into a reserve",
                "Rifles hit on the tick they fire (tracer lines)",
            }, () => Lanes(BattleLayer).WcMapBytes, () => Battle(BattleLayer), BattleScript),
            new ShowcaseStage("bullets", "Flying bullets", 22f, BulletsLayer, new[]
            {
                "The open battle with rifles that fire projectiles (150 m/s)",
                "A bullet flies tick by tick and stops on the first body, wall or ground",
                "Known gap: the pressure system does not react to projectiles, so nobody gets pinned here",
            }, () => Lanes(BulletsLayer).WcMapBytes, () => Battle(BulletsLayer), BattleScript),
            new ShowcaseStage("house", "House assault", 12f, HouseAiLayer, new[]
            {
                "Two-storey house with a stair; the point upstairs is reached by the stair only; 8 a side and 12 in reserve",
                "AI climbs, searches, suppresses and chases by floor",
                "Walls between the camera and the player fade; covered team mates show as silhouettes",
            }, () => House().WcMapBytes, HouseRecipe, HouseScript),
            new ShowcaseStage("terrain", "Hills and mud", 16f, TerrainLayer, new[]
            {
                "Height-field terrain: ridge, cliff band, plateau; road is fast, mud is slow",
                "10 a side on the field and 30 in reserve; AI with memory, search and bursts",
            }, TerrainDemo.MapBytes, TerrainRecipe, () => TerrainPlayerDemo.Script()),
            new ShowcaseStage("melee", "Ancient melee", 16f, AncientLayer, new[]
            {
                "Second cartridge: swords, spears and shields, 60 a side and 30 in reserve, no guns",
                "Player: a guard (sword and shield); AI raises its shield against swings",
            }, AncientMap, AncientRecipe, AncientScript),
        });

        // Not in the code base yet. The HUD lists these so nobody mistakes a gap for a bug.
        public static IReadOnlyList<string> Missing { get; } = Array.AsReadOnly(new[]
        {
            "Grenades and explosions (the throw key reaches the simulation, nothing consumes it)",
            "Stances: crouch and prone",
            "Climbing over walls and through windows",
            "AI reserves with a ticket count; an AI commander that splits forces",
            "Vehicles, water, day and night",
            "Melee swings and hits have no view data (only the deaths show)",
            "Health bars, sound, real art",
        });

        // The first deployed unit of team 1 in key order: the unit the player starts in.
        private static ulong FirstDeployed(ReadOnlySpan<UnitSpawn> units)
        {
            foreach (UnitSpawn unit in units)
                if (unit.Team == 1 && !unit.Reserve) return unit.Key;
            throw new InvalidOperationException("The showcase needs a deployed team 1 unit.");
        }

        private static string ProjectFile(string relative)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (; dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("Showcase file " + relative + " not found (searched upwards from " + Directory.GetCurrentDirectory() + ").");
        }

        // The three-lane map with its pack line swapped in memory (the file is not touched).
        private static MapBakeResult Lanes(string layer)
        {
            string text = File.ReadAllText(ProjectFile(LanesMap));
            if (text.IndexOf(LanesPack, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("The three-lane map no longer says " + LanesPack + ".");
            return MapBake.Run(System.Text.Encoding.UTF8.GetBytes(text.Replace(LanesPack, "\"pack\": \"" + layer + "\"")));
        }

        // The three-lane battle under one layer: its unit types (weapon, speed, dash, block), match rules (respawn included)
        // and AI rules. Only team 1's unit type carries a block, and there is no block AI, so only the player raises one.
        private static WorldRecipe Battle(string layer)
        {
            WorldRecipe recipe = MapRecipe.From(Lanes(layer), ContentCatalog.Find().Load(layer), useUnitTypes: true)
                .WithAiRules(ContentCatalog.Find().Load(layer)).WithFacing();
            return recipe.WithPlayer(FirstDeployed(recipe.Units));
        }

        // Forward along the middle lane, fire a burst, dash, hold a block, fire again.
        private static (long AtTick, PlayerInput Input)[] BattleScript() => new[]
        {
            (1L, new PlayerInput(1000, 0, 100000, 70000)),
            (120L, new PlayerInput(0, 0, 100000, 70000, primary: true)),
            (180L, new PlayerInput(1000, 0, 100000, 70000, dash: true)),
            (200L, new PlayerInput(1000, 0, 100000, 70000)),
            (300L, new PlayerInput(0, 0, 100000, 70000, block: true)),
            (360L, new PlayerInput(0, 0, 100000, 70000, primary: true)),
            (480L, new PlayerInput(1000, 0, 100000, 70000)),
        };

        private static MapBakeResult House() => MapBake.Run(PlateAssaultMap.DescBytes());

        // The assault demo house under the playtest AI rules (the setup of the ML-11b-1 battle tests) with HouseReserves more
        // soldiers a side waiting in reserve, team 1's first soldier as the player and respawn on. The description's own
        // units keep their keys; the reserves take the keys after them.
        private const int HouseReserves = 12, RespawnTicks = 150, RespawnGuardTicks = 90;

        private static WorldRecipe HouseRecipe()
        {
            MapBakeResult baked = House();
            ContentCatalog catalog = ContentCatalog.Find();
            ContentPack pack = catalog.Load(baked.PackId);
            MapSetup setup = baked.Setup;
            var units = new List<UnitSpawn>();
            ulong last = 0;
            foreach (MapSoldier s in setup.Soldiers)
            {
                units.Add(new UnitSpawn(s.Key, 0, s.Squad, s.XMm, s.ZMm, SoldierSpeedMmPerSec, s.Team, pack.WeaponOfUnitType(s.Type), s.Reserve,
                    pack.MeleeOfUnitType(s.Type), layer: s.Layer));
                if (s.Key > last) last = s.Key;
            }
            SpawnAnchor[] bases = setup.Bases;
            for (int i = 0; i < 2 * HouseReserves; i++)
            {
                int team = i % 2 + 1;
                SimVec3 at = bases[team - 1].Point;
                units.Add(new UnitSpawn(++last, 0, team, at.X.Raw * 1000 / SimFix.Scale, at.Z.Raw * 1000 / SimFix.Scale, SoldierSpeedMmPerSec, team,
                    pack.WeaponOfUnitType(team == 1 ? WarCore.Content.Rwr.RwrPack.SoldierTeam1 : WarCore.Content.Rwr.RwrPack.SoldierTeam2), true));
            }
            WorldRecipe recipe = new WorldRecipe(setup.Seed, MoverKind.InterimCrowd, units.ToArray(), Array.Empty<BridgeCommand>())
                .WithCombat(pack.BuildCombat()).WithMatch(pack.BuildMatch(setup.ControlPoints, bases)).WithPlatesPreview()
                .WithCrowdPass(CrowdPassKind.TeamIdle).WithAiRules(catalog.Load(HouseAiLayer));
            recipe = recipe.WithPlayer(FirstDeployed(recipe.Units));
            return DemoRecipes.WithRespawnGuard(DemoRecipes.WithRespawn(recipe, RespawnTicks), RespawnGuardTicks);
        }

        // East to the west doorway, in, and fire along the ground floor.
        private static (long AtTick, PlayerInput Input)[] HouseScript() => new[]
        {
            (1L, new PlayerInput(1000, 600, 14500, 11500)),
            (60L, new PlayerInput(1000, 0, 24000, 11500)),
            (150L, new PlayerInput(0, 0, 24000, 9000, primary: true)),
            (300L, new PlayerInput(0, -1000, 24000, 9000)),
            (330L, new PlayerInput(0, 0, 24000, 7000, primary: true)),
        };

        // The demo terrain match (the layout of DemoRecipes.TerrainMatch: ten a side round the team anchor on the road) with
        // TerrainReserves more a side in reserve, the playtest AI rules on the terrain layer, the player in team 1's first
        // unit and respawn on.
        private const int TerrainReserves = 30;

        private static WorldRecipe TerrainRecipe()
        {
            ContentCatalog catalog = ContentCatalog.Find();
            ContentPack content = catalog.Load(WarCore.Content.Rwr.RwrPack.M6Layer);
            int perTeam = 10 + TerrainReserves;
            var units = new UnitSpawn[2 * perTeam];
            for (int i = 0; i < units.Length; i++)
            {
                int team = i < perTeam ? 1 : 2, rank = i % perTeam;
                var offset = WorldBuilder.FormationOffsetMm(rank % 10, 10);
                units[i] = new UnitSpawn((ulong)i + 1, 0, team, (team == 1 ? 3500 : 44500) + offset.X, 16500 + offset.Z, SoldierSpeedMmPerSec, team,
                    content.WeaponOfUnitType(team == 1 ? WarCore.Content.Rwr.RwrPack.SoldierTeam1 : WarCore.Content.Rwr.RwrPack.SoldierTeam2), rank >= 10);
            }
            WorldRecipe recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(content.BuildCombat()).WithMatch(TerrainDemoMap.BuildMatch(content))
                .WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(catalog.Load(TerrainLayer));
            recipe = recipe.WithPlayer(FirstDeployed(recipe.Units));
            return DemoRecipes.WithRespawnGuard(DemoRecipes.WithRespawn(recipe, RespawnTicks), RespawnGuardTicks);
        }

        // A flat 96 x 64 m field on the cartridge's one surface.
        private static byte[] AncientMap()
        {
            var bits = new byte[AncientWidth * AncientDepth / 8];
            for (int i = 0; i < bits.Length; i++) bits[i] = 255;
            var max = new SimVec3(SimFix.FromInt(AncientWidth), SimFix.FromInt(4), SimFix.FromInt(AncientDepth));
            return WcMapWriter.Write(new WcMap("showcase-ancient-field", "none", default, max, SimFix.FromInt(AncientWidth), default,
                AncientWidth, AncientDepth, new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), bits,
                new int[(AncientWidth + 1) * (AncientDepth + 1)], new[] { "ancient.ground" }, new byte[AncientWidth * AncientDepth]));
        }

        // 60 against 60 in six ranks (guards in front, then spears, then swords) with AncientReserves more a side in reserve,
        // three points down the middle, facing and block AI on. The player is team 1's first guard; the cartridge has no
        // respawn, so a dead player's control passes to a living team mate.
        private const int AncientReserves = 30;

        private static WorldRecipe AncientRecipe()
        {
            ContentPack pack = ContentCatalog.Find().Load(AncientLayer);
            string[] rank = { "ancient.guard", "ancient.guard", "ancient.spearman", "ancient.spearman", "ancient.swordsman", "ancient.swordsman" };
            int perTeam = 60 + AncientReserves;
            var units = new UnitSpawn[2 * perTeam];
            for (int i = 0; i < perTeam; i++)
            {
                int column = i % 6, row = i / 6 % 10;
                bool reserve = i >= 60;
                units[i] = UnitSpawn.OfType((ulong)i + 1, pack, rank[column], 1, 1, 30000 - 1500 * column, 22000 + 2000 * row, reserve);
                units[i + perTeam] = UnitSpawn.OfType((ulong)(i + perTeam) + 1, pack, rank[column], 2, 2, 66000 + 1500 * column, 22000 + 2000 * row, reserve);
            }
            var points = new ControlPointDef[3];
            for (int n = 0; n < points.Length; n++)
                points[n] = new ControlPointDef(n + 1, SimVec3.Metres(48, 0, 12 + 20 * n), SimFix.FromInt(4));
            var bases = new[] { new SpawnAnchor(1, SimVec3.Metres(4, 0, 32)), new SpawnAnchor(2, SimVec3.Metres(92, 0, 32)) };
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(pack.BuildCombat())
                .WithMatch(pack.BuildMatch(points, bases)).WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(pack).WithFacing().WithBlockAi()
                .WithPlayer(1);
        }

        // Walk at the enemy line, swing, hold the shield, swing again.
        private static (long AtTick, PlayerInput Input)[] AncientScript() => new[]
        {
            (1L, new PlayerInput(1000, 0, 66000, 22000)),
            (150L, new PlayerInput(0, 0, 66000, 22000, primary: true)),
            (240L, new PlayerInput(0, 0, 66000, 22000, block: true)),
            (300L, new PlayerInput(0, 0, 66000, 22000, primary: true)),
        };
    }
}
