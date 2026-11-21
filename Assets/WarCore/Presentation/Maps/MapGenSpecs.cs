using System;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // ED-3a: terrain generation specs as text, for the editor (saving and loading the .gen.json file is not done here).
    public static class MapGenSpecs
    {
        // Canonical text of a spec (MapGenSpec.Read then Write); a bad spec throws ArgumentException.
        public static string Normalize(string genJson) => MapGenSpec.Read(genJson).Write();

        // The starting spec of a document: its size and layer, the seed, baseMm 0 when it has heights, ground the first name
        // of its surface table when it has surfaces, and no stamps.
        public static string ForDocument(MapDocument doc, ulong seed)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            MapDesc d = doc.Desc;
            return new MapGenSpec(d.Size.X, d.Size.Z, d.Pack, seed, d.Heights != null ? 0 : (int?)null,
                d.Surfaces != null ? d.Surfaces.Names[0] : null).Write();
        }
    }
}
