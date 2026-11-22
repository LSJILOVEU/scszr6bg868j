using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WarCore.Content.MapTools
{
    // ML-12a building parameters (<map id>.build.json, next to the map), the same rules as the ED-3a .gen.json: editor only,
    // never baked, never hashed; Read rejects unknown keys and bad values with ArgumentException ("build <where>: ..."); Write
    // gives canonical bytes (fixed key order, integers, two-space indent, "\n" line ends), so Write(Read(Write(s))) == Write(s).
    // It records the map's width, depth and layer so a file of another map is refused, and one entry per building stamp.
    public sealed class MapBuildSpec
    {
        public const int FormatVersion = 1, MaxBuildings = 256;

        private static readonly string[] TopKeys = { "version", "width", "depth", "pack", "buildings" };
        private static readonly string[] EntryKeys = { "instance", "x", "z", "sizeX", "sizeZ", "floors", "storeyRaw", "thickRaw",
            "doorSide", "doorAt", "doorWidth", "stairAxis", "stairX", "stairZ", "stairLength", "generations" };

        public int Width { get; }
        public int Depth { get; }
        public string Pack { get; }
        public IReadOnlyList<string> Instances { get; }
        public IReadOnlyList<MapBuilding> Buildings { get; }

        // A spec without buildings for a map of this size and layer.
        public MapBuildSpec(int width, int depth, string pack) : this(width, depth, pack, new string[0], new MapBuilding[0]) { }

        private MapBuildSpec(int width, int depth, string pack, string[] instances, MapBuilding[] buildings)
        {
            if (width < 1 || width > WcMapValidation.MaxSide) throw Bad("width", "must be in [1, " + WcMapValidation.MaxSide + "]");
            if (depth < 1 || depth > WcMapValidation.MaxSide) throw Bad("depth", "must be in [1, " + WcMapValidation.MaxSide + "]");
            Text(pack, "pack");
            if (instances.Length > MaxBuildings) throw Bad("buildings", "has more than " + MaxBuildings + " buildings");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in instances)
            {
                Text(name, "instance");
                if (!seen.Add(name)) throw Bad("buildings", "instance '" + name + "' appears twice");
            }
            Width = width; Depth = depth; Pack = pack;
            Instances = Array.AsReadOnly((string[])instances.Clone());
            Buildings = Array.AsReadOnly((MapBuilding[])buildings.Clone());
        }

        // The building of an instance, or null.
        public MapBuilding Find(string instance)
        {
            for (int k = 0; k < Instances.Count; k++)
                if (string.Equals(Instances[k], instance, StringComparison.Ordinal)) return Buildings[k];
            return null;
        }

        // A new spec with the instance's building replaced, or appended when the instance is new.
        public MapBuildSpec With(string instance, MapBuilding building)
        {
            if (building == null) throw new ArgumentNullException(nameof(building));
            var names = new List<string>(Instances);
            var list = new List<MapBuilding>(Buildings);
            int at = names.IndexOf(instance);
            if (at >= 0) list[at] = building;
            else { names.Add(instance); list.Add(building); }
            return new MapBuildSpec(Width, Depth, Pack, names.ToArray(), list.ToArray());
        }

        // A new spec without the instance (unchanged when it is not there).
        public MapBuildSpec Without(string instance)
        {
            var names = new List<string>(Instances);
            var list = new List<MapBuilding>(Buildings);
            int at = names.IndexOf(instance);
            if (at < 0) return this;
            names.RemoveAt(at); list.RemoveAt(at);
            return new MapBuildSpec(Width, Depth, Pack, names.ToArray(), list.ToArray());
        }

        // ArgumentException unless the spec was written for a map of this size and layer.
        public void CheckFor(MapDesc map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.Size.X != Width || map.Size.Z != Depth || !string.Equals(map.Pack, Pack, StringComparison.Ordinal))
                throw Bad("", "written for a " + Width + " x " + Depth + " map of layer " + Pack + ", not this " + map.Size.X + " x "
                    + map.Size.Z + " map of layer " + map.Pack);
        }

        public static MapBuildSpec Read(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JsonNode root;
            try { root = ContentJson.Parse(json, "build"); }
            catch (ContentException e) { throw new ArgumentException("build: " + e.Message); }
            if (root.Kind != JsonKind.Object) throw Bad("", "the document must be an object");
            Known(root, TopKeys, "");
            if (Int(Need(root, "version", ""), "version", long.MinValue, long.MaxValue) != FormatVersion) throw Bad("version", "must be " + FormatVersion);
            int width = (int)Int(Need(root, "width", ""), "width", 1, WcMapValidation.MaxSide);
            int depth = (int)Int(Need(root, "depth", ""), "depth", 1, WcMapValidation.MaxSide);
            string pack = Str(Need(root, "pack", ""), "pack");
            JsonNode list = Need(root, "buildings", "");
            if (list.Kind != JsonKind.Array) throw Bad("buildings", "must be an array");
            if (list.Items.Count > MaxBuildings) throw Bad("buildings", "has more than " + MaxBuildings + " buildings");
            var names = new string[list.Items.Count];
            var buildings = new MapBuilding[list.Items.Count];
            for (int k = 0; k < names.Length; k++) buildings[k] = ReadEntry(list.Items[k], "buildings[" + k + "]", width, depth, out names[k]);
            return new MapBuildSpec(width, depth, pack, names, buildings);
        }

        private static MapBuilding ReadEntry(JsonNode node, string at, int width, int depth, out string instance)
        {
            if (node.Kind != JsonKind.Object) throw Bad(at, "must be an object");
            Known(node, EntryKeys, at);
            instance = Str(Need(node, "instance", at), at + ".instance");
            int I(string key, long lo, long hi) => (int)Int(Need(node, key, at), at + "." + key, lo, hi);
            int x = I("x", 0, width), z = I("z", 0, depth), sizeX = I("sizeX", MapBuilding.MinSide, width), sizeZ = I("sizeZ", MapBuilding.MinSide, depth);
            int floors = I("floors", 1, MapBuilding.MaxFloors);
            long storey = Int(Need(node, "storeyRaw", at), at + ".storeyRaw", MapBuilding.MinStoreyRaw, int.MaxValue);
            long thick = Int(Need(node, "thickRaw", at), at + ".thickRaw", MapBuilding.MinThickRaw, int.MaxValue);
            string side = Str(Need(node, "doorSide", at), at + ".doorSide");
            if (side != "n" && side != "e" && side != "s" && side != "w") throw Bad(at + ".doorSide", "must be n, e, s or w");
            int doorAt = I("doorAt", 1, WcMapValidation.MaxSide), doorWidth = I("doorWidth", 1, WcMapValidation.MaxSide);
            string axis = Str(Need(node, "stairAxis", at), at + ".stairAxis");
            if (axis != "x" && axis != "z") throw Bad(at + ".stairAxis", "must be x or z");
            int stairX = I("stairX", 0, width), stairZ = I("stairZ", 0, depth), stairLength = I("stairLength", 1, WcMapValidation.MaxSide);
            JsonNode g = Need(node, "generations", at);
            if (g.Kind != JsonKind.Array || g.Items.Count < 1 || g.Items.Count > MapBuilding.MaxFloors)
                throw Bad(at + ".generations", "must be an array of 1 to " + MapBuilding.MaxFloors + " integers");
            var gens = new int[g.Items.Count];
            for (int n = 0; n < gens.Length; n++) gens[n] = (int)Int(g.Items[n], at + ".generations[" + n + "]", 0, MapBuilding.MaxGeneration);
            try
            {
                return new MapBuilding(new MapXZ(x, z), new MapXZ(sizeX, sizeZ), floors, storey, thick, side[0], doorAt, doorWidth,
                    axis == "x", new MapXZ(stairX, stairZ), stairLength, gens);
            }
            catch (ArgumentException e) { throw Bad(at, e.Message); }
        }

        public string Write()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"version\": ").Append(FormatVersion).Append(",\n");
            sb.Append("  \"width\": ").Append(N(Width)).Append(",\n");
            sb.Append("  \"depth\": ").Append(N(Depth)).Append(",\n");
            sb.Append("  \"pack\": "); Quote(sb, Pack); sb.Append(",\n");
            if (Buildings.Count == 0) sb.Append("  \"buildings\": []\n");
            else
            {
                sb.Append("  \"buildings\": [\n");
                for (int k = 0; k < Buildings.Count; k++)
                {
                    MapBuilding b = Buildings[k];
                    sb.Append("    { \"instance\": "); Quote(sb, Instances[k]);
                    sb.Append(", \"x\": ").Append(N(b.Cell.X)).Append(", \"z\": ").Append(N(b.Cell.Z));
                    sb.Append(", \"sizeX\": ").Append(N(b.Size.X)).Append(", \"sizeZ\": ").Append(N(b.Size.Z));
                    sb.Append(", \"floors\": ").Append(N(b.Floors)).Append(", \"storeyRaw\": ").Append(N(b.StoreyRaw));
                    sb.Append(", \"thickRaw\": ").Append(N(b.ThickRaw)).Append(", \"doorSide\": "); Quote(sb, b.DoorSide.ToString());
                    sb.Append(", \"doorAt\": ").Append(N(b.DoorAt)).Append(", \"doorWidth\": ").Append(N(b.DoorWidth));
                    sb.Append(", \"stairAxis\": "); Quote(sb, b.StairAlongX ? "x" : "z");
                    sb.Append(", \"stairX\": ").Append(N(b.StairCell.X)).Append(", \"stairZ\": ").Append(N(b.StairCell.Z));
                    sb.Append(", \"stairLength\": ").Append(N(b.StairLength)).Append(", \"generations\": [");
                    for (int n = 0; n < b.Generations.Count; n++) sb.Append(n > 0 ? ", " : "").Append(N(b.Generations[n]));
                    sb.Append("] }").Append(k + 1 < Buildings.Count ? ",\n" : "\n");
                }
                sb.Append("  ]\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

        private static void Known(JsonNode node, string[] allowed, string at)
        {
            foreach (JsonMember m in node.Members)
                if (Array.IndexOf(allowed, m.Name) < 0) throw Bad(at, "unknown key '" + m.Name + "'");
        }

        private static JsonNode Need(JsonNode node, string key, string at)
        {
            JsonNode v = node.Find(key);
            if (v == null) throw Bad(at, "missing key '" + key + "'");
            return v;
        }

        private static long Int(JsonNode v, string at, long lo, long hi)
        {
            if (v.Kind != JsonKind.Number || v.Text.IndexOf('.') >= 0
                || !long.TryParse(v.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n))
                throw Bad(at, "must be an integer");
            if (n < lo || n > hi) throw Bad(at, n + " is outside [" + lo + ", " + hi + "]");
            return n;
        }

        private static string Str(JsonNode v, string at)
        {
            if (v.Kind != JsonKind.String) throw Bad(at, "must be a string");
            Text(v.Text, at);
            return v.Text;
        }

        private static void Text(string s, string at)
        {
            if (s == null) throw Bad(at, "is missing");
            try { WcMapValidation.TextBytes(s); }
            catch (InvalidDataException) { throw Bad(at, "must be 1 to 255 UTF-8 bytes without NUL"); }
        }

        private static ArgumentException Bad(string at, string message) =>
            new ArgumentException("build" + (at.Length == 0 ? "" : " " + at) + ": " + message);

        private static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            sb.Append('"');
        }
    }
}
