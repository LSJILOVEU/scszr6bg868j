using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Tests
{
    // The demo terrain match on the frozen L6 fixture: one behaviour baseline line plus the semantic facts it stands for.
    public sealed class L6BaselineTests
    {
        private const long S = L6.S;

        // Behaviour baseline (not a determinism lock), recorded by L6. Known defect until L9: shots are horizontal from foot
        // height, so targets more than about 0.5 m higher or lower are not hit; the match ends on the time limit with the
        // ridge held 1:0. L9 must re-record it, the fixed values in the tests below and the restore evidence in
        // L6DeterminismTests (deploy and movement at the first deploy, capture at 300, reinforcement at 600), listing old and
        // new values in its PR; it may pick equivalent witness ticks (at least three distinct middle ticks, a point at or after
        // EndTick is skipped with its evidence). L9 also adds checks that an unobstructed target at a different height (over
        // 0.5 m) is hit and that a non-zero spread keeps its vertical component.
        // Columns: see L6.Line.
        private const string Golden =
            "41,9000,1,3,B9C49B7E14334C5A,827863B9B8BEF7C1,1B95F55457B8C6D0,00901EF824B88094,12,399,1,7,17,10,10,24005,42,202,93,0";

        private sealed class Result
        {
            internal string Line;
            internal L6Trace Trace;
            internal int Winner, Reason, RidgeAt600;
            internal long EndTick;
            internal int Owner1, Owner2, Dead1, Dead2;
            internal long Deployed1, Deployed2;
        }

        private static readonly Lazy<Result> Run = new Lazy<Result>(() =>
        {
            var w = L6.World();
            int ridge = -1;
            L6Trace trace = L6.RunToEnd(w, tick =>
            {
                if (tick != 600) return;
                ridge = 0;
                for (ulong key = 1; key <= 20; key++)
                    if (w.Has<Position>(key) && !w.Dead(key) && w.Read<Position>(key).Value.Y.Raw >= 4 * S) ridge++;
            });
            return new Result
            {
                Line = L6.Line(w, trace), Trace = trace, Winner = w.Match.Winner, Reason = (int)w.Match.EndReason, EndTick = w.Match.EndTick,
                RidgeAt600 = ridge, Owner1 = w.Capture.Points[0].Owner, Owner2 = w.Capture.Points[1].Owner,
                Dead1 = L6.DeadOf(w, 1), Dead2 = L6.DeadOf(w, 2), Deployed1 = w.Reinforce.Successes(1), Deployed2 = w.Reinforce.Successes(2),
            };
        });

        [Test]
        public void L6Baseline_MatchesTheRecording()
        {
            TestContext.Progress.WriteLine("L6 record: " + Run.Value.Line);
            Assert.That(Run.Value.Line, Is.EqualTo(Golden));
        }

        [Test]
        public void L6Baseline_TimeLimitDecidesOnPoints()
        {
            Result r = Run.Value;
            Assert.That(r.Winner, Is.EqualTo(1));
            Assert.That(r.Reason, Is.EqualTo((int)MatchEndReason.Time));
            Assert.That(r.EndTick, Is.EqualTo(9000));
            Assert.That(r.Owner1, Is.EqualTo(1), "the ridge point");
            Assert.That(r.Owner2, Is.EqualTo(0), "the mud point");
        }

        [Test]
        public void L6Baseline_BothTeamsHitDieAndReinforce()
        {
            Result r = Run.Value;
            Assert.That(r.Trace.TeamHit[1], Is.True, "a team 1 hit");
            Assert.That(r.Trace.TeamHit[2], Is.True, "a team 2 hit");
            Assert.That(r.Dead1, Is.GreaterThanOrEqualTo(1));
            Assert.That(r.Dead2, Is.GreaterThanOrEqualTo(1));
            Assert.That(r.Deployed1, Is.GreaterThanOrEqualTo(1));
            Assert.That(r.Deployed2, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void L6Baseline_FirstFlipIsTheRidgeToTeamOne()
        {
            L6Trace t = Run.Value.Trace;
            Assert.That(t.FirstChange.HasValue, Is.True);
            CaptureRecord first = t.FirstChange.Value;
            Assert.That((first.PointId, first.OldOwner, first.NewOwner), Is.EqualTo((1, 0, 1)));
            Assert.That(first.Tick, Is.EqualTo(t.FirstFlip));
        }

        [Test]
        public void L6Baseline_TeamOneStandsOnTheRidge()
        {
            // The ridge slots lie on ground of at least 4.25 m (x = 25.5); without the height field everyone would stay at Y = 0.
            Assert.That(Run.Value.RidgeAt600, Is.GreaterThanOrEqualTo(5));
        }

        [Test]
        public void L6Baseline_EventAnchorsExist()
        {
            Result r = Run.Value;
            Assert.That(r.Trace.FirstFire, Is.GreaterThanOrEqualTo(1), "first shot");
            Assert.That(r.Trace.FirstFire + 90, Is.LessThan(r.EndTick), "three seconds after the first shot");
            Assert.That(r.Trace.FirstFlip, Is.GreaterThanOrEqualTo(1), "first change of owner");
            Assert.That(r.Trace.FirstFlip, Is.LessThan(r.EndTick));
        }
    }
}
