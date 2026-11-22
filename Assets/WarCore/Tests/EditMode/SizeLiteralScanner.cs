using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WarCore.Tests
{
    // HC-0: finds number literals that look like a size, distance, time or speed in project code, so that each such value has
    // one source. A value is read from the content pack, derived from a named source, or listed in the ledger
    // (tools/size-guard/ledger.tsv) as canon (the one place it is defined), ui (drawn-only feel) or debt (an old copy).
    //
    // Scope: every .cs under Assets/WarCore except paths with a "Tests" or "Verification" directory. Comments, string and
    // char literals are blanked first. A line yields one token per match of:
    //   R1 a real literal (1.5f, 0.25) or an integer with a real suffix (28f), on a line without "Color(" or "Color32(",
    //      except the exemptions listed at Exempt;
    //   R2 SimFix.Ratio / FromInt / FromRaw whose arguments are all integer literals, except an all-zero call;
    //   R3 an integer literal times SimFix.Scale, or SimFix.Scale times or over an integer literal;
    //   R4 on a const or static readonly line, "Name = value" where Name ends in a unit word (Mm, Raw, Metres, Ticks, Radius,
    //      Height, Speed, ...), does not start with Max or Min, and value has no parenthesis, no R1 or R3 token and an
    //      integer literal other than 0 and 1.
    internal static class SizeLiteralScanner
    {
        private static readonly Regex Real = new Regex(
            @"(?<![\w.])((?:\d+\.\d+(?:[eE][-+]?\d+)?|\d+[eE][-+]?\d+)[fFdDmM]?|\d+[fFdDmM])(?![\w.])", RegexOptions.CultureInvariant);
        private static readonly Regex Fix = new Regex(
            @"SimFix\.(?:Ratio|FromInt|FromRaw)\(\s*(-?\d+)L?\s*(?:,\s*(-?\d+)L?\s*)?\)", RegexOptions.CultureInvariant);
        private static readonly Regex Scale = new Regex(
            @"(?<![\w.])\d+L?\s*\*\s*SimFix\.Scale(?![\w])|SimFix\.Scale\s*[*/]\s*\d+L?(?![\w.])", RegexOptions.CultureInvariant);
        private static readonly Regex Declares = new Regex(@"\bconst\b|\bstatic\s+readonly\b", RegexOptions.CultureInvariant);
        private static readonly Regex Assignment = new Regex(@"(\w+)\s*=\s*([^,;=]+)", RegexOptions.CultureInvariant);
        private static readonly Regex UnitName = new Regex(
            @"^(?!Max|Min)\w*(Mm|Raw|Metres|Metre|MmPerSec|Ticks|Seconds|Radius|Height|Reach|Width|Depth|Speed|Range|Distance)$",
            RegexOptions.CultureInvariant);
        private static readonly Regex Integer = new Regex(@"(?<![\w.])(\d+)L?(?![\w.])", RegexOptions.CultureInvariant);
        private static readonly Regex Space = new Regex(@"\s+", RegexOptions.CultureInvariant);

        internal readonly struct Hit
        {
            public readonly int Line;
            public readonly string Token;
            public readonly string Text;

            public Hit(int line, string token, string text) { Line = line; Token = token; Text = text; }
        }

        internal static bool InScope(string relativePath)
        {
            string[] parts = relativePath.Replace('\\', '/').Split('/');
            if (!relativePath.EndsWith(".cs", StringComparison.Ordinal)) return false;
            for (int i = 0; i < parts.Length - 1; i++)
                if (parts[i] == "Tests" || parts[i] == "Verification") return false;
            return true;
        }

        internal static List<Hit> Scan(string source)
        {
            var hits = new List<Hit>();
            string[] lines = Blank(source).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.IndexOf("Color(", StringComparison.Ordinal) < 0 && line.IndexOf("Color32(", StringComparison.Ordinal) < 0)
                    foreach (Match m in Real.Matches(line))
                        if (!Exempt(line, m)) hits.Add(new Hit(i + 1, m.Groups[1].Value, line.Trim()));
                foreach (Match m in Fix.Matches(line))
                    if (!AllZero(m)) hits.Add(new Hit(i + 1, Normal(m.Value), line.Trim()));
                foreach (Match m in Scale.Matches(line)) hits.Add(new Hit(i + 1, Normal(m.Value), line.Trim()));
                if (Declares.IsMatch(line))
                    foreach (Match m in Assignment.Matches(line))
                    {
                        string name = m.Groups[1].Value, value = m.Groups[2].Value;
                        if (!UnitName.IsMatch(name) || value.IndexOf('(') >= 0 || Real.IsMatch(value) || Scale.IsMatch(value)) continue;
                        if (!HasSizeInteger(value)) continue;
                        hits.Add(new Hit(i + 1, name + "=" + Normal(value), line.Trim()));
                    }
            }
            return hits;
        }

        private static bool AllZero(Match m) =>
            IsZero(m.Groups[1].Value) && (!m.Groups[2].Success || IsZero(m.Groups[2].Value));

        private static bool IsZero(string digits) => digits.TrimStart('-').Trim('0').Length == 0;

        private static bool HasSizeInteger(string value)
        {
            foreach (Match m in Integer.Matches(value))
            {
                string d = m.Groups[1].Value.TrimStart('0');
                if (d.Length > 1 || (d.Length == 1 && d != "1")) return true;
            }
            return false;
        }

        // R1 leaves out: 0 and 1; exponent forms (tolerances such as 1e-3f); 1000 and 1000000 (millimetre and microsecond
        // conversions); 2 and 0.5 right after '*' or '/' or right before '*' (halving and doubling).
        private static bool Exempt(string line, Match m)
        {
            string literal = m.Groups[1].Value;
            if (literal.IndexOf('e') >= 0 || literal.IndexOf('E') >= 0) return true;
            double v = double.Parse(literal.TrimEnd('f', 'F', 'd', 'D', 'm', 'M'), NumberStyles.Float, CultureInfo.InvariantCulture);
            if (v == 0.0 || v == 1.0 || v == 1000.0 || v == 1000000.0) return true;
            if (v != 2.0 && v != 0.5) return false;
            string before = line.Substring(0, m.Index).TrimEnd(), after = line.Substring(m.Index + m.Length).TrimStart();
            return before.EndsWith("*", StringComparison.Ordinal) || before.EndsWith("/", StringComparison.Ordinal)
                || after.StartsWith("*", StringComparison.Ordinal);
        }

        private static string Normal(string text) => Space.Replace(text, "");

        // Blanks block comments (keeping their line breaks), then on each line string and char literals and the line comment.
        internal static string Blank(string source)
        {
            var text = new StringBuilder(source.Length);
            int i = 0, n = source.Length;
            while (i < n)
            {
                char c = source[i];
                char next = i + 1 < n ? source[i + 1] : '\0';
                if (c == '/' && next == '*')
                {
                    int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0) end = n - 2;
                    for (int k = i; k < end + 2 && k < n; k++) if (source[k] == '\n') text.Append('\n');
                    i = end + 2;
                    continue;
                }
                if (c == '/' && next == '/')
                {
                    while (i < n && source[i] != '\n') i++;
                    continue;
                }
                int quote = StringStart(source, i);
                if (quote >= 0)
                {
                    bool verbatim = source.IndexOf('@', i, quote - i) >= 0;
                    i = quote + 1;
                    while (i < n && source[i] != '\n')
                    {
                        if (!verbatim && source[i] == '\\') { i += 2; continue; }
                        if (source[i] == '"')
                        {
                            if (verbatim && i + 1 < n && source[i + 1] == '"') { i += 2; continue; }
                            i++;
                            break;
                        }
                        i++;
                    }
                    text.Append("\"\"");
                    continue;
                }
                if (c == '\'')
                {
                    int k = i + 1;
                    if (k < n && source[k] == '\\') k += 2; else k++;
                    if (k < n && source[k] == '\'') { text.Append("''"); i = k + 1; continue; }
                }
                text.Append(c);
                i++;
            }
            return text.ToString();
        }

        // The index of the opening quote when a string literal starts at i ("...", @"...", $"...", $@"..." or @$"..."), else -1.
        private static int StringStart(string s, int i)
        {
            int k = i;
            while (k < s.Length && k < i + 2 && (s[k] == '@' || s[k] == '$')) k++;
            if (k < s.Length && s[k] == '"' && (k == i || !char.IsLetterOrDigit(i > 0 ? s[i - 1] : ' '))) return k;
            return -1;
        }

        // Every in-scope file under root (Assets/WarCore), keyed by its path relative to root with '/' separators.
        internal static SortedDictionary<string, List<Hit>> ScanTree(string root)
        {
            var result = new SortedDictionary<string, List<Hit>>(StringComparer.Ordinal);
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string relative = file.Substring(root.Length).TrimStart('/', '\\').Replace('\\', '/');
                if (!InScope(relative)) continue;
                List<Hit> hits = Scan(File.ReadAllText(file));
                if (hits.Count > 0) result.Add(relative, hits);
            }
            return result;
        }
    }
}
