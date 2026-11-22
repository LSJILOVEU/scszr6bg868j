using System;

namespace WarCore.Presentation
{
    // RESPAWN-AI-2: the reinforcement counts of a world with match.reentryTicks, for a view or a tool. The session's
    // public members are a locked list, so the public way in is this class and the session's part stays internal.
    public static class SessionReentry
    {
        // True in a world with match.reentryTicks.
        public static bool Has(SimSession session) => (session ?? throw new ArgumentNullException(nameof(session))).HasReentry;

        // In a world with match.reentryTicks, per team (1..TeamCount of the match rules): how many reserves the team is
        // owed (dead long enough, minus entered; may be negative) and how many entered, as of the last step. Not hashed.
        // False and zeros in any other world and for any other team.
        public static bool TryCounts(SimSession session, int team, out long owed, out long entered) =>
            (session ?? throw new ArgumentNullException(nameof(session))).TryReentry(team, out owed, out entered);
    }

    // In its own file because SimSession.cs is at the review limit.
    public sealed partial class SimSession
    {
        internal bool HasReentry => reinforce != null && reinforce.ByDebt;
        internal bool TryReentry(int team, out long owed, out long entered)
        {
            owed = 0; entered = 0;
            if (!HasReentry || team < 1 || team > matchPack.TeamCount) return false;
            owed = reinforce.Owed(team); entered = reinforce.Entered(team);
            return true;
        }
    }
}
