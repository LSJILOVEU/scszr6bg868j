using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using static WarCore.Tests.Trv2Maps;

namespace WarCore.Tests
{
    // TRV-2 edge links (K15 edge shape): T2-04..11, T2-33..36 and the tick rounding. Expected values are worked out by
    // hand from the fixture geometry; kind bounds are the trv-test layer's (read by the pack reader).
    public sealed class Trv2EdgeTests
    {
        [Test]
        public void T2_04_GroundToBoxIsOneClimb()
        {
            TraversalLink l = One(Box1(), 7, 8, 8, 8);
            Assert.That(KindOf(l), Is.EqualTo("climb"));
            Assert.That(l.Shape, Is.EqualTo(TraversalShape.Edge));
            Assert.That(new[] { l.Rise, l.Fall, l.Gap, l.Cross }, Is.EqualTo(new[] { 65536, 0, LinkDerive.NoGap, 65536 }));
            Assert.That(l.Ticks, Is.EqualTo(30));
        }

        [Test]
        public void T2_05_BoxToGroundIsOneDrop()
        {
            TraversalLink l = One(Box1(), 8, 8, 7, 8);
            Assert.That(KindOf(l), Is.EqualTo("drop"));
            Assert.That(new[] { l.Rise, l.Fall }, Is.EqualTo(new long[] { 0, 65536 }));
            Assert.That(l.Ticks, Is.EqualTo(12));
        }

        [Test]
        public void T2_06_StepRiseMaxIsInclusiveAndTableOrderWins()
        {
            // 0.6 m sits on both step (riseMax) and climb (riseMin): the first row wins.
            Assert.That(KindOf(One(Box1(39321), 7, 8, 8, 8)), Is.EqualTo("step"));
            Assert.That(KindOf(One(Box1(39322), 7, 8, 8, 8)), Is.EqualTo("climb"));
        }

        [Test]
        public void T2_07_ASeamIsNotALink()
        {
            None(Box1(19660), 7, 8, 8, 8);
            None(Box1(19660), 8, 8, 7, 8);
            Assert.That(KindOf(One(Box1(19661), 7, 8, 8, 8)), Is.EqualTo("step"));
            Assert.That(KindOf(One(Box1(19661), 8, 8, 7, 8)), Is.EqualTo("step"));
        }

        [Test]
        public void T2_08_ClimbRiseMaxIsInclusive()
        {
            Assert.That(KindOf(One(Box1(78643), 7, 8, 8, 8)), Is.EqualTo("climb"));
            None(Box1(78644), 7, 8, 8, 8);
            TraversalLink down = One(Box1(78644), 8, 8, 7, 8);
            Assert.That(KindOf(down), Is.EqualTo("drop"));
            Assert.That(down.Fall, Is.EqualTo(78644));
        }

        [Test]
        public void T2_09_DropFallMaxIsInclusive()
        {
            Assert.That(KindOf(One(Box1(163840), 8, 8, 7, 8)), Is.EqualTo("drop"));
            None(Box1(163841), 8, 8, 7, 8);
        }

        // A 1 x 1 plate sloping along z next to flat ground: the shared edge x = 8 has end points (8, 8) and (8, 9).
        private static MapDesc Ramp(long fromRaw, long toRaw) => Make(16, 16, null, new[] { Slope(1, 8, 8, 1, 1, "z", fromRaw, toRaw) });

        [Test]
        public void T2_10_RiseAndFallTakeTheLargerEndPoint()
        {
            // 20316 and 45875: max 45875 is a climb; the smaller end, the first end or the mean (33095) would be a step.
            TraversalLink up = One(Ramp(20316, 45875), 7, 8, 8, 8);
            Assert.That(KindOf(up), Is.EqualTo("climb"));
            Assert.That(new[] { up.Rise, up.Fall }, Is.EqualTo(new long[] { 45875, 0 }));
            TraversalLink down = One(Ramp(20316, 45875), 8, 8, 7, 8);
            Assert.That(KindOf(down), Is.EqualTo("drop"));
            Assert.That(new[] { down.Rise, down.Fall }, Is.EqualTo(new long[] { 0, 45875 }));
            // One end above climb's riseMax: no kind takes it.
            None(Ramp(20316, 85197), 7, 8, 8, 8);
        }

        // F-box with a thin plate over the ground cell (7, 8) whose bottom is the given height above the box top.
        private static MapDesc Ceiling(long gapRaw) => Make(16, 16, null, new[] { Box(1, 8, 8, M), Box(2, 7, 8, M + gapRaw + M / 2, M / 2) });

        [Test]
        public void T2_11_HeadroomAboveTheLinkIsMeasured()
        {
            // From the ground (layer 0) to the box (layer 1); the thin plate is layer 2.
            Assert.That(Between(Ceiling(124518), 0, 7, 8, 1, 8, 8), Is.Empty);
            TraversalLink[] l = Between(Ceiling(131072), 0, 7, 8, 1, 8, 8);
            Assert.That(l.Length, Is.EqualTo(1));
            Assert.That(KindOf(l[0]), Is.EqualTo("climb"));
            Assert.That(l[0].Gap, Is.EqualTo(131072));
        }

        [Test]
        public void T2_33_DropFallMinIsInclusive()
        {
            Assert.That(KindOf(One(Box1(39322), 8, 8, 7, 8)), Is.EqualTo("drop"));
            Assert.That(KindOf(One(Box1(39321), 8, 8, 7, 8)), Is.EqualTo("step"));
        }

        // Plate 1 flat at (7, 8), plate 2 at (8, 8) sloping along z: end point (8, 8) at from, (8, 9) at to.
        private static MapDesc TwoPlates(long flatRaw, long fromRaw, long toRaw) =>
            Make(16, 16, null, new[] { Box(1, 7, 8, flatRaw), Slope(2, 8, 8, 1, 1, "z", fromRaw, toRaw) });

        [Test]
        public void T2_34_ClimbFallMaxIsInclusive()
        {
            TraversalLink l = One(TwoPlates(M, 2 * M, M - 19660), 7, 8, 8, 8);
            Assert.That(KindOf(l), Is.EqualTo("climb"));
            Assert.That(new[] { l.Rise, l.Fall }, Is.EqualTo(new long[] { 65536, 19660 }));
            None(TwoPlates(M, 2 * M, M - 19661), 7, 8, 8, 8);
        }

        [Test]
        public void T2_35_DropRiseMaxIsInclusive()
        {
            TraversalLink l = One(TwoPlates(98304, 32768, 98304 + 19660), 7, 8, 8, 8);
            Assert.That(KindOf(l), Is.EqualTo("drop"));
            Assert.That(new[] { l.Rise, l.Fall }, Is.EqualTo(new long[] { 19660, 65536 }));
            None(TwoPlates(98304, 32768, 98304 + 19661), 7, 8, 8, 8);
        }

        [Test]
        public void T2_36_OnlyDownIsAStepWithRiseZero()
        {
            TraversalLink l = One(Box1(29491), 8, 8, 7, 8);
            Assert.That(KindOf(l), Is.EqualTo("step"));
            Assert.That(new[] { l.Rise, l.Fall }, Is.EqualTo(new long[] { 0, 29491 }));
            Assert.That(l.Ticks, Is.EqualTo(8));
        }

        [Test]
        public void TicksRoundUpPerMetreTerm()
        {
            TraversalRules r = OneKind(TraversalShape.Edge, 4 * M, 4 * M, 0, 1, 10, 3);
            Assert.That(One(Box1(65537), 7, 8, 8, 8, rules: r).Ticks, Is.EqualTo(1 + 11), "1 + ceil(10 x 65537 / 65536)");
            Assert.That(One(Box1(65537), 8, 8, 7, 8, rules: r).Ticks, Is.EqualTo(1 + 4), "1 + ceil(3 x 65537 / 65536)");
            Assert.That(One(Box1(65536), 7, 8, 8, 8, rules: r).Ticks, Is.EqualTo(1 + 10), "exact metres add nothing");
            Assert.That(One(Box1(65536), 8, 8, 7, 8, rules: r).Ticks, Is.EqualTo(1 + 3));
        }

        [Test]
        public void AnEdgeOnlyKindMakesNoOverLinksAndTheReverse()
        {
            Assert.That(Derive(Box1(), rules: OneKind(TraversalShape.Edge, 4 * M, 4 * M)), Has.All.Matches<TraversalLink>(l => l.Shape == TraversalShape.Edge));
            Assert.That(Derive(Box1(), rules: OneKind(TraversalShape.Over, 4 * M, 4 * M)), Has.All.Matches<TraversalLink>(l => l.Shape == TraversalShape.Over));
        }
    }
}
