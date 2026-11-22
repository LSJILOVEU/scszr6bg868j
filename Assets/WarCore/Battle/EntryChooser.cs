using System;
using WarCore.Sim;

namespace WarCore.Battle
{
    // RESPAWN-AI-1: one entry of a team's reinforcement list for one attempt (ReinforceSystem): the team's bases in pack order,
    // then its owned points with a spawn in pack order (with match.reinforceSkipContested, only the points nobody contests).
    public readonly struct EntryOption
    {
        public readonly bool IsBase;
        // The index of the point in MatchPack.Points; -1 for a base.
        public readonly int Point;
        // The anchor the deployment grid is laid around, in world coordinates, and its layer (0 = the ground, 1..N = a floor
        // plate): the same pair the route questions of IMoveOrders take.
        public readonly SimVec3 At;
        public readonly int Layer;
        public EntryOption(bool isBase, int point, SimVec3 at, int layer) { IsBase = isBase; Point = point; At = at; Layer = layer; }
    }

    // Picks which entry of the list a reserve tries first; the system then goes on round the list from there (the grid's
    // first cell is not the chooser's). Not bound, the system uses the rotation attempt % options.Length.
    // Rules for an implementation:
    // - First reads only what a snapshot restores (simulation state, the pack, its own fixed parameters), else a run restored
    //   from a snapshot goes another way than the run that never stopped. PolicyKey covers its parameters: it is saved and
    //   hashed with the reinforcement state and a snapshot with another key is refused.
    // - The EntityId it is given is valid for this call only: never keep it to a later step; keep the StableId if anything.
    // - Return a number from 0 to options.Length - 1; anything else throws (ArgumentOutOfRangeException).
    public interface IEntryChooser
    {
        ulong PolicyKey { get; }
        // options: this attempt's list for this team (never empty); unit: the reserve to deploy; attempt: the team's attempt
        // number before this one was counted (ReinforceSystem.Attempts(team) - 1 after the step).
        int First(int team, EntityId unit, ReadOnlySpan<EntryOption> options, long attempt);
    }
}
