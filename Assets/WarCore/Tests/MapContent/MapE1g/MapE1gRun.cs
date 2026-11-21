using System.Collections.Generic;
using System.Linq;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;

namespace WarCore.Tests
{
    // MAP-E1g: runs of the committed map (E7 one team alone, E8 the whole battle) and what they record.
    internal static class MapE1gRun
    {
        internal sealed class Result
        {
            internal int Ticks;
            internal ulong Hash;
            internal int Winner;
            // First tick each point passed to the team (point id -> tick).
            internal readonly SortedDictionary<int, int> Taken = new SortedDictionary<int, int>();
            internal readonly List<string> Failures = new List<string>();
            internal readonly List<string> OffCell = new List<string>();
            internal string FirstHashDiff;
            internal int Deployed;
            internal int Dead;
        }

        // Steps the world to `limit` ticks, or to the end of the match when it can end. team is the team whose points are
        // recorded (0: both). Every tick every living soldier on the field is checked for a failed walk and for standing off the
        // walkable cells; `again`, when given, is stepped in lockstep and its hash compared.
        internal static Result Go(M6World w, int limit, int team, M6World again = null)
        {
            var r = new Result();
            ulong[] keys = w.Recipe.Units.ToArray().Select(u => u.Key).ToArray();
            int t = 0;
            for (; t < limit && w.Match.Winner == 0; t++)
            {
                w.Step();
                if (again != null)
                {
                    again.Step();
                    if (r.FirstHashDiff == null && again.Hash != w.Hash) r.FirstHashDiff = "tick " + t;
                }
                for (int k = 0; k < w.Capture.Points.Length; k++)
                {
                    int owner = w.Capture.Points[k].Owner;
                    if (owner != 0 && (team == 0 || owner == team) && !r.Taken.ContainsKey(k + 1) && w.Recipe.Match.Points[k].InitialOwner == 0)
                        r.Taken[k + 1] = t + 1;
                }
                r.Deployed += w.Reinforce.Deployed.Length;
                foreach (ulong key in keys)
                {
                    if (!w.Has<Position>(key) || w.Dead(key)) continue;
                    if (Failed(w, key)) r.Failures.Add("unit " + key + " tick " + t);
                    SimVec3 at = At(w, key);
                    if (!MapE1gWorld.Tables.Nav.IsPassable((int)(at.X.Raw / SimFix.Scale), (int)(at.Z.Raw / SimFix.Scale)))
                        r.OffCell.Add("unit " + key + " tick " + t);
                }
            }
            r.Ticks = t; r.Hash = w.Hash; r.Winner = w.Match.Winner;
            foreach (ulong key in keys) if (w.Has<Position>(key) && w.Dead(key)) r.Dead++;
            return r;
        }
    }
}
