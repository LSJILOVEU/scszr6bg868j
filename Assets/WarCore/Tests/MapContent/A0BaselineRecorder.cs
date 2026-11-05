using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Interim;
using WarCore.Sim;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // WP-19 A0: records the behaviour columns of the three long match scenarios, tick by tick, before and after the
    // rule-mode change. Output path comes from WARCORE_A0_OUT; nothing is overwritten.
    public sealed class A0BaselineRecorder
    {
        private static void Row(List<string> lines, string scenario, M6World w)
        {
            int one = 0, two = 0, reserveOne = 0, reserveTwo = 0;
            foreach (var spawn in w.Recipe.Units)
            {
                if (w.Dead(spawn.Key)) continue;
                int team = w.Read<WarCore.Combat.Combatant>(spawn.Key).Team;
                bool placed = w.Has<Position>(spawn.Key);
                if (team == 1) { if (placed) one++; else reserveOne++; } else if (team == 2) { if (placed) two++; else reserveTwo++; }
            }
            var points = new StringBuilder();
            foreach (var p in w.Capture.Points) points.Append(p.Owner).Append(':').Append(p.Capturing).Append(':').Append(p.Progress).Append(';');
            lines.Add(string.Join("\t", scenario, w.Sim.Clock.Tick, w.Match.Winner, w.Match.EndTick, (int)w.Match.EndReason, one, two, reserveOne, reserveTwo, points));
        }

        private static void Run(List<string> lines, string scenario, M6World w, long stop, bool stopAtEnd)
        {
            Row(lines, scenario, w);
            while (w.Sim.Clock.Tick < stop && !(stopAtEnd && w.Match.Winner != 0)) { w.Step(); Row(lines, scenario, w); }
        }

        [Category("Manual"), Test, Explicit("Records the WP-19 behaviour baseline.")]
        public void RecordA0Baseline()
        {
            string path = Environment.GetEnvironmentVariable("WARCORE_A0_OUT");
            Assert.That(string.IsNullOrEmpty(path), Is.False); Assert.That(File.Exists(path), Is.False);
            var lines = new List<string> { "# scenario, tick, winner, endTick, endReason, aliveOne, aliveTwo, reserveOne, reserveTwo, points(owner:capturing:progress;)" };
            Run(lines, "M6", new M6World(DemoRecipes.M6(), DemoRecipes.M6MapBytes()), 9000, true);
            // The real match ends on tick 747 (726 before WP-21) and Sim refuses to step after that, so the rows stop at the end (the pre-A0 file went on to 900).
            Run(lines, "M7-real", M7BehaviorLockTests.Real(41), 900, true);
            Run(lines, "M6-narrow", new M6World(M7BehaviorLockTests.Demo(41), M7BehaviorLockTests.NarrowMap()), 9000, true);
            File.WriteAllLines(path, lines, new UTF8Encoding(false));
        }
    }
}
