using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;
using static WarCore.Tests.Trv2Maps;

namespace WarCore.Tests
{
    // TRV-2 whole-map properties: order (T2-23), zones (T2-24), the cap (T2-28), purity and repeatability (D6), the off
    // cases, and the step bound on a large map (D7).
    public sealed class Trv2DeriveTests
    {
        [Test]
        public void T2_23_LinksAreSortedByFromThenToAndRepeatable()
        {
            foreach (MapDesc d in new[] { Box1(), Window(), LowWall(), BoxWall(), SlopeHouse() })
            {
                TraversalLink[] first = Derive(d), second = Derive(d);
                Assert.That(first.Length, Is.GreaterThan(0));
                for (int i = 1; i < first.Length; i++)
                    Assert.That(first[i - 1].From < first[i].From || (first[i - 1].From == first[i].From && first[i - 1].To < first[i].To), Is.True,
                        "strictly ascending (from, to) at " + i + ": " + first[i - 1] + " / " + first[i]);
                Assert.That(second, Is.EqualTo(first));
            }
        }

        [Test]
        public void T2_23_TheBoxLinksInFullInNodeOrder()
        {
            // Ground nodes are z * 16 + x, the box node is 256. Edges: ground (8,7)=120, (7,8)=135, (9,8)=137, (8,9)=152.
            TraversalLink[] links = Derive(Box1());
            Assert.That(links.Select(l => l.From + ">" + l.To + ":" + KindOf(l)), Is.EqualTo(new[]
            {
                "120>152:vault", "120>256:climb", "135>137:vault", "135>256:climb", "137>135:vault", "137>256:climb",
                "152>120:vault", "152>256:climb", "256>120:drop", "256>135:drop", "256>137:drop", "256>152:drop",
            }));
        }

        [Test]
        public void T2_24_AZoneOverAnyOfTheThreeCellsRemovesTheLink()
        {
            Assert.That(Between(Window(), 8, 7, 8, 9, zones: new NoTraverseZone(8, 7, 1, 1)), Is.Empty, "a");
            Assert.That(Between(Window(), 8, 7, 8, 9, zones: new NoTraverseZone(8, 8, 1, 1)), Is.Empty, "m");
            Assert.That(Between(Window(), 8, 7, 8, 9, zones: new NoTraverseZone(8, 9, 1, 1)), Is.Empty, "b");
            Assert.That(Between(Window(), 8, 7, 8, 9, zones: new NoTraverseZone(0, 0, 16, 16)), Is.Empty, "all");
            // Zones whose far edge stops one cell short of a, m or b leave the link.
            Assert.That(Between(Window(), 8, 7, 8, 9, zones: new NoTraverseZone(8, 5, 1, 2)).Length, Is.EqualTo(1), "z far edge");
            Assert.That(Between(Window(), 8, 7, 8, 9, zones: new NoTraverseZone(6, 7, 2, 3)).Length, Is.EqualTo(1), "x far edge");
            Assert.That(Between(Window(), 8, 7, 8, 9, zones: new[] { new NoTraverseZone(9, 7, 2, 3), new NoTraverseZone(8, 10, 1, 2) }).Length, Is.EqualTo(1), "near edges");
            // Edge links as well.
            Assert.That(Between(Box1(), 7, 8, 8, 8, zones: new NoTraverseZone(8, 8, 1, 1)), Is.Empty);
            Assert.That(Between(Box1(), 7, 8, 8, 8, zones: new NoTraverseZone(7, 8, 1, 1)), Is.Empty);
            Assert.That(Between(Box1(), 7, 8, 8, 8, zones: new NoTraverseZone(5, 8, 2, 1)).Length, Is.EqualTo(1));
        }

        [Test]
        public void ZonesNeedAtLeastOneCellAndMayLeaveTheMap()
        {
            Assert.Throws<ArgumentException>(() => new NoTraverseZone(0, 0, 0, 1));
            Assert.Throws<ArgumentException>(() => new NoTraverseZone(0, 0, 1, 0));
            Assert.That(Derive(Box1(), zones: new NoTraverseZone(-5, -5, 3, 3)), Is.EqualTo(Derive(Box1())));
            Assert.That(Derive(Box1(), zones: new NoTraverseZone(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue)), Is.EqualTo(Derive(Box1())));
        }

        // 1-cell-wide 1 m strips along x on every third row: each strip of length L gives 6L + 4 links.
        private static MapDesc Strips(int size, int count, int length)
        {
            var plates = new MapPlate[count];
            for (int s = 0; s < count; s++) plates[s] = Flat((ulong)s + 1, 1, 1 + 3 * s, length, 1, M, M);
            return Make(size, size, null, plates);
        }

        [Test]
        public void T2_28_TheCapIs65535Links()
        {
            Assert.That(LinkDerive.MaxLinks, Is.EqualTo(65535));
            WcMap small = Bake(Strips(16, 2, 10));
            LayeredNav nav = Nav(small);
            Assert.That(LinkDerive.Derive(small, nav, Rules, null, 128, out _).Length, Is.EqualTo(2 * (6 * 10 + 4)));
            Assert.DoesNotThrow(() => LinkDerive.Derive(small, nav, Rules, null, 128, out _));
            Assert.Throws<ArgumentException>(() => LinkDerive.Derive(small, nav, Rules, null, 127, out _));
            // A real map over the cap: 85 strips of 254 cells give 85 x 1528 = 129880 links.
            WcMap big = Bake(Strips(256, 85, 254));
            Assert.Throws<ArgumentException>(() => LinkDerive.Derive(big, Nav(big), Rules, null));
        }

        [Test]
        public void D6_DeriveChangesNoInputAndRepeats()
        {
            WcMap map = Bake(Window());
            LayeredNav nav = Nav(map);
            byte[] before = WcMapWriter.Write(map);
            ulong digest = nav.Digest;
            var zones = new[] { new NoTraverseZone(1, 1, 2, 2) };
            var fold = new StateHash();
            Rules.Fold(fold);
            TraversalLink[] a = LinkDerive.Derive(map, nav, Rules, zones), b = LinkDerive.Derive(map, nav, Rules, zones);
            Assert.That(b, Is.EqualTo(a));
            Assert.That(WcMapWriter.Write(map), Is.EqualTo(before));
            Assert.That(nav.Digest, Is.EqualTo(digest));
            Assert.That(zones[0].X + zones[0].Z * 100 + zones[0].Width * 10000 + zones[0].Depth * 1000000, Is.EqualTo(2020101));
            var again = new StateHash();
            Rules.Fold(again);
            Assert.That(again.Value, Is.EqualTo(fold.Value));
        }

        [Test]
        public void WithoutPlatesThereAreNoLinks()
        {
            MapDesc flat = Make(16, 16, null, new MapPlate[0], new[] { Wall(1, 4, 8, 8, 1, M) });
            WcMap map = Bake(flat);
            Assert.That(WcMapLayeredNav.Build(map, Test), Is.Null);
            Assert.That(LinkDerive.Derive(map, null, Rules, null), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => LinkDerive.Derive(null, null, Rules, null));
            Assert.Throws<ArgumentNullException>(() => LinkDerive.Derive(map, null, null, null));
        }

        [Test]
        public void AGraphOfAnotherMapIsRefused()
        {
            WcMap box = Bake(Box1()), other = Bake(Make(20, 16, null, new[] { Box(1, 8, 8, M) }));
            Assert.Throws<ArgumentException>(() => LinkDerive.Derive(box, Nav(other), Rules, null));
        }

        // D7: 512 x 512, 4096 boxes on an 8-cell grid. The span reads stay within cells x 8 x spans per cell (here 1).
        [Test]
        public void D7_ALargeMapStaysWithinTheStepBound()
        {
            var plates = new List<MapPlate>();
            for (int j = 0; j < 64; j++)
                for (int i = 0; i < 64; i++) plates.Add(Box((ulong)plates.Count + 1, 4 + 8 * i, 4 + 8 * j, M));
            WcMap map = Bake(Make(512, 512, null, plates.ToArray()));
            LayeredNav nav = Nav(map);
            var clock = Stopwatch.StartNew();
            TraversalLink[] links = LinkDerive.Derive(map, nav, Rules, null, LinkDerive.MaxLinks, out long reads);
            clock.Stop();
            TestContext.WriteLine("TRV-2 D7: " + links.Length + " links, " + reads + " span reads, " + clock.ElapsedMilliseconds + " ms");
            Assert.That(links.Length, Is.EqualTo(4096 * 12));
            Assert.That(reads, Is.LessThanOrEqualTo(512L * 512 * 8 * 1));
        }
    }
}
