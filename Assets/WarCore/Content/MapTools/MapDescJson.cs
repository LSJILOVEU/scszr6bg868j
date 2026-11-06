using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WarCore.Content.MapTools
{
    // Reads a map description (strict UTF-8 without BOM, ContentJson subset) and writes its canonical bytes.
    // Canonical form: two-space indent, "key": value, "\n" line ends and one final "\n"; objects always multi-line; arrays of
    // scalars on one line ("[a, b]", "[]") except surfaces.rows, heightOwner and surfaceOwner (one string per line); arrays of
    // arrays or objects multi-line. Keys in canonical order; optional keys left out when absent. Culture independent.
    public static class MapDescJson
    {
        private static readonly UTF8Encoding Strict = new UTF8Encoding(false, true);

        public static MapDesc Read(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) throw MapDescReader.Plain(MapDescReader.Format);
            string text;
            try { text = Strict.GetString(bytes); }
            catch (DecoderFallbackException e) { throw MapDescReader.Plain(MapDescReader.Format, e); }
            return Read(text);
        }

        public static MapDesc Read(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            JsonNode root;
            try { root = ContentJson.Parse(text, "map"); }
            catch (ContentException e) { throw Translate(e); }
            return new MapDescReader(root).Read();
        }

        // "map line <n> ...": a repeated field is a key error, anything else a format error.
        private static InvalidDataException Translate(ContentException e)
        {
            const string prefix = "map line ";
            int end = prefix.Length;
            while (end < e.Message.Length && e.Message[end] >= '0' && e.Message[end] <= '9') end++;
            string code = e.Message.Contains("' is repeated in the same object") ? MapDescReader.Key : MapDescReader.Format;
            return new InvalidDataException(code + " line " + e.Message.Substring(prefix.Length, end - prefix.Length), e);
        }

        public static byte[] Write(MapDesc desc) => Strict.GetBytes(WriteText(desc));

        public static string WriteText(MapDesc desc)
        {
            if (desc == null) throw new ArgumentNullException(nameof(desc));
            var sb = new StringBuilder();
            Emit(Tree(desc), 0, sb);
            return sb.Append('\n').ToString();
        }

        // ---- output tree ----

        private abstract class Out { }

        private sealed class Obj : Out
        {
            public readonly List<KeyValuePair<string, Out>> Members = new List<KeyValuePair<string, Out>>();
            public Obj Add(string key, Out value)
            {
                if (value != null) Members.Add(new KeyValuePair<string, Out>(key, value));
                return this;
            }
        }

        private sealed class Arr : Out
        {
            public readonly List<Out> Items = new List<Out>();
            public bool Lines;
        }

        private sealed class Val : Out
        {
            public readonly string Text;
            public Val(string text) { Text = text; }
        }

        private static Val Num(long value) => new Val(value.ToString(CultureInfo.InvariantCulture));
        private static Val Num(ulong value) => new Val(value.ToString(CultureInfo.InvariantCulture));
        private static Val Str(string value) => new Val(Quote(value));
        private static Val Bool(bool value) => new Val(value ? "true" : "false");
        private static Out OptNum(int? value) => value.HasValue ? Num(value.Value) : null;

        private static Arr Pair(long x, long z) { var a = new Arr(); a.Items.Add(Num(x)); a.Items.Add(Num(z)); return a; }

        private static Arr Strings(IReadOnlyList<string> items, bool lines)
        {
            if (items == null) return null;
            var a = new Arr { Lines = lines };
            foreach (string s in items) a.Items.Add(Str(s));
            return a;
        }

        private static Arr Many<T>(IReadOnlyList<T> items, Func<T, Out> map)
        {
            var a = new Arr();
            foreach (T item in items) a.Items.Add(map(item));
            return a;
        }

        private static Obj Src(MapSrc src) => src == null ? null
            : new Obj().Add("instance", Str(src.Instance)).Add("kind", Str(src.Kind)).Add("src", Num(src.Src));

        private static Obj Tree(MapDesc d)
        {
            var root = new Obj()
                .Add("format", Str(d.Format)).Add("version", Num(d.Version))
                .Add("id", Str(d.Id)).Add("content", Str(d.Content)).Add("pack", Str(d.Pack))
                .Add("size", Pair(d.Size.X, d.Size.Z)).Add("origin", Pair(d.Origin.X, d.Origin.Z))
                .Add("floorRaw", Num(d.FloorRaw)).Add("ceilingRaw", Num(d.CeilingRaw))
                .Add("layers", Many(d.Layers, l => new Obj().Add("name", Str(l.Name)).Add("bit", Num(l.Bit))));
            if (d.Heights != null)
                root.Add("heights", Many(d.Heights, row => Many(row, h => Num(h))));
            if (d.Surfaces != null)
                root.Add("surfaces", new Obj().Add("names", Strings(d.Surfaces.Names, false)).Add("rows", Strings(d.Surfaces.Rows, true)));
            root.Add("walls", Many(d.Walls, w => new Obj()
                .Add("id", Num(w.Id)).Add("kind", Str(w.Kind)).Add("layer", Str(w.Layer))
                .Add("cell", Pair(w.Cell.X, w.Cell.Z)).Add("size", Pair(w.Size.X, w.Size.Z)).Add("heightRaw", Num(w.HeightRaw))
                .Add("owner", OptNum(w.Owner)).Add("src", Src(w.Src))));
            root.Add("points", Many(d.Points, p => new Obj()
                .Add("id", Num(p.Id)).Add("at", Pair(p.At.X, p.At.Z)).Add("radiusMm", Num(p.RadiusMm))
                .Add("initialOwner", Num(p.InitialOwner)).Add("spawn", p.Spawn.HasValue ? Pair(p.Spawn.Value.X, p.Spawn.Value.Z) : null)
                .Add("owner", OptNum(p.Owner)).Add("src", Src(p.Src))));
            root.Add("bases", Many(d.Bases, b => new Obj()
                .Add("id", Num(b.Id)).Add("team", Num(b.Team)).Add("at", Pair(b.At.X, b.At.Z))
                .Add("owner", OptNum(b.Owner)).Add("src", Src(b.Src))));
            root.Add("units", Many(d.Units, u => new Obj()
                .Add("key", Num(u.Key)).Add("type", Str(u.Type)).Add("team", Num(u.Team)).Add("squad", Num(u.Squad))
                .Add("at", Pair(u.At.X, u.At.Z)).Add("reserve", Bool(u.Reserve))
                .Add("owner", OptNum(u.Owner)).Add("src", Src(u.Src))));
            root.Add("run", new Obj().Add("seed", Num(d.Run.Seed)).Add("crowdPass", Str(d.Run.CrowdPass)));
            root.Add("owners", Strings(d.Owners, false));
            root.Add("heightOwner", Strings(d.HeightOwner, true));
            root.Add("surfaceOwner", Strings(d.SurfaceOwner, true));
            root.Add("stamps", Many(d.Stamps, s => new Obj()
                .Add("instance", Str(s.Instance)).Add("kind", Str(s.Kind)).Add("src", Num(s.Src)).Add("id", Num(s.Id))));
            root.Add("removed", Many(d.Removed, r => (Out)Src(r)));
            root.Add("next", new Obj().Add("wall", Num(d.Next.Wall)).Add("point", Num(d.Next.Point))
                .Add("base", Num(d.Next.Base)).Add("unit", Num(d.Next.Unit)));
            return root;
        }

        private static void Emit(Out value, int indent, StringBuilder sb)
        {
            switch (value)
            {
                case Val v:
                    sb.Append(v.Text);
                    break;
                case Obj o:
                    if (o.Members.Count == 0) { sb.Append("{}"); break; }
                    sb.Append("{\n");
                    for (int i = 0; i < o.Members.Count; i++)
                    {
                        sb.Append(' ', 2 * (indent + 1)).Append(Quote(o.Members[i].Key)).Append(": ");
                        Emit(o.Members[i].Value, indent + 1, sb);
                        sb.Append(i + 1 < o.Members.Count ? ",\n" : "\n");
                    }
                    sb.Append(' ', 2 * indent).Append('}');
                    break;
                case Arr a:
                    if (a.Items.Count == 0) { sb.Append("[]"); break; }
                    bool lines = a.Lines;
                    foreach (Out item in a.Items) lines |= !(item is Val);
                    if (!lines)
                    {
                        sb.Append('[');
                        for (int i = 0; i < a.Items.Count; i++) { if (i > 0) sb.Append(", "); Emit(a.Items[i], indent, sb); }
                        sb.Append(']');
                        break;
                    }
                    sb.Append("[\n");
                    for (int i = 0; i < a.Items.Count; i++)
                    {
                        sb.Append(' ', 2 * (indent + 1));
                        Emit(a.Items[i], indent + 1, sb);
                        sb.Append(i + 1 < a.Items.Count ? ",\n" : "\n");
                    }
                    sb.Append(' ', 2 * indent).Append(']');
                    break;
            }
        }

        // Only '"', '\' and U+0000-U+001F are escaped, the controls always as \u00XX (upper-case hex); the rest stays as is.
        private static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2).Append('"');
            foreach (char c in s)
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c < ' ') sb.Append("\\u00").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
