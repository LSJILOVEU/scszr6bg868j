namespace WarCore.Presentation
{
    // ED-6a: plain-value copies of the description's entries for the editor (field for field the entries of MapDesc). Every
    // id is a ulong (points and bases are int in the description; the facade converts and refuses ids above int.MaxValue).
    // An optional field is HasX plus X; X is 0 when HasX is false and is ignored on writing. Owner is "hand",
    // "stamp:<instance>" or "" (unowned); Stamp is the src instance or "" (no src). Each record has two constructors: the
    // full one (what the read side returns) and one without Id, Owner and Stamp (for adding). Adding ignores Id, Owner and
    // Stamp; replacing ignores Owner and Stamp.

    public sealed class MapWallInfo
    {
        public ulong Id { get; }
        public string Kind { get; }
        public string Layer { get; }
        public int CellX { get; }
        public int CellZ { get; }
        public int SizeX { get; }
        public int SizeZ { get; }
        public long HeightRaw { get; }
        public bool HasBase { get; }
        public long BaseRaw { get; }
        public string Owner { get; }
        public string Stamp { get; }

        public MapWallInfo(ulong id, string kind, string layer, int cellX, int cellZ, int sizeX, int sizeZ, long heightRaw, bool hasBase,
            long baseRaw, string owner, string stamp)
        {
            Id = id; Kind = kind; Layer = layer; CellX = cellX; CellZ = cellZ; SizeX = sizeX; SizeZ = sizeZ; HeightRaw = heightRaw;
            HasBase = hasBase; BaseRaw = hasBase ? baseRaw : 0; Owner = owner ?? ""; Stamp = stamp ?? "";
        }

        public MapWallInfo(string kind, string layer, int cellX, int cellZ, int sizeX, int sizeZ, long heightRaw, bool hasBase, long baseRaw)
            : this(0, kind, layer, cellX, cellZ, sizeX, sizeZ, heightRaw, hasBase, baseRaw, "", "")
        {
        }
    }

    public sealed class MapPointInfo
    {
        public ulong Id { get; }
        public long XMm { get; }
        public long ZMm { get; }
        public int RadiusMm { get; }
        public int InitialOwner { get; }
        public bool HasSpawn { get; }
        public long SpawnXMm { get; }
        public long SpawnZMm { get; }
        public bool HasSpawnPlate { get; }
        public ulong SpawnPlate { get; }
        public bool HasPlate { get; }
        public ulong Plate { get; }
        public string Owner { get; }
        public string Stamp { get; }

        public MapPointInfo(ulong id, long xMm, long zMm, int radiusMm, int initialOwner, bool hasSpawn, long spawnXMm, long spawnZMm,
            bool hasSpawnPlate, ulong spawnPlate, bool hasPlate, ulong plate, string owner, string stamp)
        {
            Id = id; XMm = xMm; ZMm = zMm; RadiusMm = radiusMm; InitialOwner = initialOwner;
            HasSpawn = hasSpawn; SpawnXMm = hasSpawn ? spawnXMm : 0; SpawnZMm = hasSpawn ? spawnZMm : 0;
            HasSpawnPlate = hasSpawnPlate; SpawnPlate = hasSpawnPlate ? spawnPlate : 0;
            HasPlate = hasPlate; Plate = hasPlate ? plate : 0;
            Owner = owner ?? ""; Stamp = stamp ?? "";
        }

        public MapPointInfo(long xMm, long zMm, int radiusMm, int initialOwner, bool hasSpawn, long spawnXMm, long spawnZMm,
            bool hasSpawnPlate, ulong spawnPlate, bool hasPlate, ulong plate)
            : this(0, xMm, zMm, radiusMm, initialOwner, hasSpawn, spawnXMm, spawnZMm, hasSpawnPlate, spawnPlate, hasPlate, plate, "", "")
        {
        }
    }

    public sealed class MapBaseInfo
    {
        public ulong Id { get; }
        public int Team { get; }
        public long XMm { get; }
        public long ZMm { get; }
        public bool HasPlate { get; }
        public ulong Plate { get; }
        public string Owner { get; }
        public string Stamp { get; }

        public MapBaseInfo(ulong id, int team, long xMm, long zMm, bool hasPlate, ulong plate, string owner, string stamp)
        {
            Id = id; Team = team; XMm = xMm; ZMm = zMm; HasPlate = hasPlate; Plate = hasPlate ? plate : 0; Owner = owner ?? ""; Stamp = stamp ?? "";
        }

        public MapBaseInfo(int team, long xMm, long zMm, bool hasPlate, ulong plate) : this(0, team, xMm, zMm, hasPlate, plate, "", "")
        {
        }
    }

    public sealed class MapUnitInfo
    {
        public ulong Key { get; }
        public string Type { get; }
        public int Team { get; }
        public int Squad { get; }
        public long XMm { get; }
        public long ZMm { get; }
        public bool Reserve { get; }
        public bool HasPlate { get; }
        public ulong Plate { get; }
        public string Owner { get; }
        public string Stamp { get; }

        public MapUnitInfo(ulong key, string type, int team, int squad, long xMm, long zMm, bool reserve, bool hasPlate, ulong plate,
            string owner, string stamp)
        {
            Key = key; Type = type; Team = team; Squad = squad; XMm = xMm; ZMm = zMm; Reserve = reserve;
            HasPlate = hasPlate; Plate = hasPlate ? plate : 0; Owner = owner ?? ""; Stamp = stamp ?? "";
        }

        public MapUnitInfo(string type, int team, int squad, long xMm, long zMm, bool reserve, bool hasPlate, ulong plate)
            : this(0, type, team, squad, xMm, zMm, reserve, hasPlate, plate, "", "")
        {
        }
    }

    // Sloped false: a flat top at YRaw (SlopeAxis "", FromRaw and ToRaw 0). Sloped true: the top rises along SlopeAxis ("x"
    // or "z") from FromRaw to ToRaw (YRaw 0). Surface "" means none.
    public sealed class MapPlateInfo
    {
        public ulong Id { get; }
        public int CellX { get; }
        public int CellZ { get; }
        public int SizeX { get; }
        public int SizeZ { get; }
        public bool Sloped { get; }
        public long YRaw { get; }
        public string SlopeAxis { get; }
        public long FromRaw { get; }
        public long ToRaw { get; }
        public long ThickRaw { get; }
        public string Surface { get; }
        public string Owner { get; }
        public string Stamp { get; }

        public MapPlateInfo(ulong id, int cellX, int cellZ, int sizeX, int sizeZ, bool sloped, long yRaw, string slopeAxis, long fromRaw,
            long toRaw, long thickRaw, string surface, string owner, string stamp)
        {
            Id = id; CellX = cellX; CellZ = cellZ; SizeX = sizeX; SizeZ = sizeZ; Sloped = sloped;
            YRaw = sloped ? 0 : yRaw;
            SlopeAxis = sloped ? slopeAxis ?? "" : "";
            FromRaw = sloped ? fromRaw : 0;
            ToRaw = sloped ? toRaw : 0;
            ThickRaw = thickRaw; Surface = surface ?? ""; Owner = owner ?? ""; Stamp = stamp ?? "";
        }

        public MapPlateInfo(int cellX, int cellZ, int sizeX, int sizeZ, bool sloped, long yRaw, string slopeAxis, long fromRaw, long toRaw,
            long thickRaw, string surface)
            : this(0, cellX, cellZ, sizeX, sizeZ, sloped, yRaw, slopeAxis, fromRaw, toRaw, thickRaw, surface, "", "")
        {
        }
    }

    public sealed class MapCutoutInfo
    {
        public ulong Id { get; }
        public ulong Plate { get; }
        public int CellX { get; }
        public int CellZ { get; }
        public int SizeX { get; }
        public int SizeZ { get; }
        public string Owner { get; }
        public string Stamp { get; }

        public MapCutoutInfo(ulong id, ulong plate, int cellX, int cellZ, int sizeX, int sizeZ, string owner, string stamp)
        {
            Id = id; Plate = plate; CellX = cellX; CellZ = cellZ; SizeX = sizeX; SizeZ = sizeZ; Owner = owner ?? ""; Stamp = stamp ?? "";
        }

        public MapCutoutInfo(ulong plate, int cellX, int cellZ, int sizeX, int sizeZ) : this(0, plate, cellX, cellZ, sizeX, sizeZ, "", "")
        {
        }
    }

    // One entry found by MapDocument.EntriesIn: Kind is "wall", "point", "base", "unit", "plate" or "cutout".
    public sealed class MapEntryRef
    {
        public string Kind { get; }
        public ulong Id { get; }
        public MapEntryRef(string kind, ulong id) { Kind = kind; Id = id; }
    }
}
