using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace WarCore.Tests
{
    // Headless only. Every entry of tools/mutation/lists/*.txt must still apply to the code: its file exists as a regular
    // file (no symbolic link on the way) and its original text occurs there exactly once, counted as UTF-8 bytes without
    // overlap. That is the rule tools/mutation/mutate.py applies before it mutates; an entry that breaks it is reported as
    // invalid there and silently stops guarding anything. The parser mirrors mutate.py parse_list, so both read the same
    // entries. A list made to hold entries that do not apply (a tool self-test) belongs in a subdirectory of lists/,
    // which this check does not read.
    public sealed class MutationListTests
    {
        internal sealed class Entry
        {
            public readonly string Id, File, Original;
            public Entry(string id, string file, string original) { Id = id; File = file; Original = original; }
        }

        private static readonly Regex IdRe = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant);
        private static readonly Regex HeadRe = new Regex(@"^\[(.+)\]$", RegexOptions.CultureInvariant);
        private static readonly Regex KeyRe = new Regex(@"^(file|filter|expect|note)\s*=\s*(.*)$", RegexOptions.CultureInvariant);

        // mutate.py parse_list: text mode (\r\n and a lone \r read as \n), split on \n, a final empty line dropped, block lines
        // "| text" or a bare "|" joined with \n, comments and blank lines skipped outside blocks. Throws FormatException.
        internal static List<Entry> Parse(string name, string text)
        {
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int count = lines.Length;
            if (count > 0 && lines[count - 1].Length == 0) count--;
            var entries = new List<Entry>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> cur = null;
            int expects = 0;
            List<string> block = null;
            string blockKey = null;

            Exception Bad(int n, string msg) => new FormatException(name + ":" + n + ": " + msg);
            void EndBlock()
            {
                if (blockKey == null) return;
                cur[blockKey] = string.Join("\n", block);
                blockKey = null; block = null;
            }
            void Close(int n)
            {
                if (cur == null) return;
                foreach (string k in new[] { "file", "filter", "note" })
                    if (!cur.ContainsKey(k)) throw Bad(n, "entry " + cur["id"] + " has no " + k);
                if (expects == 0) throw Bad(n, "entry " + cur["id"] + " has no expect");
                if (!cur.TryGetValue("original", out string original) || original.Length == 0) throw Bad(n, "entry " + cur["id"] + " has no original text");
                if (!cur.ContainsKey("replacement")) throw Bad(n, "entry " + cur["id"] + " has no replacement block");
                entries.Add(new Entry(cur["id"], cur["file"], original));
            }

            for (int i = 0; i < count; i++)
            {
                int n = i + 1;
                string line = lines[i];
                if (blockKey != null && (line == "|" || line.StartsWith("| ", StringComparison.Ordinal)))
                {
                    block.Add(line.Substring(Math.Min(2, line.Length)));
                    continue;
                }
                EndBlock();
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                Match head = HeadRe.Match(line);
                if (head.Success)
                {
                    Close(n);
                    string id = head.Groups[1].Value;
                    if (!IdRe.IsMatch(id)) throw Bad(n, "bad id");
                    if (!ids.Add(id)) throw Bad(n, "duplicate id " + id);
                    cur = new Dictionary<string, string>(StringComparer.Ordinal) { ["id"] = id };
                    expects = 0;
                    continue;
                }
                if (cur == null) throw Bad(n, "text outside an entry");
                if (line == "original:" || line == "replacement:")
                {
                    string key = line.Substring(0, line.Length - 1);
                    if (cur.ContainsKey(key)) throw Bad(n, "second " + key + " block");
                    blockKey = key; block = new List<string>();
                    continue;
                }
                Match m = KeyRe.Match(line);
                if (!m.Success) throw Bad(n, "unknown line");
                string k2 = m.Groups[1].Value, val = m.Groups[2].Value.Trim();
                if (val.Length == 0) throw Bad(n, "empty " + k2);
                if (k2 == "expect") expects++;
                else if (cur.ContainsKey(k2)) throw Bad(n, "second " + k2);
                else cur[k2] = val;
            }
            EndBlock();
            Close(count);
            foreach (Entry e in entries)
                if (!Normalized(e.File))
                    throw new FormatException(name + ": entry " + e.Id + ": file must be a normalized repository-relative path");
            if (entries.Count == 0) throw new FormatException(name + ": no entries");
            return entries;
        }

        // Same as mutate.py: not absolute, no backslash, no "..", and equal to its normal form (no empty or "." segment).
        internal static bool Normalized(string path)
        {
            if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains('\\')) return false;
            foreach (string segment in path.Split('/'))
                if (segment.Length == 0 || segment == "." || segment == "..") return false;
            return true;
        }

        // Python bytes.count: occurrences without overlap, scanning left to right.
        internal static int CountBytes(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0) throw new ArgumentException("empty needle", nameof(needle));
            int found = 0;
            ReadOnlySpan<byte> rest = haystack;
            while (true)
            {
                int at = rest.IndexOf(needle);
                if (at < 0) return found;
                found++;
                rest = rest.Slice(at + needle.Length);
            }
        }

        // One line per entry that does not apply: "<list>:<id> ...". read returns the bytes of a repository-relative file,
        // or null when it is missing, not a regular file, or reached through a symbolic link.
        internal static List<string> Problems(IEnumerable<KeyValuePair<string, string>> lists, Func<string, byte[]> read, out int entryCount)
        {
            var problems = new List<string>();
            entryCount = 0;
            foreach (KeyValuePair<string, string> list in lists)
            {
                foreach (Entry e in Parse(list.Key, list.Value))
                {
                    entryCount++;
                    byte[] bytes = read(e.File);
                    if (bytes == null) { problems.Add(list.Key + ":" + e.Id + " file missing, not a regular file, or behind a symbolic link: " + e.File); continue; }
                    int found = CountBytes(bytes, Encoding.UTF8.GetBytes(e.Original));
                    if (found != 1) problems.Add(list.Key + ":" + e.Id + " original found " + found + " times in " + e.File);
                }
            }
            return problems;
        }

        // The bytes of root/relative, or null when a part of the path is a symbolic link or the end is not a regular file.
        internal static byte[] ReadRegular(string root, string relative)
        {
            string path = root;
            foreach (string segment in relative.Split('/'))
            {
                path = Path.Combine(path, segment);
                var info = new FileInfo(path);
                if (!info.Exists && !Directory.Exists(path)) return null;
                if (info.Exists ? info.LinkTarget != null : new DirectoryInfo(path).LinkTarget != null) return null;
            }
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
        }

        // The directory above the test binary that holds tools/mutation/lists.
        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "tools", "mutation", "lists")))
                directory = directory.Parent;
            if (directory == null) throw new DirectoryNotFoundException("Cannot locate tools/mutation/lists above the test binary.");
            return directory.FullName;
        }

        private static KeyValuePair<string, string> List(string name, string text) => new KeyValuePair<string, string>(name, text);

        private static Func<string, byte[]> Files(params (string Path, string Text)[] files) =>
            path => files.Where(f => f.Path == path).Select(f => Encoding.UTF8.GetBytes(f.Text)).FirstOrDefault();

        private static string OneEntry(string id, string file, params string[] original) =>
            "[" + id + "]\nfile = " + file + "\nfilter = FullyQualifiedName~Sample\nexpect = Sample.Test\nnote = sample\noriginal:\n"
            + string.Concat(original.Select(l => l.Length == 0 ? "|\n" : "| " + l + "\n")) + "replacement:\n| x\n\n";

        [Test]
        public void EveryListEntryAppliesExactlyOnce()
        {
            string root = RepositoryRoot();
            string[] paths = Directory.GetFiles(Path.Combine(root, "tools", "mutation", "lists"), "*.txt", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Assert.That(paths.Length, Is.GreaterThan(0), "No mutation lists found.");
            var strict = new UTF8Encoding(false, true);
            var lists = paths.Select(p => List(Path.GetFileName(p), strict.GetString(System.IO.File.ReadAllBytes(p)))).ToList();
            List<string> problems = Problems(lists, rel => ReadRegular(root, rel), out int entries);
            TestContext.WriteLine("mutation lists: " + paths.Length + ", entries: " + entries);
            Assert.That(entries, Is.GreaterThan(0), "The lists hold no entries.");
            Assert.That(problems, Is.Empty,
                "Each entry's original must occur exactly once in its file (byte for byte; a checkout that turns line endings into "
                + "CRLF breaks multi-line originals). Re-point each entry to the code it was written for:\n" + string.Join("\n", problems));
        }

        [Test]
        public void SampleCountsMustBeExactlyOne()
        {
            var lists = new[]
            {
                List("a.txt", OneEntry("zero", "f.cs", "gone") + OneEntry("one", "f.cs", "kept") + OneEntry("two", "f.cs", "twice")),
            };
            List<string> problems = Problems(lists, Files(("f.cs", "kept twice twice")), out int entries);
            Assert.That(entries, Is.EqualTo(3));
            Assert.That(problems, Is.EqualTo(new[]
            {
                "a.txt:zero original found 0 times in f.cs",
                "a.txt:two original found 2 times in f.cs",
            }));
        }

        [Test]
        public void SampleEveryListIsRead()
        {
            var lists = new[] { List("a.txt", OneEntry("good", "f.cs", "kept")), List("b.txt", OneEntry("bad", "f.cs", "gone")) };
            Assert.That(Problems(lists, Files(("f.cs", "kept")), out int entries), Is.EqualTo(new[] { "b.txt:bad original found 0 times in f.cs" }));
            Assert.That(entries, Is.EqualTo(2));
        }

        [Test]
        public void SampleMissingFileIsReported()
        {
            var lists = new[] { List("a.txt", OneEntry("lost", "g.cs", "kept")) };
            Assert.That(Problems(lists, Files(("f.cs", "kept")), out _),
                Is.EqualTo(new[] { "a.txt:lost file missing, not a regular file, or behind a symbolic link: g.cs" }));
        }

        [Test]
        public void SampleOverlappingOccurrencesCountOnce()
        {
            Assert.That(CountBytes(Encoding.UTF8.GetBytes("aaa"), Encoding.UTF8.GetBytes("aa")), Is.EqualTo(1));
            Assert.That(CountBytes(Encoding.UTF8.GetBytes("aaaa"), Encoding.UTF8.GetBytes("aa")), Is.EqualTo(2));
            Assert.That(CountBytes(Encoding.UTF8.GetBytes("é"), Encoding.UTF8.GetBytes("e")), Is.EqualTo(0));
        }

        [Test]
        public void SampleBlocksJoinLikeTheTool()
        {
            List<Entry> lf = Parse("a.txt", OneEntry("multi", "f.cs", "first", "", "third") + "# a comment\n");
            Assert.That(lf.Count, Is.EqualTo(1));
            Assert.That(lf[0].Original, Is.EqualTo("first\n\nthird"));
            List<Entry> crlf = Parse("a.txt", OneEntry("multi", "f.cs", "first", "", "third").Replace("\n", "\r\n"));
            Assert.That(crlf[0].Original, Is.EqualTo("first\n\nthird"));
            Assert.That(crlf[0].File, Is.EqualTo("f.cs"));
        }

        [Test]
        public void SampleBadListsAreRefused()
        {
            string one = OneEntry("same", "f.cs", "kept");
            Assert.That(() => Parse("a.txt", one + one), Throws.TypeOf<FormatException>().With.Message.Contains("duplicate id same"));
            Assert.That(() => Parse("a.txt", OneEntry("e", "f.cs", "")), Throws.TypeOf<FormatException>().With.Message.Contains("has no original text"));
            Assert.That(() => Parse("a.txt", "file = f.cs\n"), Throws.TypeOf<FormatException>().With.Message.Contains("text outside an entry"));
            Assert.That(() => Parse("a.txt", "# only a comment\n"), Throws.TypeOf<FormatException>().With.Message.Contains("no entries"));
            foreach (string path in new[] { "/f.cs", "a\\f.cs", "a/../f.cs", "a//f.cs", "./f.cs", "a/" })
                Assert.That(() => Parse("a.txt", OneEntry("p", path, "kept")), Throws.TypeOf<FormatException>().With.Message.Contains("normalized"), path);
        }
        [Test]
        public void SampleSymbolicLinkIsNotRead()
        {
            string root = Path.Combine(Path.GetTempPath(), "mutation-list-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "real"));
            try
            {
                System.IO.File.WriteAllText(Path.Combine(root, "real", "f.cs"), "kept");
                bool linked;
                try
                {
                    System.IO.File.CreateSymbolicLink(Path.Combine(root, "real", "l.cs"), Path.Combine(root, "real", "f.cs"));
                    Directory.CreateSymbolicLink(Path.Combine(root, "dir"), Path.Combine(root, "real"));
                    linked = true;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { linked = false; }
                Assume.That(linked, "This machine does not allow symbolic links.");
                Assert.That(ReadRegular(root, "real/f.cs"), Is.EqualTo(Encoding.UTF8.GetBytes("kept")));
                Assert.That(ReadRegular(root, "real/l.cs"), Is.Null);
                Assert.That(ReadRegular(root, "dir/f.cs"), Is.Null);
                Assert.That(ReadRegular(root, "real"), Is.Null);
                Assert.That(ReadRegular(root, "real/none.cs"), Is.Null);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
