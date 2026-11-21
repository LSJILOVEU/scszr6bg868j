using System;
using System.Collections.Generic;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // ED-6a read side: the description's entries as plain records, next ids, and the entries anchored in a box of cells.
    public sealed partial class MapDocument
    {
        public IReadOnlyList<MapWallInfo> Walls() => MapEntryConvert.All(desc.Walls, w => MapEntryConvert.Info(desc, w));
        public IReadOnlyList<MapPointInfo> Points() => MapEntryConvert.All(desc.Points, p => MapEntryConvert.Info(desc, p));
        public IReadOnlyList<MapBaseInfo> Bases() => MapEntryConvert.All(desc.Bases, b => MapEntryConvert.Info(desc, b));
        public IReadOnlyList<MapUnitInfo> Units() => MapEntryConvert.All(desc.Units, u => MapEntryConvert.Info(desc, u));
        public IReadOnlyList<MapPlateInfo> Plates() => MapEntryConvert.All(desc.Plates, p => MapEntryConvert.Info(desc, p));
        public IReadOnlyList<MapCutoutInfo> Cutouts() => MapEntryConvert.All(desc.Cutouts, c => MapEntryConvert.Info(desc, c));

        // The id the next added entry of the kind gets ("wall", "point", "base", "unit", "plate" or "cutout").
        public ulong NextId(string kind)
        {
            switch (kind)
            {
                case "wall": return desc.Next.Wall;
                case "point": return (ulong)desc.Next.Point;
                case "base": return (ulong)desc.Next.Base;
                case "unit": return desc.Next.Unit;
                case "plate": return desc.Next.Plate;
                case "cutout": return desc.Next.Cutout;
                default: throw new ArgumentException("unknown entry kind: " + kind, nameof(kind));
            }
        }

        // Entries whose anchor cell is in cells x0..x1, z0..z1 (inclusive), the anchors of MapEdit.ReleaseHand: wall, plate
        // and cutout by their cell, point, base and unit by the cell of their position (rounded toward negative infinity).
        // Order: walls, points, bases, units, plates, cutouts, each kind in description order. A corner off the map throws
        // ArgumentOutOfRangeException, x0 > x1 or z0 > z1 ArgumentException.
        public IReadOnlyList<MapEntryRef> EntriesIn(int x0, int z0, int x1, int z1)
        {
            CheckCell(x0, z0);
            CheckCell(x1, z1);
            if (x0 > x1 || z0 > z1) throw new ArgumentException("empty box");
            var found = new List<MapEntryRef>();
            bool In(long x, long z) => x >= x0 && x <= x1 && z >= z0 && z <= z1;
            foreach (MapWall w in desc.Walls) if (In(w.Cell.X, w.Cell.Z)) found.Add(new MapEntryRef("wall", w.Id));
            foreach (MapPoint p in desc.Points) if (In(MmCellX(p.At), MmCellZ(p.At))) found.Add(new MapEntryRef("point", (ulong)p.Id));
            foreach (MapBase b in desc.Bases) if (In(MmCellX(b.At), MmCellZ(b.At))) found.Add(new MapEntryRef("base", (ulong)b.Id));
            foreach (MapUnit u in desc.Units) if (In(MmCellX(u.At), MmCellZ(u.At))) found.Add(new MapEntryRef("unit", u.Key));
            foreach (MapPlate p in desc.Plates) if (In(p.Cell.X, p.Cell.Z)) found.Add(new MapEntryRef("plate", p.Id));
            foreach (MapCutout c in desc.Cutouts) if (In(c.Cell.X, c.Cell.Z)) found.Add(new MapEntryRef("cutout", c.Id));
            return found.AsReadOnly();
        }

        private long MmCellX(MapMm at) => FloorMetre(at.X) - desc.Origin.X;
        private long MmCellZ(MapMm at) => FloorMetre(at.Z) - desc.Origin.Z;

        private static long FloorMetre(long mm)
        {
            long q = mm / 1000;
            return mm % 1000 < 0 ? q - 1 : q;
        }

        private void CheckCell(int x, int z)
        {
            if (x < 0 || x >= desc.Size.X) throw new ArgumentOutOfRangeException(nameof(x));
            if (z < 0 || z >= desc.Size.Z) throw new ArgumentOutOfRangeException(nameof(z));
        }
    }

    // Entries to records and back. To* builds a template for MapEdit: Add ignores its id, owner and src, Replace its owner
    // and src.
    internal static class MapEntryConvert
    {
        internal static IReadOnlyList<TInfo> All<T, TInfo>(IReadOnlyList<T> items, Func<T, TInfo> info)
        {
            var list = new TInfo[items.Count];
            for (int i = 0; i < list.Length; i++) list[i] = info(items[i]);
            return Array.AsReadOnly(list);
        }

        internal static string Owner(MapDesc d, int? owner) => owner.HasValue ? d.Owners[owner.Value] : "";

        internal static string Stamp(MapSrc src) => src == null ? "" : src.Instance;

        internal static MapWallInfo Info(MapDesc d, MapWall w) => new MapWallInfo(w.Id, w.Kind, w.Layer, w.Cell.X, w.Cell.Z, w.Size.X, w.Size.Z,
            w.HeightRaw, w.BaseRaw.HasValue, w.BaseRaw ?? 0, Owner(d, w.Owner), Stamp(w.Src));

        internal static MapPointInfo Info(MapDesc d, MapPoint p) => new MapPointInfo((ulong)p.Id, p.At.X, p.At.Z, p.RadiusMm, p.InitialOwner,
            p.Spawn.HasValue, p.Spawn?.X ?? 0, p.Spawn?.Z ?? 0, p.SpawnPlate.HasValue, p.SpawnPlate ?? 0, p.Plate.HasValue, p.Plate ?? 0,
            Owner(d, p.Owner), Stamp(p.Src));

        internal static MapBaseInfo Info(MapDesc d, MapBase b) =>
            new MapBaseInfo((ulong)b.Id, b.Team, b.At.X, b.At.Z, b.Plate.HasValue, b.Plate ?? 0, Owner(d, b.Owner), Stamp(b.Src));

        internal static MapUnitInfo Info(MapDesc d, MapUnit u) => new MapUnitInfo(u.Key, u.Type, u.Team, u.Squad, u.At.X, u.At.Z, u.Reserve,
            u.Plate.HasValue, u.Plate ?? 0, Owner(d, u.Owner), Stamp(u.Src));

        internal static MapPlateInfo Info(MapDesc d, MapPlate p) => new MapPlateInfo(p.Id, p.Cell.X, p.Cell.Z, p.Size.X, p.Size.Z, p.Slope != null,
            p.YRaw ?? 0, p.Slope?.Axis, p.Slope?.FromRaw ?? 0, p.Slope?.ToRaw ?? 0, p.ThickRaw, p.Surface, Owner(d, p.Owner), Stamp(p.Src));

        internal static MapCutoutInfo Info(MapDesc d, MapCutout c) =>
            new MapCutoutInfo(c.Id, c.Plate, c.Cell.X, c.Cell.Z, c.Size.X, c.Size.Z, Owner(d, c.Owner), Stamp(c.Src));

        internal static MapWall ToWall(MapWallInfo w, ulong id) => new MapWall(id, w.Kind, w.Layer, new MapXZ(w.CellX, w.CellZ),
            new MapXZ(w.SizeX, w.SizeZ), w.HeightRaw, null, null, w.HasBase ? w.BaseRaw : (long?)null);

        internal static MapPoint ToPoint(MapPointInfo p, ulong id) => new MapPoint(SmallId(id), new MapMm(p.XMm, p.ZMm), p.RadiusMm,
            p.InitialOwner, p.HasSpawn ? new MapMm(p.SpawnXMm, p.SpawnZMm) : (MapMm?)null, null, null,
            p.HasSpawnPlate ? p.SpawnPlate : (ulong?)null, p.HasPlate ? p.Plate : (ulong?)null);

        internal static MapBase ToBase(MapBaseInfo b, ulong id) =>
            new MapBase(SmallId(id), b.Team, new MapMm(b.XMm, b.ZMm), null, null, b.HasPlate ? b.Plate : (ulong?)null);

        internal static MapUnit ToUnit(MapUnitInfo u, ulong key) => new MapUnit(key, u.Type, u.Team, u.Squad, new MapMm(u.XMm, u.ZMm), u.Reserve,
            null, null, u.HasPlate ? u.Plate : (ulong?)null);

        internal static MapPlate ToPlate(MapPlateInfo p, ulong id) => new MapPlate(id, new MapXZ(p.CellX, p.CellZ), new MapXZ(p.SizeX, p.SizeZ),
            p.Sloped ? (long?)null : p.YRaw, p.Sloped ? new MapSlope(p.SlopeAxis, p.FromRaw, p.ToRaw) : null, p.ThickRaw,
            p.Surface.Length == 0 ? null : p.Surface);

        internal static MapCutout ToCutout(MapCutoutInfo c, ulong id) =>
            new MapCutout(id, c.Plate, new MapXZ(c.CellX, c.CellZ), new MapXZ(c.SizeX, c.SizeZ));

        // Point and base ids are int in the description.
        internal static int SmallId(ulong id)
        {
            if (id > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(id), "point and base ids are at most " + int.MaxValue);
            return (int)id;
        }
    }
}
