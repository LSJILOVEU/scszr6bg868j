using System;
using WarCore.Sim;

namespace WarCore.Content
{
    // Builds the pack's TerrainRules from the surface table and the slope rule read one stage earlier. Absent sections give null;
    // a missing field takes its default, an explicit false or 0 is a value.
    internal sealed class SurfacesSection : ArraySection
    {
        internal const string SectionKey = "surfaces";
        internal SurfacesSection() : base(SectionKey, "surface") { }

        public override int BuildStage => 1;

        public override void Build(SectionBuild b)
        {
            var entries = b.Entries(Key);
            if (entries.Count == 0 && b.Single(SlopeRuleSection.SectionKey) == null) return;
            var defs = new SurfaceDef[entries.Count];
            for (int i = 0; i < defs.Length; i++)
            {
                ContentEntry e = entries[i];
                var r = new EntryReader(b.Pack, e);
                if (e.Id.Length == 0 || System.Text.Encoding.UTF8.GetByteCount(e.Id) > 255)
                    throw new ContentException("content pack '" + b.Pack + "' layer '" + e.FirstLayer + "' " + e.Label + " id must be 1-255 bytes");
                bool passable = !r.Has("passable") || r.Bool("passable");
                int speed = r.Has("speedMilli") ? r.Int("speedMilli", 1, SurfaceDef.MaxSpeedMilli) : 1000;
                int cost = r.Has("cost") ? r.Int("cost", 1, SurfaceDef.MaxCost) : 1;
                r.Done();
                defs[i] = new SurfaceDef(e.Id, passable, speed, cost);
            }
            try { b.Terrain = new TerrainRules(b.Pack, b.SlopeNumerator, b.SlopeDenominator, defs); }
            catch (ArgumentException ex) { throw new ContentException("content pack '" + b.Pack + "' terrain is invalid: " + ex.Message); }
        }
    }
}
