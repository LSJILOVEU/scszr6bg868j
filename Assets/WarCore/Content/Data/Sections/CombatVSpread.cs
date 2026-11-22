using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Content
{
    // VSPREAD-1 combat.verticalSpread { permille, byStance?: { "<stance id>": permille, ... }, aim?: "eye" | "center",
    // snapMm? }: permille 1..4000 (the standing value and the value of every stance byStance leaves out); byStance keys are
    // the pack's stances (after every layer is merged, so a child layer that replaced the stances is checked against its
    // own), values 1..4000; aim defaults to "eye"; snapMm 1..5000, default 600, allowed only with "center". The whole object
    // needs the combat section's body; any other field is refused. Read after the body and the stances.
    internal static class CombatVSpreadReader
    {
        internal const string PermilleField = "permille", ByStanceField = "byStance", AimField = "aim", SnapField = "snapMm";
        private static readonly string[] AimNames = { "eye", "center" };

        internal static VerticalSpread Read(string pack, ContentField field, bool bodied, StanceDef[] stances)
        {
            string where = CombatSection.SectionKey + "." + field.Name;
            if (field.Node.Kind != JsonKind.Object) throw Bad(pack, field.Layer, CombatSection.SectionKey, field.Name, "must be an object");
            if (!bodied) throw Bad(pack, field.Layer, CombatSection.SectionKey, field.Name, "needs the combat section's body");
            var entry = new ContentEntry(where, null, field.Layer);
            JsonNode byStance = null;
            foreach (JsonMember member in field.Node.Members)
            {
                if (member.Name == ByStanceField) { byStance = member.Value; continue; }
                if (member.Value.Kind == JsonKind.Object || member.Value.Kind == JsonKind.Array)
                    throw Bad(pack, field.Layer, where, member.Name, "must be a number or a string");
                entry.Fields.Add(new ContentField(member.Name, member.Value, field.Layer));
            }
            var r = new EntryReader(pack, entry);
            int permille = r.Int(PermilleField, StatLine.MinPermille, StatLine.MaxPermille);
            VerticalAim aim = VerticalAim.Eye;
            if (r.Has(AimField))
            {
                string name = r.Str(AimField);
                int index = Array.IndexOf(AimNames, name);
                if (index < 0) throw r.Bad(r.FieldOf(AimField), "unknown aim '" + name + "', allowed: " + string.Join(", ", AimNames));
                aim = (VerticalAim)index;
            }
            int snap = VerticalSpread.DefaultSnapMm;
            if (r.Has(SnapField))
            {
                if (aim != VerticalAim.Center) throw r.Bad(r.FieldOf(SnapField), "is allowed only with aim \"center\"");
                snap = r.Int(SnapField, VerticalSpread.MinSnapMm, VerticalSpread.MaxSnapMm);
            }
            r.Done();
            int count = stances == null ? 0 : stances.Length;
            var levels = new int[count];
            for (int i = 0; i < count; i++) levels[i] = permille;
            if (byStance != null)
            {
                if (byStance.Kind != JsonKind.Object) throw Bad(pack, field.Layer, where, ByStanceField, "must be an object");
                if (count == 0) throw Bad(pack, field.Layer, where, ByStanceField, "needs the combat section's stances");
                string at = where + "." + ByStanceField;
                foreach (JsonMember member in byStance.Members)
                {
                    int level = -1;
                    for (int i = 0; i < count; i++) if (stances[i].Id == member.Name) level = i;
                    if (level < 0) throw Bad(pack, field.Layer, at, member.Name, "is not a stance of this pack");
                    var one = new ContentEntry(at, null, field.Layer);
                    one.Fields.Add(new ContentField(member.Name, member.Value, field.Layer));
                    levels[level] = new EntryReader(pack, one).Int(member.Name, StatLine.MinPermille, StatLine.MaxPermille);
                }
            }
            return new VerticalSpread(permille, levels, aim, snap);
        }

        private static ContentException Bad(string pack, string layer, string where, string name, string why) =>
            new ContentException("content pack '" + pack + "' layer '" + layer + "' " + where + " field '" + name + "': " + why);
    }
}
