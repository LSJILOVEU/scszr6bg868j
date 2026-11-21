using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;
using static WarCore.Tests.Trv2Maps;

namespace WarCore.Tests
{
    // TRV-2 over links (K15 over shape): T2-12..20, T2-22, T2-29..32, T2-37..40 and the lo/hi rule on sloped ground.
    public sealed class Trv2OverTests
    {
        private static readonly TraversalRules Wide = OneKind(TraversalShape.Over, HeightField.HeightLimitRaw, HeightField.HeightLimitRaw);

        [Test]
        public void T2_12_TheWindowIsOneVault()
        {
            TraversalLink l = One(Window(), 8, 7, 8, 9);
            Assert.That(KindOf(l), Is.EqualTo("vault"));
            Assert.That(l.Shape, Is.EqualTo(TraversalShape.Over));
            Assert.That(new[] { l.Cross, l.Rise, l.Fall, l.Gap }, Is.EqualTo(new long[] { 58982, 58982, 58982, 72090 }));
            Assert.That(l.Ticks, Is.EqualTo(24));
            Assert.That(KindOf(One(Window(), 8, 9, 8, 7)), Is.EqualTo("vault"));
        }

        [Test]
        public void T2_13_GapMinIsInclusive()
        {
            None(Window(lintelBaseRaw: 58982 + 65535), 8, 7, 8, 9);
            Assert.That(One(Window(lintelBaseRaw: 58982 + 65536), 8, 7, 8, 9).Gap, Is.EqualTo(65536));
        }

        [Test]
        public void T2_14_VaultRiseMaxIsInclusive()
        {
            // Wall heights are even, so the odd sill tops are a floor wall [0, 78642] capped by a 2 raw raised wall.
            TraversalLink l = One(Window(78642, 150000, Wall(5, 8, 8, 1, 1, 2, 78641)), 8, 7, 8, 9);
            Assert.That(new[] { l.Cross, l.Rise }, Is.EqualTo(new long[] { 78643, 78643 }));
            Assert.That(KindOf(l), Is.EqualTo("vault"));
            None(Window(78642, 150000, Wall(5, 8, 8, 1, 1, 2, 78642)), 8, 7, 8, 9);
        }

        [Test]
        public void T2_15_ALowWallIsVaultedAlongItsLengthOnly()
        {
            WcMap map = Bake(LowWall());
            LayeredNav nav = Nav(map);
            TraversalLink[] over = LinkDerive.Derive(map, nav, Rules, null).Where(l => l.Shape == TraversalShape.Over && nav.XOf(l.From) != 1 && nav.ZOf(l.From) != 1
                && nav.XOf(l.To) != 1 && nav.ZOf(l.To) != 1).ToArray();
            Assert.That(over.Length, Is.EqualTo(16));
            foreach (TraversalLink l in over)
            {
                Assert.That(KindOf(l), Is.EqualTo("vault"));
                Assert.That(nav.XOf(l.From), Is.EqualTo(nav.XOf(l.To)), "orthogonal, never diagonal");
                Assert.That(nav.XOf(l.From), Is.InRange(4, 11));
                Assert.That(new[] { nav.ZOf(l.From), nav.ZOf(l.To) }, Is.EquivalentTo(new[] { 7, 9 }));
                Assert.That(new[] { l.Rise, l.Fall, l.Gap }, Is.EqualTo(new[] { 65536, 65536, LinkDerive.NoGap }));
            }
            None(LowWall(), 3, 7, 4, 9);
            None(LowWall(), 3, 8, 5, 8);
        }

        [Test]
        public void T2_16_ATwoCellWallIsNotVaulted()
        {
            WcMap map = Bake(LowWall(2));
            LayeredNav nav = Nav(map);
            Assert.That(LinkDerive.Derive(map, nav, Rules, null).Count(l => nav.ZOf(l.From) >= 7 && nav.ZOf(l.From) <= 10), Is.Zero);
        }

        [Test]
        public void T2_17_FromABoxOverATwoMetreWallOneWayOnly()
        {
            TraversalLink l = One(BoxWall(), 8, 7, 8, 9);
            Assert.That(KindOf(l), Is.EqualTo("vault"));
            Assert.That(new[] { l.Cross, l.Rise, l.Fall }, Is.EqualTo(new long[] { 131072, 65536, 131072 }));
            None(BoxWall(), 8, 9, 8, 7);
            None(BoxWall(), 8, 7, 8, 8);
        }

        // A 1.5 m floor plate on z 0..7, a 2.5 m wall along z = 8, ground beyond; cap raises the window cell's wall by 1 raw.
        private static MapDesc Terrace(bool cap) => Make(16, 16, null, new[] { Flat(1, 0, 0, 16, 8, 98304) },
            cap ? new[] { Wall(1, 0, 8, 16, 1, 163840), Wall(2, 8, 8, 1, 1, 2, 163839) } : new[] { Wall(1, 0, 8, 16, 1, 163840) });

        [Test]
        public void T2_18_VaultFallMaxIsInclusive()
        {
            TraversalLink l = One(Terrace(false), 8, 7, 8, 9);
            Assert.That(new[] { l.Rise, l.Fall }, Is.EqualTo(new long[] { 65536, 163840 }));
            Assert.That(KindOf(l), Is.EqualTo("vault"));
            None(Terrace(true), 8, 7, 8, 9);
            Assert.That(One(Terrace(true), 8, 7, 8, 9, rules: Wide).Fall, Is.EqualTo(163841));
        }

        [Test]
        public void T2_19_TheCrossingHeightClimbsAStackOfSpans()
        {
            // The upper piece comes first in the description; the sill under it brings c up to its bottom.
            TraversalLink l = One(Window(58982, 150000, Wall(5, 8, 8, 1, 1, 12000, 58982)), 8, 7, 8, 9);
            Assert.That(new[] { l.Cross, l.Rise, l.Fall, l.Gap }, Is.EqualTo(new long[] { 70982, 70982, 70982, 150000 - 70982 }));
            Assert.That(KindOf(l), Is.EqualTo("vault"));
        }

        [Test]
        public void T2_20_AWalkableMiddleCellIsNotVaulted()
        {
            // A doorway: no sill, the lintel at 2 m.
            MapDesc door = Make(16, 16, null, new[] { Box(1, 1, 1, M) },
                new[] { Wall(11, 0, 8, 8, 1, 163840), Wall(12, 9, 8, 7, 1, 163840), Wall(14, 8, 8, 1, 1, M / 2, 2 * M) });
            None(door, 8, 7, 8, 9);
            // The middle cell walkable on one end's layer only (ground), with a raised wall across c0 on the other's (a 2.5 m
            // plate at (8, 9)): no link in either direction.
            MapDesc split = Make(16, 16, null, new[] { Box(1, 8, 9, 163840) }, new[] { Wall(1, 8, 8, 1, 1, 52428, 144180) });
            WcMap map = Bake(split);
            LayeredNav nav = Nav(map);
            Assert.That(nav.IsPassable(Node(nav, 0, 8, 8)), Is.True, "the middle cell is walkable on the ground");
            None(split, 8, 7, 8, 9, rules: Wide);
            None(split, 8, 9, 8, 7, rules: Wide);
        }

        // A second floor (top 158598, 2 m thick so the ground under it is closed) with a wall along z = 8 and a window at (8, 8).
        private static MapDesc Upstairs(int lastRow)
        {
            const long t = 158598;
            return Make(16, 16, null, new[] { Flat(1, 2, 2, 12, lastRow - 1, t, 2 * M) },
                new[] { Wall(1, 2, 8, 6, 1, 163840, t), Wall(2, 9, 8, 5, 1, 163840, t), Wall(3, 8, 8, 1, 1, 58982, t), Wall(4, 8, 8, 1, 1, M / 2, t + 2 * M) });
        }

        [Test]
        public void T2_22_AnUpstairsWindowInsideIsVaultedButNotDownToTheStreet()
        {
            TraversalLink l = One(Upstairs(12), 8, 7, 8, 9);
            Assert.That(KindOf(l), Is.EqualTo("vault"));
            Assert.That(new[] { l.Cross, l.Rise, l.Fall, l.Gap }, Is.EqualTo(new long[] { 158598 + 58982, 58982, 58982, 72090 }));
            None(Upstairs(8), 8, 7, 8, 9);
            Assert.That(One(Upstairs(8), 8, 7, 8, 9, rules: Wide).Fall, Is.EqualTo(158598 + 58982), "it is the fall that rules it out");
        }

        [Test]
        public void T2_29_ASteepGroundCellIsNotAWall()
        {
            const long h = 32768;
            MapDesc d = Make(16, 16, Heights(16, 16, (i, j) => (i == 8 && j == 9) || (i == 9 && j == 8) ? h : 0), new[] { Box(1, 1, 1, M) });
            WcMap map = Bake(d);
            LayeredNav nav = Nav(map, Steep);
            Assert.That(nav.IsPassable(Node(nav, 0, 7, 8)) && nav.IsPassable(Node(nav, 0, 9, 8)), Is.True, "both ends walkable at 1/2");
            Assert.That(nav.IsPassable(Node(nav, 0, 8, 8)), Is.False, "the middle cell is too steep");
            None(d, 7, 8, 9, 8, Steep);
            None(d, 9, 8, 7, 8, Steep);
        }

        [Test]
        public void T2_30_AStairwellHoleIsNotAWall()
        {
            MapDesc d = Make(16, 16, null, new[] { Flat(1, 4, 4, 9, 9, 2 * M, M / 2) }, cutouts: new[] { Ml4Maps.Cut(1, 1, 8, 8, 1, 1) });
            None(d, 7, 8, 9, 8);
            None(d, 9, 8, 7, 8);
        }

        [Test]
        public void T2_31_AirBetweenTwoBalconiesIsNotAWall()
        {
            MapDesc d = Make(16, 16, null, new[] { Flat(1, 4, 4, 4, 9, 3 * M, M / 2), Flat(2, 9, 4, 4, 9, 3 * M, M / 2) });
            None(d, 7, 8, 9, 8);
            None(d, 9, 8, 7, 8);
        }

        [Test]
        public void T2_32_UnderATableIsNotAWallButABoxIs()
        {
            None(Make(16, 16, null, new[] { Box(1, 8, 8, 72090 + M / 2, M / 2) }), 7, 8, 9, 8);
            TraversalLink l = One(Make(16, 16, null, new[] { Box(1, 8, 8, M / 2, M / 2) }), 7, 8, 9, 8);
            Assert.That(KindOf(l), Is.EqualTo("vault"));
            Assert.That(l.Cross, Is.EqualTo(M / 2));
        }

        [Test]
        public void T2_37_FromAboveALowerWallThereIsNoLink()
        {
            None(Make(16, 16, null, new[] { Box(1, 8, 7, 98304) }, new[] { Wall(1, 4, 8, 8, 1, M) }), 8, 7, 8, 9);
        }

        // A box at (8, 7) 1 m high; a raised wall [0.5 m, 104858] alone over (8, 8).
        private static MapDesc Hanging() => Make(16, 16, null, new[] { Box(1, 8, 7, M) }, new[] { Wall(1, 8, 8, 1, 1, 72090, M / 2) });

        [Test]
        public void T2_38_AHangingWallSeenFromABox()
        {
            TraversalLink l = One(Hanging(), 8, 7, 8, 9);
            Assert.That(KindOf(l), Is.EqualTo("vault"));
            Assert.That(new[] { l.Cross, l.Rise, l.Fall }, Is.EqualTo(new long[] { 104858, 39322, 104858 }));
        }

        [Test]
        public void T2_39_TheMirrorSwapsRiseAndFall()
        {
            None(Hanging(), 8, 9, 8, 7);
            TraversalLink l = One(Hanging(), 8, 9, 8, 7, rules: Wide);
            Assert.That(new[] { l.Cross, l.Rise, l.Fall }, Is.EqualTo(new long[] { 104858, 104858, 39322 }));
        }

        [Test]
        public void T2_40_AWallNoHigherThanTheBoxIsNoLink()
        {
            None(Make(16, 16, null, new[] { Box(1, 8, 7, M) }, new[] { Wall(1, 8, 8, 1, 1, M) }), 8, 7, 8, 9, rules: Wide);
        }

        [Test]
        public void OverRiseAndFallMeasureFromTheLowCorner()
        {
            // Ground rising 1311 per cell along x; a floor wall at (8, 8) 77336 high (1 m over the highest point under it).
            MapDesc d = Make(16, 16, Heights(16, 16, (i, j) => 1311 * i), new[] { Box(1, 1, 1, 1311 * 2 + M) }, new[] { Wall(1, 8, 8, 1, 1, 77336) });
            TraversalLink east = One(d, 7, 8, 9, 8);
            Assert.That(new[] { east.Cross, east.Rise, east.Fall }, Is.EqualTo(new long[] { 77336, 77336 - 1311 * 7, 77336 - 1311 * 9 }));
            TraversalLink west = One(d, 9, 8, 7, 8);
            Assert.That(new[] { west.Rise, west.Fall }, Is.EqualTo(new long[] { 77336 - 1311 * 9, 77336 - 1311 * 7 }));
        }
    }
}
