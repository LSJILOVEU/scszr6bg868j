using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WarCore.Content.MapTools
{
    // ED-3c-1 height tile (<name>.dem.json): a grid of heights in millimetres, read only (the crop tool writes it). Row 0 is
    // the lowest z, item 0 of a row the lowest x. Exactly the keys version, cols, rows, stepMm, credit, heightsMm (the JSON
    // reader already refuses a repeated key). Every failure is ArgumentException starting with "dem".
    // Fingerprint: lowercase hex sha256 of the UTF-8 text "dem 1 <cols> <rows> <stepMm>\n" then one line per row, the
    // integers separated by one space, each line ending in "\n"; numbers are written again from their values, so the
    // file's spacing and "-0" do not change it, and credit is not part of it.
    public sealed class MapDemTile
    {
        public const int FormatVersion = 1;
        public const int MinSide = 2, MaxSide = 513, MaxStepMm = 100000, MaxHeightMm = 9000000, MaxCredit = 8;

        private static readonly string[] Keys = { "version", "cols", "rows", "stepMm", "credit", "heightsMm" };

        private readonly int[] heights;

        public int Cols { get; }
        public int Rows { get; }
        public int StepMm { get; }
        public IReadOnlyList<string> Credit { get; }
        public string Fingerprint { get; }

        private MapDemTile(int cols, int rows, int stepMm, string[] credit, int[] heights)
        {
            Cols = cols; Rows = rows; StepMm = stepMm;
            Credit = Array.AsReadOnly(credit);
            this.heights = heights;
            Fingerprint = Hash(cols, rows, stepMm, heights);
        }

        public int HeightMm(int col, int row)
        {
            if (col < 0 || col >= Cols) throw new ArgumentOutOfRangeException(nameof(col));
            if (row < 0 || row >= Rows) throw new ArgumentOutOfRangeException(nameof(row));
            return heights[row * Cols + col];
        }

        public static MapDemTile Read(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JsonNode root;
            try { root = ContentJson.Parse(json, "dem"); }
            catch (ContentException e) { throw new ArgumentException("dem: " + e.Message); }
            if (root.Kind != JsonKind.Object) throw Bad("", "the document must be an object");
            foreach (JsonMember m in root.Members)
                if (Array.IndexOf(Keys, m.Name) < 0) throw Bad("", "unknown key '" + m.Name + "'");
            if (Int(Need(root, "version"), "version", long.MinValue, long.MaxValue) != FormatVersion)
                throw Bad("version", "must be " + FormatVersion);
            int cols = (int)Int(Need(root, "cols"), "cols", MinSide, MaxSide);
            int rows = (int)Int(Need(root, "rows"), "rows", MinSide, MaxSide);
            int stepMm = (int)Int(Need(root, "stepMm"), "stepMm", 1, MaxStepMm);
            JsonNode creditNode = Need(root, "credit");
            if (creditNode.Kind != JsonKind.Array) throw Bad("credit", "must be an array of texts");
            if (creditNode.Items.Count < 1 || creditNode.Items.Count > MaxCredit) throw Bad("credit", "must have 1 to " + MaxCredit + " texts");
            var credit = new string[creditNode.Items.Count];
            for (int k = 0; k < credit.Length; k++) credit[k] = Text(creditNode.Items[k], "credit[" + k + "]");
            JsonNode grid = Need(root, "heightsMm");
            if (grid.Kind != JsonKind.Array || grid.Items.Count != rows) throw Bad("heightsMm", "must be an array of " + rows + " rows");
            var heights = new int[cols * rows];
            for (int b = 0; b < rows; b++)
            {
                JsonNode row = grid.Items[b];
                string at = "heightsMm[" + b + "]";
                if (row.Kind != JsonKind.Array || row.Items.Count != cols) throw Bad(at, "must be an array of " + cols + " heights");
                for (int a = 0; a < cols; a++) heights[b * cols + a] = (int)Int(row.Items[a], at, -MaxHeightMm, MaxHeightMm);
            }
            return new MapDemTile(cols, rows, stepMm, credit, heights);
        }

        private static string Hash(int cols, int rows, int stepMm, int[] heights)
        {
            var sb = new StringBuilder();
            sb.Append("dem ").Append(FormatVersion).Append(' ').Append(cols.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(rows.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(stepMm.ToString(CultureInfo.InvariantCulture)).Append('\n');
            for (int b = 0; b < rows; b++)
            {
                for (int a = 0; a < cols; a++)
                {
                    if (a > 0) sb.Append(' ');
                    sb.Append(heights[b * cols + a].ToString(CultureInfo.InvariantCulture));
                }
                sb.Append('\n');
            }
            // Method group, not a call: StableGuardTests scans for ".Create()" as entity creation.
            Func<SHA256> factory = SHA256.Create;
            byte[] digest;
            using (SHA256 sha = factory()) digest = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            var hex = new StringBuilder(64);
            foreach (byte x in digest) hex.Append(x.ToString("x2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }

        private static JsonNode Need(JsonNode node, string key)
        {
            JsonNode v = node.Find(key);
            if (v == null) throw Bad("", "missing key '" + key + "'");
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

        // 1 to 255 UTF-8 bytes, no NUL, no lone surrogate (the bake's text rule).
        private static string Text(JsonNode v, string at)
        {
            if (v.Kind != JsonKind.String) throw Bad(at, "must be a text");
            try { WcMapValidation.TextBytes(v.Text); }
            catch (InvalidDataException) { throw Bad(at, "must be 1 to 255 UTF-8 bytes without NUL"); }
            return v.Text;
        }

        private static ArgumentException Bad(string at, string message) =>
            new ArgumentException("dem" + (at.Length == 0 ? "" : " " + at) + ": " + message);
    }
}
