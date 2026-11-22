using System.Collections.Generic;

namespace WarCore.Content
{
    // The optional section "stats": { "<line>": { dragFloorPermille?, dragCeilPermille? }, ... }: per derived number (stat line)
    // the floor and ceiling of its drag product. One row per line, each with its bounds in Rows; a row needs at least one of
    // the two fields. A layer that writes a row replaces its parents' row whole; rows it does not write are inherited. An
    // empty object and an unknown row name are refused. Without the section nothing is published and
    // ContentPack.SectionOrNull<StatClamps>() returns null.
    internal sealed class StatsSection : IContentSection
    {
        internal const string SectionKey = "stats", FloorField = "dragFloorPermille", CeilField = "dragCeilPermille";
        internal const string SpreadRow = "spread";

        // One row per stat line, with its bounds. X1: every drag line takes a floor of 1..1000 and a ceiling of 1000..4000, so a
        // unit whose drags are all 1000 is never moved by the clamp.
        internal static readonly StatRowDef[] Rows =
        {
            new StatRowDef(SpreadRow, 1, 1000, 1000, 4000),
        };

        public string Key => SectionKey;
        public int BuildStage => 0;

        public void Merge(List<ContentEntry> entries, JsonNode node, string layer)
        {
            if (node.Kind != JsonKind.Object) throw new ContentException("content layer '" + layer + "' stats must be an object");
            if (node.Members.Count == 0) throw new ContentException("content layer '" + layer + "' stats {} must not be empty");
            foreach (JsonMember member in node.Members)
            {
                if (Row(member.Name) == null)
                    throw new ContentException("content layer '" + layer + "' stats field '" + member.Name + "': unknown stat line (allowed: " + RowNames() + ")");
                if (member.Value.Kind != JsonKind.Object)
                    throw new ContentException("content layer '" + layer + "' stats '" + member.Name + "' must be an object");
                entries.RemoveAll(e => e.Id == member.Name);
                var entry = new ContentEntry(SectionKey, member.Name, layer);
                foreach (JsonMember field in member.Value.Members)
                {
                    if (field.Value.Kind == JsonKind.Object || field.Value.Kind == JsonKind.Array)
                        throw new ContentException("content layer '" + layer + "' " + entry.Label + " field '" + field.Name + "' must be a number");
                    entry.Fields.Add(new ContentField(field.Name, field.Value, layer));
                }
                entries.Add(entry);
            }
        }

        public void Build(SectionBuild b)
        {
            List<ContentEntry> entries = b.Entries(Key);
            if (entries.Count == 0) return;
            var clamps = new StatClamps();
            foreach (ContentEntry entry in entries)
            {
                StatRowDef row = Row(entry.Id);
                var r = new EntryReader(b.Pack, entry);
                if (!r.Has(FloorField) && !r.Has(CeilField))
                    throw new ContentException("content pack '" + b.Pack + "' layer '" + entry.FirstLayer + "' " + entry.Label + " needs " + FloorField + " or " + CeilField);
                int floor = r.Has(FloorField) ? r.Int(FloorField, row.MinFloor, row.MaxFloor) : 0;
                int ceil = r.Has(CeilField) ? r.Int(CeilField, row.MinCeil, row.MaxCeil) : 0;
                r.Done();
                clamps.Set(row.Id, floor, ceil);
            }
            b.Publish(clamps);
        }

        private static StatRowDef Row(string id)
        {
            foreach (StatRowDef row in Rows) if (row.Id == id) return row;
            return null;
        }

        private static string RowNames()
        {
            var names = new string[Rows.Length];
            for (int i = 0; i < Rows.Length; i++) names[i] = Rows[i].Id;
            return string.Join(", ", names);
        }
    }

    // One stat line of the stats section and the bounds of its two fields (inclusive).
    internal sealed class StatRowDef
    {
        internal readonly string Id;
        internal readonly int MinFloor, MaxFloor, MinCeil, MaxCeil;
        internal StatRowDef(string id, int minFloor, int maxFloor, int minCeil, int maxCeil)
        { Id = id; MinFloor = minFloor; MaxFloor = maxFloor; MinCeil = minCeil; MaxCeil = maxCeil; }
    }

    // What the stats section publishes: per stat line its drag floor and ceiling, permille (0 = none; a line no layer wrote
    // has both 0).
    public sealed class StatClamps
    {
        private readonly Dictionary<string, (int Floor, int Ceil)> rows = new Dictionary<string, (int Floor, int Ceil)>();

        internal void Set(string line, int floor, int ceil) => rows[line] = (floor, ceil);

        public int FloorOf(string line) => rows.TryGetValue(line, out var row) ? row.Floor : 0;
        public int CeilOf(string line) => rows.TryGetValue(line, out var row) ? row.Ceil : 0;
        public bool Has(string line) => rows.ContainsKey(line);

        public const string Spread = StatsSection.SpreadRow;
    }
}
