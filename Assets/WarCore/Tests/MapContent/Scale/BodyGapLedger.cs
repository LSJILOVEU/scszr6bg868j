using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace WarCore.Tests.Scale
{
    // HC-6: the known-gap ledger tools/size-guard/body-gaps.tsv. Rows are "id<TAB>body<TAB>family<TAB>note" (body giant or
    // tiny; a probe that crashed has the note "threw <Type>"), plus one line "gaps-max<TAB>N" that must equal the number of
    // rows. Lines starting with # are comments. Compare is keyed by (id, body); the note is not compared.
    internal static class BodyGapLedger
    {
        internal const string Relative = "tools/size-guard/body-gaps.tsv";
        internal static readonly string[] Bodies = { ProbeBodies.GiantName, ProbeBodies.TinyName };

        internal sealed class Row
        {
            public string Id, Body, Family, Note;
            public int Line;
            public string Key => KeyOf(Id, Body);
        }

        internal sealed class Ledger
        {
            public readonly List<Row> Rows = new List<Row>();
            public int GapsMax = -1;
            public readonly List<string> Problems = new List<string>();
        }

        // One gap the probes found now.
        internal readonly struct Found
        {
            internal readonly string Id, Body, Note;
            internal Found(string id, string body, string note) { Id = id; Body = body; Note = note; }
            internal string Key => KeyOf(Id, Body);
        }

        // Rows and gaps are matched by probe and body together.
        internal static string KeyOf(string id, string body) => id + " " + body;

        internal static string Path() =>
            System.IO.Path.Combine(Directory.GetParent(ArchitectureTests.AssetRoot()).FullName, Relative);

        internal static Ledger Parse(string[] lines)
        {
            var ledger = new Ledger();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] cells = line.Split('\t');
                if (cells[0] == "gaps-max")
                {
                    if (cells.Length != 2 || !int.TryParse(cells[1], NumberStyles.None, CultureInfo.InvariantCulture, out ledger.GapsMax))
                        ledger.Problems.Add("line " + (i + 1) + ": gaps-max needs one whole number");
                    continue;
                }
                if (cells.Length != 4)
                {
                    ledger.Problems.Add("line " + (i + 1) + ": needs id, body, family and note");
                    continue;
                }
                var row = new Row { Id = cells[0], Body = cells[1], Family = cells[2], Note = cells[3], Line = i + 1 };
                if (Array.IndexOf(Bodies, row.Body) < 0) ledger.Problems.Add("line " + (i + 1) + ": body must be giant or tiny");
                ledger.Rows.Add(row);
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Row row in ledger.Rows)
                if (!seen.Add(row.Key)) ledger.Problems.Add("line " + row.Line + ": " + row.Key + " is listed twice");
            if (ledger.GapsMax < 0) ledger.Problems.Add("the gaps-max line is missing");
            else if (ledger.GapsMax != ledger.Rows.Count) ledger.Problems.Add("gaps-max " + ledger.GapsMax + " but " + ledger.Rows.Count + " rows");
            return ledger;
        }

        // The differences between the gaps found now and the ledger, one line each; empty when they agree.
        internal static List<string> Compare(IEnumerable<Found> found, Ledger ledger)
        {
            var problems = new List<string>(ledger.Problems);
            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (Row row in ledger.Rows) listed.Add(row.Key);
            var now = new HashSet<string>(StringComparer.Ordinal);
            foreach (Found f in found)
            {
                now.Add(f.Key);
                if (!listed.Contains(f.Key))
                    problems.Add("NEW " + f.Key + ": " + f.Note + "  -> the body no longer follows here; fix it (a new row needs a review answer)");
            }
            foreach (Row row in ledger.Rows)
                if (!now.Contains(row.Key)) problems.Add("FIXED " + row.Key + ": delete the row and lower gaps-max (ledger line " + row.Line + ")");
            return problems;
        }
    }
}
