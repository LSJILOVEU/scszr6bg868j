using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // C1c record (no improvement gate). (a) The C1b world and kill table with guard 0 and 90: every switch with its death tick and
    // offset. (b) The demo recipe on the M7 map with one planned kill and a fixed fire script. Output goes to files in
    // C1C_RECORD_DIR when set, else to the test log. Hard conditions: no unplanned death inside the guard; for (b) with guard 90 at
    // least one enemy hit in the window, all of them zero, at least one player shot, and no match end within 120 ticks.
    public sealed class C1cRecordTests
    {
        internal const long DemoKill = 1100, FireFrom = 1400, FireTo = 1600, DemoEnd = 3000, NoEndTicks = 120;

        [TestCase(0), TestCase(90), Explicit("C1c record: run alone and keep the output."), Category("C1cRecord")]
        public void RecordTheC1bTable(int guard)
        {
            WorldRecipe recipe = DemoRecipes.WithRespawnGuard(C1bRecordTests.Recipe(), guard);
            var w = new M6World(recipe, DemoRecipes.M7MapBytes());
            long[] kills = C1bRecordTests.Kills;
            w.Sim.RegisterGameplay("test.respawn-kills", new KillControlled(w, kills), SimSystemPhase.Queries);
            var switches = new List<(long tick, ulong key)>();
            var deaths = new Dictionary<ulong, long>();
            long finished = -1;
            for (long tick = 1; tick <= C1bRecordTests.End; tick++)
            {
                if (w.Match.Winner != 0) { finished = w.Match.EndTick; break; }
                w.Sim.Step(default);
                if (Switches(w) == 1) switches.Add((tick, w.Session.ControlledKey.Value));
                foreach (var s in switches) if (!deaths.ContainsKey(s.key) && w.Dead(s.key)) deaths[s.key] = tick;
            }
            long window = guard > 0 ? guard : C1bRecordTests.Window;
            int inGuard = 0, afterGuard = 0, planned = 0, incomplete = 0;
            var lines = new StringBuilder();
            lines.AppendLine("guard " + guard + "; respawnTicks " + C1bRecordTests.RespawnTicks + "; kills " + string.Join(",", kills));
            lines.AppendLine("switch tick | key | death tick | offset | planned | window " + window);
            foreach (var s in switches)
            {
                string note = "";
                foreach (long k in kills) if (k >= s.tick && k < s.tick + window) note = "incomplete (planned kill at " + k + ")";
                if (note.Length > 0) incomplete++;
                if (deaths.TryGetValue(s.key, out long died))
                {
                    long offset = died - s.tick;
                    bool plan = Array.IndexOf(kills, died) >= 0;
                    if (plan) planned++;
                    else if (offset < guard) inGuard++;
                    else if (offset < guard + C1bRecordTests.Window) afterGuard++;
                    lines.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} | {1} | {2} | +{3} | {4} | {5}",
                        s.tick, s.key, died, offset, plan ? "yes" : "no", note));
                }
                else lines.AppendLine(s.tick + " | " + s.key + " | alive | - | - | " + note);
            }
            lines.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "switches {0}; unplanned death inside guard {1}; unplanned death within 30 after guard {2}; planned {3}; incomplete {4}; match end {5} winner {6}",
                switches.Count, inGuard, afterGuard, planned, incomplete, finished, w.Match.Winner));
            Write("record-g" + guard + ".txt", lines.ToString());
            Assert.That(switches.Count, Is.GreaterThan(0), "the record must contain switches");
            Assert.That(inGuard, Is.EqualTo(0), "no unplanned death inside the guard");
        }

        [TestCase(0), TestCase(90), Explicit("C1c record: run alone and keep the output."), Category("C1cRecord")]
        public void RecordTheDemoScript(int guard)
        {
            WorldRecipe recipe = DemoRecipes.WithRespawnGuard(DemoRecipes.MpRespawnDemo(), guard);
            var w = new M6World(recipe, DemoRecipes.M7MapBytes());
            w.Sim.RegisterGameplay("test.respawn-kills", new KillControlled(w, new[] { DemoKill }), SimSystemPhase.Queries);
            var aim = new InputFrame(default, SimVec3.Metres(80, 0, 50), primaryAttack: true);
            long sw = -1, died = -1, finished = -1;
            ulong key = 0, id = 0;
            int hitsIn = 0, zeroIn = 0, hitsAfter = 0, shots = 0;
            string at = "";
            for (long t = 1; t <= DemoEnd; t++)
            {
                if (w.Match.Winner != 0) { finished = w.Match.EndTick; break; }
                w.Sim.Step(t >= FireFrom && t <= FireTo ? aim : default);
                if (sw < 0 && Switches(w) == 1)
                {
                    sw = t; key = w.Session.ControlledKey.Value; id = w.Sim.Entities.StableOf(w.Unit(key)).Value;
                    SimVec3 p = Point(w, key);
                    at = string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2})", p.X.Raw / 65536.0, p.Z.Raw / 65536.0);
                }
                if (sw < 0 || died >= 0) continue;
                foreach (var f in w.Combat.Fires)
                {
                    if (f.Shooter.Value == id) shots++;
                    if (f.Target.Value != id || f.Outcome != FireOutcome.Hit) continue;
                    if (t < sw + guard) { hitsIn++; if (f.DamageRaw == 0) zeroIn++; }
                    else hitsAfter++;
                }
                if (w.Dead(key)) died = t;
            }
            string text = string.Format(CultureInfo.InvariantCulture,
                "guard {0}; kill {1}; fire [{2}, {3}] at (80, 0, 50)\nswitch {4} key {5} at {6}\nhits in window {7} (zero {8}); hits after window {9}; player shots {10}\ndied {11} (+{12}); match end {13} winner {14}\n",
                guard, DemoKill, FireFrom, FireTo, sw, key, at, hitsIn, zeroIn, hitsAfter, shots, died, died >= 0 ? died - sw : -1, finished, w.Match.Winner);
            Write("record-demo-g" + guard + ".txt", text);
            Assert.That(sw, Is.GreaterThan(0), "the script must switch");
            if (guard == 0) return;
            Assert.That(hitsIn, Is.GreaterThan(0), "enemy hits inside the window");
            Assert.That(zeroIn, Is.EqualTo(hitsIn), "every hit inside the window is zero");
            Assert.That(shots, Is.GreaterThan(0), "the player fires");
            Assert.That(finished < 0 || finished > sw + NoEndTicks, Is.True, "no match end within 120 ticks of the switch");
        }

        private static void Write(string name, string text)
        {
            string dir = Environment.GetEnvironmentVariable("C1C_RECORD_DIR");
            if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
            TestContext.Out.Write(text);
        }
    }
}
