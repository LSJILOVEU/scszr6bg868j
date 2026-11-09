using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Determinism of the full terrain match on the L6 fixture: reversed physical allocation, and snapshot restores in the middle
    // (a deploy tick, capture in progress, the first change of owner, after reinforcements) and at the end. Permanent locks:
    // a later package may move the witness ticks to equivalent states but must keep every check below.
    public sealed class L6DeterminismTests
    {
        [Test]
        public void L6Determinism_ReversedAllocationGivesTheSameMatch()
        {
            var a = L6.World();
            var b = L6.World(reverse: true);
            Assert.That(b.Unit(1), Is.Not.EqualTo(a.Unit(1)), "physical allocation must actually change");
            while (a.Match.Winner == 0 && a.Sim.Clock.Tick < L6.StepLimit)
            {
                a.Step(); b.Step();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + a.Sim.Clock.Tick);
            }
            Assert.That(a.Match.Winner, Is.Not.EqualTo(0));
            Assert.That((b.Match.EndTick, b.Match.Winner, b.Match.EndReason), Is.EqualTo((a.Match.EndTick, a.Match.Winner, a.Match.EndReason)));
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(a.Sim.SaveRetainedSnapshot()));
        }

        private static M6World Restore(M6World a, byte[] snapshot)
        {
            var c = new M6World(a.Recipe, L6.MapBytes());
            c.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: true);
            Assert.That(c.Unit(1), Is.Not.EqualTo(a.Unit(1)), "physical allocation must actually change");
            Assert.That(c.Sim.SaveRetainedSnapshot(), Is.EqualTo(snapshot), "save after restore");
            Assert.That(c.Capture.Changes.Length, Is.Zero, "capture events must not replay");
            Assert.That(c.Reinforce.Deployed.Length, Is.Zero, "deploy events must not replay");
            return c;
        }

        private static int Moving(M6World w)
        {
            int moving = 0;
            for (ulong key = 1; key <= 40; key++)
                if (w.Has<MoveIntent>(key) && w.Read<MoveIntent>(key).Active) moving++;
            return moving;
        }

        [Test]
        public void L6Determinism_RestoresInTheMiddleAndAtTheEnd()
        {
            var a = L6.World();
            var restored = new List<(long Tick, M6World World)>();
            var taken = new HashSet<long>();
            long firstDeploy = -1, firstFlip = -1;
            bool capturing300 = false, reinforced600 = false;
            int movingAtDeploy = 0, deployedAtDeploy = 0;
            while (a.Match.Winner == 0 && a.Sim.Clock.Tick < L6.StepLimit)
            {
                a.Step();
                long tick = a.Sim.Clock.Tick;
                foreach (var (_, c) in restored)
                {
                    c.Step();
                    Assert.That(c.Hash, Is.EqualTo(a.Hash), "tick " + tick);
                    Assert.That(c.Capture.Changes.ToArray(), Is.EqualTo(a.Capture.Changes.ToArray()), "capture tick " + tick);
                    Assert.That(c.Reinforce.Deployed.ToArray(), Is.EqualTo(a.Reinforce.Deployed.ToArray()), "deploy tick " + tick);
                }
                // Events are noted on the end tick too, so a point that falls on it is reported as skipped below.
                bool point = false;
                if (firstDeploy < 0 && a.Reinforce.Deployed.Length > 0)
                { firstDeploy = tick; deployedAtDeploy = a.Reinforce.Deployed.Length; movingAtDeploy = Moving(a); point = true; }
                if (firstFlip < 0 && a.Capture.Changes.Length > 0) { firstFlip = tick; point = true; }
                if (tick == 300) { capturing300 = a.Capture.Points[0].Owner == 0 && a.Capture.Points[0].Progress > 0; point = true; }
                if (tick == 600) { reinforced600 = a.Reinforce.Successes(1) >= 1; point = true; }
                if (a.Match.Winner != 0) break; // the end is checked below, never as a middle point
                if (point && taken.Add(tick)) restored.Add((tick, Restore(a, a.Sim.SaveRetainedSnapshot())));
            }
            Assert.That(a.Match.Winner, Is.Not.EqualTo(0));
            TestContext.Progress.WriteLine("L6 restore points: " + string.Join(",", taken) + "; skipped (at or after the end, or missing): "
                + Skipped(a.Match.EndTick, firstDeploy, firstFlip));
            Assert.That(taken.Count, Is.GreaterThanOrEqualTo(3), "at least three distinct middle ticks");

            // Evidence that the middle points caught the states they stand for (a point at or after the end has none).
            if (firstDeploy >= 0 && firstDeploy < a.Match.EndTick)
            {
                Assert.That(deployedAtDeploy, Is.GreaterThan(0));
                Assert.That(movingAtDeploy, Is.GreaterThan(0), "AI orders in flight at the deploy tick");
            }
            if (300 < a.Match.EndTick) Assert.That(capturing300, Is.True, "point 1 is being captured at tick 300");
            if (600 < a.Match.EndTick) Assert.That(reinforced600, Is.True, "team 1 has reinforced by tick 600");

            byte[] end = a.Sim.SaveRetainedSnapshot();
            foreach (var (tick, c) in restored)
            {
                Assert.That((c.Match.EndTick, c.Match.Winner, c.Match.EndReason), Is.EqualTo((a.Match.EndTick, a.Match.Winner, a.Match.EndReason)), "restored at " + tick);
                Assert.That(c.Sim.SaveRetainedSnapshot(), Is.EqualTo(end), "restored at " + tick);
            }

            // The end: the result survives a restore and a finished session does not advance.
            var last = Restore(a, end);
            Assert.That((last.Match.Winner, last.Match.EndTick, last.Match.EndReason), Is.EqualTo((a.Match.Winner, a.Match.EndTick, a.Match.EndReason)));
            Assert.That(last.Session.IsFinished, Is.True);
            long before = last.Session.Tick;
            last.Session.StepOnce();
            Assert.That(last.Session.Tick, Is.EqualTo(before));
            Assert.That(last.Sim.SaveRetainedSnapshot(), Is.EqualTo(end));
        }

        // Every middle point at or after the end is skipped with its evidence (one rule, >= EndTick); a missing event is -1.
        private static string Skipped(long endTick, long firstDeploy, long firstFlip)
        {
            var skipped = new List<string>();
            var points = new (string Name, long Tick)[] { ("firstDeploy", firstDeploy), ("300", 300), ("firstFlip", firstFlip), ("600", 600) };
            foreach (var (name, tick) in points)
                if (tick < 0 || tick >= endTick) skipped.Add(name + "=" + tick);
            return skipped.Count == 0 ? "none" : string.Join(",", skipped);
        }
    }
}
