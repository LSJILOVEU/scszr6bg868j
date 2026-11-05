using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class P0ShotPlanTests
    {
        private static WorldRecipe Recipe(ulong seed)
        {
            WorldRecipe d = DemoRecipes.M7();
            return new WorldRecipe(seed, d.Mover, d.Units.ToArray(), d.Script.ToArray()).WithCombat(d.Combat).WithMatch(d.Match).WithCrowdPass(d.CrowdPass);
        }

        // Runs a whole match; the expected event ticks are recomputed here from the frames, not read from ShotPlan.
        [TestCase(41UL)]
        [TestCase(42UL)]
        public void EightAnchorsFireOnceEachOnAWholeM7Match(ulong seed)
        {
            SimSession session = SimSession.Create(Recipe(seed), DemoRecipes.M7MapBytes());
            var plan = new ShotPlan();
            var batch = new List<ShotAnchor>();
            var got = new List<ShotAnchor>();
            long firstFire = -1, firstFlip = -1, end = -1;
            FrameView previous = session.Current;
            plan.Observe(session.Current, batch); got.AddRange(batch);
            while (session.Winner == 0 && session.Tick < 9000)
            {
                session.StepOnce();
                FrameView frame = session.Current;
                if (firstFire < 0 && frame.Fires.Length > 0) firstFire = frame.Tick;
                if (firstFlip < 0 && Enumerable.Range(0, frame.Points.Length).Any(i => frame.Points[i].Owner != previous.Points[i].Owner)) firstFlip = frame.Tick;
                previous = frame;
                plan.Observe(frame, batch); got.AddRange(batch);
            }
            end = session.Tick;
            Assert.That(session.Winner, Is.Not.Zero, "match must finish for the end anchor");
            Assert.That(firstFire, Is.GreaterThan(0)); Assert.That(firstFlip, Is.GreaterThan(0));
            Assert.That(end, Is.GreaterThan(600), "the 4 fixed ticks must all be inside the match");

            var expected = new List<(ShotKind, long)>
            {
                (ShotKind.Tick, 0), (ShotKind.Tick, 150), (ShotKind.Tick, 300), (ShotKind.Tick, 600),
                (ShotKind.FirstFire, firstFire), (ShotKind.FireAfter3s, firstFire + 90),
                (ShotKind.FirstFlip, firstFlip), (ShotKind.End, end),
            };
            Assert.That(firstFire + 90, Is.LessThan(end), "the +3 s anchor must be inside the match");
            Assert.That(got.Select(a => (a.Kind, a.Tick)).OrderBy(a => a.Tick).ThenBy(a => a.Kind).ToArray(),
                Is.EqualTo(expected.OrderBy(a => a.Item2).ThenBy(a => a.Item1).ToArray()));
            Assert.That(got.Count, Is.EqualTo(8));
            // Event pictures look at the event: the first shot's midpoint, the flipped point's centre.
            ShotAnchor fire = got.Single(a => a.Kind == ShotKind.FirstFire), flip = got.Single(a => a.Kind == ShotKind.FirstFlip);
            Assert.That(fire.HasFocus && flip.HasFocus, Is.True);
        }

        [Test]
        public void AnchorsThatAreNeverReachedDoNotFire()
        {
            SimSession session = SimSession.Create(Recipe(41), DemoRecipes.M7MapBytes());
            var plan = new ShotPlan();
            var batch = new List<ShotAnchor>();
            plan.Observe(session.Current, batch);
            Assert.That(batch.Select(a => a.Kind), Is.EqualTo(new[] { ShotKind.Tick }));
            session.StepOnce();
            plan.Observe(session.Current, batch);
            Assert.That(batch, Is.Empty);
        }

        [Test]
        public void FileNamesFollowTheEarlierShotStyle()
        {
            Assert.That(new ShotAnchor(ShotKind.Tick, 150).FileName("P0"), Is.EqualTo("P0-t0150.png"));
            Assert.That(new ShotAnchor(ShotKind.FirstFire, 321).FileName("P0"), Is.EqualTo("P0-first-fire-t0321.png"));
            Assert.That(new ShotAnchor(ShotKind.FireAfter3s, 411).FileName("P0"), Is.EqualTo("P0-fire3s-t0411.png"));
            Assert.That(new ShotAnchor(ShotKind.FirstFlip, 77).FileName("P0"), Is.EqualTo("P0-first-flip-t0077.png"));
            Assert.That(new ShotAnchor(ShotKind.End, 2062).FileName("P0"), Is.EqualTo("P0-end-t2062.png"));
        }

        private static FrameView Frame(long tick, int owner = 0, ViewFire[] fires = null, int winner = 0) => new FrameView(tick, new ViewEntity[0],
            fires ?? new ViewFire[0], new[] { new ViewPoint(1, 40f, 50f, 6f, owner, 0, 0f) }, new ViewMatch(winner, 0, 0, 1, 1, 0, 0));

        [Test]
        public void SeveralAnchorsOnOneTickAllFireWithDistinctFileNamesAndTheirOwnFocus()
        {
            var plan = new ShotPlan();
            var anchors = new List<ShotAnchor>();
            plan.Observe(Frame(0), anchors);
            plan.Observe(Frame(600, owner: 1, fires: new[] { new ViewFire(10f, 20f, 14f, 30f, 1, false) }), anchors);
            Assert.That(anchors.Select(a => a.Kind), Is.EquivalentTo(new[] { ShotKind.Tick, ShotKind.FirstFire, ShotKind.FirstFlip }));
            Assert.That(anchors.Select(a => a.FileName("P0")).Distinct().Count(), Is.EqualTo(3));
            ShotAnchor fire = anchors.Single(a => a.Kind == ShotKind.FirstFire);
            Assert.That((fire.FocusX, fire.FocusZ), Is.EqualTo((12f, 25f)));
            ShotAnchor flip = anchors.Single(a => a.Kind == ShotKind.FirstFlip);
            Assert.That((flip.FocusX, flip.FocusZ), Is.EqualTo((40f, 50f)));
            Assert.That(anchors.Single(a => a.Kind == ShotKind.Tick).HasFocus, Is.False);
        }

        [Test]
        public void AMatchThatEndsEarlyFiresEndOnlyAndNeverTheLaterAnchors()
        {
            var plan = new ShotPlan();
            var anchors = new List<ShotAnchor>();
            plan.Observe(Frame(0), anchors);
            plan.Observe(Frame(100, winner: 1), anchors);
            Assert.That(anchors.Select(a => a.Kind), Is.EqualTo(new[] { ShotKind.End }));
            plan.Observe(Frame(101, winner: 1), anchors);
            Assert.That(anchors, Is.Empty);
        }

        [Test]
        public void OfferingTheSameTickTwiceFiresNothingTheSecondTime()
        {
            var plan = new ShotPlan();
            var anchors = new List<ShotAnchor>();
            plan.Observe(Frame(150), anchors);
            Assert.That(anchors.Count, Is.EqualTo(1));
            plan.Observe(Frame(150), anchors);
            Assert.That(anchors, Is.Empty);
        }

        [Test]
        public void ThreeSecondsAfterTheFirstShotTheFocusIsTheShotOnThatTickIfThereIsOne()
        {
            var plan = new ShotPlan();
            var anchors = new List<ShotAnchor>();
            var shot = new[] { new ViewFire(0f, 0f, 8f, 4f, 2, true) };
            plan.Observe(Frame(10, fires: shot), anchors);
            plan.Observe(Frame(10 + ShotPlan.AfterFireTicks), anchors);
            Assert.That(anchors.Single().Kind, Is.EqualTo(ShotKind.FireAfter3s));
            Assert.That(anchors.Single().HasFocus, Is.False);
            var again = new ShotPlan();
            again.Observe(Frame(10, fires: shot), anchors);
            again.Observe(Frame(10 + ShotPlan.AfterFireTicks, fires: shot), anchors);
            Assert.That((anchors.Single().FocusX, anchors.Single().FocusZ), Is.EqualTo((4f, 2f)));
        }
    }
}
