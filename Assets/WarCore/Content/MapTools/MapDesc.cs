using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WarCore.Content.MapTools
{
    // Integer pair on the metre grid: "size" [W, D], "origin" [x, z], wall "cell" and "size".
    public readonly struct MapXZ : IEquatable<MapXZ>
    {
        public int X { get; }
        public int Z { get; }
        public MapXZ(int x, int z) { X = x; Z = z; }
        public bool Equals(MapXZ other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is MapXZ other && Equals(other);
        public override int GetHashCode() => X * 397 ^ Z;
    }

    // Millimetre pair: "at" and "spawn" [x, z].
    public readonly struct MapMm : IEquatable<MapMm>
    {
        public long X { get; }
        public long Z { get; }
        public MapMm(long x, long z) { X = x; Z = z; }
        public bool Equals(MapMm other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is MapMm other && Equals(other);
        public override int GetHashCode() => X.GetHashCode() * 397 ^ Z.GetHashCode();
    }

    public sealed class MapLayer
    {
        public string Name { get; }
        public byte Bit { get; }
        public MapLayer(string name, byte bit) { Name = name; Bit = bit; }
    }

    // "surfaces": ordinal-ascending names and one row string per Z row (one base64 character per cell).
    public sealed class MapSurfaces
    {
        public IReadOnlyList<string> Names { get; }
        public IReadOnlyList<string> Rows { get; }
        public MapSurfaces(string[] names, string[] rows)
        {
            Names = MapDesc.Copy(names, nameof(names));
            Rows = MapDesc.Copy(rows, nameof(rows));
        }
    }

    // Element of "removed" and the optional "src" of an entry.
    public sealed class MapSrc
    {
        public string Instance { get; }
        public string Kind { get; }
        public ulong Src { get; }
        public MapSrc(string instance, string kind, ulong src) { Instance = instance; Kind = kind; Src = src; }
    }

    public sealed class MapStamp
    {
        public string Instance { get; }
        public string Kind { get; }
        public ulong Src { get; }
        // Typed by Kind: wall and unit ids are ulong, point and base ids are int.
        public ulong Id { get; }
        public MapStamp(string instance, string kind, ulong src, ulong id) { Instance = instance; Kind = kind; Src = src; Id = id; }
    }

    public sealed class MapWall
    {
        public ulong Id { get; }
        public string Kind { get; }
        public string Layer { get; }
        public MapXZ Cell { get; }
        public MapXZ Size { get; }
        public long HeightRaw { get; }
        public int? Owner { get; }
        public MapSrc Src { get; }
        // Absolute raw Y of the wall's bottom ("baseRaw", description version 2); null: the wall stands on floorRaw.
        public long? BaseRaw { get; }
        public MapWall(ulong id, string kind, string layer, MapXZ cell, MapXZ size, long heightRaw, int? owner = null, MapSrc src = null,
            long? baseRaw = null)
        {
            Id = id; Kind = kind; Layer = layer; Cell = cell; Size = size; HeightRaw = heightRaw; Owner = owner; Src = src; BaseRaw = baseRaw;
        }
    }

    public sealed class MapPoint
    {
        public int Id { get; }
        public MapMm At { get; }
        public int RadiusMm { get; }
        public int InitialOwner { get; }
        public MapMm? Spawn { get; }
        public int? Owner { get; }
        public MapSrc Src { get; }
        // The plate id the spawn stands on (version 2; only with a spawn); null = the ground.
        public ulong? SpawnPlate { get; }
        // The plate id the centre stands on (version 2, ML-9b); null = the ground.
        public ulong? Plate { get; }
        public MapPoint(int id, MapMm at, int radiusMm, int initialOwner, MapMm? spawn = null, int? owner = null, MapSrc src = null,
            ulong? spawnPlate = null, ulong? plate = null)
        {
            Id = id; At = at; RadiusMm = radiusMm; InitialOwner = initialOwner; Spawn = spawn; Owner = owner; Src = src; SpawnPlate = spawnPlate;
            Plate = plate;
        }
    }

    public sealed class MapBase
    {
        public int Id { get; }
        public int Team { get; }
        public MapMm At { get; }
        public int? Owner { get; }
        public MapSrc Src { get; }
        // The plate id the base stands on (version 2); null = the ground.
        public ulong? Plate { get; }
        public MapBase(int id, int team, MapMm at, int? owner = null, MapSrc src = null, ulong? plate = null)
        {
            Id = id; Team = team; At = at; Owner = owner; Src = src; Plate = plate;
        }
    }

    public sealed class MapUnit
    {
        public ulong Key { get; }
        public string Type { get; }
        public int Team { get; }
        public int Squad { get; }
        public MapMm At { get; }
        public bool Reserve { get; }
        public int? Owner { get; }
        public MapSrc Src { get; }
        // The plate id the unit stands on (version 2; not for reserves); null = the ground.
        public ulong? Plate { get; }
        public MapUnit(ulong key, string type, int team, int squad, MapMm at, bool reserve, int? owner = null, MapSrc src = null, ulong? plate = null)
        {
            Key = key; Type = type; Team = team; Squad = squad; At = at; Reserve = reserve; Owner = owner; Src = src; Plate = plate;
        }
    }

    public sealed class MapRun
    {
        public ulong Seed { get; }
        public string CrowdPass { get; }
        public MapRun(ulong seed, string crowdPass) { Seed = seed; CrowdPass = crowdPass; }
    }

    public sealed class MapNext
    {
        public ulong Wall { get; }
        public int Point { get; }
        public int Base { get; }
        public ulong Unit { get; }
        // Version 2: written only when above 1.
        public ulong Plate { get; }
        public ulong Cutout { get; }
        public MapNext(ulong wall, int point, int @base, ulong unit) : this(wall, point, @base, unit, 1, 1) { }
        public MapNext(ulong wall, int point, int @base, ulong unit, ulong plate, ulong cutout)
        {
            Wall = wall; Point = point; Base = @base; Unit = unit; Plate = plate; Cutout = cutout;
        }
    }

    // "slope" of a plate: the top rises along Axis ("x" or "z") from FromRaw on the plate's minimum edge to ToRaw on its
    // maximum edge (absolute raw Y).
    public sealed class MapSlope
    {
        public string Axis { get; }
        public long FromRaw { get; }
        public long ToRaw { get; }
        public MapSlope(string axis, long fromRaw, long toRaw) { Axis = axis; FromRaw = fromRaw; ToRaw = toRaw; }
    }

    // Floor plate (description version 2): a whole-cell rectangle; YRaw (flat top) or Slope, exactly one; absolute raw Y.
    public sealed class MapPlate
    {
        public ulong Id { get; }
        public MapXZ Cell { get; }
        public MapXZ Size { get; }
        public long? YRaw { get; }
        public MapSlope Slope { get; }
        public long ThickRaw { get; }
        public string Surface { get; }
        public int? Owner { get; }
        public MapSrc Src { get; }
        public MapPlate(ulong id, MapXZ cell, MapXZ size, long? yRaw, MapSlope slope, long thickRaw, string surface = null,
            int? owner = null, MapSrc src = null)
        {
            Id = id; Cell = cell; Size = size; YRaw = yRaw; Slope = slope; ThickRaw = thickRaw; Surface = surface; Owner = owner; Src = src;
        }
    }

    // Cutout (description version 2): cells [Cell, Cell + Size) of plate Plate (an id) are not solid; cells outside the
    // plate are ignored.
    public sealed class MapCutout
    {
        public ulong Id { get; }
        public ulong Plate { get; }
        public MapXZ Cell { get; }
        public MapXZ Size { get; }
        public int? Owner { get; }
        public MapSrc Src { get; }
        public MapCutout(ulong id, ulong plate, MapXZ cell, MapXZ size, int? owner = null, MapSrc src = null)
        {
            Id = id; Plate = plate; Cell = cell; Size = size; Owner = owner; Src = src;
        }
    }

    // Map description model (format "warcore.map" version 1, or 2 when it uses something version 1 cannot hold, see
    // NeedsVersion2). Deeply immutable: every
    // input array is copied, including the inner rows of Heights, and every collection is handed out read-only. One
    // property per top-level JSON key. Optional keys are null when absent. An owner grid made only of '.' is the same as
    // no grid and is stored as null.
    public sealed class MapDesc
    {
        public const string FormatName = "warcore.map";
        public const int FormatVersion = 1;
        // Written only when the description uses something version 1 cannot hold (NeedsVersion2).
        public const int WallBaseVersion = 2;
        public const int MaxOwnersVersion1 = 64;
        public const int MaxOwners = 4096;

        public string Format => FormatName;
        public int Version { get; }
        public string Id { get; }
        public string Content { get; }
        public string Pack { get; }
        public MapXZ Size { get; }
        public MapXZ Origin { get; }
        public long FloorRaw { get; }
        public long CeilingRaw { get; }
        public IReadOnlyList<MapLayer> Layers { get; }
        // Z rows (Depth + 1) of X points (Width + 1), raw Y.
        public IReadOnlyList<IReadOnlyList<int>> Heights { get; }
        public MapSurfaces Surfaces { get; }
        public IReadOnlyList<MapPlate> Plates { get; }
        public IReadOnlyList<MapCutout> Cutouts { get; }
        public IReadOnlyList<MapWall> Walls { get; }
        public IReadOnlyList<MapPoint> Points { get; }
        public IReadOnlyList<MapBase> Bases { get; }
        public IReadOnlyList<MapUnit> Units { get; }
        public MapRun Run { get; }
        public IReadOnlyList<string> Owners { get; }
        public IReadOnlyList<string> HeightOwner { get; }
        public IReadOnlyList<string> SurfaceOwner { get; }
        public IReadOnlyList<MapStamp> Stamps { get; }
        public IReadOnlyList<MapSrc> Removed { get; }
        public MapNext Next { get; }

        public MapDesc(string id, string content, string pack, MapXZ size, MapXZ origin, long floorRaw, long ceilingRaw,
            MapLayer[] layers, int[][] heights, MapSurfaces surfaces, MapWall[] walls, MapPoint[] points, MapBase[] bases,
            MapUnit[] units, MapRun run, string[] owners, string[] heightOwner, string[] surfaceOwner, MapStamp[] stamps,
            MapSrc[] removed, MapNext next)
            : this(id, content, pack, size, origin, floorRaw, ceilingRaw, layers, heights, surfaces, new MapPlate[0], new MapCutout[0],
                walls, points, bases, units, run, owners, heightOwner, surfaceOwner, stamps, removed, next)
        {
        }

        public MapDesc(string id, string content, string pack, MapXZ size, MapXZ origin, long floorRaw, long ceilingRaw,
            MapLayer[] layers, int[][] heights, MapSurfaces surfaces, MapPlate[] plates, MapCutout[] cutouts, MapWall[] walls,
            MapPoint[] points, MapBase[] bases, MapUnit[] units, MapRun run, string[] owners, string[] heightOwner,
            string[] surfaceOwner, MapStamp[] stamps, MapSrc[] removed, MapNext next)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Pack = pack ?? throw new ArgumentNullException(nameof(pack));
            Size = size; Origin = origin; FloorRaw = floorRaw; CeilingRaw = ceilingRaw;
            Layers = Copy(layers, nameof(layers));
            if (heights != null)
            {
                var rows = new IReadOnlyList<int>[heights.Length];
                for (int j = 0; j < heights.Length; j++) rows[j] = Copy(heights[j], nameof(heights));
                Heights = new ReadOnlyCollection<IReadOnlyList<int>>(rows);
            }
            Surfaces = surfaces;
            Plates = Copy(plates, nameof(plates));
            Cutouts = Copy(cutouts, nameof(cutouts));
            Walls = Copy(walls, nameof(walls));
            Points = Copy(points, nameof(points));
            Bases = Copy(bases, nameof(bases));
            Units = Copy(units, nameof(units));
            Run = run ?? throw new ArgumentNullException(nameof(run));
            Owners = Copy(owners, nameof(owners));
            HeightOwner = OwnerGrid(heightOwner);
            SurfaceOwner = OwnerGrid(surfaceOwner);
            Stamps = Copy(stamps, nameof(stamps));
            Removed = Copy(removed, nameof(removed));
            Next = next ?? throw new ArgumentNullException(nameof(next));
            Version = NeedsVersion2 ? WallBaseVersion : FormatVersion;
        }

        // Kinds of entries version 1 does not know; their rows in stamps or removed need version 2.
        internal static bool IsVersion2Kind(string kind) => kind == "plate" || kind == "cutout";

        // Anything version 1 cannot hold. MapDescReader.UsesVersion2 makes the same decision on the bare JSON tree.
        public bool NeedsVersion2
        {
            get
            {
                foreach (MapWall wall in Walls)
                    if (wall.BaseRaw.HasValue) return true;
                if (Plates.Count > 0 || Cutouts.Count > 0 || Owners.Count > MaxOwnersVersion1) return true;
                foreach (MapStamp row in Stamps)
                    if (IsVersion2Kind(row.Kind)) return true;
                foreach (MapSrc row in Removed)
                    if (IsVersion2Kind(row.Kind)) return true;
                foreach (MapPoint point in Points)
                    if (point.SpawnPlate.HasValue || point.Plate.HasValue) return true;
                foreach (MapBase anchor in Bases)
                    if (anchor.Plate.HasValue) return true;
                foreach (MapUnit unit in Units)
                    if (unit.Plate.HasValue) return true;
                return Next.Plate > 1 || Next.Cutout > 1;
            }
        }

        internal static ReadOnlyCollection<T> Copy<T>(T[] items, string name) =>
            new ReadOnlyCollection<T>((T[])(items ?? throw new ArgumentNullException(name)).Clone());

        private static IReadOnlyList<string> OwnerGrid(string[] rows)
        {
            if (rows == null) return null;
            foreach (string row in rows)
                if (row == null || row.Trim('.').Length != 0) return Copy(rows, nameof(rows));
            return null;
        }
    }
}
