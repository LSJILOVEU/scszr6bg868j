using WarCore.Combat;

namespace WarCore.Content
{
    // NERVE-1 combat.status { pinned?: { spreadPermille }, broken?: { spreadPermille }, wounded?: { belowPermille, spreadPermille } }:
    // at least one row, every field of a written row required, spreadPermille 1..4000, belowPermille 1..1000; any other row or
    // field name is refused (wounded.speedPermille too: slowing a wounded unit belongs to the movement section). The object is
    // kept whole across layers: a child layer's status replaces its parent's. A pressure level whose row is absent counts 1000:
    // with only "pinned" written a broken unit shoots as a calm one, it does not fall back to the pinned row.
    // Nothing is published without the field, so such a pack builds exactly the combat pack it built before.
    internal static class CombatNerveReader
    {
        internal const string PinnedRow = "pinned", BrokenRow = "broken", WoundedRow = "wounded";
        internal const string SpreadField = "spreadPermille", BelowField = "belowPermille";

        internal static CombatStatus Status(string pack, ContentField field)
        {
            string where = "combat." + field.Name;
            if (field.Node.Kind != JsonKind.Object) throw Bad(pack, field.Layer, where, null, "must be an object");
            int pinned = 0, broken = 0, below = 0, wounded = 0;
            bool any = false;
            foreach (JsonMember member in field.Node.Members)
            {
                if (member.Name != PinnedRow && member.Name != BrokenRow && member.Name != WoundedRow)
                    throw Bad(pack, field.Layer, where, member.Name, "unknown field name (allowed: pinned, broken, wounded)");
                string label = where + "." + member.Name;
                if (member.Value.Kind != JsonKind.Object) throw Bad(pack, field.Layer, label, null, "must be an object");
                var entry = new ContentEntry(label, null, field.Layer);
                foreach (JsonMember inner in member.Value.Members) entry.Fields.Add(new ContentField(inner.Name, inner.Value, field.Layer));
                var r = new EntryReader(pack, entry);
                if (member.Name == WoundedRow) below = r.Int(BelowField, CombatStatus.MinBelowPermille, CombatStatus.MaxBelowPermille);
                int spread = r.Int(SpreadField, CombatStatus.MinSpreadPermille, CombatStatus.MaxSpreadPermille);
                r.Done();
                if (member.Name == PinnedRow) pinned = spread;
                else if (member.Name == BrokenRow) broken = spread;
                else wounded = spread;
                any = true;
            }
            if (!any) throw Bad(pack, field.Layer, where, null, "status needs at least one of pinned, broken, wounded");
            return new CombatStatus(pinned, broken, below, wounded);
        }

        private static ContentException Bad(string pack, string layer, string where, string name, string why) =>
            new ContentException("content pack '" + pack + "' layer '" + layer + "' " + where + (name == null ? "" : " field '" + name + "'") + ": " + why);
    }
}
