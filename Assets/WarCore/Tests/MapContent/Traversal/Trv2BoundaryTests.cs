using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using static WarCore.Tests.Trv2Maps;

namespace WarCore.Tests
{
    // TRV-2 boundaries the appendix does not list but the implementation's branches need: which cells the gap reads, the
    // inclusive and exclusive gap bounds, and a floor above zero (walls are stored relative to it).
    public sealed class Trv2BoundaryTests
    {
        [Test]
        public void EdgeGapReadsTheTargetCellToo()
        {
            // A plate over the box at exactly the 2 m stacking clearance: gap 131072 from the box top.
            MapDesc d = Make(16, 16, null, new[] { Box(1, 8, 8, M), Box(2, 8, 8, 3 * M + M / 2, M / 2) });
            Assert.That(Between(d, 0, 7, 8, 1, 8, 8, OneKind(TraversalShape.Edge, 4 * M, 4 * M, 131073)), Is.Empty);
            TraversalLink[] l = Between(d, 0, 7, 8, 1, 8, 8, OneKind(TraversalShape.Edge, 4 * M, 4 * M, 131072));
            Assert.That(l.Length, Is.EqualTo(1));
            Assert.That(l[0].Gap, Is.EqualTo(131072));
        }

        [Test]
        public void EdgeGapCountsASpanWhoseBottomIsTheTop()
        {
            // A 2 m box; over the ground cell beside it a plate whose bottom is at 2 m (the ground cell stays walkable).
            MapDesc d = Make(16, 16, null, new[] { Box(1, 8, 8, 2 * M), Box(2, 7, 8, 2 * M + M / 2, M / 2) });
            Assert.That(Between(d, 0, 7, 8, 1, 8, 8, OneKind(TraversalShape.Edge, 4 * M, 4 * M, 1)), Is.Empty);
            TraversalLink[] l = Between(d, 0, 7, 8, 1, 8, 8, OneKind(TraversalShape.Edge, 4 * M, 4 * M, 0));
            Assert.That(l.Length, Is.EqualTo(1));
            Assert.That(l[0].Gap, Is.Zero);
        }

        // F-window with the lintel at 200000 and optional extra plates.
        private static MapDesc HighWindow(params MapPlate[] more)
        {
            var plates = new System.Collections.Generic.List<MapPlate> { Box(1, 1, 1, M) };
            plates.AddRange(more);
            return Make(16, 16, null, plates.ToArray(), new[] { Wall(11, 0, 8, 8, 1, 163840), Wall(12, 9, 8, 7, 1, 163840), Wall(13, 8, 8, 1, 1, 58982), Wall(14, 8, 8, 1, 1, M / 2, 200000) });
        }

        [Test]
        public void OverGapReadsAllThreeCells()
        {
            TraversalRules r = OneKind(TraversalShape.Over, 4 * M, 4 * M, 80000);
            Assert.That(One(HighWindow(), 8, 7, 8, 9, rules: r).Gap, Is.EqualTo(200000 - 58982));
            // A ceiling at 2 m over the start or over the end cell: gap 131072 - 58982 = 72090 < 80000.
            Assert.That(Between(HighWindow(Box(2, 8, 7, 2 * M + M / 2, M / 2)), 0, 8, 7, 0, 8, 9, r), Is.Empty, "over a");
            Assert.That(Between(HighWindow(Box(2, 8, 9, 2 * M + M / 2, M / 2)), 0, 8, 7, 0, 8, 9, r), Is.Empty, "over b");
        }

        [Test]
        public void OverGapSkipsASpanWhoseBottomIsTheCrossing()
        {
            // Upstairs (top t): a 2 m sill, the lintel 1 m above it, and a ceiling over the start cell whose bottom is the sill top.
            const long t = 158598;
            MapDesc d = Make(16, 16, null, new[] { Flat(1, 2, 2, 12, 11, t, 2 * M), Box(2, 8, 7, t + 2 * M + M / 2, M / 2) },
                new[] { Wall(1, 2, 8, 6, 1, 4 * M, t), Wall(2, 9, 8, 5, 1, 4 * M, t), Wall(3, 8, 8, 1, 1, 2 * M, t), Wall(4, 8, 8, 1, 1, M / 2, t + 3 * M) });
            TraversalLink[] l = Between(d, 1, 8, 7, 1, 8, 9, OneKind(TraversalShape.Over, 4 * M, 4 * M, 1));
            Assert.That(l.Length, Is.EqualTo(1));
            Assert.That(new[] { l[0].Cross, l[0].Gap }, Is.EqualTo(new long[] { t + 2 * M, M }));
        }

        [Test]
        public void AFloorAboveZeroMeasuresTheSame()
        {
            const long f = M;
            MapDesc d = Make(16, 16, null, new[] { Box(1, 1, 1, f + M) },
                new[] { Wall(11, 0, 8, 8, 1, 163840), Wall(12, 9, 8, 7, 1, 163840), Wall(13, 8, 8, 1, 1, 58982), Wall(14, 8, 8, 1, 1, M / 2, f + 2 * M) }, floorRaw: f);
            TraversalLink l = One(d, 8, 7, 8, 9);
            Assert.That(KindOf(l), Is.EqualTo("vault"));
            Assert.That(new[] { l.Cross, l.Rise, l.Fall, l.Gap }, Is.EqualTo(new long[] { f + 58982, 58982, 58982, 72090 }));
        }
    }
}
