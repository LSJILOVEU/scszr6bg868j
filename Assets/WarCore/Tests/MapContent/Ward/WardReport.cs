using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // MAP-BL0 W8: seeds 41 to 60 (Document(seed) baked, the AI recipe), each run to the end or 9000 ticks, twice side by side.
    // The only pass line: nothing thrown and the same hash every tick. End tick, winner, end reason and the stuck counts are
    // printed, not asserted. Stuck, two ways, each over a 300 and a 900 tick window (design/thin-walls-kb K-T7):
    //   activity: alive on the field, the walk intent active on every tick of the window, the two ends of the window less
    //             than 1 m apart (horizontal);
    //   m6:       the same, and a living enemy within sight (horizontal distance only, walls ignored) restarts the window,
    //             as the M6 StuckMeter does.
    // Not part of any suite.
    public sealed class WardReport
    {
        private static readonly int[] Windows = { 300, 900 };

        private sealed class Meter
        {
            private readonly bool sight;
            private readonly int window;
            private readonly int[] run;
            private readonly SimVec3[,] history;
            internal readonly int[] StuckTicks;
            internal readonly List<string> First = new List<string>();
            internal string Name => (sight ? "m6" : "activity") + window;
            internal int Units => StuckTicks.Count(t => t > 0);
            internal long UnitTicks => StuckTicks.Sum(t => (long)t);

            internal Meter(int count, bool sight, int window)
            {
                this.sight = sight; this.window = window;
                run = new int[count]; history = new SimVec3[count, window + 1]; StuckTicks = new int[count];
            }

            internal void Sample(int tick, int i, ulong key, bool active, bool enemyNear, SimVec3 p)
            {
                if (!active || (sight && enemyNear)) { run[i] = 0; return; }
                run[i]++;
                history[i, tick % (window + 1)] = p;
                if (run[i] < window + 1) return;
                SimVec3 then = history[i, (tick - window) % (window + 1)];
                if (CombatSystem.Horizontal(p - then).LengthSquared >= new SimVec3(SimFix.FromInt(1), default, default).LengthSquared) return;
                if (StuckTicks[i] == 0 && First.Count < 20)
                    First.Add(key + "@(" + p.X.Raw / SimFix.Scale + "," + p.Z.Raw / SimFix.Scale + ")t" + tick);
                StuckTicks[i]++;
            }
        }

        [Test, Explicit("prints the 20-seed report"), Category("Manual")]
        public void Print()
        {
            int seeds = int.TryParse(Environment.GetEnvironmentVariable("WARD_REPORT_SEEDS"), out int n) ? n : 20;
            var total = System.Diagnostics.Stopwatch.StartNew();
            for (ulong seed = 41; seed < 41 + (ulong)seeds; seed++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                MapBakeResult baked = MapBake.Run(WardBuild.Document(seed).Save());
                M6World a = WardWorld.World(WardDemo.AiRecipe(baked), baked), b = WardWorld.World(WardDemo.AiRecipe(baked), baked);
                UnitSpawn[] units = a.Recipe.Units.ToArray();
                var meters = new List<Meter>();
                foreach (bool sight in new[] { false, true })
                    foreach (int window in Windows) meters.Add(new Meter(units.Length, sight, window));
                var sightSq = new SimVec3(a.Recipe.Match.Sight, default, default).LengthSquared;
                var alive = new bool[units.Length];
                var at = new SimVec3[units.Length];
                string diff = null;
                int tick = 0;
                while (a.Match.Winner == 0 && tick < 9000)
                {
                    a.Step(); b.Step(); tick++;
                    if (diff == null && a.Hash != b.Hash) diff = "tick " + tick;
                    for (int i = 0; i < units.Length; i++)
                    {
                        ulong key = units[i].Key;
                        alive[i] = a.Has<Position>(key) && !a.Dead(key);
                        if (alive[i]) at[i] = a.Read<Position>(key).Value;
                    }
                    for (int i = 0; i < units.Length; i++)
                    {
                        ulong key = units[i].Key;
                        bool active = alive[i] && a.Has<MoveIntent>(key) && a.Read<MoveIntent>(key).Active;
                        bool near = false;
                        if (active)
                            for (int j = 0; j < units.Length && !near; j++)
                                near = alive[j] && units[j].Team != units[i].Team && CombatSystem.Horizontal(at[i] - at[j]).LengthSquared <= sightSq;
                        foreach (Meter m in meters) m.Sample(tick, i, key, active, near, at[i]);
                    }
                }
                TestContext.WriteLine("MAPBL0 W8 seed=" + seed + " end=" + tick + " winner=" + a.Match.Winner + " reason=" + a.Match.EndReason
                    + " hashSame=" + (diff == null) + (diff == null ? "" : " firstDiff=" + diff) + " endHash=" + a.Hash.ToString("X16") + " ms=" + sw.ElapsedMilliseconds);
                foreach (Meter m in meters)
                    TestContext.WriteLine("MAPBL0 W8 seed=" + seed + " " + m.Name + " stuckUnits=" + m.Units + " stuckUnitTicks=" + m.UnitTicks
                        + " first=" + string.Join(" ", m.First));
                Assert.That(diff, Is.Null, "the same hash every tick, seed " + seed);
            }
            TestContext.WriteLine("MAPBL0 W8 total ms=" + total.ElapsedMilliseconds);
        }
    }
}
