using System;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // ED-1: many grid edits in one step, the surface name table, turning on heights or surfaces, and new maps. Like the
    // other With* members each returns a new document and leaves this one as it was.
    public sealed partial class MapDocument
    {
        // All edits of the list in order, as one MapEdit.Batch: the same bytes as doing them one by one, or, when one throws,
        // nothing (MapEdit.Batch rules).
        public MapDocument WithEdits(MapEditList edits)
        {
            if (edits == null) throw new ArgumentNullException(nameof(edits));
            return Edited(e => e.Batch(b => edits.ApplyTo(b)));
        }

        public MapDocument WithSurfaceName(string name) => Edited(e => e.AddSurfaceName(name));

        public MapDocument WithHeightsEnabled(int raw) => Edited(e => e.EnableHeights(raw));

        public MapDocument WithSurfacesEnabled(string name) => Edited(e => e.EnableSurfaces(name));

        // A new empty map (MapNew.Desc rules); a setting the reader refuses throws its InvalidDataException.
        public static MapDocument New(MapNewSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return new MapDocument(MapNew.Desc(new MapNewSpec
            {
                Id = settings.Id, Content = settings.Content, Pack = settings.Pack, Width = settings.Width, Depth = settings.Depth,
                FloorRaw = settings.FloorRaw, CeilingRaw = settings.CeilingRaw, Seed = settings.Seed, CrowdPass = settings.CrowdPass,
                HeightRaw = settings.Heights ? settings.HeightRaw : (int?)null, Surface = settings.Surface,
            }));
        }
    }
}
