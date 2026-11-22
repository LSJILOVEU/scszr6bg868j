using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;
using static WarCore.Tests.RespawnAi2World;

namespace WarCore.Tests
{
    // RESPAWN-AI-2 helpers that use the new API: the pack with match.reentryTicks, and stepping with the two invariants.
    internal static class RespawnAi2
    {
        internal const int Reentry = 60;

        internal static MatchPack Pack(int reentry = Reentry, int respawn = 0, bool teamOneBase = true, bool skip = false, int pOwner = 1) =>
            new MatchPack(new[] { P(pOwner), Far() }, Bases(teamOneBase), 2, 1, false, 8000, InterruptMode.Reset, 1, Interval, 1000000, 100000,
                SimFix.FromInt(35), 2, 2, SimFix.FromInt(2), respawn, reinforceSkipContested: skip, reentryTicks: reentry);

        // One step that must not throw, then what holds after every step of a world with the switch: no team has had more
        // reserves enter than it has dead, and no team has more men on the field than at the start.
        internal static void Go(M6World w, int[] atStart = null)
        {
            Assert.DoesNotThrow(() => w.Step());
            for (int team = 1; team <= 2; team++)
            {
                Assert.That(w.Reinforce.Entered(team), Is.LessThanOrEqualTo(DeadOf(w, team)), "entered <= dead, team " + team + " at tick " + w.Sim.Clock.Tick);
                if (atStart != null) Assert.That(Fielded(w, team), Is.LessThanOrEqualTo(atStart[team - 1]), "on the field <= at the start, team " + team + " at tick " + w.Sim.Clock.Tick);
            }
        }

        // Steps until the step of `tick` has run.
        internal static void GoTo(M6World w, long tick, int[] atStart = null)
        {
            Assert.That(w.Sim.Clock.Tick, Is.LessThan(tick), "the world is already at or past tick " + tick);
            while (w.Sim.Clock.Tick < tick) Go(w, atStart);
        }

        internal static int[] AtStart(M6World w) => new[] { Fielded(w, 1), Fielded(w, 2) };

        // (due, entered, owed, successes) of a team as of the last step.
        internal static (long due, long entered, long owed, long successes) Counts(M6World w, int team) =>
            (w.Reinforce.Due(team), w.Reinforce.Entered(team), w.Reinforce.Owed(team), w.Reinforce.Successes(team));

        // The entry records of a team from the steps up to `tick`, as (tick, point index: -1 = a base).
        internal static List<(long tick, int point)> Entries(M6World w, int team, long tick, int[] atStart = null)
        {
            var list = new List<(long, int)>();
            while (w.Sim.Clock.Tick < tick)
            {
                Go(w, atStart);
                foreach (DeployRecord r in w.Reinforce.Deployed) if (r.Team == team) list.Add((r.Tick, r.PointIndex));
            }
            return list;
        }
    }
}
