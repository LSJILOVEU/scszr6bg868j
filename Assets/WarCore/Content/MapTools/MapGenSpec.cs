using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WarCore.Content.MapTools
{
    // One stamp of a terrain generation spec. Only the fields of its kind are meaningful (the others are 0, null or empty):
    //   hill, mesa: X Z Radius HeightMm;  hills: Count X0 Z0 X1 Z1 RMin RMax HMinMm HMaxMm;  ridge: Points HalfWidth HeightMm;
    //   patch: X Z Radius Surface Rough;  river: Points HalfWidth DepthMm Surface;  road: Points HalfWidth Surface.
    public sealed class MapGenStamp
    {
        public string Kind { get; }
        public int X { get; private set; }
        public int Z { get; private set; }
        public int Radius { get; private set; }
        public int HeightMm { get; private set; }
        public int Count { get; private set; }
        public int X0 { get; private set; }
        public int Z0 { get; private set; }
        public int X1 { get; private set; }
        public int Z1 { get; private set; }
        public int RMin { get; private set; }
        public int RMax { get; private set; }
        public int HMinMm { get; private set; }
        public int HMaxMm { get; private set; }
        public int HalfWidth { get; private set; }
        public int DepthMm { get; private set; }
        public int Rough { get; private set; }
        public string Surface { get; private set; }
        public IReadOnlyList<MapXZ> Points { get; private set; } = Array.AsReadOnly(new MapXZ[0]);

        internal MapGenStamp(string kind) { Kind = kind; }

        // True for the kinds that change heights (road included: it flattens its band).
        public bool ChangesHeights => Kind != "patch";

        internal int Get(string key)
        {
            switch (key)
            {
                case "x": return X; case "z": return Z; case "radius": return Radius; case "heightMm": return HeightMm;
                case "count": return Count; case "x0": return X0; case "z0": return Z0; case "x1": return X1; case "z1": return Z1;
                case "rMin": return RMin; case "rMax": return RMax; case "hMinMm": return HMinMm; case "hMaxMm": return HMaxMm;
                case "halfWidth": return HalfWidth; case "depthMm": return DepthMm; case "rough": return Rough;
                default: throw new ArgumentException(key);
            }
        }

        internal void Set(string key, int v)
        {
            switch (key)
            {
                case "x": X = v; break; case "z": Z = v; break; case "radius": Radius = v; break; case "heightMm": HeightMm = v; break;
                case "count": Count = v; break; case "x0": X0 = v; break; case "z0": Z0 = v; break; case "x1": X1 = v; break;
                case "z1": Z1 = v; break; case "rMin": RMin = v; break; case "rMax": RMax = v; break; case "hMinMm": HMinMm = v; break;
                case "hMaxMm": HMaxMm = v; break; case "halfWidth": HalfWidth = v; break; case "depthMm": DepthMm = v; break;
                case "rough": Rough = v; break;
                default: throw new ArgumentException(key);
            }
        }

        internal void SetSurface(string name) { Surface = name; }

        internal void SetPoints(MapXZ[] points) { Points = Array.AsReadOnly((MapXZ[])points.Clone()); }
    }

    // ED-3a terrain generation spec (<map id>.gen.json, next to the map). Editor only: never baked, never hashed, and the map
    // description format does not change. Read rejects unknown keys and out-of-range values with ArgumentException; Write
    // gives canonical bytes (fixed key order, integers, two-space indent, "\n" line ends), so Write(Read(Write(s))) == Write(s).
    public sealed class MapGenSpec
    {
        public const int FormatVersion = 1;
        public const int MaxStamps = 256, MaxOutside = 1024, MaxMm = 1000000, MaxRadius = 256, MaxPoints = 64, MaxCount = 256,
            MaxRough = 512;

        // Keys of each kind after "kind", in canonical order. Every key is required.
        private static readonly Dictionary<string, string[]> KindKeys = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "hill", new[] { "x", "z", "radius", "heightMm" } },
            { "hills", new[] { "count", "x0", "z0", "x1", "z1", "rMin", "rMax", "hMinMm", "hMaxMm" } },
            { "ridge", new[] { "points", "halfWidth", "heightMm" } },
            { "mesa", new[] { "x", "z", "radius", "heightMm" } },
            { "patch", new[] { "x", "z", "radius", "surface", "rough" } },
            { "river", new[] { "points", "halfWidth", "depthMm", "surface" } },
            { "road", new[] { "points", "halfWidth", "surface" } },
        };

        private static readonly string[] TopKeys = { "version", "width", "depth", "pack", "seed", "baseMm", "ground", "stamps" };

        public int Width { get; }
        public int Depth { get; }
        public string Pack { get; }
        public ulong Seed { get; }
        public int? BaseMm { get; }
        public string Ground { get; }
        public IReadOnlyList<MapGenStamp> Stamps { get; }

        // A spec without stamps (the starting spec of a map); the same top-level checks as Read.
        public MapGenSpec(int width, int depth, string pack, ulong seed, int? baseMm, string ground)
            : this(width, depth, pack, seed, baseMm, ground, new MapGenStamp[0])
        {
        }

        private MapGenSpec(int width, int depth, string pack, ulong seed, int? baseMm, string ground, MapGenStamp[] stamps)
        {
            if (width < 1 || width > WcMapValidation.MaxSide) throw Bad("width", "must be in [1, " + WcMapValidation.MaxSide + "]");
            if (depth < 1 || depth > WcMapValidation.MaxSide) throw Bad("depth", "must be in [1, " + WcMapValidation.MaxSide + "]");
            Text(pack, "pack");
            if (baseMm.HasValue && (baseMm.Value < -MaxMm || baseMm.Value > MaxMm)) throw Bad("baseMm", "must be in [-" + MaxMm + ", " + MaxMm + "]");
            if (ground != null) Text(ground, "ground");
            Width = width; Depth = depth; Pack = pack; Seed = seed; BaseMm = baseMm; Ground = ground;
            Stamps = Array.AsReadOnly((MapGenStamp[])stamps.Clone());
        }

        // raw = floor(mm * 65536 / 1000), toward negative infinity.
        public static int MmToRaw(int mm)
        {
            long n = (long)mm * 65536;
            long q = n / 1000;
            if (n % 1000 != 0 && n < 0) q--;
            return (int)q;
        }

        public static MapGenSpec Read(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JsonNode root;
            try { root = ContentJson.Parse(json, "gen"); }
            catch (ContentException e) { throw new ArgumentException("gen: " + e.Message); }
            if (root.Kind != JsonKind.Object) throw Bad("", "the document must be an object");
            Known(root, TopKeys, "");
            if (Int(Need(root, "version", ""), "version", long.MinValue, long.MaxValue) != FormatVersion)
                throw Bad("version", "must be " + FormatVersion);
            int width = (int)Int(Need(root, "width", ""), "width", 1, WcMapValidation.MaxSide);
            int depth = (int)Int(Need(root, "depth", ""), "depth", 1, WcMapValidation.MaxSide);
            string pack = Str(Need(root, "pack", ""), "pack");
            ulong seed = Seed64(Need(root, "seed", ""));
            JsonNode baseNode = root.Find("baseMm"), groundNode = root.Find("ground");
            int? baseMm = baseNode == null ? (int?)null : (int)Int(baseNode, "baseMm", -MaxMm, MaxMm);
            string ground = groundNode == null ? null : Str(groundNode, "ground");
            JsonNode list = Need(root, "stamps", "");
            if (list.Kind != JsonKind.Array) throw Bad("stamps", "must be an array");
            if (list.Items.Count > MaxStamps) throw Bad("stamps", "has more than " + MaxStamps + " stamps");
            var stamps = new MapGenStamp[list.Items.Count];
            for (int k = 0; k < stamps.Length; k++) stamps[k] = ReadStamp(list.Items[k], "stamps[" + k + "]", width, depth);
            return new MapGenSpec(width, depth, pack, seed, baseMm, ground, stamps);
        }

        private static MapGenStamp ReadStamp(JsonNode node, string at, int width, int depth)
        {
            if (node.Kind != JsonKind.Object) throw Bad(at, "must be an object");
            string kind = Str(Need(node, "kind", at), at + ".kind");
            if (!KindKeys.TryGetValue(kind, out string[] keys)) throw Bad(at + ".kind", "unknown kind '" + kind + "'");
            var allowed = new string[keys.Length + 1];
            allowed[0] = "kind";
            Array.Copy(keys, 0, allowed, 1, keys.Length);
            Known(node, allowed, at);
            var stamp = new MapGenStamp(kind);
            foreach (string key in keys)
            {
                JsonNode v = Need(node, key, at);
                string where = at + "." + key;
                if (key == "surface") stamp.SetSurface(Str(v, where));
                else if (key == "points") stamp.SetPoints(Points(v, where, width, depth));
                else
                {
                    Range(key, width, depth, out long lo, out long hi);
                    stamp.Set(key, (int)Int(v, where, lo, hi));
                }
            }
            if (kind == "hills")
            {
                if (stamp.X0 > stamp.X1) throw Bad(at, "x0 is greater than x1");
                if (stamp.Z0 > stamp.Z1) throw Bad(at, "z0 is greater than z1");
                if (stamp.RMin > stamp.RMax) throw Bad(at, "rMin is greater than rMax");
                if (stamp.HMinMm > stamp.HMaxMm) throw Bad(at, "hMinMm is greater than hMaxMm");
            }
            return stamp;
        }

        // Range of each integer key.
        private static void Range(string key, int width, int depth, out long lo, out long hi)
        {
            switch (key)
            {
                case "x": case "x0": case "x1": lo = -MaxOutside; hi = (long)width + MaxOutside; return;
                case "z": case "z0": case "z1": lo = -MaxOutside; hi = (long)depth + MaxOutside; return;
                case "radius": case "rMin": case "rMax": case "halfWidth": lo = 1; hi = MaxRadius; return;
                case "heightMm": case "hMinMm": case "hMaxMm": lo = -MaxMm; hi = MaxMm; return;
                case "depthMm": lo = 1; hi = MaxMm; return;
                case "count": lo = 1; hi = MaxCount; return;
                case "rough": lo = 0; hi = MaxRough; return;
                default: throw new ArgumentException(key);
            }
        }

        private static MapXZ[] Points(JsonNode v, string at, int width, int depth)
        {
            if (v.Kind != JsonKind.Array) throw Bad(at, "must be an array of [x, z]");
            if (v.Items.Count < 2 || v.Items.Count > MaxPoints) throw Bad(at, "must have 2 to " + MaxPoints + " points");
            var points = new MapXZ[v.Items.Count];
            for (int n = 0; n < points.Length; n++)
            {
                JsonNode p = v.Items[n];
                string where = at + "[" + n + "]";
                if (p.Kind != JsonKind.Array || p.Items.Count != 2) throw Bad(where, "must be [x, z]");
                int x = (int)Int(p.Items[0], where, -MaxOutside, (long)width + MaxOutside);
                int z = (int)Int(p.Items[1], where, -MaxOutside, (long)depth + MaxOutside);
                points[n] = new MapXZ(x, z);
            }
            return points;
        }

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

        private static ulong Seed64(JsonNode v)
        {
            if (v.Kind != JsonKind.Number || v.Text.IndexOf('.') >= 0 || v.Text.StartsWith("-", StringComparison.Ordinal)
                || !ulong.TryParse(v.Text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong n))
                throw Bad("seed", "must be an integer in [0, " + ulong.MaxValue + "]");
            return n;
        }

        private static string Str(JsonNode v, string at)
        {
            if (v.Kind != JsonKind.String) throw Bad(at, "must be a string");
            Text(v.Text, at);
            return v.Text;
        }

        // 1 to 255 UTF-8 bytes, no NUL, no lone surrogate (the bake's text rule).
        private static void Text(string s, string at)
        {
            if (s == null) throw Bad(at, "is missing");
            try { WcMapValidation.TextBytes(s); }
            catch (InvalidDataException) { throw Bad(at, "must be 1 to 255 UTF-8 bytes without NUL"); }
        }

        private static ArgumentException Bad(string at, string message) =>
            new ArgumentException("gen" + (at.Length == 0 ? "" : " " + at) + ": " + message);

        public string Write()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"version\": ").Append(FormatVersion).Append(",\n");
            sb.Append("  \"width\": ").Append(Width.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"depth\": ").Append(Depth.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"pack\": "); Quote(sb, Pack); sb.Append(",\n");
            sb.Append("  \"seed\": ").Append(Seed.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            if (BaseMm.HasValue) sb.Append("  \"baseMm\": ").Append(BaseMm.Value.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            if (Ground != null) { sb.Append("  \"ground\": "); Quote(sb, Ground); sb.Append(",\n"); }
            if (Stamps.Count == 0) sb.Append("  \"stamps\": []\n");
            else
            {
                sb.Append("  \"stamps\": [\n");
                for (int k = 0; k < Stamps.Count; k++)
                {
                    WriteStamp(sb, Stamps[k]);
                    sb.Append(k + 1 < Stamps.Count ? ",\n" : "\n");
                }
                sb.Append("  ]\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }

        private static void WriteStamp(StringBuilder sb, MapGenStamp s)
        {
            sb.Append("    { \"kind\": "); Quote(sb, s.Kind);
            foreach (string key in KindKeys[s.Kind])
            {
                sb.Append(", \"").Append(key).Append("\": ");
                if (key == "surface") Quote(sb, s.Surface);
                else if (key == "points")
                {
                    sb.Append('[');
                    for (int n = 0; n < s.Points.Count; n++)
                    {
                        if (n > 0) sb.Append(", ");
                        sb.Append('[').Append(s.Points[n].X.ToString(CultureInfo.InvariantCulture)).Append(", ")
                            .Append(s.Points[n].Z.ToString(CultureInfo.InvariantCulture)).Append(']');
                    }
                    sb.Append(']');
                }
                else sb.Append(s.Get(key).ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(" }");
        }

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
