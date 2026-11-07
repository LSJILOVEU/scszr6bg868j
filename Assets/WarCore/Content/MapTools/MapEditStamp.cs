using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WarCore.Content.MapTools
{
    public readonly struct MapHeightWrite
    {
        public int I { get; }
        public int J { get; }
        public int Raw { get; }
        public MapHeightWrite(int i, int j, int raw) { I = i; J = j; Raw = raw; }
    }

    public readonly struct MapSurfaceWrite
    {
        public int X { get; }
        public int Z { get; }
        public string Name { get; }
        public MapSurfaceWrite(int x, int z, string name) { X = x; Z = z; Name = name; }
    }

    // One entry a stamp produces: its source entry number and the fields to use. The template's id, owner and src are ignored.
    public sealed class MapStampEntry<T> where T : class
    {
        public ulong Src { get; }
        public T Template { get; }
        public MapStampEntry(ulong src, T template)
        {
            Src = src;
            Template = template ?? throw new ArgumentNullException(nameof(template));
        }
    }

    // What one stamp instance writes. Every input array is copied.
    public sealed class MapStampPlan
    {
        public string Instance { get; }
        public IReadOnlyList<MapHeightWrite> Heights { get; }
        public IReadOnlyList<MapSurfaceWrite> Surfaces { get; }
        public IReadOnlyList<MapStampEntry<MapWall>> Walls { get; }
        public IReadOnlyList<MapStampEntry<MapPoint>> Points { get; }
        public IReadOnlyList<MapStampEntry<MapBase>> Bases { get; }
        public IReadOnlyList<MapStampEntry<MapUnit>> Units { get; }

        public MapStampPlan(string instance, MapHeightWrite[] heights, MapSurfaceWrite[] surfaces,
            MapStampEntry<MapWall>[] walls, MapStampEntry<MapPoint>[] points, MapStampEntry<MapBase>[] bases,
            MapStampEntry<MapUnit>[] units)
        {
            if (string.IsNullOrEmpty(instance)) throw new ArgumentException("stamp instance name is empty", nameof(instance));
            Instance = instance;
            Heights = Copy(heights, nameof(heights));
            Surfaces = Copy(surfaces, nameof(surfaces));
            Walls = Copy(walls, nameof(walls));
            Points = Copy(points, nameof(points));
            Bases = Copy(bases, nameof(bases));
            Units = Copy(units, nameof(units));
        }

        private static ReadOnlyCollection<T> Copy<T>(T[] items, string name)
        {
            if (items == null) throw new ArgumentNullException(name);
            var copy = (T[])items.Clone();
            foreach (T item in copy)
                if (item == null) throw new ArgumentNullException(name);
            return new ReadOnlyCollection<T>(copy);
        }
    }

    // Counts of one stamp run. Every accepted write counts as written, even when the value did not change; every write
    // left out for its owner or a tombstone counts as skipped.
    public sealed class MapStampResult
    {
        public int HeightsWritten { get; }
        public int HeightsSkipped { get; }
        public int SurfacesWritten { get; }
        public int SurfacesSkipped { get; }
        public int EntriesWritten { get; }
        public int EntriesSkipped { get; }
        public MapStampResult(int heightsWritten, int heightsSkipped, int surfacesWritten, int surfacesSkipped,
            int entriesWritten, int entriesSkipped)
        {
            HeightsWritten = heightsWritten; HeightsSkipped = heightsSkipped;
            SurfacesWritten = surfacesWritten; SurfacesSkipped = surfacesSkipped;
            EntriesWritten = entriesWritten; EntriesSkipped = entriesSkipped;
        }
    }

    public sealed partial class MapEdit
    {
        // Runs a stamp, in this order: (a) owner "stamp:<instance>" (reused when listed, else appended; MAPDESC_OWNER when the
        // table already has 64); (b) height points then surface cells in plan order, written only where unowned or already
        // the stamp's own; (c) walls, points, bases, units in plan order: a tombstoned (instance, kind, src) is skipped; an
        // entry with that src is replaced when unowned or the stamp's own (id, src and position kept) and skipped otherwise;
        // with no such entry a new one is appended, with the id of its stamps row or, without one, next (row appended).
        // Entries an earlier run made and this plan leaves out are kept.
        public MapStampResult Stamp(MapStampPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            MapStampResult result = null;
            Step(draft =>
            {
                int k = OwnerFor(draft, "stamp:" + plan.Instance);
                char own = MapEditDraft.OwnerChar(k);
                int hw = 0, hs = 0, sw = 0, ss = 0;
                foreach (MapHeightWrite h in plan.Heights)
                {
                    if (draft.Heights == null) throw new ArgumentException("the description has no heights");
                    CheckPoint(h.I, h.J);
                    char c = draft.HeightOwner[h.J][h.I];
                    if (c != MapEditDraft.NoOwner && c != own) { hs++; continue; }
                    draft.Heights[h.J][h.I] = h.Raw;
                    draft.HeightOwner[h.J][h.I] = own;
                    hw++;
                }
                foreach (MapSurfaceWrite s in plan.Surfaces)
                {
                    if (draft.SurfaceRows == null) throw new ArgumentException("the description has no surfaces");
                    CheckCell(s.X, s.Z);
                    char name = SurfaceChar(s.Name);
                    char c = draft.SurfaceOwner[s.Z][s.X];
                    if (c != MapEditDraft.NoOwner && c != own) { ss++; continue; }
                    draft.SurfaceRows[s.Z][s.X] = name;
                    draft.SurfaceOwner[s.Z][s.X] = own;
                    sw++;
                }
                int ew = 0, es = 0;
                CheckAll(MapEditKinds.Wall, plan.Walls);
                CheckAll(MapEditKinds.Point, plan.Points);
                CheckAll(MapEditKinds.Base, plan.Bases);
                CheckAll(MapEditKinds.Unit, plan.Units);
                Place(draft, MapEditKinds.Wall, plan.Instance, k, plan.Walls, ref ew, ref es);
                Place(draft, MapEditKinds.Point, plan.Instance, k, plan.Points, ref ew, ref es);
                Place(draft, MapEditKinds.Base, plan.Instance, k, plan.Bases, ref ew, ref es);
                Place(draft, MapEditKinds.Unit, plan.Instance, k, plan.Units, ref ew, ref es);
                result = new MapStampResult(hw, hs, sw, ss, ew, es);
            });
            return result;
        }

        private void CheckAll<T>(MapEditKind<T> kind, IReadOnlyList<MapStampEntry<T>> entries) where T : class
        {
            foreach (MapStampEntry<T> entry in entries) CheckInside(kind, entry.Template);
        }

        private static int OwnerFor(MapEditDraft draft, string name)
        {
            int k = draft.Owners.IndexOf(name);
            if (k >= 0) return k;
            if (draft.Owners.Count >= MapEditDraft.MaxOwners) throw new InvalidOperationException("MAPDESC_OWNER");
            draft.Owners.Add(name);
            return draft.Owners.Count - 1;
        }

        private static void Place<T>(MapEditDraft draft, MapEditKind<T> kind, string instance, int k,
            IReadOnlyList<MapStampEntry<T>> entries, ref int written, ref int skipped) where T : class
        {
            List<T> items = draft.Items(kind);
            foreach (MapStampEntry<T> entry in entries)
            {
                var src = new MapSrc(instance, kind.Name, entry.Src);
                if (Tombstone(draft, src) >= 0) { skipped++; continue; }
                int at = -1;
                for (int n = 0; n < items.Count; n++)
                {
                    MapSrc s = kind.Src(items[n]);
                    if (s != null && s.Instance == instance && s.Src == entry.Src) { at = n; break; }
                }
                if (at >= 0)
                {
                    int? owner = kind.Owner(items[at]);
                    if (owner.HasValue && owner.Value != k) { skipped++; continue; }
                    items[at] = kind.Make(entry.Template, kind.Id(items[at]), k, kind.Src(items[at]));
                    written++;
                    continue;
                }
                ulong id = StampId(draft, kind, instance, entry.Src);
                items.Add(kind.Make(entry.Template, id, k, src));
                written++;
            }
        }

        // Id of the stamps row (instance, kind, src); without a row, next is taken and the row appended.
        private static ulong StampId<T>(MapEditDraft draft, MapEditKind<T> kind, string instance, ulong src) where T : class
        {
            foreach (MapStamp row in draft.Stamps)
                if (row.Instance == instance && row.Kind == kind.Name && row.Src == src) return row.Id;
            ulong id = draft.Allocate(kind.Name, kind.Slot, kind.Limit);
            draft.Stamps.Add(new MapStamp(instance, kind.Name, src, id));
            return id;
        }
    }
}
