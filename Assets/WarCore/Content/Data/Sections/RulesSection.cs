using System.Collections.Generic;

namespace WarCore.Content
{
    internal sealed class RulesSection : IContentSection
    {
        public string Key => "rules";
        public int BuildStage => 0;

        public void Merge(List<ContentEntry> entries, JsonNode node, string layer)
        {
            EntryMerge.Array(entries, node, "rule", layer);
            if (node.Items.Count == 0) throw new ContentException("content layer '" + layer + "' rules [] must not be empty");
        }

        public void Build(SectionBuild b)
        {
            var entries = b.Entries(Key);
            if (entries.Count == 0) return;
            var ids = new string[entries.Count];
            for (int i = 0; i < ids.Length; i++)
            {
                new EntryReader(b.Pack, entries[i]).Done();
                ids[i] = entries[i].Id;
            }
            b.RuleIds = ids;
        }
    }
}
