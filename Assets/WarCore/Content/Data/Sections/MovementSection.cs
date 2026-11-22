using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Content
{
    // SLOPE-1: what the optional "movement" section publishes. Only "slope" for now; without the section nothing is
    // published and ContentPack.SectionOrNull<MovementRules>() is null.
    public sealed class MovementRules
    {
        public SlopeTable Slope { get; }
        internal MovementRules(SlopeTable slope) { Slope = slope; }
    }

    // "movement": { "slope": { "up": [ { "abovePermille", "speedPermille" }, ... ], "down": [ ... ] } }. Every field is an
    // object kept whole across layers (a child layer's "slope" replaces the parent's). "slope" is the only field read; any other
    // name is refused as unknown. "up" or "down" (or both), each 1..8 bands, abovePermille 0..2000 strictly increasing,
    // speedPermille 1..1000. An empty "movement" object is refused.
    internal sealed class MovementSection : IContentSection
    {
        internal const string SectionKey = "movement", SlopeField = "slope", UpField = "up", DownField = "down";
        internal const string AboveField = "abovePermille", SpeedField = "speedPermille";

        public string Key => SectionKey;
        public int BuildStage => 0;

        public void Merge(List<ContentEntry> entries, JsonNode node, string layer)
        {
            EntryMerge.Object(entries, node, Key, layer, _ => true);
            if (node.Members.Count == 0) throw new ContentException("content layer '" + layer + "' movement {} must not be empty");
        }

        public void Build(SectionBuild b)
        {
            ContentEntry movement = b.Single(Key);
            if (movement == null) return;
            var r = new EntryReader(b.Pack, movement);
            ContentField slopeField = r.FieldOf(SlopeField);
            if (slopeField != null) slopeField.Used = true;
            r.Done();
            b.Publish(new MovementRules(slopeField == null ? null : Slope(b.Pack, slopeField)));
        }

        private static SlopeTable Slope(string pack, ContentField field)
        {
            string where = SectionKey + "." + SlopeField;
            if (field.Node.Kind != JsonKind.Object) throw Bad(pack, field.Layer, SectionKey, SlopeField, "must be an object");
            JsonNode up = null, down = null;
            foreach (JsonMember member in field.Node.Members)
            {
                if (member.Name == UpField) up = member.Value;
                else if (member.Name == DownField) down = member.Value;
                else throw Bad(pack, field.Layer, where, member.Name, "unknown field name (misspelled?)");
            }
            if (up == null && down == null)
                throw new ContentException("content pack '" + pack + "' layer '" + field.Layer + "' " + where + " needs field '" + UpField + "' or '" + DownField + "'");
            int[] upAbove = null, upSpeed = null, downAbove = null, downSpeed = null;
            if (up != null) Bands(pack, field.Layer, where, UpField, up, out upAbove, out upSpeed);
            if (down != null) Bands(pack, field.Layer, where, DownField, down, out downAbove, out downSpeed);
            return new SlopeTable(upAbove, upSpeed, downAbove, downSpeed);
        }

        private static void Bands(string pack, string layer, string where, string name, JsonNode node, out int[] above, out int[] speed)
        {
            if (node.Kind != JsonKind.Array) throw Bad(pack, layer, where, name, "must be an array of bands");
            int count = node.Items.Count;
            if (count < 1 || count > SlopeTable.MaxBands)
                throw Bad(pack, layer, where, name, "has " + count + " bands, allowed 1 to " + SlopeTable.MaxBands);
            above = new int[count];
            speed = new int[count];
            for (int i = 0; i < count; i++)
            {
                JsonNode band = node.Items[i];
                string label = where + "." + name + "[" + i + "]";
                if (band.Kind != JsonKind.Object) throw Bad(pack, layer, label, null, "must be an object");
                var entry = new ContentEntry(label, null, layer);
                foreach (JsonMember member in band.Members) entry.Fields.Add(new ContentField(member.Name, member.Value, layer));
                var e = new EntryReader(pack, entry);
                above[i] = e.Int(AboveField, 0, SlopeTable.MaxAbovePermille);
                speed[i] = e.Int(SpeedField, SlopeTable.MinSpeedPermille, SlopeTable.MaxSpeedPermille);
                e.Done();
                if (i > 0 && above[i] <= above[i - 1])
                    throw e.Bad(e.FieldOf(AboveField), "must be greater than the band before (" + above[i - 1] + ")");
            }
        }

        private static ContentException Bad(string pack, string layer, string where, string name, string why) =>
            new ContentException("content pack '" + pack + "' layer '" + layer + "' " + where + (name == null ? "" : " field '" + name + "'") + ": " + why);
    }
}
