namespace WarCore.Presentation
{
    // RESPAWN-AI-2: the reinforcement counts of a world with match.reentryTicks, for a view or a tool. In its own file
    // because SimSession.cs is at the review limit.
    public sealed partial class SimSession
    {
        // True in a world with match.reentryTicks. There, per team (1..TeamCount of the match rules), how many reserves
        // the team is owed (dead long enough, minus entered; may be negative) and how many entered, as of the last step.
        // Not hashed; false and zeros in any other world.
        public bool HasReentry => reinforce != null && reinforce.ByDebt;
        public bool TryReentry(int team, out long owed, out long entered)
        {
            owed = 0; entered = 0;
            if (!HasReentry || team < 1 || team > matchPack.TeamCount) return false;
            owed = reinforce.Owed(team); entered = reinforce.Entered(team);
            return true;
        }
    }
}
