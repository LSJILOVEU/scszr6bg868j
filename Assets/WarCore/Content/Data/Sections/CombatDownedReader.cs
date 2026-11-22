using WarCore.Combat;

namespace WarCore.Content
{
    // DOWN-1 combat.downed { when, ticks, stance?, crawlPermille, finish?, finishAfterTicks?, stunTicks?, targetable? }: when is
    // "lethal" or "gear"; ticks 1..108000; stance the id of one of the pack's stances (refused without stances); crawlPermille
    // 1..1000; finish true by default; finishAfterTicks 0..ticks, default 0; stunTicks 0..600, default 0; targetable true by
    // default. The object is one value: a child layer's downed replaces the parent's whole. Any other field is refused.
    internal static class CombatDownedReader
    {
        internal const string Field = "downed";
        internal const string WhenField = "when", TicksField = "ticks", StanceField = "stance", CrawlField = "crawlPermille", FinishField = "finish",
            FinishAfterField = "finishAfterTicks", StunField = "stunTicks", TargetableField = "targetable";

        internal static DownedRules Read(string pack, ContentField field, StanceDef[] stances)
        {
            var entry = new ContentEntry(CombatSection.SectionKey + "." + Field, null, field.Layer);
            if (field.Node.Kind != JsonKind.Object)
                throw new ContentException("content pack '" + pack + "' layer '" + field.Layer + "' combat field '" + Field + "': must be an object");
            foreach (JsonMember member in field.Node.Members) entry.Fields.Add(new ContentField(member.Name, member.Value, field.Layer));
            var r = new EntryReader(pack, entry);
            string whenText = r.Str(WhenField);
            DownedWhen when;
            if (whenText == "lethal") when = DownedWhen.Lethal;
            else if (whenText == "gear") when = DownedWhen.Gear;
            else throw r.Bad(r.FieldOf(WhenField), "unknown value '" + whenText + "', allowed: lethal, gear");
            int ticks = r.Int(TicksField, 1, DownedRules.MaxTicks);
            int crawl = r.Int(CrawlField, 1, DownedRules.MaxCrawlPermille);
            int level = 0;
            string stance = r.StrOrNull(StanceField, out ContentField stanceField);
            if (stance != null)
            {
                if (stances == null || stances.Length == 0) throw r.Bad(stanceField, "needs the combat section's stances");
                for (int i = 0; i < stances.Length && level == 0; i++)
                    if (stances[i].Id == stance) level = i + 1;
                if (level == 0) throw r.Bad(stanceField, "'" + stance + "' is not a stance of this pack");
            }
            bool finish = !r.Has(FinishField) || r.Bool(FinishField);
            int finishAfter = r.Has(FinishAfterField) ? r.Int(FinishAfterField, 0, ticks) : 0;
            int stun = r.Has(StunField) ? r.Int(StunField, 0, DownedRules.MaxStunTicks) : 0;
            bool targetable = !r.Has(TargetableField) || r.Bool(TargetableField);
            r.Done();
            return new DownedRules(when, ticks, crawl, level, finish, finishAfter, stun, targetable);
        }
    }
}
