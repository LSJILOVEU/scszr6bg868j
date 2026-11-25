using System;
using System.Collections.Generic;
using System.IO;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // One playable setup of the showcase: a map file, the one content layer it is played under, a scripted player for the
    // pictures and the lines the HUD shows about it.
    public sealed class ShowcaseStage
    {
        private readonly Func<byte[]> map;
        private readonly Func<WorldRecipe> recipe;
        private readonly Func<(long AtTick, PlayerInput Input)[]> script;
        private readonly Func<BridgeCommand[]> commands;

        public string Id { get; }
        public string Title { get; }
        // What is switched on in this stage, one short line each.
        public IReadOnlyList<string> Shows { get; }
        // Half the height of the picture in metres when the stage starts.
        public float Zoom { get; }
        // SHOW-2: the blast kind the player's grenade sets off (G), or null where the grenade is the drawn-only preview.
        public string GrenadeBlast { get; }
        // SHOW-2: the squad a stance order (Y) goes to, or 0 where the stage takes no stance orders.
        public int OrderSquad { get; }
        // The one content layer the stage runs on: the pack its world reads, the cartridge its colours come from and, with a
        // "pick" section (AIM-2), the mouse's reach.
        public string Layer { get; }
        // The map file the stage is played on, from the root of the project: the ground, the soldiers and the reserves, the
        // points, the bases, the seed and the crowd pass. It is read as it is committed. It may name another layer than the
        // stage's (two stages play the three-lane map); the stage plays it under its own.
        public string Map { get; }

        // A stage of the showcase (RC1-K4): its world is its map file under its layer and nothing else. No stage has
        // recipe code of its own.
        internal ShowcaseStage(string id, string title, float zoom, string layer, string map, string[] shows,
            Func<(long AtTick, PlayerInput Input)[]> script, Func<BridgeCommand[]> commands = null, string grenadeBlast = null, int orderSquad = 0)
        {
            Id = id; Title = title; Zoom = zoom; Layer = layer; Map = map; Shows = Array.AsReadOnly(shows); this.script = script;
            this.commands = commands ?? (() => Array.Empty<BridgeCommand>());
            GrenadeBlast = grenadeBlast; OrderSquad = orderSquad;
            this.map = () => Bake().WcMapBytes;
            recipe = () => World(Bake());
        }

        // Not a stage of the showcase: a world an old demo builds by hand, for the showcase's scene to draw (the scale
        // lineup). It has no map file. It goes when the old demos go.
        internal ShowcaseStage(string id, string title, float zoom, string layer, string[] shows, Func<byte[]> map, Func<WorldRecipe> recipe,
            Func<(long AtTick, PlayerInput Input)[]> script) : this(id, title, zoom, layer, (string)null, shows, script)
        {
            this.map = map; this.recipe = recipe;
        }

        // The colours of the cartridge that holds the stage's layer (its palette.json, or the built-in ones), for this map.
        public ShowcaseColors Colors(ViewMap drawn)
        {
            if (drawn == null) throw new ArgumentNullException(nameof(drawn));
            MapPalette palette = MapPalette.ForLayer(ContentCatalog.Find(), Layer);
            var surfaces = new int[drawn.SurfaceCount];
            for (int i = 0; i < surfaces.Length; i++) surfaces[i] = palette.GroundColor(drawn.SurfaceName(i), out _);
            var teams = new int[palette.Team.Count];
            for (int i = 0; i < teams.Length; i++) teams[i] = palette.Team[i];
            return new ShowcaseColors(teams, palette.Neutral, palette.NoGround, palette.StructSide, palette.StructTop, surfaces);
        }

        // AIM-2: the mouse's reach from the layer's pick section, or null when it has none (the old pick). Read it once per
        // stage, not every frame.
        public PickReach Reach() => PickReach.Of(ContentCatalog.Find().Load(Layer));

        // RC2-1: how many soldiers the squad the player leads takes under the stage's layer (ai.ledSize), or 0 where the
        // layer has no led squad. Read it once per stage, not every frame.
        public int LedSize() => ContentCatalog.Find().Load(Layer).Section<AiRules>()?.Led?.Size ?? 0;

        public byte[] MapBytes() => map();
        public WorldRecipe Recipe() => recipe();
        // A new session at tick 0. Every call builds the same world.
        public SimSession Start() => SimSession.Create(recipe(), map());

        // The stage's map file, baked for the stage's layer.
        private MapBakeResult Bake() => MapBake.Run(File.ReadAllBytes(Showcase.ProjectFile(Map)), Layer);

        // The one way a stage becomes a world: the map file's own recipe (MapRecipe) with the unit types of the layer
        // (weapon, melee, speed, dash, block), and the player in team 1's first deployed unit. Everything that is switched
        // on comes from the layer the world reads: match rules with respawn, AI rules, facing, the block AI.
        private static WorldRecipe World(MapBakeResult baked)
        {
            WorldRecipe recipe = MapRecipe.From(baked, ContentCatalog.Find().Load(baked.PackId), useUnitTypes: true);
            return recipe.WithPlayer(Showcase.FirstDeployed(recipe.Units));
        }

        // The scripted player for pictures taken without a person at the keys. A new array each call.
        public (long AtTick, PlayerInput Input)[] Script() => script();
        // SHOW-2: the scripted orders (grenades, stance orders) queued with the script for pictures taken without a person. A
        // new array each call; empty for most stages.
        public BridgeCommand[] Commands() => commands();

        // SHOW-2: how tall each stance level of the stage's combat pack draws, as a share of the standing body (level 0 is 1).
        // Just [1] for a pack without stances.
        public float[] StanceScales()
        {
            WarCore.Combat.CombatPack combat = recipe().Combat;
            if (combat == null || !combat.HasStances || !combat.Body.HasValue) return new[] { 1f };
            ReadOnlySpan<WarCore.Combat.StanceDef> stances = combat.Stances;
            var scales = new float[stances.Length + 1];
            scales[0] = 1f;
            for (int i = 0; i < stances.Length; i++) scales[i + 1] = (float)stances[i].Body.Height.Raw / combat.Body.Value.Height.Raw;
            return scales;
        }
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

    // SHOW-1: every finished feature switched on at once, in a few stages a person can play (SHOW-2 added the sixth,
    // Showcase.Hamlet.cs). Nothing here is a rule of the game, and nothing here builds a world (RC1-K4): a stage names a map
    // file and one content layer. PLACEHOLDER values throughout (they come from the map files and the content layers).
    public static partial class Showcase
    {
        public const string BattleLayer = "rwr-showcase", BulletsLayer = "rwr-showcase-bullets";
        public const string HouseLayer = "rwr-showcase-house", TerrainLayer = "rwr-showcase-terrain", AncientLayer = "ancient-field";
        // AIM-2: the house stage fights with the body and the mouse reach; the legacy house (LegacyHouse) has neither.
        internal const string LegacyHouseLayer = "rwr-house";
        // The three-lane map (it names rwr-m6; the open battle and the bullets stage play it under their own layers) and the
        // maps of the house, the hills and the melee field, which name their stage's layer.
        private const string LanesMap = "Assets/WarCore/Maps/lanes-playtest.map.json";
        private const string HouseMap = "Assets/WarCore/Maps/showcase-house.map.json";
        private const string TerrainMap = "Assets/WarCore/Maps/showcase-terrain.map.json";
        private const string FieldMap = "Assets/WarCore/Maps/showcase-field.map.json";

        public static IReadOnlyList<ShowcaseStage> Stages { get; } = Array.AsReadOnly(new[]
        {
            new ShowcaseStage("battle", "Open battle", 22f, BattleLayer, LanesMap, new[]
            {
                "Three-lane map, 7 capture points, 100 + 50 reserves a side",
                "AI: squads, spread attack, bounding, pressure (pinned / broken), cover, bursts, memory",
                "Squads differ in temper (rash, normal or steady) and fall back to regroup when under half strength",
                "A reserve steps in 3 s after each loss, but not at a point the enemy contests",
                "Player: rifle, dash, block, respawn into a reserve",
                "Rifles hit on the tick they fire (tracer lines)",
                "A tap is accurate, a held trigger spreads; pinned and broken soldiers shoot wider (you too)",
            }, BattleScript),
            new ShowcaseStage("bullets", "Flying bullets", 22f, BulletsLayer, LanesMap, new[]
            {
                "The open battle with rifles that fire projectiles (150 m/s)",
                "A bullet flies tick by tick and stops on the first body, wall or ground",
                "Attackers split into a front group, flankers and a rear group (stage 1 scatters them round the point)",
                "Known gap: the pressure system does not react to projectiles, so nobody gets pinned here",
            }, BattleScript),
            HouseStage(HouseLayer),
            new ShowcaseStage("terrain", "Hills and mud", 16f, TerrainLayer, TerrainMap, new[]
            {
                "Height-field terrain: ridge, cliff band, plateau; road is fast, mud is slow",
                "Uphill is slower the steeper it is (down to half speed); a steep way down is a little slower too",
                "10 a side on the field and 30 in reserve; AI with memory, search and bursts",
            }, () => TerrainPlayerDemo.Script()),
            new ShowcaseStage("melee", "Ancient melee", 16f, AncientLayer, FieldMap, new[]
            {
                "Second cartridge: swords, spears and shields, 60 a side and 30 in reserve, no guns",
                "Player: a guard (sword and shield); AI raises its shield against swings",
                "Nerve: blows and deaths nearby pin or break a soldier; pinned he holds, broken he falls back",
            }, AncientScript),
            new ShowcaseStage("hamlet", "Swiss valley hamlet", 18f, HamletLayer, HamletMap, new[]
            {
                "Real terrain: a 160 m crop of the Emmental valley with a hamlet; 62 walls, 5 points, 40 + 20 reserves a side",
                "AI: squads, spread attack, bounding, pressure (pinned / broken), bursts, memory, search, suppression",
                "Squads differ in temper and regroup; a reserve steps in 3 s after each loss; AI choices favour neither side",
                "Player: rifle, dash, block, respawn; aim spreads while moving",
                "Shots also go high or low (less from a crouch or prone); uphill is slower; pinned and broken soldiers shoot wider",
                "G throws a real grenade: it bursts after a short fuse and hurts everyone inside the ring it draws, walls shield",
                "Y orders your team's soldiers to stand, crouch or go prone (lower is slower, steadier and harder to see)",
                "Known gap: the AI's cover rule refuses a height-field map, so nobody seeks cover here",
            }, HamletScript, HamletCommands, HamletBlast, HamletOrderSquad),
        });

        // Not in the code base yet. The HUD lists these so nobody mistakes a gap for a bug.
        public static IReadOnlyList<string> Missing { get; } = Array.AsReadOnly(new[]
        {
            "AI grenades; the player's grenade is real in stage 6 only (it bursts after a fixed fuse and does not fly in the simulation)",
            "Your own crouch and prone (stances work in stage 6, but only as orders to your team); AI that picks its own stance",
            "Cover seeking on height-field maps (the AI's cover rule refuses them)",
            "Climbing over walls and through windows",
            "An AI commander that splits forces",
            "Vehicles, water, day and night",
            "Melee swings and hits have no view data (only the deaths show)",
            "Health bars, sound, real art",
        });

        // The first deployed unit of team 1 in key order: the unit the player starts in.
        internal static ulong FirstDeployed(ReadOnlySpan<UnitSpawn> units)
        {
            foreach (UnitSpawn unit in units)
                if (unit.Team == 1 && !unit.Reserve) return unit.Key;
            throw new InvalidOperationException("The showcase needs a deployed team 1 unit.");
        }

        internal static string ProjectFile(string relative)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (; dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException("Showcase file " + relative + " not found (searched upwards from " + Directory.GetCurrentDirectory() + ").");
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

        // AIM-2: the house stage as it was before AIM-2 (no body, no reach), kept out of Stages for the tests that pin the old
        // pick on a stage without a body.
        internal static ShowcaseStage LegacyHouse { get; } = HouseStage(LegacyHouseLayer);

        internal static ShowcaseStage HouseStage(string layer) => new ShowcaseStage("house", "House assault", 12f, layer, HouseMap, new[]
            {
                "Two-storey house with a stair; the point upstairs is reached by the stair only; 8 a side and 12 in reserve",
                "AI climbs, searches, suppresses and chases by floor",
                "Walls between the camera and the player fade; covered team mates show as silhouettes",
            }, HouseScript);

        // East to the west doorway, in, and fire along the ground floor.
        private static (long AtTick, PlayerInput Input)[] HouseScript() => new[]
        {
            (1L, new PlayerInput(1000, 600, 14500, 11500)),
            (60L, new PlayerInput(1000, 0, 24000, 11500)),
            (150L, new PlayerInput(0, 0, 24000, 9000, primary: true)),
            (300L, new PlayerInput(0, -1000, 24000, 9000)),
            (330L, new PlayerInput(0, 0, 24000, 7000, primary: true)),
        };

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
