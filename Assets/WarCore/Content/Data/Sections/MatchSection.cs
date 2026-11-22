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
            int decay = mr.Int("decayPerTick", interruptName == "decay" ? 1 : 0), interval = mr.Int("intervalTicks", 1), think = mr.Int("thinkInterval", 1), max = mr.Int("maxTicks", 1);
            SimFix sight = mr.Fix("sight");
            int columns = mr.Int("gridColumns", 1, 4096), rows = mr.Int("gridRows", 1, 4096);
            SimFix spacing = mr.Fix("gridSpacing");
            ContentField interruptField = mr.FieldOf("interrupt");
            int respawn = mr.Has("respawnTicks") ? mr.Int("respawnTicks", 0, MatchPack.MaxRespawnTicks) : 0;
            int guard = mr.Has("respawnGuardTicks") ? mr.Int("respawnGuardTicks", 0, MatchPack.MaxRespawnTicks) : 0;
            bool skipContested = mr.Has("reinforceSkipContested") && mr.Bool("reinforceSkipContested");
            // RESPAWN-AI-2: all three optional. The type is read only with a positive count; without one it is accepted unread.
            int reentry = mr.Has("reentryTicks") ? mr.Int("reentryTicks", 0, MatchPack.MaxReentryTicks) : 0;
            int reserveCount = mr.Has("reserveCount") ? mr.Int("reserveCount", 0, MatchPack.MaxReserveCount) : 0;
            string reserveType = null;
            if (reserveCount > 0)
            {
                reserveType = mr.Str("reserveType");
                if (System.Array.IndexOf(b.UnitIds, reserveType) < 0) throw mr.Bad(mr.FieldOf("reserveType"), "unit type '" + reserveType + "' does not exist");
            }
            else if (mr.Has("reserveType")) mr.FieldOf("reserveType").Used = true;
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
            b.Rules = new MatchRules(num, den, strict, captureTicks, interrupt, decay, interval, think, max, sight, columns, rows, spacing, respawn, guard, skipContested, reentry, reserveCount, reserveType);
        }
    }
}
