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

        // Behaviour baseline (not a determinism lock), re-recorded by L9 (terrain blocks sight and shots; auto fire aims in
        // 3D). Before L9 shots were horizontal from foot height and the match ended on the time limit (tick 9000, ridge 1:0);
        // now team 1 takes both points and the match ends at tick 842. The restore evidence in L6DeterminismTests is unchanged
        // (first deploy 90, capture at 300, reinforcement at 600). Columns: see L6.Line.
        private const string Golden =
            "41,842,1,1,12FF1D25B04195BA,46593F38D966A6B0,-,FA2539C41BAC3512,187,332,3,15,19,9,9,8,75,4,6,0";

        private sealed class Result
        {
            internal string Line;
            internal L6Trace Trace;
            internal int Winner, Reason, RidgeAt300;
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
                if (tick != 300) return;
                ridge = 0;
                for (ulong key = 1; key <= 20; key++)
                    if (w.Has<Position>(key) && !w.Dead(key) && w.Read<Position>(key).Value.Y.Raw >= 4 * S) ridge++;
            });
            return new Result
            {
                Line = L6.Line(w, trace), Trace = trace, Winner = w.Match.Winner, Reason = (int)w.Match.EndReason, EndTick = w.Match.EndTick,
                RidgeAt300 = ridge, Owner1 = w.Capture.Points[0].Owner, Owner2 = w.Capture.Points[1].Owner,
                Dead1 = L6.DeadOf(w, 1), Dead2 = L6.DeadOf(w, 2), Deployed1 = w.Reinforce.Successes(1), Deployed2 = w.Reinforce.Successes(2),
            };
        });

        [Test]
        public void L6Baseline_MatchesTheRecording()
        {
            TestContext.Progress.WriteLine("L6 record: " + Run.Value.Line);
            Assert.That(Run.Value.Line, Is.EqualTo(Golden));
        }

        // The name is kept (tools/mutation/lists/l6.txt refers to it); since L9 the match no longer reaches the time limit: team 1
        // holds both points at tick 842.
        [Test]
        public void L6Baseline_TimeLimitDecidesOnPoints()
        {
            Result r = Run.Value;
            Assert.That(r.Winner, Is.EqualTo(1));
            Assert.That(r.Reason, Is.EqualTo((int)MatchEndReason.AllPoints));
            Assert.That(r.EndTick, Is.EqualTo(842));
            Assert.That(r.Owner1, Is.EqualTo(1), "the ridge point");
            Assert.That(r.Owner2, Is.EqualTo(1), "the mud point");
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

        // The name is kept (l6.txt); since L9 the first flip is the mud point to team 2 (tick 332), the ridge follows at 365.
        [Test]
        public void L6Baseline_FirstFlipIsTheRidgeToTeamOne()
        {
            L6Trace t = Run.Value.Trace;
            Assert.That(t.FirstChange.HasValue, Is.True);
            CaptureRecord first = t.FirstChange.Value;
            Assert.That((first.PointId, first.OldOwner, first.NewOwner), Is.EqualTo((2, 0, 2)));
            Assert.That(first.Tick, Is.EqualTo(t.FirstFlip));
        }

        [Test]
        public void L6Baseline_TeamOneStandsOnTheRidge()
        {
            // The ridge slots lie on ground of at least 4.25 m (x = 25.5); without the height field everyone would stay at Y = 0.
            // Counted at tick 300 since L9 (10 then; by 600 the ridge is taken and team 1 has moved down the slope, 1 left).
            Assert.That(Run.Value.RidgeAt300, Is.GreaterThanOrEqualTo(5));
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
