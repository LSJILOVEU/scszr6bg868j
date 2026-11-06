using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // T1a-2a acceptance 2: worlds without terrain are byte-for-byte what they were. The constants below were produced on the
    // baseline commit 4ba1795 (m0-sim-core after #43) by running this same trace there; they must never be re-recorded.
    public sealed class Wp23BaselineTests
    {
        private const long S = Wp23.S;
        private static readonly int[] Ticks = { 0, 1, 2, 5, 10, 50, 100, 200, 300, 450 };

        private static string Trace(MoverKind mover)
        {
            var boxes = new[] { (10, 4, 2, 12), (20, 0, 2, 14), (20, 18, 2, 6), (30, 6, 2, 16) };
            WcMap flat = Wp23.Make(40, 24, 0, 6 * S, boxes, 4 * S);
            var units = new[]
            {
                Wp23.Unit(1, 1, 1500, 3500, 4000), Wp23.Unit(2, 1, 1500, 5500, 3000), Wp23.Unit(3, 2, 2500, 12500, 5000),
                Wp23.Unit(4, 2, 2500, 14500, 4000), Wp23.Unit(5, 3, 1500, 20500, 3500), Wp23.Unit(6, 3, 3500, 21500, 4500),
            };
            var script = new[] { BridgeCommand.MoveTo(1, 1, 37500, 3500), BridgeCommand.MoveTo(2, 2, 37500, 13500), BridgeCommand.MoveTo(3, 3, 37500, 21500) };
            SimSession s = Wp23.Session(flat, mover, units, script);
            var parts = new List<string>
            {
                "version=" + M3TestWorld.Locomotion(s).StateVersion,
                "snapshot0=" + M3TestWorld.Sim(s).SaveRetainedSnapshot().Length,
            };
            int next = 0;
            for (int tick = 0; tick <= Ticks[Ticks.Length - 1]; tick++)
            {
                if (tick != 0) s.StepOnce();
                if (tick == Ticks[next]) { parts.Add($"{tick}:{s.StateHash:x16}"); next++; }
            }
            parts.Add("snapshotEnd=" + M3TestWorld.Sim(s).SaveRetainedSnapshot().Length);
            return string.Join(" ", parts);
        }

        private const string PathBaseline = "version=3 snapshot0=5841 0:6cdfbfd2b73608eb 1:cb6173e60002f850 2:428edbeef85add88 5:9eb81f50365c8ed0 10:26b8027c237f8b68 50:3d42d05cc49f46df 100:1b986f0a5f47150e 200:8c821d9a3b51ba5f 300:63cbb10be462f39d 450:bd0f39f88161c543 snapshotEnd=5969";
        private const string CrowdBaseline = "version=4 snapshot0=5849 0:135a574ba9fb75f8 1:d5f2d20b9e90254a 2:093f425300596092 5:0adfc4e60174e554 10:1596bd8799157751 50:c8f9f697228257cd 100:3578b464adadde84 200:28cd29735101dd35 300:5fe4af95bbe7a301 450:9f60dc52a81fbf76 snapshotEnd=5977";

        [Test]
        public void FlatPathWorld_IsUnchangedFromTheBaseline() => Check(MoverKind.InterimPath, PathBaseline);

        [Test]
        public void FlatCrowdWorld_IsUnchangedFromTheBaseline() => Check(MoverKind.InterimCrowd, CrowdBaseline);

        private static void Check(MoverKind mover, string expected)
        {
            string actual = Trace(mover);
            Console.WriteLine("WP23 BASELINE " + mover + " " + actual);
            Assert.That(actual, Is.EqualTo(expected));
        }
    }
}
