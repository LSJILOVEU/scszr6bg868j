using System;

namespace WarCore.Battle
{
    // One way a match can end, judged from read-only facts. Rules run in a fixed order and the first that ends the
    // match decides it; none keeps state, so the mode remains the only owner of the result.
    internal interface IMatchRule
    {
        bool TryEnd(IMatchReader world, long tick, out int winner, out MatchEndReason reason);
    }

    // One team owns every point. Searched from the highest team down (NT-1), so a reader where two teams both report every
    // point keeps the old answer, team 2.
    internal sealed class AllPointsRule : IMatchRule
    {
        public bool TryEnd(IMatchReader world, long tick, out int winner, out MatchEndReason reason)
        {
            int points = world.PointCount;
            winner = 0; reason = MatchEndReason.None;
            for (int team = world.TeamCount; team >= 1; team--)
                if (world.PointsOwnedBy(team) == points) { winner = team; reason = MatchEndReason.AllPoints; return true; }
            return false;
        }
    }

    // At most one team has anyone left alive (NT-1: of teams 1..TeamCount). The count includes units still held in reserve;
    // the last team standing wins, every team empty is a draw.
    internal sealed class ExhaustedRule : IMatchRule
    {
        public bool TryEnd(IMatchReader world, long tick, out int winner, out MatchEndReason reason)
        {
            // 8 = MatchPack.MaxTeams (the stack budget test reads literals or Sim consts only); Slice fails loudly if they part.
            Span<int> alive = (stackalloc int[8]).Slice(0, MatchPack.MaxTeams);
            world.CountAlive(alive);
            winner = 0; reason = MatchEndReason.None;
            int standing = 0, last = 0;
            for (int team = 1; team <= world.TeamCount; team++)
                if (alive[team - 1] != 0) { standing++; last = team; }
            if (standing >= 2) return false;
            winner = standing == 0 ? -1 : last;
            reason = winner == -1 ? MatchEndReason.Draw : MatchEndReason.Exhausted;
            return true;
        }
    }

    // The time limit: the one team with the most points; with a tie for the most, the one of the tied teams with the most units
    // alive; else a draw. Teams not tied for the most points never compete on units (NT-1).
    internal sealed class TimeLimitRule : IMatchRule
    {
        private readonly int maxTicks;
        public TimeLimitRule(int maxTicks) { this.maxTicks = maxTicks; }

        public bool TryEnd(IMatchReader world, long tick, out int winner, out MatchEndReason reason)
        {
            winner = 0; reason = MatchEndReason.None;
            if (tick < maxTicks) return false;
            int teams = world.TeamCount;
            // 8 = MatchPack.MaxTeams, as in ExhaustedRule.
            Span<int> points = (stackalloc int[8]).Slice(0, MatchPack.MaxTeams);
            Span<int> alive = (stackalloc int[8]).Slice(0, MatchPack.MaxTeams);
            int most = int.MinValue;
            for (int team = 1; team <= teams; team++) { points[team - 1] = world.PointsOwnedBy(team); most = Math.Max(most, points[team - 1]); }
            world.CountAlive(alive);
            // Among the teams with the most points (just one, or a tie), the one with the most units alive; a tie there is a draw.
            int best = 0, bestAlive = 0; bool tie = false;
            for (int team = 1; team <= teams; team++)
            {
                if (points[team - 1] != most) continue;
                if (best == 0 || alive[team - 1] > bestAlive) { best = team; bestAlive = alive[team - 1]; tie = false; }
                else if (alive[team - 1] == bestAlive) tie = true;
            }
            winner = tie ? -1 : best;
            reason = winner == -1 ? MatchEndReason.Draw : MatchEndReason.Time;
            return true;
        }
    }
}
