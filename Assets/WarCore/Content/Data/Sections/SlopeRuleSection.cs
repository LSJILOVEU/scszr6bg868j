using WarCore.Sim;

namespace WarCore.Content
{
    // Terrain rules (WP-22) are two sections that make one TerrainRules. The slope rule is checked before the surfaces
    // (SurfacesSection builds one stage later) although the layers merge surfaces first.
    internal sealed class SlopeRuleSection : ObjectSection
    {
        internal const string SectionKey = "slopeRule";
        internal SlopeRuleSection() : base(SectionKey) { }

        public override void Build(SectionBuild b)
        {
            ContentEntry rule = b.Single(Key);
            if (rule == null) return;
            var sr = new EntryReader(b.Pack, rule);
            int den = sr.Has("maxWalkableDen") ? sr.Int("maxWalkableDen", 1, HeightField.MaxWalkableDenominator) : b.SlopeDenominator;
            int num = sr.Has("maxWalkableNum") ? sr.Int("maxWalkableNum", 0, HeightField.MaxSlopeNumerator * den) : HeightField.MaxSlopeNumerator * den;
            sr.Done();
            b.SlopeNumerator = num; b.SlopeDenominator = den;
        }
    }
}
