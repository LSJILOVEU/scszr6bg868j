using System;
using System.Collections.Generic;

namespace WarCore.Content
{
    // Layer file shape (all names are exact; unknown names are errors):
    //   { "id": "<file name>", "extends": "<layer id>" or [ "<layer id>", ... ], <one key per data section> }
    // "id" and "extends" are the loader's own. The data sections ("weapons", "melee", "block", "unitTypes", "combat", "match", "surfaces",
    // "slopeRule", "rules") are listed in SectionTable, one file each under Sections/. A layer is merged after the layers it extends,
    // in the order it lists them, and once: a layer reached again through another parent stays where it was first merged. Entries
    // with the same id merge field by field (the later layer's fields win, including an explicit 0 or false); new ids are appended
    // after the entries merged before; "combat", "match" and "slopeRule" merge field by field.
    // Of the layers that set one field, those that no other of them extends must set it to one value. So the order in which a
    // layer lists what it extends decides no value, nor whether the pack loads: where two of them disagree, a layer that extends
    // both has to say.
    // Field tables: docs/content-data.md.
    public static class ContentLoader
    {
        public static ContentPack Load(IContentSource source, string packId) => Load(source, packId, SectionTable.Default);

        internal static ContentPack Load(IContentSource source, string packId, SectionTable table)
        {
            var layers = new Layers(source, table);
            layers.Merge(packId);
            layers.CheckInLine(packId);
            return table.Build(packId, layers.Merged);
        }

        // One load: the layers merged so far, each with every layer it extends, and what each one wrote.
        private sealed class Layers
        {
            private readonly IContentSource source;
            private readonly SectionTable table;
            private readonly List<string> chain = new List<string>();
            private readonly Dictionary<string, HashSet<string>> extended = new Dictionary<string, HashSet<string>>();
            private readonly List<Write> writes = new List<Write>();
            internal readonly MergedLayers Merged = new MergedLayers();

            internal Layers(IContentSource source, SectionTable table) { this.source = source; this.table = table; }

            internal void Merge(string layerId)
            {
                if (chain.Contains(layerId))
                    throw new ContentException("content layer inheritance cycle: " + string.Join(" -> ", chain) + " -> " + layerId);
                if (extended.ContainsKey(layerId)) return;
                if (!source.TryRead(layerId, out string text, out string location))
                    throw new ContentException("content layer '" + layerId + "' not found" + (chain.Count > 0 ? " (extended by '" + chain[chain.Count - 1] + "')" : "") + ", looked at: " + location);
                chain.Add(layerId);
                JsonNode root = ContentJson.Parse(text, "content layer '" + layerId + "'");
                if (root.Kind != JsonKind.Object) throw new ContentException("content layer '" + layerId + "' must be a JSON object");
                foreach (var member in root.Members)
                    if (!table.IsAllowedKey(member.Name))
                        throw new ContentException("content layer '" + layerId + "' has unknown field '" + member.Name + "' (line " + member.Value.Line + "); allowed: " + table.AllowedKeys);
                string declared = ScalarText(root.Find("id"), layerId, "id");
                if (declared != layerId) throw new ContentException("content layer file name is '" + layerId + "' but its id is '" + declared + "'");
                var above = new HashSet<string>();
                foreach (string parent in Parents(root, layerId))
                {
                    Merge(parent);
                    above.Add(parent);
                    above.UnionWith(extended[parent]);
                }
                chain.RemoveAt(chain.Count - 1);
                extended.Add(layerId, above);

                foreach (IContentSection section in table.All)
                {
                    JsonNode node = root.Find(section.Key);
                    if (node == null) continue;
                    section.Merge(Merged.Of(section.Key), node, layerId);
                    Note(section.Key, node, layerId);
                }
            }

            // What a layer wrote, by the unit that is merged: a field of an entry for a list of entries with ids, a field of
            // the section for an object. The value is the field's whole node, as the sections keep it.
            private void Note(string section, JsonNode node, string layer)
            {
                if (node.Kind == JsonKind.Object)
                    foreach (JsonMember field in node.Members) writes.Add(new Write(section + "." + field.Name, field.Value, layer));
                if (node.Kind != JsonKind.Array) return;
                foreach (JsonNode item in node.Items)
                {
                    JsonNode id = item.Kind == JsonKind.Object ? item.Find("id") : null;
                    if (id == null || id.Kind != JsonKind.String) continue;
                    foreach (JsonMember field in item.Members)
                        if (field.Name != "id") writes.Add(new Write(section + "[" + id.Text + "]." + field.Name, field.Value, layer));
                }
            }

            // A field's value is the one merged last. Any other value must come from a layer that another writer of the field
            // extends: it was overridden in its own line. What is left are layers that disagree with no layer above them both.
            internal void CheckInLine(string packId)
            {
                var writers = new Dictionary<string, List<Write>>();
                foreach (Write write in writes)
                {
                    if (!writers.TryGetValue(write.Path, out List<Write> list)) writers[write.Path] = list = new List<Write>();
                    list.Add(write);
                }
                foreach (Write write in writes)
                {
                    List<Write> list = writers[write.Path];
                    Write final = list[list.Count - 1];
                    if (Same(write.Node, final.Node) || list.Exists(other => extended[other.Layer].Contains(write.Layer))) continue;
                    throw new ContentException("content pack '" + packId + "' field '" + write.Path + "': layers '" + write.Layer + "' and '" + final.Layer
                        + "' set it differently and neither extends the other; set it in a layer that extends both");
                }
            }
        }

        private sealed class Write
        {
            internal readonly string Path, Layer;
            internal readonly JsonNode Node;
            internal Write(string path, JsonNode node, string layer) { Path = path; Node = node; Layer = layer; }
        }

        private static List<string> Parents(JsonNode root, string layerId)
        {
            var parents = new List<string>();
            JsonNode extends = root.Find("extends");
            if (extends == null) return parents;
            if (extends.Kind != JsonKind.Array) { parents.Add(ScalarText(extends, layerId, "extends")); return parents; }
            foreach (JsonNode item in extends.Items)
            {
                if (item.Kind != JsonKind.String) throw new ContentException("content layer '" + layerId + "' field 'extends' must list layer ids (line " + item.Line + ")");
                if (parents.Contains(item.Text)) throw new ContentException("content layer '" + layerId + "' field 'extends' lists '" + item.Text + "' twice");
                parents.Add(item.Text);
            }
            if (parents.Count == 0) throw new ContentException("content layer '" + layerId + "' field 'extends' lists no layer");
            return parents;
        }

        // Numbers are compared as they are written: 1.5 and 1.50 differ.
        private static bool Same(JsonNode a, JsonNode b)
        {
            if (a.Kind != b.Kind) return false;
            switch (a.Kind)
            {
                case JsonKind.Object:
                    if (a.Members.Count != b.Members.Count) return false;
                    foreach (JsonMember member in a.Members)
                    {
                        JsonNode other = b.Find(member.Name);
                        if (other == null || !Same(member.Value, other)) return false;
                    }
                    return true;
                case JsonKind.Array:
                    if (a.Items.Count != b.Items.Count) return false;
                    for (int i = 0; i < a.Items.Count; i++)
                        if (!Same(a.Items[i], b.Items[i])) return false;
                    return true;
                case JsonKind.Bool:
                    return a.Flag == b.Flag;
                default:
                    return a.Text == b.Text;
            }
        }

        private static string ScalarText(JsonNode node, string layer, string field)
        {
            if (node == null) throw new ContentException("content layer '" + layer + "' is missing field '" + field + "'");
            if (node.Kind != JsonKind.String) throw new ContentException("content layer '" + layer + "' field '" + field + "' must be a string");
            return node.Text;
        }
    }
}
