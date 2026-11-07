using System;
using System.Collections.Generic;

namespace WarCore.Content.MapTools
{
    // The only writer of map descriptions. Each step builds a new immutable MapDesc; a step either takes effect whole or
    // throws and leaves Current unchanged. Ownership (appendix B.5): owners[0] is "hand", stamp instance i owns as
    // "stamp:" + i; height points and surface cells by their owner grid character, entries by their "owner".
    // Hand edits always record "hand" and may overwrite stamped or unowned data; stamps never overwrite another owner.
    public sealed partial class MapEdit
    {
        public const string Hand = "hand";
        private const int HandIndex = 0;

        public MapDesc Current { get; private set; }

        // start is a description MapDescJson.Read accepted. The editing links it does not check are checked here:
        // per kind, (i) non-null src unique among entries, (ii) stamps ids below next, (iii) stamps ids unique,
        // (iv) an entry whose id is a stamps row id of the same kind carries that row's src. Every step keeps them.
        public MapEdit(MapDesc start)
        {
            Current = start ?? throw new ArgumentNullException(nameof(start));
            CheckLinks(start, MapEditKinds.Wall, start.Walls, start.Next.Wall);
            CheckLinks(start, MapEditKinds.Point, start.Points, (ulong)start.Next.Point);
            CheckLinks(start, MapEditKinds.Base, start.Bases, (ulong)start.Next.Base);
            CheckLinks(start, MapEditKinds.Unit, start.Units, start.Next.Unit);
        }

        private static void CheckLinks<T>(MapDesc d, MapEditKind<T> kind, IReadOnlyList<T> items, ulong next) where T : class
        {
            var srcs = new HashSet<(string, ulong)>();
            foreach (T item in items)
            {
                MapSrc src = kind.Src(item);
                if (src != null && !srcs.Add((src.Instance, src.Src))) throw Link(kind.Name, "src repeated");
            }
            var rows = new Dictionary<ulong, MapStamp>();
            foreach (MapStamp row in d.Stamps)
            {
                if (row.Kind != kind.Name) continue;
                if (row.Id >= next) throw Link(kind.Name, "stamps id not below next");
                if (rows.ContainsKey(row.Id)) throw Link(kind.Name, "stamps id repeated");
                rows.Add(row.Id, row);
            }
            foreach (T item in items)
            {
                if (!rows.TryGetValue(kind.Id(item), out MapStamp row)) continue;
                MapSrc src = kind.Src(item);
                if (src == null || src.Instance != row.Instance || src.Src != row.Src) throw Link(kind.Name, "id taken by another src");
            }
        }

        private static ArgumentException Link(string kind, string what) => new ArgumentException("MAPEDIT_LINK " + kind + ": " + what);

        // ---- queries ----

        // Owner name of a height point, or null when unowned.
        public string HeightOwner(int i, int j)
        {
            if (Current.Heights == null) throw new ArgumentException("the description has no heights");
            CheckPoint(i, j);
            return OwnerName(Current.HeightOwner == null ? MapEditDraft.NoOwner : Current.HeightOwner[j][i]);
        }

        public string SurfaceOwner(int x, int z)
        {
            if (Current.Surfaces == null) throw new ArgumentException("the description has no surfaces");
            CheckCell(x, z);
            return OwnerName(Current.SurfaceOwner == null ? MapEditDraft.NoOwner : Current.SurfaceOwner[z][x]);
        }

        public string EntryOwner(string kind, ulong id)
        {
            int? owner;
            switch (kind)
            {
                case "wall": owner = Find(MapEditKinds.Wall, Current.Walls, id).Owner; break;
                case "point": owner = Find(MapEditKinds.Point, Current.Points, id).Owner; break;
                case "base": owner = Find(MapEditKinds.Base, Current.Bases, id).Owner; break;
                case "unit": owner = Find(MapEditKinds.Unit, Current.Units, id).Owner; break;
                default: throw new ArgumentException("unknown entry kind: " + kind);
            }
            return owner.HasValue ? Current.Owners[owner.Value] : null;
        }

        private string OwnerName(char c)
        {
            int index = MapEditDraft.OwnerIndex(c);
            return index < 0 ? null : Current.Owners[index];
        }

        private static T Find<T>(MapEditKind<T> kind, IReadOnlyList<T> items, ulong id) where T : class
        {
            foreach (T item in items)
                if (kind.Id(item) == id) return item;
            throw new ArgumentException("no " + kind.Name + " with id " + id);
        }

        // ---- hand edits ----

        public void SetHeight(int i, int j, int raw) => Step(draft =>
        {
            if (draft.Heights == null) throw new ArgumentException("the description has no heights");
            CheckPoint(i, j);
            draft.Heights[j][i] = raw;
            draft.HeightOwner[j][i] = MapEditDraft.OwnerChar(HandIndex);
        });

        // name must already be in surfaces.names (adding names is the surface brush's job).
        public void SetSurface(int x, int z, string name) => Step(draft =>
        {
            if (draft.SurfaceRows == null) throw new ArgumentException("the description has no surfaces");
            CheckCell(x, z);
            draft.SurfaceRows[z][x] = SurfaceChar(name);
            draft.SurfaceOwner[z][x] = MapEditDraft.OwnerChar(HandIndex);
        });

        // New entries take next as id (next grows by one), are owned by hand, carry no src and go to the end of their array.
        // The template's id, owner and src are ignored.
        public ulong AddWall(MapWall template) => Add(MapEditKinds.Wall, template);
        public int AddPoint(MapPoint template) => (int)Add(MapEditKinds.Point, template);
        public int AddBase(MapBase template) => (int)Add(MapEditKinds.Base, template);
        public ulong AddUnit(MapUnit unit) => Add(MapEditKinds.Unit, unit);

        // Replaces the entry with the same id: new fields, same id, same array position, owner hand, src kept from the old entry.
        public void ReplaceWall(MapWall entry) => Replace(MapEditKinds.Wall, entry);
        public void ReplacePoint(MapPoint entry) => Replace(MapEditKinds.Point, entry);
        public void ReplaceBase(MapBase entry) => Replace(MapEditKinds.Base, entry);
        public void ReplaceUnit(MapUnit entry) => Replace(MapEditKinds.Unit, entry);

        // Deletes an entry; one with a src leaves a tombstone in "removed" so stamps do not bring it back. Its stamps row stays.
        public void Remove(string kind, ulong id)
        {
            switch (kind)
            {
                case "wall": Remove(MapEditKinds.Wall, id); break;
                case "point": Remove(MapEditKinds.Point, id); break;
                case "base": Remove(MapEditKinds.Base, id); break;
                case "unit": Remove(MapEditKinds.Unit, id); break;
                default: throw new ArgumentException("unknown entry kind: " + kind);
            }
        }

        // Cells x0..x1, z0..z1 (inclusive): hand-owned surface cells, the height points i in [x0, x1 + 1], j in [z0, z1 + 1]
        // and the entries anchored in the box (wall cell; point, base and unit at) become unowned. Stamp owners, unowned
        // data and tombstones are left alone.
        public void ReleaseHand(int x0, int z0, int x1, int z1) => Step(draft =>
        {
            CheckCell(x0, z0);
            CheckCell(x1, z1);
            if (x0 > x1 || z0 > z1) throw new ArgumentException("empty box");
            char hand = MapEditDraft.OwnerChar(HandIndex);
            if (draft.SurfaceOwner != null)
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                        if (draft.SurfaceOwner[z][x] == hand) draft.SurfaceOwner[z][x] = MapEditDraft.NoOwner;
            if (draft.HeightOwner != null)
                for (int j = z0; j <= z1 + 1; j++)
                    for (int i = x0; i <= x1 + 1; i++)
                        if (draft.HeightOwner[j][i] == hand) draft.HeightOwner[j][i] = MapEditDraft.NoOwner;
            Release(draft, MapEditKinds.Wall, x0, z0, x1, z1);
            Release(draft, MapEditKinds.Point, x0, z0, x1, z1);
            Release(draft, MapEditKinds.Base, x0, z0, x1, z1);
            Release(draft, MapEditKinds.Unit, x0, z0, x1, z1);
        });

        private static void Release<T>(MapEditDraft draft, MapEditKind<T> kind, int x0, int z0, int x1, int z1) where T : class
        {
            List<T> items = draft.Items(kind);
            for (int n = 0; n < items.Count; n++)
            {
                T item = items[n];
                if (kind.Owner(item) != HandIndex) continue;
                (long x, long z) = kind.Cell(item, draft.Origin);
                if (x < x0 || x > x1 || z < z0 || z > z1) continue;
                items[n] = kind.Make(item, kind.Id(item), null, kind.Src(item));
            }
        }

        private ulong Add<T>(MapEditKind<T> kind, T template) where T : class
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            CheckInside(kind, template);
            ulong id = 0;
            Step(draft =>
            {
                id = draft.Allocate(kind.Name, kind.Slot, kind.Limit);
                draft.Items(kind).Add(kind.Make(template, id, HandIndex, null));
            });
            return id;
        }

        private void Replace<T>(MapEditKind<T> kind, T entry) where T : class
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            CheckInside(kind, entry);
            Step(draft =>
            {
                List<T> items = draft.Items(kind);
                int at = IndexOf(kind, items, kind.Id(entry));
                items[at] = kind.Make(entry, kind.Id(entry), HandIndex, kind.Src(items[at]));
            });
        }

        private void Remove<T>(MapEditKind<T> kind, ulong id) where T : class => Step(draft =>
        {
            List<T> items = draft.Items(kind);
            int at = IndexOf(kind, items, id);
            MapSrc src = kind.Src(items[at]);
            items.RemoveAt(at);
            if (src != null && Tombstone(draft, src) < 0) draft.Removed.Add(new MapSrc(src.Instance, src.Kind, src.Src));
        });

        private static int Tombstone(MapEditDraft draft, MapSrc src)
        {
            for (int n = 0; n < draft.Removed.Count; n++)
            {
                MapSrc r = draft.Removed[n];
                if (r.Instance == src.Instance && r.Kind == src.Kind && r.Src == src.Src) return n;
            }
            return -1;
        }

        private static int IndexOf<T>(MapEditKind<T> kind, List<T> items, ulong id) where T : class
        {
            for (int n = 0; n < items.Count; n++)
                if (kind.Id(items[n]) == id) return n;
            throw new ArgumentException("no " + kind.Name + " with id " + id);
        }

        // ---- shared ----

        private void Step(Action<MapEditDraft> change)
        {
            var draft = new MapEditDraft(Current);
            change(draft);
            Current = draft.Build();
        }

        private char SurfaceChar(string name)
        {
            IReadOnlyList<string> names = Current.Surfaces.Names;
            for (int n = 0; n < names.Count; n++)
                if (string.Equals(names[n], name, StringComparison.Ordinal)) return MapEditDraft.OwnerChar(n);
            throw new ArgumentException("surface name not in the table: " + name);
        }

        private void CheckInside<T>(MapEditKind<T> kind, T entry) where T : class
        {
            if (!kind.Inside(entry, Current)) throw new ArgumentOutOfRangeException(nameof(entry), kind.Name + " is off the map");
        }

        private void CheckCell(int x, int z)
        {
            if (x < 0 || x >= Current.Size.X) throw new ArgumentOutOfRangeException(nameof(x));
            if (z < 0 || z >= Current.Size.Z) throw new ArgumentOutOfRangeException(nameof(z));
        }

        private void CheckPoint(int i, int j)
        {
            if (i < 0 || i > Current.Size.X) throw new ArgumentOutOfRangeException(nameof(i));
            if (j < 0 || j > Current.Size.Z) throw new ArgumentOutOfRangeException(nameof(j));
        }
    }
}
