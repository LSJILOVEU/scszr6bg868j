using System;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Read-only view of the world a rule set judges. Bound once, before the first Step.
    public interface IMatchReader
    {
        int PointCount { get; }
        void CountAlive(out int teamOne, out int teamTwo);
        int PointsOwnedBy(int team);
    }

    // WP-19 A0: the single owner of the match result. Start and Step never judge; battle.match (Queries, Order 50)
    // calls Evaluate after this tick's capture and before damage settles, exactly where the old MatchSystem judged.
    // The judging is done by the rule parts in MatchRules.cs, in the order of the old if/else.
    public sealed class MatchMode : IRetainedGameplayMode
    {
        public const string RwrRuleSetId = "rwr.m6-demo";
        // PLACEHOLDER: remove in A1. Tests that must keep stepping a world whose match would end select this id; the mode
        // then never judges. A1 replaces it with an injected rule whose condition is never true.
        public const string TestNeverEndsId = "test.never-ends";
        private readonly MatchPack pack;
        // First match decides: all points, then exhaustion, then the time limit.
        private readonly IMatchRule[] rules;
        private IMatchReader reader;
        public string RuleSetId { get; }
        public int Winner { get; private set; }
        public long EndTick { get; private set; }
        public MatchEndReason EndReason { get; private set; }
        public bool IsFinished => Winner != 0;
        public string StateTypeId => "battle.match-mode";
        public int StateVersion => 1;

        public MatchMode(MatchPack pack, string ruleSetId = RwrRuleSetId)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            if (string.IsNullOrEmpty(ruleSetId)) throw new ArgumentException("A rule set id is required.", nameof(ruleSetId));
            this.pack = pack; RuleSetId = ruleSetId;
            rules = new IMatchRule[] { new AllPointsRule(), new ExhaustedRule(), new TimeLimitRule(pack.MaxTicks) };
        }

        public void Bind(IMatchReader matchReader)
        {
            if (reader != null) throw new InvalidOperationException("The match mode is already bound.");
            reader = matchReader ?? throw new ArgumentNullException(nameof(matchReader));
        }

        public void Start(SimContext context) => RequireBound();
        public void Step(SimContext context) { }

        private void RequireBound()
        {
            if (reader == null) throw new InvalidOperationException("The match mode has no world bound; the world cannot advance.");
        }

        public void Evaluate(long tick)
        {
            RequireBound();
            if (Winner != 0 || RuleSetId == TestNeverEndsId) return;
            foreach (IMatchRule rule in rules)
                if (rule.TryEnd(reader, tick, out int winner, out MatchEndReason reason))
                { Winner = winner; EndReason = reason; break; }
            if (Winner != 0) EndTick = tick;
        }

        public void SaveState(SimStateWriter writer)
        { writer.UInt64(pack.PolicyDigest); writer.Int32(Winner); writer.Int64(EndTick); writer.Int32((int)EndReason); }

        public void LoadState(SimStateReader reader)
        {
            pack.Check(reader); Winner = reader.Int32(); EndTick = reader.Int64(); EndReason = (MatchEndReason)reader.Int32();
        }

        public void WriteState(StateHash hash)
        { hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.PolicyDigest); hash.Add(Winner); hash.Add(EndTick); hash.Add((int)EndReason); }
    }
}
