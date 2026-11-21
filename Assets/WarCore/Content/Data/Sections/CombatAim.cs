using System;
using WarCore.Combat;

namespace WarCore.Content
{
    // AIM-1: what the combat section publishes when a layer wrote combat.moveSpread or combat.manualReload: true; nothing
    // is published otherwise, so such a pack builds exactly the combat pack it built before.
    internal sealed class CombatAim
    {
        internal readonly MoveSpread MoveSpread;
        internal readonly bool ManualReload;
        internal CombatAim(MoveSpread moveSpread, bool manualReload) { MoveSpread = moveSpread; ManualReload = manualReload; }
    }

    // combat.moveSpread { bands: [ { aboveMmPerSec, permille }, ... ], byWeapon: { "<weapon id>": [ bands ], ... } }:
    // 1 to 8 bands, aboveMmPerSec 0..300000 strictly increasing, permille 1..4000; byWeapon optional, its keys weapons of the
    // pack, each value a table of the same rules that replaces the default table for that weapon.
    internal static class CombatAimReader
    {
        internal const string BandsField = "bands", ByWeaponField = "byWeapon", AboveField = "aboveMmPerSec", PermilleField = "permille";

        internal static MoveSpread MoveSpread(string pack, ContentField field, string[] weaponIds)
        {
            ContentEntry entry = EntryOf(pack, field, out JsonNode bands, out JsonNode byWeapon);
            MoveSpreadTable defaults = Table(pack, field.Layer, entry.Label + "." + BandsField, bands);
            if (byWeapon == null) return new MoveSpread(defaults);
            string where = entry.Label + "." + ByWeaponField;
            if (byWeapon.Kind != JsonKind.Object) throw Bad(pack, field.Layer, entry.Label, ByWeaponField, "must be an object");
            var tables = new MoveSpreadTable[weaponIds.Length];
            foreach (JsonMember member in byWeapon.Members)
            {
                int weapon = Array.IndexOf(weaponIds, member.Name);
                if (weapon < 0) throw Bad(pack, field.Layer, where, member.Name, "is not a weapon of this pack");
                tables[weapon] = Table(pack, field.Layer, where + "." + member.Name, member.Value);
            }
            return new MoveSpread(defaults, tables);
        }

        // The object's two fields: bands (required) and byWeapon (optional); any other name is refused.
        private static ContentEntry EntryOf(string pack, ContentField field, out JsonNode bands, out JsonNode byWeapon)
        {
            var entry = new ContentEntry("combat." + field.Name, null, field.Layer);
            bands = byWeapon = null;
            foreach (JsonMember member in field.Node.Members)
            {
                if (member.Name == BandsField) bands = member.Value;
                else if (member.Name == ByWeaponField) byWeapon = member.Value;
                else throw Bad(pack, field.Layer, entry.Label, member.Name, "unknown field name (misspelled?)");
            }
            if (bands == null) throw new ContentException("content pack '" + pack + "' layer '" + field.Layer + "' " + entry.Label + " is missing field '" + BandsField + "'");
            return entry;
        }

        private static MoveSpreadTable Table(string pack, string layer, string where, JsonNode node)
        {
            if (node.Kind != JsonKind.Array) throw Bad(pack, layer, where, null, "must be an array of bands");
            int count = node.Items.Count;
            if (count < 1 || count > MoveSpreadTable.MaxBands)
                throw Bad(pack, layer, where, null, "has " + count + " bands, allowed 1 to " + MoveSpreadTable.MaxBands);
            var above = new int[count];
            var permille = new int[count];
            for (int i = 0; i < count; i++)
            {
                JsonNode band = node.Items[i];
                string label = where + "[" + i + "]";
                if (band.Kind != JsonKind.Object) throw Bad(pack, layer, label, null, "must be an object");
                var entry = new ContentEntry(label, null, layer);
                foreach (JsonMember member in band.Members) entry.Fields.Add(new ContentField(member.Name, member.Value, layer));
                var r = new EntryReader(pack, entry);
                above[i] = r.Int(AboveField, 0, MoveSpreadTable.MaxAboveMmPerSec);
                permille[i] = r.Int(PermilleField, WarCore.Sim.StatLine.MinPermille, WarCore.Sim.StatLine.MaxPermille);
                r.Done();
                if (i > 0 && above[i] <= above[i - 1])
                    throw r.Bad(r.FieldOf(AboveField), "must be greater than the band before (" + above[i - 1] + ")");
            }
            return new MoveSpreadTable(above, permille);
        }

        private static ContentException Bad(string pack, string layer, string where, string name, string why) =>
            new ContentException("content pack '" + pack + "' layer '" + layer + "' " + where + (name == null ? "" : " field '" + name + "'") + ": " + why);
    }
}
