using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // MAP-BL0: the committed map file, baked once, and the worlds the tests start from it.
    internal static class WardWorld
    {
        internal const string MapPath = "Assets/WarCore/Maps/walled-ward-160.map.json";
        internal static readonly ContentPack Pack = RwrPack.Load("rwr-showcase");

        private static byte[] file;
        private static MapBakeResult baked;

        internal static byte[] File => file ?? (file = System.IO.File.ReadAllBytes(MapPath));
        internal static MapBakeResult Baked => baked ?? (baked = MapBake.Run(File));

        internal static M6World World(WorldRecipe recipe, MapBakeResult from = null) => new M6World(recipe, (from ?? Baked).WcMapBytes);
    }
}
