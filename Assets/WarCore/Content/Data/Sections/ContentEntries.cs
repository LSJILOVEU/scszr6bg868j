using System.Collections.Generic;

namespace WarCore.Content
{
    // One field of a merged entry; Used is how the reader finds names nobody read (misspelled fields).
    internal sealed class ContentField
    {
        internal readonly string Name, Layer;
        internal readonly JsonNode Node;
        internal bool Used;
        internal ContentField(string name, JsonNode node, string layer) { Name = name; Node = node; Layer = layer; }
    }

    // One entry of a section as merged over the inheritance chain: an array element (Id set) or an object section (Id null).
    internal sealed class ContentEntry
    {
        internal readonly string Section, Id, FirstLayer;
        internal readonly List<ContentField> Fields = new List<ContentField>();
        internal ContentEntry(string section, string id, string layer) { Section = section; Id = id; FirstLayer = layer; }
        internal string Label => Id == null ? Section : Section + " '" + Id + "'";
    }

    // The entries of every section after the layers are merged, keyed by the section's top-level key.
    internal sealed class MergedLayers
    {
        private readonly Dictionary<string, List<ContentEntry>> sections = new Dictionary<string, List<ContentEntry>>();

        internal List<ContentEntry> Of(string key)
        {
            if (!sections.TryGetValue(key, out List<ContentEntry> entries)) sections[key] = entries = new List<ContentEntry>();
            return entries;
        }
    }

    internal static class SectionLimits
    {
        // Bounds keep CombatSystem's arithmetic (tick * 1000 + interval, effectRaw * falloff) far from long overflow.
        internal const long MaxMilli = 1_000_000_000_000L, MaxEffectRaw = 1L << 40;
    }

    // The two ways a layer can carry a section: an array of entries with ids, or one object.
    internal static class EntryMerge
    {
        internal static void Array(List<ContentEntry> target, JsonNode array, string section, string layer)
        {
            if (array.Kind != JsonKind.Array) throw new ContentException("content layer '" + layer + "' " + section + " must be an array");
            var seen = new List<string>();
            foreach (JsonNode item in array.Items)
            {
                if (item.Kind != JsonKind.Object) throw new ContentException("content layer '" + layer + "' " + section + " entry must be an object (line " + item.Line + ")");
                JsonNode idNode = item.Find("id");
                if (idNode == null || idNode.Kind != JsonKind.String || idNode.Text.Length == 0)
                    throw new ContentException("content layer '" + layer + "' " + section + " entry needs a string id (line " + item.Line + ")");
                string id = idNode.Text;
                if (seen.Contains(id)) throw new ContentException("content layer '" + layer + "' " + section + " '" + id + "' is duplicated");
                seen.Add(id);
                ContentEntry entry = target.Find(e => e.Id == id);
                if (entry == null) { entry = new ContentEntry(section, id, layer); target.Add(entry); }
                Overlay(entry, item, layer);
            }
        }

        // An object section keeps its single entry as the only element of the list.
        internal static void Object(List<ContentEntry> target, JsonNode node, string section, string layer)
        {
            if (node.Kind != JsonKind.Object) throw new ContentException("content layer '" + layer + "' " + section + " must be an object");
            if (target.Count == 0) target.Add(new ContentEntry(section, null, layer));
            Overlay(target[0], node, layer);
        }

        private static void Overlay(ContentEntry entry, JsonNode node, string layer)
        {
            foreach (var member in node.Members)
            {
                if (member.Name == "id" && entry.Id != null) continue;
                if (member.Value.Kind == JsonKind.Object || member.Value.Kind == JsonKind.Array)
                    throw new ContentException("content layer '" + layer + "' " + entry.Label + " field '" + member.Name + "' must be a number, string or boolean");
                entry.Fields.RemoveAll(f => f.Name == member.Name);
                entry.Fields.Add(new ContentField(member.Name, member.Value, layer));
            }
        }
    }
}
