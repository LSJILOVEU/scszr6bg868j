using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class AiPlaytestMatchTests
    {
        private static M6World World() => new M6World(ContentLayers.Terrain("rwr-ai-playtest"), TerrainDemo.MapBytes());

        [Test, Explicit("The playtest match ends before 3000 ticks."), Category("Manual")]
        public void FullMatch_TwoTeamsFightCaptureAndRepeatEveryHash()
        {
            M6World a = World(), b = World();
            L6Trace trace = L6.RunToEnd(a, tick =>
            {
                b.Step();
                Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + tick);
            });
            TestContext.Progress.WriteLine("AI playtest: " + L6.Line(a, trace));
            Assert.That(a.Session.IsFinished, Is.True);
            Assert.That(trace.TeamHit[1], Is.True, "team 1 hits an enemy");
            Assert.That(trace.TeamHit[2], Is.True, "team 2 hits an enemy");
            Assert.That(L6.DeadOf(a, 1) + L6.DeadOf(a, 2), Is.GreaterThan(0));
            Assert.That(trace.Flips, Is.GreaterThan(0));
            Assert.That(trace.FirstChange.Value.NewOwner, Is.Not.EqualTo(trace.FirstChange.Value.OldOwner));
            Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(a.Sim.SaveRetainedSnapshot()));
            Assert.That(a.Match.EndTick, Is.GreaterThanOrEqualTo(3000));
        }

        [Test, Explicit("The playtest match ends before the third save point."), Category("Manual")]
        public void ThreeMidMatchRestores_RepeatEveryRemainingHash()
        {
            M6World uninterrupted = World();
            var restored = new List<M6World>();
            while (!uninterrupted.Session.IsFinished && uninterrupted.Sim.Clock.Tick < uninterrupted.Recipe.Match.MaxTicks)
            {
                uninterrupted.Step();
                long tick = uninterrupted.Sim.Clock.Tick;
                foreach (M6World copy in restored)
                {
                    copy.Step();
                    Assert.That(copy.Hash, Is.EqualTo(uninterrupted.Hash), "tick " + tick);
                }
                if (tick == 300 || tick == 600 || tick == 900)
                {
                    Assert.That(uninterrupted.Session.IsFinished, Is.False, "save before match end");
                    byte[] snapshot = uninterrupted.Sim.SaveRetainedSnapshot();
                    M6World copy = World();
                    copy.Sim.RestoreRetainedSnapshot(snapshot);
                    Assert.That(copy.Hash, Is.EqualTo(uninterrupted.Hash), "restore tick " + tick);
                    restored.Add(copy);
                }
            }
            TestContext.Progress.WriteLine("AI playtest restores: count=" + restored.Count + "; endTick=" + uninterrupted.Match.EndTick);
            Assert.That(uninterrupted.Session.IsFinished, Is.True);
            foreach (M6World copy in restored)
                Assert.That(copy.Sim.SaveRetainedSnapshot(), Is.EqualTo(uninterrupted.Sim.SaveRetainedSnapshot()));
            Assert.That(restored.Count, Is.EqualTo(3));
            Assert.That(uninterrupted.Match.EndTick, Is.GreaterThanOrEqualTo(3000));
        }
    }
}
