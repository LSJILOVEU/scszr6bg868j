using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // ED-6a entry writes as MapEdit steps, shared by the MapDocument.With* members and MapEntryEditList. Every check runs
    // inside the step (only a null record or name is refused when the step is made), and every step reads the editor's
    // Current, so inside a batch ids, instance names and prefab inputs follow the steps before it.
    internal static class MapEntrySteps
    {
        internal const int MaxSquad = 100;

        internal static Action<MapEdit> AddWall(MapWallInfo w) { NotNull(w); return e => e.AddWall(MapEntryConvert.ToWall(w, 0)); }
        internal static Action<MapEdit> AddPoint(MapPointInfo p) { NotNull(p); return e => e.AddPoint(MapEntryConvert.ToPoint(p, 0)); }
        internal static Action<MapEdit> AddBase(MapBaseInfo b) { NotNull(b); return e => e.AddBase(MapEntryConvert.ToBase(b, 0)); }
        internal static Action<MapEdit> AddUnit(MapUnitInfo u) { NotNull(u); return e => e.AddUnit(MapEntryConvert.ToUnit(u, 0)); }
        internal static Action<MapEdit> AddPlate(MapPlateInfo p) { NotNull(p); return e => e.AddPlate(MapEntryConvert.ToPlate(p, 0)); }
        internal static Action<MapEdit> AddCutout(MapCutoutInfo c) { NotNull(c); return e => e.AddCutout(MapEntryConvert.ToCutout(c, 0)); }

        internal static Action<MapEdit> ReplaceWall(MapWallInfo w) { NotNull(w); return e => e.ReplaceWall(MapEntryConvert.ToWall(w, w.Id)); }
        internal static Action<MapEdit> ReplacePoint(MapPointInfo p) { NotNull(p); return e => e.ReplacePoint(MapEntryConvert.ToPoint(p, p.Id)); }
        internal static Action<MapEdit> ReplaceBase(MapBaseInfo b) { NotNull(b); return e => e.ReplaceBase(MapEntryConvert.ToBase(b, b.Id)); }
        internal static Action<MapEdit> ReplaceUnit(MapUnitInfo u) { NotNull(u); return e => e.ReplaceUnit(MapEntryConvert.ToUnit(u, u.Key)); }
        internal static Action<MapEdit> ReplacePlate(MapPlateInfo p) { NotNull(p); return e => e.ReplacePlate(MapEntryConvert.ToPlate(p, p.Id)); }
        internal static Action<MapEdit> ReplaceCutout(MapCutoutInfo c) { NotNull(c); return e => e.ReplaceCutout(MapEntryConvert.ToCutout(c, c.Id)); }

        internal static Action<MapEdit> Remove(string kind, ulong id) => e => e.Remove(kind, id);

        // Wall, plate or cutout moved by whole cells (Replace: same id and src, owner hand).
        internal static Action<MapEdit> MoveCells(string kind, ulong id, long dx, long dz) => e =>
        {
            MapDesc d = e.Current;
            switch (kind)
            {
                case "wall": e.ReplaceWall(Shift(Find(d.Walls, w => w.Id, id, kind), dx, dz)); break;
                case "plate": e.ReplacePlate(Shift(Find(d.Plates, p => p.Id, id, kind), dx, dz)); break;
                case "cutout": e.ReplaceCutout(Shift(Find(d.Cutouts, c => c.Id, id, kind), dx, dz)); break;
                default: throw new ArgumentException("not a wall, plate or cutout: " + kind, nameof(kind));
            }
        };

        // Point (with its spawn), base or unit moved by millimetres.
        internal static Action<MapEdit> MoveMm(string kind, ulong id, long dxMm, long dzMm) => e =>
        {
            MapDesc d = e.Current;
            switch (kind)
            {
                case "point": e.ReplacePoint(Shift(Find(d.Points, p => (ulong)p.Id, id, kind), dxMm, dzMm)); break;
                case "base": e.ReplaceBase(Shift(Find(d.Bases, b => (ulong)b.Id, id, kind), dxMm, dzMm)); break;
                case "unit": e.ReplaceUnit(Shift(Find(d.Units, u => u.Key, id, kind), dxMm, dzMm)); break;
                default: throw new ArgumentException("not a point, base or unit: " + kind, nameof(kind));
            }
        };

        // A new entry with the fields of an existing one moved by dx, dz (cells for walls, plates and cutouts, millimetres for
        // the others): next id, owner hand, no src. A copied cutout cuts the same plate.
        internal static Action<MapEdit> Copy(string kind, ulong id, long dx, long dz) => e =>
        {
            MapDesc d = e.Current;
            switch (kind)
            {
                case "wall": e.AddWall(Shift(Find(d.Walls, w => w.Id, id, kind), dx, dz)); break;
                case "point": e.AddPoint(Shift(Find(d.Points, p => (ulong)p.Id, id, kind), dx, dz)); break;
                case "base": e.AddBase(Shift(Find(d.Bases, b => (ulong)b.Id, id, kind), dx, dz)); break;
                case "unit": e.AddUnit(Shift(Find(d.Units, u => u.Key, id, kind), dx, dz)); break;
                case "plate": e.AddPlate(Shift(Find(d.Plates, p => p.Id, id, kind), dx, dz)); break;
                case "cutout": e.AddCutout(Shift(Find(d.Cutouts, c => c.Id, id, kind), dx, dz)); break;
                default: throw new ArgumentException("unknown entry kind: " + kind, nameof(kind));
            }
        };

        // MapPrefabs.Door stamped as "door-<n>" on the description's first layer, kind "wall".
        internal static Action<MapEdit> DoorWall(int startX, int startZ, bool alongX, int length, int doorStart, int doorWidth, long topRaw) => e =>
        {
            MapDesc d = e.Current;
            e.Stamp(MapPrefabs.Door(d, FreeInstance(d, "door"), new MapXZ(startX, startZ), alongX, length, doorStart, doorWidth, topRaw,
                FirstLayer(d), "wall"));
        };

        // MapPrefabs.Window (default sill and opening) stamped as "window-<n>", with the combat body of the description's layer.
        internal static Action<MapEdit> Window(int startX, int startZ, bool alongX, int length, int windowStart, int windowWidth, long topRaw) => e =>
        {
            MapDesc d = e.Current;
            CombatBody body = BodyOf(d.Pack);
            e.Stamp(MapPrefabs.Window(d, FreeInstance(d, "window"), new MapXZ(startX, startZ), alongX, length, windowStart, windowWidth, topRaw,
                FirstLayer(d), "wall", body));
        };

        // Removes every entry whose src names the instance (hand-edited ones too), each leaving its tombstone. Heights,
        // surfaces, the owner table and the stamps table stay as they are (the owner row keeps the name taken).
        internal static Action<MapEdit> RemoveStamp(string instance)
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));
            return e =>
            {
                MapDesc d = e.Current;
                var found = new List<(string, ulong)>();
                foreach (MapWall w in d.Walls) if (From(w.Src, instance)) found.Add(("wall", w.Id));
                foreach (MapPoint p in d.Points) if (From(p.Src, instance)) found.Add(("point", (ulong)p.Id));
                foreach (MapBase b in d.Bases) if (From(b.Src, instance)) found.Add(("base", (ulong)b.Id));
                foreach (MapUnit u in d.Units) if (From(u.Src, instance)) found.Add(("unit", u.Key));
                foreach (MapCutout c in d.Cutouts) if (From(c.Src, instance)) found.Add(("cutout", c.Id));
                foreach (MapPlate p in d.Plates) if (From(p.Src, instance)) found.Add(("plate", p.Id));
                if (found.Count == 0) throw new ArgumentException("no entry comes from stamp " + instance, nameof(instance));
                foreach ((string kind, ulong id) in found) e.Remove(kind, id);
            };
        }

        // count units (1 to MaxSquad) in the 1 m formation around the centre, keys from next.
        internal static Action<MapEdit> Squad(string type, int team, int squad, long centerXMm, long centerZMm, int count, bool reserve) => e =>
        {
            if (count < 1 || count > MaxSquad) throw new ArgumentOutOfRangeException(nameof(count), "a squad has 1 to " + MaxSquad + " units");
            for (int rank = 0; rank < count; rank++)
            {
                (long x, long z) = WorldBuilder.FormationOffsetMm(rank, count);
                e.AddUnit(new MapUnit(0, type, team, squad, new MapMm(AddMm(centerXMm, x), AddMm(centerZMm, z)), reserve));
            }
        };

        private static void NotNull(object record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
        }

        private static bool From(MapSrc src, string instance) => src != null && src.Instance == instance;

        private static T Find<T>(IReadOnlyList<T> items, Func<T, ulong> id, ulong want, string kind)
        {
            foreach (T item in items)
                if (id(item) == want) return item;
            throw new ArgumentException("no " + kind + " with id " + want);
        }

        private static string FirstLayer(MapDesc d)
        {
            if (d.Layers.Count == 0) throw new InvalidOperationException("the description has no layer");
            return d.Layers[0].Name;
        }

        private static CombatBody BodyOf(string pack)
        {
            CombatBody? body = ContentCatalog.Find().Load(pack).Body;
            if (!body.HasValue)
                throw new InvalidOperationException("the content layer \"" + pack + "\" has no combat body, so a window cannot be placed");
            return body.Value;
        }

        // "<prefix>-<n>" with the smallest n >= 1 that no owner row, stamps row, tombstone or entry src uses (a hand-written
        // description may hold a tombstone without an owner row; a new stamp under that name would be silently skipped).
        internal static string FreeInstance(MapDesc d, string prefix)
        {
            var taken = new HashSet<string>();
            foreach (string owner in d.Owners) if (owner.StartsWith("stamp:", StringComparison.Ordinal)) taken.Add(owner.Substring(6));
            foreach (MapStamp row in d.Stamps) taken.Add(row.Instance);
            foreach (MapSrc row in d.Removed) taken.Add(row.Instance);
            foreach (MapWall w in d.Walls) Take(taken, w.Src);
            foreach (MapPoint p in d.Points) Take(taken, p.Src);
            foreach (MapBase b in d.Bases) Take(taken, b.Src);
            foreach (MapUnit u in d.Units) Take(taken, u.Src);
            foreach (MapPlate p in d.Plates) Take(taken, p.Src);
            foreach (MapCutout c in d.Cutouts) Take(taken, c.Src);
            for (int n = 1; ; n++)
            {
                string name = prefix + "-" + n;
                if (!taken.Contains(name)) return name;
            }
        }

        private static void Take(HashSet<string> taken, MapSrc src)
        {
            if (src != null) taken.Add(src.Instance);
        }

        private static int AddCells(int v, long d)
        {
            long r = v + d;
            if (r < int.MinValue || r > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(d), "the move leaves the map");
            return (int)r;
        }

        private static long AddMm(long v, long d)
        {
            try { return checked(v + d); }
            catch (OverflowException) { throw new ArgumentOutOfRangeException(nameof(d), "the move leaves the map"); }
        }

        private static MapXZ Cells(MapXZ cell, long dx, long dz) => new MapXZ(AddCells(cell.X, dx), AddCells(cell.Z, dz));

        private static MapMm Mm(MapMm at, long dx, long dz) => new MapMm(AddMm(at.X, dx), AddMm(at.Z, dz));

        // Moved copies; the id, owner and src are kept (Add ignores them, Replace keeps the old src and makes the owner hand).
        private static MapWall Shift(MapWall w, long dx, long dz) =>
            new MapWall(w.Id, w.Kind, w.Layer, Cells(w.Cell, dx, dz), w.Size, w.HeightRaw, w.Owner, w.Src, w.BaseRaw);

        private static MapPlate Shift(MapPlate p, long dx, long dz) =>
            new MapPlate(p.Id, Cells(p.Cell, dx, dz), p.Size, p.YRaw, p.Slope, p.ThickRaw, p.Surface, p.Owner, p.Src);

        private static MapCutout Shift(MapCutout c, long dx, long dz) =>
            new MapCutout(c.Id, c.Plate, Cells(c.Cell, dx, dz), c.Size, c.Owner, c.Src);

        private static MapPoint Shift(MapPoint p, long dx, long dz) => new MapPoint(p.Id, Mm(p.At, dx, dz), p.RadiusMm, p.InitialOwner,
            p.Spawn.HasValue ? Mm(p.Spawn.Value, dx, dz) : (MapMm?)null, p.Owner, p.Src, p.SpawnPlate, p.Plate);

        private static MapBase Shift(MapBase b, long dx, long dz) => new MapBase(b.Id, b.Team, Mm(b.At, dx, dz), b.Owner, b.Src, b.Plate);

        private static MapUnit Shift(MapUnit u, long dx, long dz) =>
            new MapUnit(u.Key, u.Type, u.Team, u.Squad, Mm(u.At, dx, dz), u.Reserve, u.Owner, u.Src, u.Plate);
    }
}
