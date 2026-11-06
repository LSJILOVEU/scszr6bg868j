namespace WarCore.Battle
{
    // One way a match can end, judged from read-only facts. Rules run in a fixed order and the first that ends the
    // match decides it; none keeps state, so the mode remains the only owner of the result.
    internal interface IMatchRule
    {
        bool TryEnd(IMatchReader world, long tick, out int winner, out MatchEndReason reason);
    }

    // One team owns every point.
    internal sealed class AllPointsRule : IMatchRule
    {
        public bool TryEnd(IMatchReader world, long tick, out int winner, out MatchEndReason reason)
        {
            int one = world.PointsOwnedBy(1), two = world.PointsOwnedBy(2), points = world.PointCount;
            winner = 0; reason = MatchEndReason.None;
            if (one != points && two != points) return false;
            winner = one > two ? 1 : 2; reason = MatchEndReason.AllPoints;
            return true;
        }
    }

    // A team has no one left alive. The count includes units still held in reserve; both teams empty is a draw.
    internal sealed class ExhaustedRule : IMatchRule
    {
        public bool TryEnd(IMatchReader world, long tick, out int winner, out MatchEndReason reason)
        {
            world.CountAlive(out int one, out int two);
            winner = 0; reason = MatchEndReason.None;
            if (one != 0 && two != 0) return false;
            winner = one == two ? -1 : one > 0 ? 1 : 2;
            reason = winner == -1 ? MatchEndReason.Draw : MatchEndReason.Exhausted;
            return true;
        }
    }

    // The time limit: more points, then more units alive, else a draw.
    internal sealed class TimeLimitRule : IMatchRule
    {
        private readonly int maxTicks;
        public TimeLimitRule(int maxTicks) { this.maxTicks = maxTicks; }

        public bool TryEnd(IMatchReader world, long tick, out int winner, out MatchEndReason reason)
        {
            winner = 0; reason = MatchEndReason.None;
            if (tick < maxTicks) return false;
            int pointsOne = world.PointsOwnedBy(1), pointsTwo = world.PointsOwnedBy(2);
            world.CountAlive(out int one, out int two);
            winner = pointsOne != pointsTwo ? (pointsOne > pointsTwo ? 1 : 2) : one != two ? (one > two ? 1 : 2) : -1;
            reason = winner == -1 ? MatchEndReason.Draw : MatchEndReason.Time;
            return true;
        }
    }
}
