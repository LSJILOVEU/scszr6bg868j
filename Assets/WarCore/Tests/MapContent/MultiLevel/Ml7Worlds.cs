using System;
using System.Collections;
using System.Reflection;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // ML-7 fixtures. Map origin (0, 0), 1 m cells, flat ground at height 0.
    //   Plate 1 (layer 1): flat on cells x 6..9, z 1..3, top 3 m, 0.5 m thick (its bottom, 2.5 m, leaves the ground walkable);
    //     cutout on cell (8, 2); a wall based at 3 m on cell (9, 3) makes that cell solid but not walkable (the wall is on every
    //     variant of the map, plates or not; it does not touch the ground's walking band).
    //   Plate 2 (layer 2): sloped along x on cells x 1..3, z 3..4, top 0.5 m at x = 1 rising to 2 m at x = 4 (0.5 m a cell).
    internal static class Ml7Worlds
    {
        internal const long M = SimFix.Scale;

        internal static MapDesc Desc(bool plates = true, bool slope = true, int width = 12, int depth = 6)
        {
            MapPlate[] list = !plates ? new MapPlate[0] : slope
                ? new[] { Ml4Maps.Flat(1, 6, 1, 4, 3, 3 * M), Ml4Maps.Slope(2, 1, 3, 3, 2, "x", M / 2, 2 * M) }
                : new[] { Ml4Maps.Flat(1, 6, 1, 4, 3, 3 * M) };
            MapCutout[] cutouts = plates ? new[] { Ml4Maps.Cut(1, 1, 8, 2, 1, 1) } : null;
            MapWall[] walls = { Ml4Maps.Wall(1, 9, 3, 1, 1, 2 * M, 3 * M) };
            return Ml4Maps.Make(0, width, depth, null, list, cutouts, walls);
        }

        internal static byte[] Bytes(MapDesc d) => MapBake.Run(Ml4Maps.Bytes(d)).WcMapBytes;
        internal static readonly byte[] Plates = Bytes(Desc());
        internal static readonly byte[] HighOnly = Bytes(Desc(slope: false));
        internal static readonly byte[] NoPlates = Bytes(Desc(plates: false));

        internal static UnitSpawn Unit(ulong key, int squad, long xMm, long zMm, int layer = 0, bool reserve = false) =>
            new UnitSpawn(key, 0, squad, xMm, zMm, 6000, reserve: reserve, layer: layer);

        internal static WorldRecipe Recipe(UnitSpawn[] units, BridgeCommand[] script = null, MoverKind mover = MoverKind.InterimCrowd, bool preview = true)
        {
            var recipe = new WorldRecipe(41, mover, units, script ?? Array.Empty<BridgeCommand>());
            return preview ? recipe.WithPlatesPreview() : recipe;
        }

        internal static SimSession World(byte[] map, UnitSpawn[] units, BridgeCommand[] script = null, bool preview = true) =>
            SimSession.Create(Recipe(units, script, preview: preview), map);

        // An 80 x 40 arena for the battle helpers (RespawnWorld): plate 1 flat on cells x 40..43, z 30..33, top 3 m, cutout (42, 31).
        internal static byte[] Arena(bool plates = true) => Bytes(Ml4Maps.Make(0, 80, 40, null,
            plates ? new[] { Ml4Maps.Flat(1, 40, 30, 4, 4, 3 * M) } : new MapPlate[0], plates ? new[] { Ml4Maps.Cut(1, 1, 42, 31, 1, 1) } : null));

        internal static VerticalState Vertical(SimSession s, ulong key) => M3TestWorld.Component<VerticalState>(s, key);

        internal static InputFrame Input(SimSession s)
        {
            Simulation sim = M3TestWorld.Sim(s);
            object stable = typeof(Simulation).GetField("stableInput", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sim);
            return ((StableInputFrame)stable).Payload;
        }

        internal static T Field<T>(object owner, string name) =>
            (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner);
    }

    // An option from outside the builder that copies the preview's id.
    internal sealed class ForgedPreview : IWorldOption
    {
        public string OptionId => "world.plates-preview";
    }
}
