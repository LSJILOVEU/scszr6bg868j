using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using WarCore.Content.MapTools;

namespace WarCore.Presentation
{
    // The words a map tool shows for a problem code. The table is one text file, UTF-8 without byte order mark: one line
    // per code, "<code><TAB><text>", sorted by code in ordinal order; empty lines and lines starting with # are skipped.
    // A broken file is refused with InvalidDataException naming the line (comments and empty lines count as lines).
    public sealed class MapMessages
    {
        public const string RelativePath = "Assets/WarCore/Content/MapTools/Text/map-messages.zh.txt";
        private const char Tab = '\t';

        private readonly Dictionary<string, string> texts;
        private readonly string[] codes;

        private MapMessages(Dictionary<string, string> texts, string[] codes) { this.texts = texts; this.codes = codes; }

        // The codes of the table in file order (which is ordinal order).
        public IReadOnlyList<string> Codes => Array.AsReadOnly(codes);

        public static MapMessages Load(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            string text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(path));
            return Parse(text, Path.GetFileName(path));
        }

        // The table of the project, found by looking upwards from the working directory (as ContentCatalog.Find).
        public static MapMessages Find()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, RelativePath);
                if (File.Exists(candidate)) return Load(candidate);
                dir = dir.Parent;
            }
            throw new FileNotFoundException("message table " + RelativePath + " not found (searched upwards from " + Directory.GetCurrentDirectory() + ")");
        }

        private static MapMessages Parse(string content, string name)
        {
            var texts = new Dictionary<string, string>(StringComparer.Ordinal);
            var order = new List<string>();
            string[] lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Trim().Length == 0 || line[0] == '#') continue;
                int at = line.IndexOf(Tab);
                if (at < 0) throw Fail(name, i, "no tab between the code and the text");
                string code = line.Substring(0, at), text = line.Substring(at + 1);
                if (code.Length == 0) throw Fail(name, i, "empty code");
                if (text.Trim().Length == 0) throw Fail(name, i, "empty text for " + code);
                if (texts.ContainsKey(code)) throw Fail(name, i, "code " + code + " is listed twice");
                if (order.Count > 0 && string.CompareOrdinal(order[order.Count - 1], code) > 0)
                    throw Fail(name, i, "code " + code + " is out of order (after " + order[order.Count - 1] + ")");
                texts.Add(code, text);
                order.Add(code);
            }
            return new MapMessages(texts, order.ToArray());
        }

        private static InvalidDataException Fail(string name, int index, string what) =>
            new InvalidDataException(name + " line " + (index + 1) + ": " + what);

        // The text of a code; a code the table does not know is returned as it is.
        public string Text(string code) => code != null && texts.TryGetValue(code, out string text) ? text : code;

        // The text of a problem. A writer problem (MAPDESC_WCMAP) carries the writer's own code in Id: both texts, joined.
        public string Describe(MapProblem problem)
        {
            if (problem == null) throw new ArgumentNullException(nameof(problem));
            string text = Text(problem.Code);
            if (problem.Code == "MAPDESC_WCMAP" && problem.Id != null) text += "：" + Text(problem.Id);
            return text;
        }
    }
}
