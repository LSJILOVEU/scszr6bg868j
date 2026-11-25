using System;
using System.IO;
using WarCore.Content;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // SHOW-2: the sixth stage, the Swiss valley hamlet (the MAP-E1g map file, no floors) under the content layer
    // "rwr-showcase-hamlet": the open battle's weapons, dash, block, respawn and AI (cover left out: it refuses a height-field
    // map) plus stances, the frag blast and move spread. The map file is read as it is committed; only its pack line is
    // swapped in memory, as the battle stage does with the three-lane map. PLACEHOLDER values throughout.
    public static partial class Showcase
    {
        public const string HamletLayer = "rwr-showcase-hamlet";
        private const string HamletMap = "Assets/WarCore/Maps/emmental-hamlet-160-ground.map.json";
        private const string HamletPack = "\"pack\": \"rwr-field\"";
        // The layer's blast kind for the player's grenade, and the map file's squad of team 1 (every team 1 soldier is in it).
        private const string HamletBlast = "rwr.frag";
        private const int HamletOrderSquad = 1;

        private static MapBakeResult Hamlet()
        {
            string text = File.ReadAllText(ProjectFile(HamletMap));
            if (text.IndexOf(HamletPack, StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("The hamlet map no longer says " + HamletPack + ".");
            return MapBake.Run(System.Text.Encoding.UTF8.GetBytes(text.Replace(HamletPack, "\"pack\": \"" + HamletLayer + "\"")));
        }

        // The map file's own recipe (soldiers, points, bases, seed, crowd pass) with the layer's unit types; the layer is the
        // world's one pack (terrain rules, AI rules). Respawn comes from the layer's match.
        private static WorldRecipe HamletRecipe()
        {
            WorldRecipe recipe = MapRecipe.From(Hamlet(), ContentCatalog.Find().Load(HamletLayer), useUnitTypes: true).WithFacing();
            return recipe.WithPlayer(FirstDeployed(recipe.Units));
        }

        // From team 1's base north up the valley toward point A, firing on the way, a dash, a block, and on toward E.
        private static (long AtTick, PlayerInput Input)[] HamletScript() => new[]
        {
            (1L, new PlayerInput(120, 1000, 102000, 60000)),
            (150L, new PlayerInput(0, 0, 104000, 80000, primary: true)),
            (210L, new PlayerInput(120, 1000, 104000, 80000, dash: true)),
            (230L, new PlayerInput(120, 1000, 104000, 80000)),
            (360L, new PlayerInput(0, 0, 110000, 100000, block: true)),
            (400L, new PlayerInput(0, 0, 110000, 100000, primary: true)),
            (480L, new PlayerInput(300, 1000, 110000, 110000)),
        };

        // For the pictures: team 1 crouches at tick 180 and stands at 420; a grenade bursts at tick 360 between A and E, among
        // team 2's soldiers coming down toward A.
        private static BridgeCommand[] HamletCommands() => new[]
        {
            BridgeCommand.Stance(180, HamletOrderSquad, 1),
            BridgeCommand.Blast(360, 105000, 89000, HamletBlast),
            BridgeCommand.Stance(420, HamletOrderSquad, 0),
        };
    }
}
