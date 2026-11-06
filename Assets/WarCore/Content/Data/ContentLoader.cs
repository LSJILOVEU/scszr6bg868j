using System;
using System.Collections.Generic;

namespace WarCore.Content
{
    // Layer file shape (all names are exact; unknown names are errors):
    //   { "id": "<file name>", "extends": "<layer id>", <one key per data section> }
    // "id" and "extends" are the loader's own. The data sections ("weapons", "melee", "unitTypes", "combat", "match", "surfaces",
    // "slopeRule") are listed in SectionTable, one file each under Sections/. Inheritance is single. Entries with the same id
    // merge field by field (the child's fields win, including an explicit 0 or false); new ids are appended after the parent's
    // entries; "combat", "match" and "slopeRule" merge field by field. Field tables: docs/content-data.md.
    public static class ContentLoader
    {
        public static ContentPack Load(IContentSource source, string packId) => Load(source, packId, SectionTable.Default);

        internal static ContentPack Load(IContentSource source, string packId, SectionTable table)
        {
            MergedLayers merged = LoadMerged(source, packId, new List<string>(), table);
            return table.Build(packId, merged);
        }

        private static MergedLayers LoadMerged(IContentSource source, string layerId, List<string> chain, SectionTable table)
        {
            if (chain.Contains(layerId))
                throw new ContentException("content layer inheritance cycle: " + string.Join(" -> ", chain) + " -> " + layerId);
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
            JsonNode extends = root.Find("extends");
            MergedLayers merged = extends == null ? new MergedLayers() : LoadMerged(source, ScalarText(extends, layerId, "extends"), chain, table);
            chain.RemoveAt(chain.Count - 1);

            foreach (IContentSection section in table.Sections)
            {
                JsonNode node = root.Find(section.Key);
                if (node != null) section.Merge(merged.Of(section.Key), node, layerId);
            }
            return merged;
        }

        private static string ScalarText(JsonNode node, string layer, string field)
        {
            if (node == null) throw new ContentException("content layer '" + layer + "' is missing field '" + field + "'");
            if (node.Kind != JsonKind.String) throw new ContentException("content layer '" + layer + "' field '" + field + "' must be a string");
            return node.Text;
        }
    }
}
