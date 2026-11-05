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
    // The judging below is the old if/else, moved unchanged; A1 replaces it with rule parts.
    public sealed class MatchMode : IRetainedGameplayMode
    {
        public const string RwrRuleSetId = "rwr.m6-demo";
        // PLACEHOLDER: remove in A1. Tests that must keep stepping a world whose match would end select this id; the mode
        // then never judges. A1 replaces it with an injected rule whose condition is never true.
        public const string TestNeverEndsId = "test.never-ends";
        private readonly MatchPack pack;
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
            reader.CountAlive(out int one, out int two);
            int pointsOne = reader.PointsOwnedBy(1), pointsTwo = reader.PointsOwnedBy(2), points = reader.PointCount;
            if (pointsOne == points || pointsTwo == points)
            { Winner = pointsOne > pointsTwo ? 1 : 2; EndReason = MatchEndReason.AllPoints; }
            else if (one == 0 || two == 0)
            { Winner = one == two ? -1 : one > 0 ? 1 : 2; EndReason = Winner == -1 ? MatchEndReason.Draw : MatchEndReason.Exhausted; }
            else if (tick >= pack.MaxTicks)
            {
                Winner = pointsOne != pointsTwo ? (pointsOne > pointsTwo ? 1 : 2) : one != two ? (one > two ? 1 : 2) : -1;
                EndReason = Winner == -1 ? MatchEndReason.Draw : MatchEndReason.Time;
            }
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
