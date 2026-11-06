using System.Collections.Generic;

namespace WarCore.Content
{
    // One data section of a content layer (a top-level key such as "weapons"). A new kind of data is one new class plus
    // one row in SectionTable; the loader itself does not change.
    internal interface IContentSection
    {
        // The top-level key in the layer file.
        string Key { get; }

        // Sections build in ascending stage and, inside one stage, in table order. Merging always follows table order.
        // A section that needs values another section built must sit in a later stage or later in the table.
        int BuildStage { get; }

        // Folds one layer's value for this key (never absent) into the entries merged from the parent layers.
        void Merge(List<ContentEntry> entries, JsonNode node, string layer);

        // Turns the merged entries into typed values on the build state. Called for every section, also when no layer wrote it.
        void Build(SectionBuild build);
    }

    // A section made of entries with ids ("weapons": [ ... ]); entries with the same id merge field by field.
    internal abstract class ArraySection : IContentSection
    {
        private readonly string noun;
        protected ArraySection(string key, string noun) { Key = key; this.noun = noun; }
        public string Key { get; }
        public virtual int BuildStage => 0;
        public void Merge(List<ContentEntry> entries, JsonNode node, string layer) => EntryMerge.Array(entries, node, noun, layer);
        public abstract void Build(SectionBuild build);
    }

    // A section that is one object ("combat": { ... }); layers merge it field by field.
    internal abstract class ObjectSection : IContentSection
    {
        protected ObjectSection(string key) { Key = key; }
        public string Key { get; }
        public virtual int BuildStage => 0;
        public void Merge(List<ContentEntry> entries, JsonNode node, string layer) => EntryMerge.Object(entries, node, Key, layer);
        public abstract void Build(SectionBuild build);
    }
}
