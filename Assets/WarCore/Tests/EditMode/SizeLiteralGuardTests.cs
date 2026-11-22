using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace WarCore.Tests
{
    // HC-0: the project code may not gain a new size, distance, time or speed literal (SizeLiteralScanner) unless the ledger
    // tools/size-guard/ledger.tsv lists it. Rows are "file<TAB>token<TAB>count<TAB>tag<TAB>note"; tags canon (the one source
    // of a value), ui (drawn-only feel) and math (not a size: a unit vector, a cell centre, a unit conversion) need a note;
    // debt rows are old copies to remove, and the line "debt-max<TAB>N" must equal their total count, so a change that
    // removes a copy lowers it and a change that adds one shows in review.
    public sealed class SizeLiteralGuardTests
    {
        internal const string LedgerRelative = "tools/size-guard/ledger.tsv";
        private static readonly string[] Tags = { "canon", "ui", "math", "debt" };

        internal sealed class Row
        {
            public string File, Token, Tag, Note;
            public int Count, Line;
            public string Key => File + "\t" + Token;
        }

        internal sealed class Ledger
        {
            public readonly List<Row> Rows = new List<Row>();
            public int DebtMax = -1;
            public readonly List<string> Problems = new List<string>();
        }

        private static string Root() => Path.Combine(ArchitectureTests.AssetRoot(), "WarCore");

        private static string LedgerPath() => Path.Combine(Directory.GetParent(ArchitectureTests.AssetRoot()).FullName, LedgerRelative);

        internal static Ledger Parse(string[] lines)
        {
            var ledger = new Ledger();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] cells = line.Split('\t');
                if (cells[0] == "debt-max")
                {
                    if (cells.Length != 2 || !int.TryParse(cells[1], NumberStyles.None, CultureInfo.InvariantCulture, out ledger.DebtMax))
                        ledger.Problems.Add("line " + (i + 1) + ": debt-max needs one whole number");
                    continue;
                }
                if (cells.Length < 4 || cells.Length > 5)
                {
                    ledger.Problems.Add("line " + (i + 1) + ": needs file, token, count, tag and an optional note");
                    continue;
                }
                var row = new Row { File = cells[0], Token = cells[1], Tag = cells[3], Note = cells.Length == 5 ? cells[4] : "", Line = i + 1 };
                if (!int.TryParse(cells[2], NumberStyles.None, CultureInfo.InvariantCulture, out row.Count) || row.Count < 1)
                    ledger.Problems.Add("line " + (i + 1) + ": count must be a whole number of at least 1");
                if (Array.IndexOf(Tags, row.Tag) < 0) ledger.Problems.Add("line " + (i + 1) + ": tag must be canon, ui, math or debt");
                if (row.Tag != "debt" && row.Note.Trim().Length == 0)
                    ledger.Problems.Add("line " + (i + 1) + ": a " + row.Tag + " row needs a note saying why the value lives here");
                if (!SizeLiteralScanner.InScope(row.File)) ledger.Problems.Add("line " + (i + 1) + ": " + row.File + " is outside the scanned code");
                ledger.Rows.Add(row);
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Row row in ledger.Rows)
                if (!seen.Add(row.Key)) ledger.Problems.Add("line " + row.Line + ": " + row.File + " " + row.Token + " is listed twice");
            int debt = ledger.Rows.Where(r => r.Tag == "debt").Sum(r => r.Count);
            if (ledger.DebtMax < 0) ledger.Problems.Add("the debt-max line is missing");
            else if (debt != ledger.DebtMax)
                ledger.Problems.Add("debt rows total " + debt + " but debt-max says " + ledger.DebtMax + "; set debt-max to " + debt
                    + " (it may only go down)");
            return ledger;
        }

        // The differences between the scanned tree and the ledger, one line each; empty when they agree.
        internal static List<string> Compare(SortedDictionary<string, List<SizeLiteralScanner.Hit>> tree, Ledger ledger)
        {
            var found = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var file in tree)
                foreach (SizeLiteralScanner.Hit hit in file.Value)
                {
                    string key = file.Key + "\t" + hit.Token;
                    if (!found.TryGetValue(key, out List<string> places)) found.Add(key, places = new List<string>());
                    places.Add(file.Key + ":" + hit.Line + "  " + hit.Text);
                }
            var listed = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (Row row in ledger.Rows) listed[row.Key] = row;
            var problems = new List<string>();
            foreach (var entry in found)
            {
                int allowed = listed.TryGetValue(entry.Key, out Row row) ? row.Count : 0;
                if (entry.Value.Count > allowed)
                    problems.Add("NEW size literal " + Token(entry.Key) + " (" + entry.Value.Count + " found, " + allowed + " listed): "
                        + string.Join(" | ", entry.Value) + "  -> read it from the content pack or from the one named source;"
                        + " only a canon, ui or math row with a note may list it");
            }
            foreach (Row row in ledger.Rows)
            {
                int now = found.TryGetValue(row.Key, out List<string> places) ? places.Count : 0;
                if (now < row.Count)
                    problems.Add("FEWER " + row.File + " " + row.Token + ": ledger line " + row.Line + " says " + row.Count + ", the code has "
                        + now + "  -> " + (now == 0 ? "delete the row" : "set the count to " + now) + (row.Tag == "debt" ? " and lower debt-max" : ""));
            }
            return problems;
        }

        private static string Token(string key) => "'" + key.Substring(key.IndexOf('\t') + 1) + "'";

        [Test]
        public void LedgerIsWellFormed()
        {
            Ledger ledger = Parse(File.ReadAllLines(LedgerPath()));
            Assert.That(ledger.Problems, Is.Empty, string.Join("\n", ledger.Problems));
            Assert.That(ledger.Rows.Count, Is.GreaterThan(0));
            foreach (Row row in ledger.Rows)
                Assert.That(File.Exists(Path.Combine(Root(), row.File)), Is.True, "ledger line " + row.Line + ": " + row.File + " does not exist");
        }

        [Test]
        public void ProjectCodeHasNoUnlistedSizeLiteral()
        {
            Ledger ledger = Parse(File.ReadAllLines(LedgerPath()));
            List<string> problems = Compare(SizeLiteralScanner.ScanTree(Root()), ledger);
            Assert.That(problems, Is.Empty, "\n" + string.Join("\n", problems));
        }

        // Maintenance aid, never run by CI: prints the ledger rows for the tree as it is now. Rows the ledger already has keep
        // their tag and note; new keys come out with the tag "?", which LedgerIsWellFormed refuses until a person sets it.
        [Test, Explicit, Category("Manual")]
        public void PrintLedgerForTheTree()
        {
            Ledger ledger = Parse(File.ReadAllLines(LedgerPath()));
            var old = new Dictionary<string, Row>(StringComparer.Ordinal);
            foreach (Row row in ledger.Rows) old[row.Key] = row;
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var file in SizeLiteralScanner.ScanTree(Root()))
                foreach (SizeLiteralScanner.Hit hit in file.Value)
                {
                    string key = file.Key + "\t" + hit.Token;
                    counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            var text = new StringBuilder();
            foreach (var entry in counts)
            {
                Row known;
                old.TryGetValue(entry.Key, out known);
                text.Append(entry.Key).Append('\t').Append(entry.Value.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(known != null ? known.Tag : "?").Append('\t').Append(known != null ? known.Note : "").Append('\n');
            }
            TestContext.Out.Write(text.ToString());
        }
    }
}
