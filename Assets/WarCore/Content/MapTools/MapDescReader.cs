using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using WarCore.Sim;

namespace WarCore.Content.MapTools
{
    // Decimal integer text split into sign and magnitude, so every range is judged before any narrowing.
    internal readonly struct MapNum
    {
        public readonly bool Negative;
        public readonly ulong Magnitude;
        public readonly bool Huge; // above ulong.MaxValue

        private MapNum(bool negative, ulong magnitude, bool huge) { Negative = negative; Magnitude = magnitude; Huge = huge; }

        public static MapNum Parse(string text)
        {
            bool negative = text[0] == '-';
            ulong magnitude = 0;
            for (int i = negative ? 1 : 0; i < text.Length; i++)
            {
                uint digit = (uint)(text[i] - '0');
                if (magnitude > (ulong.MaxValue - digit) / 10) return new MapNum(negative, 0, true);
                magnitude = magnitude * 10 + digit;
            }
            return new MapNum(negative, magnitude, false);
        }

        public bool TryLong(out long value)
        {
            value = 0;
            if (Huge) return false;
            if (Negative)
            {
                if (Magnitude > 9223372036854775808UL) return false;
                value = Magnitude == 9223372036854775808UL ? long.MinValue : -(long)Magnitude;
                return true;
            }
            if (Magnitude > long.MaxValue) return false;
            value = (long)Magnitude;
            return true;
        }

        public bool TryULong(out ulong value)
        {
            value = Magnitude;
            return !Huge && !Negative;
        }
    }

    // Checks a parsed description in the frozen order (lexis, format and version, pass 1, pass 2) and builds the model.
    // Every error is an InvalidDataException "<code> [<entry> [<id>]] line <n>".
    internal sealed partial class MapDescReader
    {
        internal const string Format = "MAPDESC_FORMAT", Key = "MAPDESC_KEY", SizeCode = "MAPDESC_SIZE", Grid = "MAPDESC_GRID",
            IdCode = "MAPDESC_ID", Team = "MAPDESC_TEAM", WallCode = "MAPDESC_WALL", PointCode = "MAPDESC_POINT", OwnerCode = "MAPDESC_OWNER",
            PlateCode = "MAPDESC_PLATE";
        // Closed bound: HeightField.HeightLimitRaw is a strict upper bound, so the largest accepted height is one less.
        internal const int HeightLimit = (int)(HeightField.HeightLimitRaw - 1);
        internal static readonly string[] Kinds = { "wall", "point", "base", "unit" };
        internal static readonly string[] Kinds2 = { "wall", "point", "base", "unit", "plate", "cutout" };
        private static readonly string[] TopKeys = { "format", "version", "id", "content", "pack", "size", "origin", "floorRaw",
            "ceilingRaw", "layers", "heights", "surfaces", "walls", "points", "bases", "units", "run", "owners", "heightOwner",
            "surfaceOwner", "stamps", "removed", "next" };
        private static readonly string[] TopKeys2 = { "format", "version", "id", "content", "pack", "size", "origin", "floorRaw",
            "ceilingRaw", "layers", "heights", "surfaces", "plates", "cutouts", "walls", "points", "bases", "units", "run", "owners",
            "heightOwner", "surfaceOwner", "stamps", "removed", "next" };

        private readonly JsonNode root;
        private string entry, entryId;
        private int version;

        internal MapDescReader(JsonNode root) { this.root = root; }

        internal static InvalidDataException Plain(string code, Exception inner = null) => new InvalidDataException(code, inner);

        private InvalidDataException Fail(string code, int line) => new InvalidDataException(code
            + (entry == null ? "" : " " + entry + (entryId == null ? "" : " " + entryId)) + " line " + line.ToString(CultureInfo.InvariantCulture));

        internal MapDesc Read()
        {
            Lexis(root);
            if (root.Kind != JsonKind.Object) throw Fail(Format, root.Line);
            JsonNode format = root.Find("format"), version = root.Find("version");
            if (format == null) throw Fail(Format, root.Line);
            if (format.Kind != JsonKind.String || format.Text != MapDesc.FormatName) throw Fail(Format, format.Line);
            if (version == null) throw Fail(Format, root.Line);
            if (version.Kind != JsonKind.Number || (version.Text != "1" && version.Text != "2")) throw Fail(Format, version.Line);
            this.version = version.Text == "2" ? MapDesc.WallBaseVersion : MapDesc.FormatVersion;
            // Version 2 only for what version 1 cannot hold, checked on the bare tree before pass 1: a version 2 file must
            // use a version 2 key ("baseRaw" on a wall object); otherwise it is a version error, so writer and reader agree.
            if (this.version == MapDesc.WallBaseVersion && !UsesVersion2(root)) throw Fail(Format, version.Line);
            MapDesc desc = First();
            Second(desc);
            return desc;
        }

        // The bare-tree form of MapDesc.NeedsVersion2: a wall "baseRaw"; non-empty "plates" or "cutouts"; more than 64 owners;
        // a stamps or removed row of kind "plate" or "cutout"; a point "spawnPlate" or "plate", a base or unit "plate"; next.plate or
        // next.cutout other than 1.
        private static bool UsesVersion2(JsonNode root)
        {
            JsonNode walls = root.Find("walls");
            if (walls != null && walls.Kind == JsonKind.Array)
                foreach (JsonNode wall in walls.Items)
                    if (wall.Kind == JsonKind.Object && wall.Find("baseRaw") != null) return true;
            if (NonEmpty(root.Find("plates")) || NonEmpty(root.Find("cutouts"))) return true;
            JsonNode owners = root.Find("owners");
            if (owners != null && owners.Kind == JsonKind.Array && owners.Items.Count > MapDesc.MaxOwnersVersion1) return true;
            if (HasVersion2Kind(root.Find("stamps")) || HasVersion2Kind(root.Find("removed"))) return true;
            if (HasKey(root.Find("points"), "spawnPlate") || HasKey(root.Find("points"), "plate") || HasKey(root.Find("bases"), "plate") || HasKey(root.Find("units"), "plate")) return true;
            JsonNode next = root.Find("next");
            if (next == null || next.Kind != JsonKind.Object) return false;
            return NotOne(next.Find("plate")) || NotOne(next.Find("cutout"));
        }

        private static bool HasKey(JsonNode rows, string key)
        {
            if (rows == null || rows.Kind != JsonKind.Array) return false;
            foreach (JsonNode row in rows.Items)
                if (row.Kind == JsonKind.Object && row.Find(key) != null) return true;
            return false;
        }

        private static bool NonEmpty(JsonNode node) => node != null && node.Kind == JsonKind.Array && node.Items.Count > 0;

        private static bool NotOne(JsonNode node) => node != null && !(node.Kind == JsonKind.Number && node.Text == "1");

        private static bool HasVersion2Kind(JsonNode rows)
        {
            if (rows == null || rows.Kind != JsonKind.Array) return false;
            foreach (JsonNode row in rows.Items)
            {
                JsonNode kind = row.Kind == JsonKind.Object ? row.Find("kind") : null;
                if (kind != null && kind.Kind == JsonKind.String && MapDesc.IsVersion2Kind(kind.Text)) return true;
            }
            return false;
        }

        private bool Version2 => version == MapDesc.WallBaseVersion;
        private string[] KnownKinds => Version2 ? Kinds2 : Kinds;

        // Whole tree in document order, before the version value: a number with a fraction or "-0" is a key error.
        private void Lexis(JsonNode node)
        {
            switch (node.Kind)
            {
                case JsonKind.Number:
                    if (node.Text.IndexOf('.') >= 0 || node.Text == "-0") throw Fail(Key, node.Line);
                    break;
                case JsonKind.Object:
                    foreach (JsonMember member in node.Members) Lexis(member.Value);
                    break;
                case JsonKind.Array:
                    foreach (JsonNode item in node.Items) Lexis(item);
                    break;
            }
        }

        // ---- pass 1 helpers: unknown keys (document order), then missing key, type and single-value range per key ----

        private void Keys(JsonNode obj, params string[] keys)
        {
            foreach (JsonMember member in obj.Members)
                if (Array.IndexOf(keys, member.Name) < 0) throw Fail(Key, member.Value.Line);
        }

        private JsonNode Req(JsonNode obj, string name, JsonKind kind)
        {
            JsonNode value = obj.Find(name);
            if (value == null) throw Fail(Key, obj.Line);
            return Item(value, kind);
        }

        private JsonNode Opt(JsonNode obj, string name, JsonKind kind)
        {
            JsonNode value = obj.Find(name);
            return value == null ? null : Item(value, kind);
        }

        // Type check; a string value that holds a lone surrogate is a key error too (checked as the string is read).
        private JsonNode Item(JsonNode item, JsonKind kind)
        {
            if (item.Kind != kind) throw Fail(Key, item.Line);
            if (kind == JsonKind.String)
            {
                string s = item.Text;
                for (int i = 0; i < s.Length; i++)
                {
                    if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { i++; continue; }
                    if (char.IsSurrogate(s[i])) throw Fail(Key, item.Line);
                }
            }
            return item;
        }

        private long Long(JsonNode n, long min, long max, string code)
        {
            Item(n, JsonKind.Number);
            if (!MapNum.Parse(n.Text).TryLong(out long value) || value < min || value > max) throw Fail(code, n.Line);
            return value;
        }

        private int Int(JsonNode n, int min, int max, string code)
        {
            Item(n, JsonKind.Number);
            if (!MapNum.Parse(n.Text).TryLong(out long value) || value < min || value > max) throw Fail(code, n.Line);
            return (int)value;
        }

        private ulong ULong(JsonNode n, ulong min, string code)
        {
            Item(n, JsonKind.Number);
            if (!MapNum.Parse(n.Text).TryULong(out ulong value) || value < min) throw Fail(code, n.Line);
            return value;
        }

        private JsonNode Pair(JsonNode arr)
        {
            // Element types are checked with their ranges, element by element (Int, Long).
            if (arr.Items.Count != 2) throw Fail(Key, arr.Line);
            return arr;
        }

        private MapXZ IntPair(JsonNode arr, int min, string code)
        {
            Pair(arr);
            return new MapXZ(Int(arr.Items[0], min, int.MaxValue, code), Int(arr.Items[1], min, int.MaxValue, code));
        }

        private MapMm MmPair(JsonNode arr)
        {
            Pair(arr);
            return new MapMm(Long(arr.Items[0], long.MinValue, long.MaxValue, PointCode), Long(arr.Items[1], long.MinValue, long.MaxValue, PointCode));
        }

        private string[] Strings(JsonNode arr)
        {
            var items = new string[arr.Items.Count];
            for (int i = 0; i < items.Length; i++) items[i] = Item(arr.Items[i], JsonKind.String).Text;
            return items;
        }

        // The entry's own id (key for units) when it is already a legal integer of its type; otherwise no id in messages.
        private void Enter(string name, JsonNode element, string idName, bool wide)
        {
            entry = name; entryId = null;
            if (idName == null || element.Kind != JsonKind.Object) return;
            JsonNode id = element.Find(idName);
            if (id == null || id.Kind != JsonKind.Number) return;
            MapNum num = MapNum.Parse(id.Text);
            if (wide ? num.TryULong(out ulong u) && u >= 1 : num.TryLong(out long v) && v >= 1 && v <= int.MaxValue)
                entryId = num.Magnitude.ToString(CultureInfo.InvariantCulture);
        }

        private void Leave() { entry = null; entryId = null; }

        private static bool Wide(string kind) => kind == "wall" || kind == "unit" || kind == "plate" || kind == "cutout";

        private void EnterStamp(JsonNode element)
        {
            JsonNode kind = element.Kind == JsonKind.Object ? element.Find("kind") : null;
            bool known = kind != null && kind.Kind == JsonKind.String && Array.IndexOf(KnownKinds, kind.Text) >= 0;
            Enter("stamp", element, known ? "id" : null, known && Wide(kind.Text));
        }

        private MapSrc Src(JsonNode obj)
        {
            Keys(obj, "instance", "kind", "src");
            return SrcFields(obj);
        }

        // instance, kind, src of a "src", "removed" or "stamps" object (unknown keys already checked by the caller).
        private MapSrc SrcFields(JsonNode obj)
        {
            JsonNode instance = Req(obj, "instance", JsonKind.String);
            if (instance.Text.Length == 0) throw Fail(OwnerCode, instance.Line);
            JsonNode kind = Req(obj, "kind", JsonKind.String);
            if (Array.IndexOf(KnownKinds, kind.Text) < 0) throw Fail(OwnerCode, kind.Line);
            ulong src = ULong(Req(obj, "src", JsonKind.Number), 0, OwnerCode);
            return new MapSrc(instance.Text, kind.Text, src);
        }

        private int? Owner(JsonNode obj)
        {
            JsonNode owner = Opt(obj, "owner", JsonKind.Number);
            return owner == null ? (int?)null : Int(owner, 0, int.MaxValue, OwnerCode);
        }

        // A plate id reference (version 2 keys only reach here): absent = the ground; the id must exist (pass 2).
        private ulong? PlateRef(JsonNode obj, string name)
        {
            JsonNode plate = Opt(obj, name, JsonKind.Number);
            return plate == null ? (ulong?)null : ULong(plate, 1, PlateCode);
        }

        private MapSrc OptSrc(JsonNode obj)
        {
            JsonNode src = Opt(obj, "src", JsonKind.Object);
            return src == null ? null : Src(src);
        }

        // ---- pass 1 ----

        private MapDesc First()
        {
            Keys(root, Version2 ? TopKeys2 : TopKeys);
            string id = Req(root, "id", JsonKind.String).Text;
            string content = Req(root, "content", JsonKind.String).Text;
            string pack = Req(root, "pack", JsonKind.String).Text;
            JsonNode sizeNode = Pair(Req(root, "size", JsonKind.Array));
            var size = new MapXZ(Int(sizeNode.Items[0], 1, WcMapValidation.MaxSide, SizeCode), Int(sizeNode.Items[1], 1, WcMapValidation.MaxSide, SizeCode));
            JsonNode originNode = Pair(Req(root, "origin", JsonKind.Array));
            var origin = new MapXZ(Int(originNode.Items[0], int.MinValue, int.MaxValue, SizeCode), Int(originNode.Items[1], int.MinValue, int.MaxValue, SizeCode));
            long floor = Long(Req(root, "floorRaw", JsonKind.Number), long.MinValue, long.MaxValue, SizeCode);
            long ceiling = Long(Req(root, "ceilingRaw", JsonKind.Number), long.MinValue, long.MaxValue, SizeCode);

            JsonNode layersNode = Req(root, "layers", JsonKind.Array);
            var layers = new MapLayer[layersNode.Items.Count];
            for (int i = 0; i < layers.Length; i++)
            {
                JsonNode e = layersNode.Items[i];
                Enter("layer", e, null, false);
                Item(e, JsonKind.Object);
                Keys(e, "name", "bit");
                string name = Req(e, "name", JsonKind.String).Text;
                layers[i] = new MapLayer(name, (byte)Int(Req(e, "bit", JsonKind.Number), 0, 255, Key));
                Leave();
            }

            int[][] heights = null;
            JsonNode heightsNode = Opt(root, "heights", JsonKind.Array);
            if (heightsNode != null)
            {
                heights = new int[heightsNode.Items.Count][];
                for (int j = 0; j < heights.Length; j++)
                {
                    JsonNode row = Item(heightsNode.Items[j], JsonKind.Array);
                    heights[j] = new int[row.Items.Count];
                    for (int i = 0; i < heights[j].Length; i++) heights[j][i] = Int(row.Items[i], -HeightLimit, HeightLimit, Grid);
                }
            }

            MapSurfaces surfaces = null;
            JsonNode surfacesNode = Opt(root, "surfaces", JsonKind.Object);
            if (surfacesNode != null)
            {
                Keys(surfacesNode, "names", "rows");
                JsonNode namesNode = Req(surfacesNode, "names", JsonKind.Array);
                string[] names = Strings(namesNode);
                if (names.Length > 64) throw Fail(Grid, namesNode.Line);
                for (int i = 1; i < names.Length; i++)
                    if (string.CompareOrdinal(names[i - 1], names[i]) >= 0) throw Fail(Grid, namesNode.Items[i].Line);
                surfaces = new MapSurfaces(names, Strings(Req(surfacesNode, "rows", JsonKind.Array)));
            }

            MapPlate[] plates = Plates();
            MapCutout[] cutouts = Cutouts();

            JsonNode wallsNode = Req(root, "walls", JsonKind.Array);
            var walls = new MapWall[wallsNode.Items.Count];
            for (int i = 0; i < walls.Length; i++)
            {
                JsonNode e = wallsNode.Items[i];
                Enter("wall", e, "id", true);
                Item(e, JsonKind.Object);
                if (version == MapDesc.WallBaseVersion) Keys(e, "id", "kind", "layer", "cell", "size", "heightRaw", "baseRaw", "owner", "src");
                else Keys(e, "id", "kind", "layer", "cell", "size", "heightRaw", "owner", "src");
                ulong wallId = ULong(Req(e, "id", JsonKind.Number), 1, IdCode);
                string kind = Req(e, "kind", JsonKind.String).Text;
                string layer = Req(e, "layer", JsonKind.String).Text;
                MapXZ cell = IntPair(Req(e, "cell", JsonKind.Array), 0, WallCode);
                MapXZ wallSize = IntPair(Req(e, "size", JsonKind.Array), 1, WallCode);
                JsonNode heightNode = Req(e, "heightRaw", JsonKind.Number);
                long height = Long(heightNode, 1, long.MaxValue, WallCode);
                if (height % 2 != 0) throw Fail(WallCode, heightNode.Line);
                JsonNode baseNode = Opt(e, "baseRaw", JsonKind.Number);
                long? wallBase = baseNode == null ? (long?)null : Long(baseNode, long.MinValue, long.MaxValue, WallCode);
                walls[i] = new MapWall(wallId, kind, layer, cell, wallSize, height, Owner(e), OptSrc(e), wallBase);
                Leave();
            }

            JsonNode pointsNode = Req(root, "points", JsonKind.Array);
            var points = new MapPoint[pointsNode.Items.Count];
            for (int i = 0; i < points.Length; i++)
            {
                JsonNode e = pointsNode.Items[i];
                Enter("point", e, "id", false);
                Item(e, JsonKind.Object);
                if (Version2) Keys(e, "id", "at", "radiusMm", "initialOwner", "spawn", "spawnPlate", "plate", "owner", "src");
                else Keys(e, "id", "at", "radiusMm", "initialOwner", "spawn", "owner", "src");
                int pointId = Int(Req(e, "id", JsonKind.Number), 1, int.MaxValue, IdCode);
                MapMm at = MmPair(Req(e, "at", JsonKind.Array));
                int radius = Int(Req(e, "radiusMm", JsonKind.Number), 1, int.MaxValue, PointCode);
                int initialOwner = Int(Req(e, "initialOwner", JsonKind.Number), 0, 2, Team);
                JsonNode spawnNode = Opt(e, "spawn", JsonKind.Array);
                MapMm? spawn = spawnNode == null ? (MapMm?)null : MmPair(spawnNode);
                points[i] = new MapPoint(pointId, at, radius, initialOwner, spawn, Owner(e), OptSrc(e), PlateRef(e, "spawnPlate"), PlateRef(e, "plate"));
                Leave();
            }

            JsonNode basesNode = Req(root, "bases", JsonKind.Array);
            var bases = new MapBase[basesNode.Items.Count];
            for (int i = 0; i < bases.Length; i++)
            {
                JsonNode e = basesNode.Items[i];
                Enter("base", e, "id", false);
                Item(e, JsonKind.Object);
                if (Version2) Keys(e, "id", "team", "at", "plate", "owner", "src");
                else Keys(e, "id", "team", "at", "owner", "src");
                int baseId = Int(Req(e, "id", JsonKind.Number), 1, int.MaxValue, IdCode);
                int team = Int(Req(e, "team", JsonKind.Number), 1, 2, Team);
                MapMm baseAt = MmPair(Req(e, "at", JsonKind.Array));
                bases[i] = new MapBase(baseId, team, baseAt, Owner(e), OptSrc(e), PlateRef(e, "plate"));
                Leave();
            }

            JsonNode unitsNode = Req(root, "units", JsonKind.Array);
            var units = new MapUnit[unitsNode.Items.Count];
            for (int i = 0; i < units.Length; i++)
            {
                JsonNode e = unitsNode.Items[i];
                Enter("unit", e, "key", true);
                Item(e, JsonKind.Object);
                if (Version2) Keys(e, "key", "type", "team", "squad", "at", "reserve", "plate", "owner", "src");
                else Keys(e, "key", "type", "team", "squad", "at", "reserve", "owner", "src");
                ulong key = ULong(Req(e, "key", JsonKind.Number), 1, IdCode);
                string type = Req(e, "type", JsonKind.String).Text;
                int team = Int(Req(e, "team", JsonKind.Number), 1, 2, Team);
                int squad = Int(Req(e, "squad", JsonKind.Number), 0, int.MaxValue, Key);
                MapMm at = MmPair(Req(e, "at", JsonKind.Array));
                bool reserve = Req(e, "reserve", JsonKind.Bool).Flag;
                units[i] = new MapUnit(key, type, team, squad, at, reserve, Owner(e), OptSrc(e), PlateRef(e, "plate"));
                Leave();
            }

            JsonNode runNode = Req(root, "run", JsonKind.Object);
            Keys(runNode, "seed", "crowdPass");
            ulong seed = ULong(Req(runNode, "seed", JsonKind.Number), 0, Key);
            JsonNode crowd = Req(runNode, "crowdPass", JsonKind.String);
            if (crowd.Text != "none" && crowd.Text != "teamIdle") throw Fail(Key, crowd.Line);

            JsonNode ownersNode = Req(root, "owners", JsonKind.Array);
            string[] owners = Strings(ownersNode);
            if (owners.Length < 1 || owners.Length > (Version2 ? MapDesc.MaxOwners : MapDesc.MaxOwnersVersion1)) throw Fail(OwnerCode, ownersNode.Line);
            for (int i = 0; i < owners.Length; i++)
            {
                bool repeated = Array.IndexOf(owners, owners[i], 0, i) >= 0;
                if (owners[i].Length == 0 || (i == 0 && owners[i] != "hand") || repeated) throw Fail(OwnerCode, ownersNode.Items[i].Line);
            }

            JsonNode heightOwnerNode = Opt(root, "heightOwner", JsonKind.Array);
            string[] heightOwner = heightOwnerNode == null ? null : Strings(heightOwnerNode);
            JsonNode surfaceOwnerNode = Opt(root, "surfaceOwner", JsonKind.Array);
            string[] surfaceOwner = surfaceOwnerNode == null ? null : Strings(surfaceOwnerNode);

            JsonNode stampsNode = Req(root, "stamps", JsonKind.Array);
            var stamps = new MapStamp[stampsNode.Items.Count];
            for (int i = 0; i < stamps.Length; i++)
            {
                JsonNode e = stampsNode.Items[i];
                EnterStamp(e);
                Item(e, JsonKind.Object);
                Keys(e, "instance", "kind", "src", "id");
                MapSrc head = SrcFields(e);
                JsonNode idNode = Req(e, "id", JsonKind.Number);
                ulong stampId = Wide(head.Kind) ? ULong(idNode, 1, OwnerCode) : (ulong)Int(idNode, 1, int.MaxValue, OwnerCode);
                stamps[i] = new MapStamp(head.Instance, head.Kind, head.Src, stampId);
                Leave();
            }

            JsonNode removedNode = Req(root, "removed", JsonKind.Array);
            var removed = new MapSrc[removedNode.Items.Count];
            for (int i = 0; i < removed.Length; i++)
            {
                JsonNode e = removedNode.Items[i];
                Enter("removed", e, null, false);
                Item(e, JsonKind.Object);
                removed[i] = Src(e);
                Leave();
            }

            JsonNode nextNode = Req(root, "next", JsonKind.Object);
            if (Version2) Keys(nextNode, "wall", "point", "base", "unit", "plate", "cutout");
            else Keys(nextNode, "wall", "point", "base", "unit");
            ulong nextWall = ULong(Req(nextNode, "wall", JsonKind.Number), 1, IdCode);
            int nextPoint = Int(Req(nextNode, "point", JsonKind.Number), 1, int.MaxValue, IdCode);
            int nextBase = Int(Req(nextNode, "base", JsonKind.Number), 1, int.MaxValue, IdCode);
            ulong nextUnit = ULong(Req(nextNode, "unit", JsonKind.Number), 1, IdCode);
            JsonNode nextPlate = Opt(nextNode, "plate", JsonKind.Number), nextCutout = Opt(nextNode, "cutout", JsonKind.Number);
            var next = new MapNext(nextWall, nextPoint, nextBase, nextUnit, nextPlate == null ? 1 : ULong(nextPlate, 1, IdCode),
                nextCutout == null ? 1 : ULong(nextCutout, 1, IdCode));

            return new MapDesc(id, content, pack, size, origin, floor, ceiling, layers, heights, surfaces, plates, cutouts, walls,
                points, bases, units, new MapRun(seed, crowd.Text), owners, heightOwner, surfaceOwner, stamps, removed, next);
        }
    }
}
