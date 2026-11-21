using System;
using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Sim;
using static WarCore.Tests.RespawnAi1World;

namespace WarCore.Tests
{
    // RESPAWN-AI-1 helpers that use the new API: the pack with the switch, and test choosers.
    internal static class RespawnAi1
    {
        internal static MatchPack Pack(ControlPointDef point, bool skip, bool teamOneBase = true, int interval = Interval) =>
            new MatchPack(new[] { point, Far() }, Bases(teamOneBase), 2, 1, false, 8000, InterruptMode.Reset, 1, interval, 1000000, 100000,
                SimFix.FromInt(35), 2, 2, SimFix.FromInt(2), reinforceSkipContested: skip);

        // A chooser by a rule over (team, attempt, count); it records nothing and allocates nothing.
        internal sealed class Rule : IEntryChooser
        {
            private readonly Func<int, long, int, int> rule;
            public ulong PolicyKey { get; }
            internal Rule(ulong key, Func<int, long, int, int> rule) { PolicyKey = key; this.rule = rule; }
            public int First(int team, EntityId unit, ReadOnlySpan<EntryOption> options, long attempt) => rule(team, attempt, options.Length);
        }

        internal static Rule Rotation(ulong key = 77) => new Rule(key, (team, attempt, count) => (int)(attempt % count));
        // Team 1 tries entry `first`, every other team its first entry.
        internal static Rule TeamOne(int first, ulong key = 78) => new Rule(key, (team, attempt, count) => team == 1 ? first : 0);

        internal readonly struct Call
        {
            internal readonly long Tick, Attempt;
            internal readonly int Team;
            internal readonly StableId Unit;
            internal readonly EntryOption[] Options;
            internal Call(long tick, int team, StableId unit, EntryOption[] options, long attempt) { Tick = tick; Team = team; Unit = unit; Options = options; Attempt = attempt; }
        }

        // Records every call (copying the list and turning the unit into its stable id at once), then answers like `inner`.
        internal sealed class Recorder : IEntryChooser
        {
            private readonly IEntryChooser inner;
            internal M6World World;
            internal readonly List<Call> Calls = new List<Call>();
            internal Recorder(IEntryChooser inner) { this.inner = inner; }
            public ulong PolicyKey => inner.PolicyKey;
            public int First(int team, EntityId unit, ReadOnlySpan<EntryOption> options, long attempt)
            {
                Calls.Add(new Call(World.Sim.Clock.Tick, team, World.Sim.Entities.StableOf(unit), options.ToArray(), attempt));
                return inner.First(team, unit, options, attempt);
            }
        }

        internal static Recorder Record(M6World w, IEntryChooser inner = null)
        {
            var recorder = new Recorder(inner ?? Rotation()) { World = w };
            w.Reinforce.BindChooser(recorder);
            return recorder;
        }

        internal static (bool, int, SimVec3, int) Of(EntryOption o) => (o.IsBase, o.Point, o.At, o.Layer);
        internal static (bool, int, SimVec3, int)[] Of(EntryOption[] options) => Array.ConvertAll(options, Of);
        internal static (bool, int, SimVec3, int) Base1 => (true, -1, B1, 0);
        internal static (bool, int, SimVec3, int) PEntry => (false, 0, PCentre, 0);
    }
}
