using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Content
{
    // Builds after combat: a sight-limited selector (a combat value) needs match.sight.
    internal sealed class MatchSection : ObjectSection
    {
        internal const string SectionKey = "match";
        internal MatchSection() : base(SectionKey) { }

        public override void Build(SectionBuild b)
        {
            ContentEntry match = b.Single(Key);
            if (match == null)
            {
                if (b.SelectorId == ContentPolicies.SightLimitedNearest)
                    throw new ContentException("content pack '" + b.Pack + "' uses selector sight-limited, which needs match.sight");
                return;
            }
            var mr = new EntryReader(b.Pack, match);
            int num = mr.Int("captureAdvantageNum", 1), den = mr.Int("captureAdvantageDen", 1);
            bool strict = mr.Bool("captureStrict");
            int captureTicks = mr.Int("captureTicks", 1);
            string interruptName = mr.Str("interrupt");
            int decay = mr.Int("decayPerTick", 1), interval = mr.Int("intervalTicks", 1), think = mr.Int("thinkInterval", 1), max = mr.Int("maxTicks", 1);
            SimFix sight = mr.Fix("sight");
            int columns = mr.Int("gridColumns", 1, 4096), rows = mr.Int("gridRows", 1, 4096);
            SimFix spacing = mr.Fix("gridSpacing");
            ContentField interruptField = mr.FieldOf("interrupt");
            int respawn = mr.Has("respawnTicks") ? mr.Int("respawnTicks", 0, MatchPack.MaxRespawnTicks) : 0;
            mr.Done();
            if (num < den) throw mr.Bad(mr.FieldOf("captureAdvantageNum"), "must be at least captureAdvantageDen");
            if (sight.Raw <= 0) throw mr.Bad(mr.FieldOf("sight"), "must be greater than 0");
            if (spacing.Raw <= 0) throw mr.Bad(mr.FieldOf("gridSpacing"), "must be greater than 0");
            InterruptMode interrupt;
            switch (interruptName)
            {
                case "reset": interrupt = InterruptMode.Reset; break;
                case "pause": interrupt = InterruptMode.Pause; break;
                case "decay": interrupt = InterruptMode.Decay; break;
                default: throw mr.Bad(interruptField, "must be reset, pause or decay");
            }
            b.Rules = new MatchRules(num, den, strict, captureTicks, interrupt, decay, interval, think, max, sight, columns, rows, spacing, respawn);
        }
    }
}
