using System;
using System.Collections.Generic;

namespace WarCore.Content.MapTools
{
    // One entry category as the editor sees it: id, owner, src, anchor cell and a copy with a new id, owner and src.
    internal sealed class MapEditKind<T> where T : class
    {
        internal readonly string Name;
        internal readonly int Slot;
        internal readonly ulong Limit;
        internal readonly Func<T, ulong> Id;
        internal readonly Func<T, int?> Owner;
        internal readonly Func<T, MapSrc> Src;
        internal readonly Func<T, ulong, int?, MapSrc, T> Make;
        internal readonly Func<T, MapXZ, (long X, long Z)> Cell;
        // True when the entry's coordinates lie on the map: wall cell and size inside [0, W] x [0, D], millimetre positions
        // in the half-open range origin <= v < origin + size.
        internal readonly Func<T, MapDesc, bool> Inside;

        internal MapEditKind(string name, int slot, ulong limit, Func<T, ulong> id, Func<T, int?> owner, Func<T, MapSrc> src,
            Func<T, ulong, int?, MapSrc, T> make, Func<T, MapXZ, (long X, long Z)> cell, Func<T, MapDesc, bool> inside)
        {
            Name = name; Slot = slot; Limit = limit; Id = id; Owner = owner; Src = src; Make = make; Cell = cell; Inside = inside;
        }
    }

    internal static class MapEditKinds
    {
        internal static readonly MapEditKind<MapWall> Wall = new MapEditKind<MapWall>("wall", 0, ulong.MaxValue,
            w => w.Id, w => w.Owner, w => w.Src,
            (w, id, owner, src) => new MapWall(id, w.Kind, w.Layer, w.Cell, w.Size, w.HeightRaw, owner, src),
            (w, origin) => (w.Cell.X, w.Cell.Z),
            (w, d) => w.Cell.X >= 0 && w.Cell.Z >= 0 && w.Size.X >= 1 && w.Size.Z >= 1
                && (long)w.Cell.X + w.Size.X <= d.Size.X && (long)w.Cell.Z + w.Size.Z <= d.Size.Z);

        internal static readonly MapEditKind<MapPoint> Point = new MapEditKind<MapPoint>("point", 1, int.MaxValue,
            p => (ulong)p.Id, p => p.Owner, p => p.Src,
            (p, id, owner, src) => new MapPoint((int)id, p.At, p.RadiusMm, p.InitialOwner, p.Spawn, owner, src),
            (p, origin) => MmCell(p.At, origin),
            (p, d) => OnMap(p.At, d) && (!p.Spawn.HasValue || OnMap(p.Spawn.Value, d)));

        internal static readonly MapEditKind<MapBase> Base = new MapEditKind<MapBase>("base", 2, int.MaxValue,
            b => (ulong)b.Id, b => b.Owner, b => b.Src,
            (b, id, owner, src) => new MapBase((int)id, b.Team, b.At, owner, src),
            (b, origin) => MmCell(b.At, origin),
            (b, d) => OnMap(b.At, d));

        internal static readonly MapEditKind<MapUnit> Unit = new MapEditKind<MapUnit>("unit", 3, ulong.MaxValue,
            u => u.Key, u => u.Owner, u => u.Src,
            (u, id, owner, src) => new MapUnit(id, u.Type, u.Team, u.Squad, u.At, u.Reserve, owner, src),
            (u, origin) => MmCell(u.At, origin),
            (u, d) => OnMap(u.At, d));

        private static bool OnMap(MapMm at, MapDesc d)
        {
            long x0 = d.Origin.X * 1000L, z0 = d.Origin.Z * 1000L;
            return at.X >= x0 && at.X < x0 + d.Size.X * 1000L && at.Z >= z0 && at.Z < z0 + d.Size.Z * 1000L;
        }

        // Cell of a millimetre position: floor(at / 1000) - origin, rounding toward negative infinity.
        internal static (long X, long Z) MmCell(MapMm at, MapXZ origin) => (FloorDiv(at.X) - origin.X, FloorDiv(at.Z) - origin.Z);

        private static long FloorDiv(long mm)
        {
            long q = mm / 1000;
            return mm % 1000 < 0 ? q - 1 : q;
        }
    }

    // Mutable working copy of one description. Every editor step fills a fresh draft from Current and builds a new MapDesc
    // only when the whole step succeeded, so a step that throws leaves Current as it was.
    internal sealed class MapEditDraft
    {
        internal const string Base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
        internal const char NoOwner = '.';
        internal const int MaxOwners = 64;

        private readonly MapDesc d;
        internal readonly int Width, Depth;
        internal readonly int[][] Heights;
        internal readonly char[][] SurfaceRows;
        internal readonly char[][] HeightOwner;
        internal readonly char[][] SurfaceOwner;
        internal readonly List<string> Owners;
        internal readonly List<MapStamp> Stamps;
        internal readonly List<MapSrc> Removed;
        internal readonly ulong[] Next;
        internal readonly List<MapWall> Walls;
        internal readonly List<MapPoint> Points;
        internal readonly List<MapBase> Bases;
        internal readonly List<MapUnit> Units;

        internal MapEditDraft(MapDesc desc)
        {
            d = desc;
            Width = desc.Size.X; Depth = desc.Size.Z;
            if (desc.Heights != null)
            {
                Heights = new int[desc.Heights.Count][];
                for (int j = 0; j < Heights.Length; j++)
                {
                    Heights[j] = new int[desc.Heights[j].Count];
                    for (int i = 0; i < Heights[j].Length; i++) Heights[j][i] = desc.Heights[j][i];
                }
                HeightOwner = Grid(desc.HeightOwner, Depth + 1, Width + 1);
            }
            if (desc.Surfaces != null)
            {
                SurfaceRows = Grid(desc.Surfaces.Rows, Depth, Width);
                SurfaceOwner = Grid(desc.SurfaceOwner, Depth, Width);
            }
            Owners = new List<string>(desc.Owners);
            Stamps = new List<MapStamp>(desc.Stamps);
            Removed = new List<MapSrc>(desc.Removed);
            Next = new[] { desc.Next.Wall, (ulong)desc.Next.Point, (ulong)desc.Next.Base, desc.Next.Unit };
            Walls = new List<MapWall>(desc.Walls);
            Points = new List<MapPoint>(desc.Points);
            Bases = new List<MapBase>(desc.Bases);
            Units = new List<MapUnit>(desc.Units);
        }

        internal MapXZ Origin => d.Origin;

        internal List<T> Items<T>(MapEditKind<T> kind) where T : class
        {
            if (kind.Slot == 0) return (List<T>)(object)Walls;
            if (kind.Slot == 1) return (List<T>)(object)Points;
            if (kind.Slot == 2) return (List<T>)(object)Bases;
            return (List<T>)(object)Units;
        }

        // Next id of the kind; the pool is full when next already is the type limit.
        internal ulong Allocate(string kind, int slot, ulong limit)
        {
            if (Next[slot] >= limit) throw new InvalidOperationException("MAPDESC_ID " + kind);
            return Next[slot]++;
        }

        internal static int OwnerIndex(char c) => c == NoOwner ? -1 : Base64.IndexOf(c);

        internal static char OwnerChar(int index) => Base64[index];

        internal MapDesc Build()
        {
            int[][] heights = Heights;
            MapSurfaces surfaces = null;
            if (SurfaceRows != null) surfaces = new MapSurfaces(Copy(d.Surfaces.Names), Rows(SurfaceRows));
            return new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, Copy(d.Layers), heights,
                surfaces, Walls.ToArray(), Points.ToArray(), Bases.ToArray(), Units.ToArray(), d.Run, Owners.ToArray(),
                HeightOwner == null ? null : Rows(HeightOwner), SurfaceOwner == null ? null : Rows(SurfaceOwner),
                Stamps.ToArray(), Removed.ToArray(), new MapNext(Next[0], (int)Next[1], (int)Next[2], Next[3]));
        }

        // A missing owner grid is all '.'.
        private static char[][] Grid(IReadOnlyList<string> rows, int count, int length)
        {
            var grid = new char[count][];
            for (int j = 0; j < count; j++)
            {
                grid[j] = rows == null ? new string(NoOwner, length).ToCharArray() : rows[j].ToCharArray();
            }
            return grid;
        }

        private static string[] Rows(char[][] grid)
        {
            var rows = new string[grid.Length];
            for (int j = 0; j < grid.Length; j++) rows[j] = new string(grid[j]);
            return rows;
        }

        private static T[] Copy<T>(IReadOnlyList<T> items)
        {
            var copy = new T[items.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = items[i];
            return copy;
        }
    }
}
