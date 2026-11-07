using System;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // Hand edits through MapEdit. The description field is read-only, so every edit returns a new document and leaves this
    // one as it was (keeping the old document is the undo). Owner queries return "hand", "stamp:<instance>" or null.
    // kind is "wall", "point", "base" or "unit".
    public sealed partial class MapDocument
    {
        public MapDocument WithHeight(int i, int j, int raw) => Edited(e => e.SetHeight(i, j, raw));

        public MapDocument WithSurface(int x, int z, string name) => Edited(e => e.SetSurface(x, z, name));

        public MapDocument WithoutEntry(string kind, ulong id) => Edited(e => e.Remove(kind, id));

        public MapDocument WithHandReleased(int x0, int z0, int x1, int z1) => Edited(e => e.ReleaseHand(x0, z0, x1, z1));

        public string HeightOwnerAt(int i, int j) => new MapEdit(desc).HeightOwner(i, j);

        public string SurfaceOwnerAt(int x, int z) => new MapEdit(desc).SurfaceOwner(x, z);

        public string EntryOwner(string kind, ulong id) => new MapEdit(desc).EntryOwner(kind, id);

        private MapDocument Edited(Action<MapEdit> step)
        {
            var edit = new MapEdit(desc);
            step(edit);
            return new MapDocument(edit.Current);
        }
    }
}
