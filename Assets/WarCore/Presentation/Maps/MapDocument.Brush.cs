using System;
using WarCore.Content;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // ED-2: terrain brushes. Each member computes the brush's writes (MapBrush rules) and applies them as hand edits in one
    // MapEdit.Batch; it returns a new document and leaves this one as it was.
    public sealed partial class MapDocument
    {
        public MapDocument WithRaise(int i, int j, int radius, int amount) =>
            WithHeights(MapBrush.Raise(desc, i, j, radius, amount));

        public MapDocument WithFlatten(int i, int j, int radius, int target) =>
            WithHeights(MapBrush.Flatten(desc, i, j, radius, target));

        public MapDocument WithSmooth(int i, int j, int radius) =>
            WithHeights(MapBrush.Smooth(desc, i, j, radius));

        // The walkable slope is the terrain rules' of this description's layer, found through the content catalog; a layer
        // without terrain rules is InvalidOperationException.
        public MapDocument WithRamp(int i0, int j0, int h0, int i1, int j1, int h1, int halfWidth)
        {
            TerrainRules rules = ContentCatalog.Find().Load(PackId).Terrain;
            if (rules == null) throw new InvalidOperationException("layer " + PackId + " has no terrain rules");
            return WithHeights(MapBrush.Ramp(desc, i0, j0, h0, i1, j1, h1, halfWidth, rules.MaxWalkableNum, rules.MaxWalkableDen));
        }

        public MapDocument WithBank(int i0, int j0, int i1, int j1, int halfWidth, int amount) =>
            WithHeights(MapBrush.Bank(desc, i0, j0, i1, j1, halfWidth, amount));

        public MapDocument WithSurfaceCircle(int x, int z, int radius, string name) =>
            WithSurfaces(MapBrush.SurfaceCircle(desc, x, z, radius, name), name);

        public MapDocument WithSurfaceSquare(int x, int z, int radius, string name) =>
            WithSurfaces(MapBrush.SurfaceSquare(desc, x, z, radius, name), name);

        private MapDocument WithHeights(MapHeightWrite[] writes) => Edited(e => e.Batch(b =>
        {
            foreach (MapHeightWrite w in writes) b.SetHeight(w.I, w.J, w.Raw);
        }));

        // A name not yet in the table is added first, in the same batch, but only when some cell changes.
        private MapDocument WithSurfaces(MapSurfaceWrite[] writes, string name) => Edited(e => e.Batch(b =>
        {
            if (writes.Length == 0) return;
            b.AddSurfaceName(name);
            foreach (MapSurfaceWrite w in writes) b.SetSurface(w.X, w.Z, w.Name);
        }));
    }
}
