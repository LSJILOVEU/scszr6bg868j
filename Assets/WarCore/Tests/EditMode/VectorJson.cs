using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace WarCore.Tests
{
    // Integer tokens must never pass through floating point: vectors include both long endpoints.
    internal sealed class VectorJson
    {
        private readonly object value;
        private VectorJson(object value)
        {
            this.value = value;
        }

        internal VectorJson this[string key] => ((Dictionary<string, VectorJson>)value)[key];
        internal VectorJson this[int index] => ((List<VectorJson>)value)[index];
        internal int Count => ((List<VectorJson>)value).Count;
        internal IEnumerable<string> Keys => ((Dictionary<string, VectorJson>)value).Keys;
        internal long Integer => (long)value;
        internal bool Boolean => (bool)value;
        internal string Text => (string)value;
        internal bool IsString => value is string;
        internal bool IsNull => value == null;

        internal long[] Integers()
        {
            var result = new long[Count];
            for (int i = 0; i < result.Length; i++)
                result[i] = this[i].Integer;
            return result;
        }

        internal static string PathFor(string file) => Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Tests/EditMode/Vectors", file);
        internal static VectorJson Load(string file) => Parse(File.ReadAllText(PathFor(file)));
        internal static VectorJson Parse(string source) => new Parser(source).Document();

        private sealed class Parser
        {
            private readonly string source;
            private int offset;

            internal Parser(string source)
            {
                this.source = source;
            }

            internal VectorJson Document()
            {
                VectorJson result = Value();
                Whitespace();
                if (offset != source.Length)
                    throw Error("Trailing input");
                return result;
            }

            private FormatException Error(string message) => new FormatException(message + " at JSON offset " + offset);

            private void Whitespace()
            {
                while (offset < source.Length && (source[offset] == ' ' || source[offset] == '\t' || source[offset] == '\n' || source[offset] == '\r'))
                    offset++;
            }

            private bool Take(char token)
            {
                Whitespace();
                if (offset == source.Length || source[offset] != token)
                    return false;
                offset++;
                return true;
            }

            private void Require(char token)
            {
                if (!Take(token))
                    throw Error("Expected '" + token + "'");
            }

            private VectorJson Value()
            {
                Whitespace();
                if (offset == source.Length)
                    throw Error("Missing value");
                char token = source[offset];
                if (token == '{')
                    return Object();
                if (token == '[')
                    return Array();
                if (token == '"')
                    return new VectorJson(String());
                if (token == 't')
                    return Literal("true", true);
                if (token == 'f')
                    return Literal("false", false);
                if (token == 'n')
                    return Literal("null", null);
                return Number();
            }

            private VectorJson Object()
            {
                Require('{');
                var fields = new Dictionary<string, VectorJson>(StringComparer.Ordinal);
                if (Take('}'))
                    return new VectorJson(fields);
                do
                {
                    string key = String();
                    Require(':');
                    if (fields.ContainsKey(key))
                        throw Error("Duplicate property " + key);
                    fields.Add(key, Value());
                }
                while (Take(','));
                Require('}');
                return new VectorJson(fields);
            }

            private VectorJson Array()
            {
                Require('[');
                var items = new List<VectorJson>();
                if (Take(']'))
                    return new VectorJson(items);
                do
                {
                    items.Add(Value());
                }
                while (Take(','));
                Require(']');
                return new VectorJson(items);
            }

            private string String()
            {
                Require('"');
                var text = new StringBuilder();
                while (offset < source.Length)
                {
                    char token = source[offset++];
                    if (token == '"')
                        return text.ToString();
                    if (token < 32)
                        throw Error("Unescaped control character");
                    if (token != '\\')
                    {
                        text.Append(token);
                        continue;
                    }

                    if (offset == source.Length)
                        throw Error("Incomplete escape");
                    switch (source[offset++])
                    {
                        case '"':
                            text.Append('"');
                            break;
                        case '\\':
                            text.Append('\\');
                            break;
                        case '/':
                            text.Append('/');
                            break;
                        case 'b':
                            text.Append('\b');
                            break;
                        case 'f':
                            text.Append('\f');
                            break;
                        case 'n':
                            text.Append('\n');
                            break;
                        case 'r':
                            text.Append('\r');
                            break;
                        case 't':
                            text.Append('\t');
                            break;
                        case 'u':
                            if (offset + 4 > source.Length)
                                throw Error("Incomplete unicode escape");
                            text.Append((char)ushort.Parse(source.Substring(offset, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                            offset += 4;
                            break;
                        default:
                            throw Error("Unknown escape");
                    }
                }

                throw Error("Unterminated string");
            }

            private VectorJson Literal(string token, object result)
            {
                if (offset + token.Length > source.Length || string.CompareOrdinal(source, offset, token, 0, token.Length) != 0)
                    throw Error("Invalid literal");
                offset += token.Length;
                return new VectorJson(result);
            }

            private static bool Digit(char token) => token >= '0' && token <= '9';

            private VectorJson Number()
            {
                int start = offset;
                if (source[offset] == '-')
                    offset++;
                int digits = offset;
                while (offset < source.Length && Digit(source[offset]))
                    offset++;
                if (digits == offset || (offset - digits > 1 && source[digits] == '0'))
                    throw Error("Invalid integer");
                if (offset < source.Length && (source[offset] == '.' || source[offset] == 'e' || source[offset] == 'E'))
                    throw Error("Only exact integer tokens are supported");
                return new VectorJson(long.Parse(source.Substring(start, offset - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture));
            }
        }
    }
}
