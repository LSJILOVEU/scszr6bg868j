using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WarCore.Content;

namespace WarCore.Presentation
{
    // The colours a map tool draws with, from palette.json of the cartridge (Content/<cartridge>/Data/palette.json). Colours
    // are 0xRRGGBB. Only the sections used here are read: version, ground, cliff, struct slot "1", team and neutral; other
    // top-level sections are left to other readers. A missing file or section gives the built-in colour of that section.
    // The built-in values name no surface: a ground colour the palette lacks is a fall-back computed from the surface name.
    // The palette is not content: it never goes through ContentLoader and into no hash.
    public sealed class MapPalette
    {
        public const string FileName = ContentCatalog.ColorsFile + ".json";
        private const int MinTeams = 2, MaxTeams = 4;

        private readonly Dictionary<string, int> ground;
        private readonly int[] team;

        public IReadOnlyDictionary<string, int> Ground => ground;
        public int Cliff { get; }
        public int StructSide { get; }
        public int StructTop { get; }
        public IReadOnlyList<int> Team => Array.AsReadOnly(team);
        public int Neutral { get; }
        // The colour of a map that has no surface table.
        public int NoGround => 0x9A9A8C;

        private MapPalette(Dictionary<string, int> ground, int cliff, int side, int top, int[] team, int neutral)
        {
            this.ground = ground; Cliff = cliff; StructSide = side; StructTop = top; this.team = team; Neutral = neutral;
        }

        // Every colour built in, and no ground colour.
        public static MapPalette Default =>
            new MapPalette(new Dictionary<string, int>(StringComparer.Ordinal), 0x8A8378, 0xA39E95, 0xB8B3AA, new[] { 0x2F6FD6, 0xD6452F }, 0xE8E4D8);

        // The primary colour of a surface name; fallback tells that the palette does not list the name.
        public int GroundColor(string name, out bool fallback)
        {
            fallback = !ground.TryGetValue(name, out int color);
            return fallback ? FallbackColor(name) : color;
        }

        // FNV-1a (32 bit) over the UTF-8 bytes of the name; the lowest, second and third byte give red, green and blue, each as
        // 112 + (byte & 31). Greyish on purpose: chroma stays under 0.06 in OKLab.
        public static int FallbackColor(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            uint hash = 2166136261u;
            foreach (byte b in Encoding.UTF8.GetBytes(name)) hash = (hash ^ b) * 16777619u;
            int red = 112 + (int)(hash & 31), green = 112 + (int)(hash >> 8 & 31), blue = 112 + (int)(hash >> 16 & 31);
            return red << 16 | green << 8 | blue;
        }

        // The palette of the cartridge that holds the layer.
        public static MapPalette ForLayer(ContentCatalog catalog, string layerId)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            string cartridge = catalog.CartridgeOf(layerId);
            return Load(Path.Combine(catalog.DirectoryOf(cartridge), FileName), cartridge);
        }

        // A file that does not exist gives Default. Any other trouble is an InvalidDataException that names the cartridge and
        // the field ("Rwr palette.json ground.mud[0]: ...").
        public static MapPalette Load(string path, string cartridge)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (cartridge == null) throw new ArgumentNullException(nameof(cartridge));
            if (!File.Exists(path)) return Default;
            string where = cartridge + " " + FileName;
            JsonNode root;
            try { root = ContentJson.Parse(File.ReadAllText(path), where); }
            catch (ContentException e) { throw new InvalidDataException(e.Message, e); }
            if (root.Kind != JsonKind.Object) throw Bad(where, "(root)", "must be a JSON object");
            return Read(root, where);
        }

        private static MapPalette Read(JsonNode root, string where)
        {
            MapPalette d = Default;
            JsonNode version = root.Find("version");
            if (version != null && (version.Kind != JsonKind.Number || version.Text != "1")) throw Bad(where, "version", "must be the integer 1");
            var ground = new Dictionary<string, int>(StringComparer.Ordinal);
            JsonNode groundNode = root.Find("ground");
            if (groundNode != null)
            {
                if (groundNode.Kind != JsonKind.Object) throw Bad(where, "ground", "must be an object of surface name to colours");
                foreach (JsonMember m in groundNode.Members)
                {
                    string path = "ground." + m.Name;
                    if (m.Value.Kind != JsonKind.Array || m.Value.Items.Count < 1 || m.Value.Items.Count > 2)
                        throw Bad(where, path, "must be an array of 1 or 2 colours");
                    for (int i = 1; i < m.Value.Items.Count; i++) Color(m.Value.Items[i], where, path + "[" + i + "]");
                    ground[m.Name] = Color(m.Value.Items[0], where, path + "[0]");
                }
            }
            int cliff = Optional(root, "cliff", d.Cliff, where);
            int side = d.StructSide, top = d.StructTop;
            JsonNode structure = root.Find("struct");
            if (structure != null)
            {
                if (structure.Kind != JsonKind.Object) throw Bad(where, "struct", "must be an object");
                JsonNode slot = structure.Find("1");
                if (slot != null)
                {
                    if (slot.Kind != JsonKind.Object) throw Bad(where, "struct.1", "must be an object with side and top");
                    side = Optional(slot, "side", side, where, "struct.1.");
                    top = Optional(slot, "top", top, where, "struct.1.");
                }
            }
            int[] team = d.team;
            JsonNode teamNode = root.Find("team");
            if (teamNode != null)
            {
                if (teamNode.Kind != JsonKind.Array || teamNode.Items.Count < MinTeams || teamNode.Items.Count > MaxTeams)
                    throw Bad(where, "team", "must be an array of " + MinTeams + " to " + MaxTeams + " colours");
                team = new int[teamNode.Items.Count];
                for (int i = 0; i < team.Length; i++) team[i] = Color(teamNode.Items[i], where, "team[" + i + "]");
            }
            return new MapPalette(ground, cliff, side, top, team, Optional(root, "neutral", d.Neutral, where));
        }

        private static int Optional(JsonNode parent, string name, int fallback, string where, string prefix = "")
        {
            JsonNode node = parent.Find(name);
            return node == null ? fallback : Color(node, where, prefix + name);
        }

        private static int Color(JsonNode node, string where, string path)
        {
            string s = node.Kind == JsonKind.String ? node.Text : null;
            if (s == null || s.Length != 7 || s[0] != '#') throw Bad(where, path, "must be a colour written #RRGGBB");
            int rgb = 0;
            for (int i = 1; i < 7; i++)
            {
                char c = s[i];
                int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'A' && c <= 'F' ? c - 'A' + 10 : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1;
                if (digit < 0) throw Bad(where, path, "must be a colour written #RRGGBB");
                rgb = rgb << 4 | digit;
            }
            return rgb;
        }

        private static InvalidDataException Bad(string where, string path, string what) => new InvalidDataException(where + " " + path + ": " + what);
    }
}
