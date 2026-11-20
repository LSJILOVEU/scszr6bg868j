using System;
using WarCore.Content;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // Description bytes -> the bake and the recipe of a world that runs it, the same way MapDocument.Start makes one: the content
    // layer the description names is found in the cartridges (ContentCatalog.Find). For tools that run a map file headless.
    public sealed class MapRunSetup
    {
        public MapBakeResult Bake { get; }
        public WorldRecipe Recipe { get; }

        private MapRunSetup(MapBakeResult bake, WorldRecipe recipe) { Bake = bake; Recipe = recipe; }

        // A description the bake refuses throws InvalidDataException (MAPDESC_*); a layer no cartridge holds throws ContentException.
        public static MapRunSetup From(byte[] descBytes)
        {
            if (descBytes == null) throw new ArgumentNullException(nameof(descBytes));
            MapBakeResult bake = MapBake.Run(descBytes);
            ContentPack pack = ContentCatalog.Find().Load(bake.PackId);
            return new MapRunSetup(bake, MapRecipe.From(bake, pack));
        }
    }
}
