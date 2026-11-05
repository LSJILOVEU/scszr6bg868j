using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WarCore.Content
{
    public sealed class ContentException : Exception
    {
        public ContentException(string message) : base(message) { }
    }

    public enum JsonKind { Object, Array, String, Number, Bool }

    public sealed class JsonMember
    {
        public readonly string Name;
        public readonly JsonNode Value;
        public JsonMember(string name, JsonNode value) { Name = name; Value = value; }
    }

    // Strict subset: objects, arrays, strings, numbers (kept as text, no exponent), booleans. No null, no duplicate names.
    public sealed class JsonNode
    {
        public readonly JsonKind Kind;
        public readonly int Line;
        public string Text;
        public bool Flag;
        public List<JsonMember> Members;
        public List<JsonNode> Items;
        public JsonNode(JsonKind kind, int line) { Kind = kind; Line = line; }

        public JsonNode Find(string name)
        {
            foreach (var member in Members) if (member.Name == name) return member.Value;
            return null;
        }
    }

    public static class ContentJson
    {
        public static JsonNode Parse(string text, string where)
        {
            var parser = new Parser(text, where);
            var node = parser.Value();
            parser.SkipSpace();
            if (!parser.AtEnd) throw parser.Error("unexpected content after the end of the document");
            return node;
        }

        private sealed class Parser
        {
            private readonly string text, where;
            private int pos, line = 1;
            private readonly List<string> path = new List<string>();
            internal Parser(string text, string where) { this.text = text; this.where = where; }
            internal bool AtEnd => pos >= text.Length;

            internal ContentException Error(string message) =>
                new ContentException(where + " line " + line + (path.Count == 0 ? "" : " (" + string.Join(".", path) + ")") + ": " + message);

            internal void SkipSpace()
            {
                while (pos < text.Length)
                {
                    char c = text[pos];
                    if (c == '\n') { line++; pos++; }
                    else if (c == ' ' || c == '\t' || c == '\r') pos++;
                    else break;
                }
            }

            internal JsonNode Value()
            {
                SkipSpace();
                if (AtEnd) throw Error("unexpected end of file");
                char c = text[pos];
                if (c == '{') return ObjectValue();
                if (c == '[') return ArrayValue();
                if (c == '"') { var s = new JsonNode(JsonKind.String, line); s.Text = StringValue(); return s; }
                if (c == '-' || (c >= '0' && c <= '9')) return NumberValue();
                if (Match("true")) return Bool(true);
                if (Match("false")) return Bool(false);
                throw Error("unexpected '" + c + "' (null and exponent numbers are not supported)");
            }

            private JsonNode Bool(bool value) { var n = new JsonNode(JsonKind.Bool, line); n.Flag = value; return n; }

            private bool Match(string word)
            {
                if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0) return false;
                pos += word.Length;
                return true;
            }

            private JsonNode ObjectValue()
            {
                var node = new JsonNode(JsonKind.Object, line) { Members = new List<JsonMember>() };
                pos++;
                SkipSpace();
                if (pos < text.Length && text[pos] == '}') { pos++; return node; }
                while (true)
                {
                    SkipSpace();
                    if (AtEnd || text[pos] != '"') throw Error("a quoted field name is expected here");
                    string name = StringValue();
                    foreach (var member in node.Members)
                        if (member.Name == name) throw Error("field '" + name + "' is repeated in the same object");
                    SkipSpace();
                    if (AtEnd || text[pos] != ':') throw Error("missing colon after field '" + name + "'");
                    pos++;
                    path.Add(name);
                    JsonNode value = Value();
                    path.RemoveAt(path.Count - 1);
                    node.Members.Add(new JsonMember(name, value));
                    if (name == "id" && value.Kind == JsonKind.String && path.Count > 0 && path[path.Count - 1].StartsWith("["))
                        path[path.Count - 1] = "[id=" + value.Text + "]";
                    SkipSpace();
                    if (AtEnd) throw Error("object is not closed");
                    if (text[pos] == ',') { pos++; continue; }
                    if (text[pos] == '}') { pos++; return node; }
                    throw Error("missing comma between fields");
                }
            }

            private JsonNode ArrayValue()
            {
                var node = new JsonNode(JsonKind.Array, line) { Items = new List<JsonNode>() };
                pos++;
                SkipSpace();
                if (pos < text.Length && text[pos] == ']') { pos++; return node; }
                while (true)
                {
                    path.Add("[" + node.Items.Count + "]");
                    node.Items.Add(Value());
                    path.RemoveAt(path.Count - 1);
                    SkipSpace();
                    if (AtEnd) throw Error("array is not closed");
                    if (text[pos] == ',') { pos++; continue; }
                    if (text[pos] == ']') { pos++; return node; }
                    throw Error("missing comma between array items");
                }
            }

            private string StringValue()
            {
                var sb = new StringBuilder();
                pos++;
                while (true)
                {
                    if (AtEnd) throw Error("string is not closed");
                    char c = text[pos++];
                    if (c == '"') return sb.ToString();
                    if (c < ' ') throw Error("a string cannot contain a raw control character (use an escape)");
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("string is not closed");
                    char e = text[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 > text.Length || !int.TryParse(text.Substring(pos, 4), NumberStyles.AllowHexSpecifier,
                                CultureInfo.InvariantCulture, out int code)) throw Error("\\u must be followed by four hex digits");
                            sb.Append((char)code); pos += 4; break;
                        default: throw Error("unsupported escape \\" + e);
                    }
                }
            }

            private JsonNode NumberValue()
            {
                int start = pos;
                if (text[pos] == '-') pos++;
                int digits = pos;
                while (pos < text.Length && text[pos] >= '0' && text[pos] <= '9') pos++;
                if (pos == digits) throw Error("number has no digits");
                if (text[digits] == '0' && pos - digits > 1) throw Error("a number cannot start with 0");
                if (pos < text.Length && text[pos] == '.')
                {
                    pos++;
                    int frac = pos;
                    while (pos < text.Length && text[pos] >= '0' && text[pos] <= '9') pos++;
                    if (pos == frac) throw Error("no digits after the decimal point");
                }
                if (pos < text.Length && (text[pos] == 'e' || text[pos] == 'E')) throw Error("exponent numbers are not supported");
                var node = new JsonNode(JsonKind.Number, line);
                node.Text = text.Substring(start, pos - start);
                return node;
            }
        }
    }
}
