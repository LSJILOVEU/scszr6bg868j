using System.Globalization;
using WarCore.Sim;

namespace WarCore.Content
{
    // Typed reading of one merged entry. Every field a section reads is marked used; Done() then rejects the rest.
    internal sealed class EntryReader
    {
        private readonly string pack;
        private readonly ContentEntry entry;
        internal EntryReader(string pack, ContentEntry entry) { this.pack = pack; this.entry = entry; }

        internal ContentField FieldOf(string name) => entry.Fields.Find(f => f.Name == name);

        internal ContentException Bad(ContentField f, string why) => new ContentException("content pack '" + pack + "' layer '" + f.Layer + "' " + entry.Label + " field '" + f.Name + "': " + why);

        private ContentField Take(string name)
        {
            foreach (var f in entry.Fields) if (f.Name == name) { f.Used = true; return f; }
            throw new ContentException("content pack '" + pack + "' layer '" + entry.FirstLayer + "' " + entry.Label + " is missing field '" + name + "'");
        }

        internal void Done()
        {
            foreach (var f in entry.Fields)
                if (!f.Used) throw Bad(f, "unknown field name (misspelled?)");
        }

        // An optional string field: null when the entry has no such field.
        internal string StrOrNull(string name, out ContentField field)
        {
            foreach (var f in entry.Fields)
                if (f.Name == name)
                {
                    f.Used = true; field = f;
                    if (f.Node.Kind != JsonKind.String) throw Bad(f, "must be a string");
                    return f.Node.Text;
                }
            field = null;
            return null;
        }

        internal bool Has(string name)
        {
            foreach (var f in entry.Fields) if (f.Name == name) return true;
            return false;
        }

        internal string Str(string name)
        {
            ContentField f = Take(name);
            if (f.Node.Kind != JsonKind.String) throw Bad(f, "must be a string");
            return f.Node.Text;
        }

        internal bool Bool(string name)
        {
            ContentField f = Take(name);
            if (f.Node.Kind != JsonKind.Bool) throw Bad(f, "must be true or false");
            return f.Node.Flag;
        }

        internal long Long(string name, long min = long.MinValue, long max = long.MaxValue)
        {
            ContentField f = Take(name);
            if (f.Node.Kind != JsonKind.Number || f.Node.Text.IndexOf('.') >= 0) throw Bad(f, "must be an integer");
            if (!long.TryParse(f.Node.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long v)) throw Bad(f, "number too large");
            if (v < min || v > max) throw Bad(f, "out of range [" + min + ", " + max + "]");
            return v;
        }

        internal int Int(string name, int min = int.MinValue, int max = int.MaxValue) => (int)Long(name, min, max);

        // Decimal (a JSON number or a string such as "1.5") to SimFix with integer arithmetic only:
        // digits / 10^fraction, rounded as SimFix.Ratio rounds. The magnitude is checked before Ratio can overflow.
        internal SimFix Fix(string name)
        {
            ContentField f = Take(name);
            if (f.Node.Kind != JsonKind.Number && f.Node.Kind != JsonKind.String) throw Bad(f, "must be a number");
            string text = f.Node.Text;
            int start = text.Length > 0 && text[0] == '-' ? 1 : 0, dot = text.IndexOf('.');
            string whole = dot < 0 ? text.Substring(start) : text.Substring(start, dot - start);
            string frac = dot < 0 ? "" : text.Substring(dot + 1);
            if (whole.Length == 0 || !AllDigits(whole) || (dot >= 0 && (frac.Length == 0 || !AllDigits(frac))))
                throw Bad(f, "must be a decimal number such as 34 or 1.5");
            if (whole.Length + frac.Length > 15) throw Bad(f, "too many digits (at most 15)");
            long scale = 1;
            for (int i = 0; i < frac.Length; i++) scale *= 10;
            long numerator = long.Parse(whole + frac, CultureInfo.InvariantCulture);
            if (numerator / scale > int.MaxValue) throw Bad(f, "value too large");
            return SimFix.Ratio(start == 1 ? -numerator : numerator, scale);
        }

        private static bool AllDigits(string text)
        {
            foreach (char c in text) if (c < '0' || c > '9') return false;
            return true;
        }
    }
}
