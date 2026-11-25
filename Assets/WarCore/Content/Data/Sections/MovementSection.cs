using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Content
{
    // SLOPE-1: what the optional "movement" section publishes: the slope speeds and (RC1-K3) the inertia, each null when the
    // section does not name it. Without the section nothing is published and ContentPack.SectionOrNull<MovementRules>() is null.
    public sealed class MovementRules
    {
        public SlopeTable Slope { get; }
        public InertiaRates Inertia { get; }
        internal MovementRules(SlopeTable slope, InertiaRates inertia) { Slope = slope; Inertia = inertia; }
    }

    // RC1-K3: how fast a computer-driven unit speeds up and slows down, in mm/s^2. A rate is 0 (that half stays off) or
    // MinMmPerS2 to MaxMmPerS2; a pack never carries both at 0, so a pack that has the rates has inertia.
    public sealed class InertiaRates
    {
        public const long MinMmPerS2 = 100, MaxMmPerS2 = 1000000;
        public long AccelMmPerS2 { get; }
        public long DecelMmPerS2 { get; }
        internal InertiaRates(long accelMmPerS2, long decelMmPerS2) { AccelMmPerS2 = accelMmPerS2; DecelMmPerS2 = decelMmPerS2; }
        public static bool IsRate(long mmPerS2) => mmPerS2 == 0 || (mmPerS2 >= MinMmPerS2 && mmPerS2 <= MaxMmPerS2);
    }

    // "movement": { "slope": { "up": [ { "abovePermille", "speedPermille" }, ... ], "down": [ ... ] },
    //               "inertia": { "accelMmPerS2", "decelMmPerS2" } }. Every field is an object kept whole across layers (a child
    // layer's "slope" replaces the parent's). Any other name is refused as unknown. "up" or "down" (or both), each 1..8 bands,
    // abovePermille 0..2000 strictly increasing, speedPermille 1..1000. "inertia" names both rates (see InertiaRates), not both
    // 0. An empty "movement" object is refused.
    internal sealed class MovementSection : IContentSection
    {
        internal const string SectionKey = "movement", SlopeField = "slope", UpField = "up", DownField = "down";
        internal const string AboveField = "abovePermille", SpeedField = "speedPermille";
        internal const string InertiaField = "inertia", AccelField = "accelMmPerS2", DecelField = "decelMmPerS2";

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
            ContentField slopeField = r.FieldOf(SlopeField), inertiaField = r.FieldOf(InertiaField);
            if (slopeField != null) slopeField.Used = true;
            if (inertiaField != null) inertiaField.Used = true;
            r.Done();
            b.Publish(new MovementRules(slopeField == null ? null : Slope(b.Pack, slopeField),
                inertiaField == null ? null : Inertia(b.Pack, inertiaField)));
        }

        private static InertiaRates Inertia(string pack, ContentField field)
        {
            string where = SectionKey + "." + InertiaField;
            if (field.Node.Kind != JsonKind.Object) throw Bad(pack, field.Layer, SectionKey, InertiaField, "must be an object");
            var entry = new ContentEntry(where, null, field.Layer);
            foreach (JsonMember member in field.Node.Members) entry.Fields.Add(new ContentField(member.Name, member.Value, field.Layer));
            var e = new EntryReader(pack, entry);
            long accel = Rate(e, AccelField), decel = Rate(e, DecelField);
            e.Done();
            if (accel == 0 && decel == 0)
                throw Bad(pack, field.Layer, where, null, "'" + AccelField + "' and '" + DecelField + "' are both 0; leave '" + InertiaField + "' out for no inertia");
            return new InertiaRates(accel, decel);
        }

        private static long Rate(EntryReader e, string name)
        {
            long rate = e.Long(name, 0, InertiaRates.MaxMmPerS2);
            if (!InertiaRates.IsRate(rate))
                throw e.Bad(e.FieldOf(name), "is " + rate + ", allowed 0 or " + InertiaRates.MinMmPerS2 + " to " + InertiaRates.MaxMmPerS2);
            return rate;
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
