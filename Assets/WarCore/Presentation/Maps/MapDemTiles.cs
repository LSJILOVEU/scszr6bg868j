using System.Collections.Generic;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // ED-3c-1: height tiles (.dem.json text) for the editor: the fingerprint that goes into a spec's base, and the credit
    // texts that travel with the tile. A bad tile throws ArgumentException. No file is read here.
    public static class MapDemTiles
    {
        public static string Fingerprint(string demJson) => MapDemTile.Read(demJson).Fingerprint;

        public static IReadOnlyList<string> Credit(string demJson) => MapDemTile.Read(demJson).Credit;
    }
}
