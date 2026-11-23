using System.Collections.Generic;
using WarCore.Sim;

namespace WarCore.Content
{
    // SCALE-2b: the optional object section "scale": a theme's standard parts in whole millimetres. All thirteen fields are
    // required after merging (a child layer may change one), each an integer 1 to ScaleRules.MaxMm. Built in stage 2 because
    // it checks the parts against the body and stances and the traversal kinds (stage 0) and the walkable slope (stage 1);
    // every failing relation goes into one ContentException. Without the section nothing is published and ScaleRules.Of
    // gives ScaleRules.Default. Not in the "allowed" list (SectionTable).
    internal sealed class ScaleSection : ObjectSection
    {
        internal const string SectionKey = "scale";

        internal static readonly string[] Fields =
        {
            "storeyMm", "plateMm", "lowWallMm", "chestWallMm", "highWallMm", "windowSillMm", "windowTopMm", "railingMm",
            "stairRiseMm", "stairRunMm", "laneMm", "sidewalkMm", "alleyMm",
        };

        internal ScaleSection() : base(SectionKey) { }

        public override int BuildStage => 2;

        public override void Build(SectionBuild b)
        {
            ContentEntry entry = b.Single(Key);
            if (entry == null) return;
            var r = new EntryReader(b.Pack, entry);
            var mm = new int[Fields.Length];
            for (int i = 0; i < mm.Length; i++) mm[i] = r.Int(Fields[i], 1, ScaleRules.MaxMm);
            r.Done();
            string prefix = "content pack '" + b.Pack + "' layer '" + entry.FirstLayer + "' scale: ";
            if (!b.Body.HasValue) throw new ContentException(prefix + "scale needs the combat section's body");
            var rules = new ScaleRules(mm[0], mm[1], mm[2], mm[3], mm[4], mm[5], mm[6], mm[7], mm[8], mm[9], mm[10], mm[11], mm[12]);
            long num = b.Terrain?.MaxWalkableNum ?? HeightField.MaxSlopeNumerator, den = b.Terrain?.MaxWalkableDen ?? HeightField.MaxSlopeDenominator;
            List<string> problems = ScaleRules.Problems(rules, b.Body.Value, b.Stances, num, den, b.PublishedOrNull<TraversalRules>());
            if (problems.Count > 0) throw new ContentException(prefix + string.Join("; ", problems));
            b.Publish(rules);
        }
    }
}
