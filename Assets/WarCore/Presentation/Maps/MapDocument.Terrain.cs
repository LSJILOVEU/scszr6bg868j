using System;
using System.Collections.Generic;
using WarCore.Content;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // ED-3a: the terrain stamp. WithTerrain reads a generation spec (.gen.json text, MapGenSpec rules), composes the plan
    // (MapTerrain.Compose) with the terrain rules of this description's layer, and runs it as one MapEdit.Batch: first the
    // surface names the plan uses that the table lacks (ordinal order), then MapEdit.Stamp, which skips hand-owned points and
    // cells and counts them. Like the other With* members it returns a new document and leaves this one as it was; on any
    // failure nothing changes. It reads and writes no file.
    public sealed partial class MapDocument
    {
        public MapTerrainResult WithTerrain(string genJson) => WithTerrain(genJson, null);

        // ED-3c-1: demJson is the .dem.json text of the spec's base tile (null when the spec has no base).
        public MapTerrainResult WithTerrain(string genJson, string demJson)
        {
            MapGenSpec spec = MapGenSpec.Read(genJson);
            MapDemTile tile = demJson == null ? null : MapDemTile.Read(demJson);
            TerrainRules rules = ContentCatalog.Find().Load(PackId).Terrain;
            if (rules == null) throw new InvalidOperationException("layer " + PackId + " has no terrain rules");
            MapStampPlan plan = MapTerrain.Compose(desc, spec, rules, tile);
            var edit = new MapEdit(desc);
            MapStampResult result = null;
            edit.Batch(b =>
            {
                foreach (string name in MissingNames(plan)) b.AddSurfaceName(name);
                result = b.Stamp(plan);
            });
            return new MapTerrainResult(new MapDocument(edit.Current), result.HeightsSkipped, result.SurfacesSkipped);
        }

        internal MapDesc Desc => desc;

        private List<string> MissingNames(MapStampPlan plan)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (MapSurfaceWrite w in plan.Surfaces) names.Add(w.Name);
            var missing = new List<string>();
            foreach (string name in names)
                if (desc.Surfaces == null || !Contains(desc.Surfaces.Names, name)) missing.Add(name);
            return missing;
        }

        private static bool Contains(IReadOnlyList<string> list, string name)
        {
            foreach (string s in list)
                if (string.Equals(s, name, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
