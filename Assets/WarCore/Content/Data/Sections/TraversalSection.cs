using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Content
{
    // TRV-2: the optional section "traversal": { "kinds": [ { "id": ..., "shape": "edge" | "over", ... }, ... ] }.
    // The kinds are an ordered table (first match wins), so a layer that writes "kinds" replaces its parents' table whole
    // instead of merging entries by id. Without the section nothing is published and ContentPack.SectionOrNull returns null.
    internal sealed class TraversalSection : IContentSection
    {
        internal const string SectionKey = "traversal", KindsKey = "kinds", Noun = "traversal kind";

        public string Key => SectionKey;
        public int BuildStage => 0;

        public void Merge(List<ContentEntry> entries, JsonNode node, string layer)
        {
            if (node.Kind != JsonKind.Object) throw new ContentException("content layer '" + layer + "' traversal must be an object");
            JsonNode kinds = null;
            foreach (JsonMember member in node.Members)
            {
                if (member.Name != KindsKey) throw new ContentException("content layer '" + layer + "' traversal field '" + member.Name + "': unknown field name (allowed: kinds)");
                kinds = member.Value;
            }
            if (kinds == null) throw new ContentException("content layer '" + layer + "' traversal is missing field 'kinds'");
            entries.Clear();
            EntryMerge.Array(entries, kinds, Noun, layer);
            if (entries.Count == 0) throw new ContentException("content layer '" + layer + "' traversal kinds [] must not be empty");
            if (entries.Count > TraversalRules.MaxKinds)
                throw new ContentException("content layer '" + layer + "' traversal has more than " + TraversalRules.MaxKinds + " kinds");
        }

        public void Build(SectionBuild b)
        {
            List<ContentEntry> entries = b.Entries(Key);
            if (entries.Count == 0) return;
            var kinds = new TraversalKind[entries.Count];
            for (int i = 0; i < kinds.Length; i++) kinds[i] = Read(b.Pack, entries[i]);
            b.Publish(new TraversalRules(kinds));
        }

        private static TraversalKind Read(string pack, ContentEntry e)
        {
            var r = new EntryReader(pack, e);
            string shapeText = r.Str("shape");
            TraversalShape shape;
            if (shapeText == "edge") shape = TraversalShape.Edge;
            else if (shapeText == "over") shape = TraversalShape.Over;
            else if (shapeText == "manual") throw Bad(pack, e, "shape 'manual' is not supported yet (hand-placed links come later)");
            else throw Bad(pack, e, "unknown shape '" + shapeText + "' (allowed: edge, over)");
            long riseMin = r.Long("riseMin", 0, HeightField.HeightLimitRaw), riseMax = r.Long("riseMax", 0, HeightField.HeightLimitRaw);
            long fallMin = r.Long("fallMin", 0, HeightField.HeightLimitRaw), fallMax = r.Long("fallMax", 0, HeightField.HeightLimitRaw);
            long gapMin = r.Long("gapMin", 0, HeightField.HeightLimitRaw);
            int ticksBase = r.Int("ticksBase", 1, TraversalKind.MaxTicksBase);
            int up = r.Int("ticksPerMetreUp", 0, TraversalKind.MaxTicksPerMetre), down = r.Int("ticksPerMetreDown", 0, TraversalKind.MaxTicksPerMetre);
            long pathCost = r.Long("pathCost", 0, TraversalKind.MaxPathCost);
            bool fire = r.Has("fire") && r.Bool("fire");
            long noise = r.Has("noiseRaw") ? r.Long("noiseRaw", 0, TraversalKind.MaxNoiseRaw) : 0;
            r.Done();
            if (riseMin > riseMax) throw Bad(pack, e, "riseMin must not exceed riseMax");
            if (fallMin > fallMax) throw Bad(pack, e, "fallMin must not exceed fallMax");
            return new TraversalKind(e.Id, shape, riseMin, riseMax, fallMin, fallMax, gapMin, ticksBase, up, down, pathCost, fire, noise);
        }

        private static ContentException Bad(string pack, ContentEntry e, string why) =>
            new ContentException("content pack '" + pack + "' layer '" + e.FirstLayer + "' " + e.Label + ": " + why);
    }
}
