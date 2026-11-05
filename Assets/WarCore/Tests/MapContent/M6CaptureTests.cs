using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class M6CaptureTests
    {
        [TestCase(1, 0, false, true)]
        [TestCase(3, 1, false, true)]
        [TestCase(1, 1, false, false)]
        [TestCase(2, 1, false, true)]
        [TestCase(2, 1, true, false)]
        public void D1_D2_PLACEHOLDER_RatioAndExactCaptureTick(int one, int two, bool strict, bool expected)
        {
            var units = new UnitSpawn[one + two + 3];
            for (int i = 0; i < one + two; i++) units[i] = M6World.U((ulong)i + 1, i < one ? 1 : 2, 20, 18 + i);
            units[one + two] = M6World.U(100, 2, 27); // outside radius
            units[one + two + 1] = M6World.U(101, 2, reserve: true);
            units[one + two + 2] = M6World.U(102, 0);
            var w = new M6World(M6World.RecipeOf(units, M6World.Pack(strict: strict)));
            w.Step(); Assert.That(w.Capture.Points[0].Progress, Is.EqualTo(expected ? 1 : 0));
            w.Step(); Assert.That(w.Capture.Points[0].Owner, Is.Zero);
            w.Step(); Assert.That(w.Capture.Points[0].Owner, Is.EqualTo(expected ? 1 : 0));
            Assert.That(w.Capture.Points[0].Progress, Is.Zero);
            Assert.That(w.Capture.Changes.Length, Is.EqualTo(expected ? 1 : 0));
            if (expected) Assert.That(w.Capture.Changes[0].Tick, Is.EqualTo(3));
        }
        [Test]
        public void D1_PLACEHOLDER_EqualCountsNeverLeadEvenOneToOne_DeadAndReserveExcluded_BoundaryIncluded()
        {
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, 26), M6World.U(2, 2), M6World.U(3, 2, reserve: true) }, M6World.Pack(num: 1)));
            w.Sim.RegisterGameplay("test.death", new M6Damage(w, 1, 2), SimSystemPhase.Queries);
            w.Step(); Assert.That(w.Capture.Points[0].Progress, Is.Zero); Assert.That(w.Dead(2), Is.True);
            w.Step(); Assert.That(w.Capture.Points[0].Progress, Is.EqualTo(1));
            w.Step(2); Assert.That(w.Capture.Points[0].Owner, Is.EqualTo(1));
            Assert.That(w.Capture.Changes[0].Tick, Is.EqualTo(4));
        }
        [TestCase(InterruptMode.Reset, 0)]
        [TestCase(InterruptMode.Pause, 2)]
        [TestCase(InterruptMode.Decay, 1)]
        public void D3_PLACEHOLDER_InterruptModesAndChangedLeader(InterruptMode mode, int progress)
        {
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1), M6World.U(2, 2, 60) }, M6World.Pack(ticks: 10, mode: mode)));
            w.Step(2); Assert.That(w.Capture.Points[0].Progress, Is.EqualTo(2));
            w.Set(1, new Position(M6World.P(50))); w.Step();
            Assert.That(w.Capture.Points[0].Progress, Is.EqualTo(progress));
            w.Set(2, new Position(M6World.P(20))); w.Step();
            Assert.That(w.Capture.Points[0].Capturing, Is.EqualTo(2)); Assert.That(w.Capture.Points[0].Progress, Is.EqualTo(1));
            w.Set(2, new Position(M6World.P(60))); w.Step(2);
            if (mode != InterruptMode.Pause) Assert.That(w.Capture.Points[0].Capturing, Is.Zero);
        }
        [Test]
        public void D4_PLACEHOLDER_RecaptureEventsAreOrderedExactAndEphemeral()
        {
            var points = new[] { new ControlPointDef(8, M6World.P(20), SimFix.FromInt(6)), new ControlPointDef(3, M6World.P(20), SimFix.FromInt(6)) };
            var w = new M6World(M6World.Endless(M6World.RecipeOf(new[] { M6World.U(1, 1), M6World.U(2, 2, 60) }, M6World.Pack(points))));
            for (int turn = 0; turn < 3; turn++)
            {
                int team = turn % 2 + 1;
                w.Set(1, new Position(M6World.P(team == 1 ? 20 : 60)));
                w.Set(2, new Position(M6World.P(team == 2 ? 20 : 60)));
                w.Step(3);
                Assert.That(w.Capture.Changes.Length, Is.EqualTo(2));
                for (int i = 0; i < 2; i++)
                {
                    var change = w.Capture.Changes[i];
                    Assert.That(change.PointId, Is.EqualTo(points[i].Id)); Assert.That(change.Tick, Is.EqualTo((turn + 1) * 3));
                    Assert.That(change.NewOwner, Is.EqualTo(team)); Assert.That(change.OldOwner, Is.EqualTo(turn == 0 ? 0 : 3 - team));
                    Assert.That(w.Capture.Points[i].Owner, Is.EqualTo(team)); Assert.That(w.Capture.Points[i].Progress, Is.Zero);
                }
            }
            w.Step(); Assert.That(w.Capture.Changes.Length, Is.Zero);
        }
        [Test]
        public void D2_D6_PLACEHOLDER_CaptureAndDeadlineObservePreSettleLife()
        {
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1), M6World.U(2, 2, 60) }));
            w.Sim.RegisterGameplay("test.death", new M6Damage(w, 3, 1), SimSystemPhase.Queries);
            w.Step(3); Assert.That(w.Dead(1), Is.True); Assert.That(w.Capture.Points[0].Owner, Is.EqualTo(1));
            Assert.That(w.Match.Winner, Is.EqualTo(1)); Assert.That(w.Match.EndReason, Is.EqualTo(MatchEndReason.AllPoints));
            var deadline = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, 5), M6World.U(2, 1, 7), M6World.U(3, 2, 70) }, M6World.Pack(max: 2)));
            deadline.Sim.RegisterGameplay("test.death", new M6Damage(deadline, 2, 1, 2, 3), SimSystemPhase.Queries);
            deadline.Step(2); Assert.That(deadline.Match.Winner, Is.EqualTo(1)); Assert.That(deadline.Match.EndReason, Is.EqualTo(MatchEndReason.Time));
            Assert.That(deadline.Dead(1) && deadline.Dead(2) && deadline.Dead(3), Is.True);
            // After the end Sim refuses another step; the result it latched is unchanged.
            Assert.Throws<InvalidOperationException>(() => deadline.Step());
            Assert.That(deadline.Match.Winner, Is.EqualTo(1)); Assert.That(deadline.Match.EndTick, Is.EqualTo(2)); Assert.That(deadline.Sim.Clock.Tick, Is.EqualTo(2));
        }
    }
}
