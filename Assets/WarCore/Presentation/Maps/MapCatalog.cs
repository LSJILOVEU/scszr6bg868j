using System;
using System.Collections.Generic;
using WarCore.Content;

namespace WarCore.Presentation
{
    // The editor's view of the content cartridges, in plain strings so the Unity assembly can use it without seeing Content.
    // Every call looks the directories up again (ContentCatalog.Find), so a changed environment is never answered from a cache.
    public static class MapCatalog
    {
        public static IReadOnlyList<string> Cartridges() => Copy(ContentCatalog.Find().Cartridges);

        public static IReadOnlyList<string> Layers(string cartridge) => Copy(ContentCatalog.Find().Layers(cartridge));

        // The layer a new map of the cartridge starts with, or null.
        public static string DefaultLayer(string cartridge) => ContentCatalog.Find().DefaultLayer(cartridge);

        public static bool LayerHasTerrain(string layerId) => ContentCatalog.Find().Load(layerId).Terrain != null;

        public static bool LayerHasMatch(string layerId) => ContentCatalog.Find().Load(layerId).HasMatch;

        // Surface ids of the layer's terrain rules in ordinal order; empty when the layer has none.
        public static IReadOnlyList<string> SurfaceNames(string layerId) => SurfaceNames(ContentCatalog.Find().Load(layerId));

        // Unit type ids in the pack's order.
        public static IReadOnlyList<string> UnitTypeNames(string layerId) => UnitTypeNames(ContentCatalog.Find().Load(layerId));

        internal static IReadOnlyList<string> SurfaceNames(ContentPack pack)
        {
            TerrainRules rules = pack.Terrain;
            if (rules == null) return Array.AsReadOnly(Array.Empty<string>());
            var names = new string[rules.SurfaceCount];
            for (int i = 0; i < names.Length; i++) names[i] = rules.Surface(i).Id;
            return Array.AsReadOnly(names);
        }

        internal static IReadOnlyList<string> UnitTypeNames(ContentPack pack)
        {
            var names = new string[pack.UnitTypeCount];
            for (int i = 0; i < names.Length; i++) names[i] = pack.UnitTypeId(i);
            return Array.AsReadOnly(names);
        }

        private static IReadOnlyList<string> Copy(IReadOnlyList<string> items)
        {
            var copy = new string[items.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = items[i];
            return Array.AsReadOnly(copy);
        }
    }
}
