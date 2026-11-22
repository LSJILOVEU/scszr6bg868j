using System.Collections.Generic;

namespace WarCore.Content
{
    // One row of the "stats" section: the floor and ceiling of one stat line's drag product, permille, 0 when not written.
    public sealed class StatClampRow
    {
        public const int MinFloorPermille = 1, MaxFloorPermille = 1000, MinCeilPermille = 1000, MaxCeilPermille = 4000;
        internal const string FloorField = "dragFloorPermille", CeilField = "dragCeilPermille";

        public int DragFloorPermille { get; }
        public int DragCeilPermille { get; }

        internal StatClampRow(int floor, int ceil) { DragFloorPermille = floor; DragCeilPermille = ceil; }

        // A row { dragFloorPermille?, dragCeilPermille? }: at least one field; the floor 1..1000 and the ceiling 1000..4000, so
        // a unit whose drags are all 1000 is never clamped and the floor never exceeds the ceiling.
        internal static StatClampRow Read(string pack, ContentEntry row)
        {
            if (row.Fields.Count == 0)
                throw new ContentException("content pack '" + pack + "' layer '" + row.FirstLayer + "' " + row.Label + " {} must not be empty");
            var e = new EntryReader(pack, row);
            int floor = e.Has(FloorField) ? e.Int(FloorField, MinFloorPermille, MaxFloorPermille) : 0;
            int ceil = e.Has(CeilField) ? e.Int(CeilField, MinCeilPermille, MaxCeilPermille) : 0;
            e.Done();
            return new StatClampRow(floor, ceil);
        }
    }

    // What the optional "stats" section publishes: one row per stat line that a layer wrote, null for the others.
    public sealed class StatClamps
    {
        // SLOPE-1: the foot step's drags (surface, slope, registered drags).
        public StatClampRow Speed { get; }
        internal StatClamps(StatClampRow speed) { Speed = speed; }
    }

    // "stats": { "<line>": { "dragFloorPermille": ..., "dragCeilPermille": ... }, ... }. Each row is an object kept whole across
    // layers (a child layer that writes a row replaces the parent's row). Rows: "speed". Any other row name is refused as
    // unknown, and so is an empty "stats" object or an empty row.
    internal sealed class StatsSection : IContentSection
    {
        internal const string SectionKey = "stats", SpeedRow = "speed";

        public string Key => SectionKey;
        public int BuildStage => 0;

        public void Merge(List<ContentEntry> entries, JsonNode node, string layer)
        {
            EntryMerge.Object(entries, node, Key, layer, _ => true);
            if (node.Members.Count == 0) throw new ContentException("content layer '" + layer + "' stats {} must not be empty");
        }

        public void Build(SectionBuild b)
        {
            ContentEntry stats = b.Single(Key);
            if (stats == null) return;
            var r = new EntryReader(b.Pack, stats);
            ContentEntry speed = r.ObjectOrNull(SpeedRow);
            r.Done();
            b.Publish(new StatClamps(speed == null ? null : StatClampRow.Read(b.Pack, speed)));
        }
    }
}
