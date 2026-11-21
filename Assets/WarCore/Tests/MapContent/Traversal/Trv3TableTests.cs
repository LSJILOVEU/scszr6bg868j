using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;
using static WarCore.Tests.Trv2Maps;
using static WarCore.Tests.Trv3Maps;

namespace WarCore.Tests
{
    // TRV-3: the link table of LayeredNav, its digest and its checks (T3-01, 02, 14, 16, 18, 19, 20, 21).
    public sealed class Trv3TableTests
    {
        private static TerrainTables Line(int w) => Ml6Nav.Open(w, 1);

        private static NavLink L(int from, int to, int span = 2, long cost = 0, int kind = 0, int ticks = 1, long cross = 0, long gap = NavLink.NoGap) =>
            new NavLink(from, to, span, cost, kind, ticks, cross, gap);

        // The digest of a graph of ground cells only, written out field by field as LayeredNav.Hash and HashLinks write it.
        private static ulong HandDigest(TerrainTables ground, long flat, int count, params NavLink[] links)
        {
            var h = new StateHash();
            h.Add(LayeredNav.AlgorithmVersion); h.Add(LayeredNav.StepRaw); h.Add(ground.Digest); h.Add(ground.Ground == null); h.Add(flat);
            h.Add(0);                                   // plates
            h.Add(0);                                   // seams
            for (int i = 0; i <= count; i++) h.Add(0);  // seam starts
            if (links != null)
            {
                h.Add("nav.links.v1"); h.Add(links.Length);
                foreach (NavLink l in links)
                {
                    h.Add(l.From); h.Add(l.To); h.Add(l.Span); h.Add(l.PathCost); h.Add(l.Kind); h.Add(l.Ticks); h.Add(l.Cross); h.Add(l.Gap);
                }
            }
            return h.Value;
        }

        // ---- T3-01 / T3-19: without links nothing changes ----

        [Test]
        public void T3_01_NoTableIsTheOldGraph()
        {
            TerrainTables g = Line(4);
            var plain = new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][]);
            var none = new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][], null);
            Assert.That(plain.Digest, Is.EqualTo(HandDigest(g, 0, 4, null)), "the hand digest is the old layout");
            Assert.That(none.Digest, Is.EqualTo(plain.Digest));
            Assert.That(none.HasLinks, Is.False);
            Assert.That(none.LinkCount, Is.Zero);
            Assert.That(none.LinkFromCount(0) + none.LinkToCount(0) + none.LinkFromFirst(0), Is.Zero);
            Assert.That(none.TryLink(0, 2, out int index), Is.False);
            Assert.That(index, Is.EqualTo(-1));
        }

        private static readonly (string Name, Func<MapDesc> Map, ulong Digest)[] MainDigests =
        {
            ("F-box", () => Box1(), 0x3159F8BE52E6A995UL),
            ("F-window", () => Window(), 0x671A952A4A610B61UL),
            ("F-low-wall", () => LowWall(), 0x20BB4E80D9AFEBF3UL),
            ("F-box-wall", () => BoxWall(), 0x90B3E9B7AA90B0FCUL),
        };

        // The digests were recorded on main before TRV-3 (the same graphs, both old overloads).
        [Test]
        public void T3_19_OldOverloadsKeepTheMainDigests()
        {
            foreach (var (name, make, digest) in MainDigests)
            {
                WcMap map = Bake(make());
                Assert.That(WcMapLayeredNav.Build(map, Test).Digest, Is.EqualTo(digest), name + " (pack)");
                Assert.That(WcMapLayeredNav.Build(map, Test.Terrain, Test.Id).Digest, Is.EqualTo(digest), name + " (rules)");
                LayeredNav viaNew = WcMapLayeredNav.Build(map, Test.Terrain, Test.Id, null, null);
                Assert.That(viaNew.Digest, Is.EqualTo(digest), name + " (new overload, no traversal rules)");
                Assert.That(viaNew.HasLinks, Is.False, name);
            }
        }

        [Test]
        public void TheNewOverloadIsNullWithoutPlates()
        {
            WcMap flat = Bake(Make(4, 4, null, new MapPlate[0]));
            Assert.That(WcMapLayeredNav.Build(flat, Test.Terrain, Test.Id, Rules, null), Is.Null);
        }

        // ---- T3-02 / T3-16 / T3-20: the digest with a table ----

        [Test]
        public void T3_16_AnEmptyTableIsNotNoTable()
        {
            TerrainTables g = Line(4);
            var none = new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][]);
            var empty = new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][], new NavLink[0]);
            Assert.That(empty.HasLinks, Is.True);
            Assert.That(empty.LinkCount, Is.Zero);
            Assert.That(empty.Digest, Is.Not.EqualTo(none.Digest));
            Assert.That(empty.Digest, Is.EqualTo(HandDigest(g, 0, 4, new NavLink[0])), "tag and count 0");
        }

        [Test]
        public void T3_02_TheTableEndsTheDigestFieldByField()
        {
            TerrainTables g = Line(5);
            NavLink[] links = { L(0, 2, 2, 7, 1, 3, 65536, 90000), L(4, 2, 2, 0, 0, 9, 1, NavLink.NoGap) };
            var nav = new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][], links);
            Assert.That(nav.Digest, Is.EqualTo(HandDigest(g, 0, 5, links)));
            ulong d = nav.Digest;
            Func<NavLink[], ulong> digest = ls => new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][], ls).Digest;
            Assert.That(digest(new[] { links[0] }), Is.Not.EqualTo(d), "one link fewer");
            Assert.That(digest(new[] { L(0, 2, 2, 7, 2, 3, 65536, 90000), links[1] }), Is.Not.EqualTo(d), "kind");
            Assert.That(digest(new[] { L(0, 2, 2, 8, 1, 3, 65536, 90000), links[1] }), Is.Not.EqualTo(d), "path cost");
            Assert.That(digest(new[] { L(0, 2, 2, 7, 1, 4, 65536, 90000), links[1] }), Is.Not.EqualTo(d), "ticks");
            Assert.That(digest(new[] { L(0, 2, 2, 7, 1, 3, 65537, 90000), links[1] }), Is.Not.EqualTo(d), "cross");
            Assert.That(digest(new[] { L(0, 2, 2, 7, 1, 3, 65536, 90001), links[1] }), Is.Not.EqualTo(d), "gap");
            Assert.That(digest(new[] { L(0, 2, 2, 7, 1, 3, 65536, 90000), L(3, 1, 2, 0, 0, 9, 1, NavLink.NoGap) }), Is.Not.EqualTo(d), "other ends");
            // The order is fixed by the check (ascending from, to), so "the same links in another order" cannot be built.
        }

        [Test]
        public void T3_20_OnlyTheGapChangedChangesTheDigest()
        {
            LayeredNav nav = Linked(Window());
            int i = Enumerable.Range(0, nav.LinkCount).First(k => nav.Link(k).Gap != NavLink.NoGap);
            NavLink l = nav.Link(i);
            var changed = Enumerable.Range(0, nav.LinkCount).Select(k => k != i ? nav.Link(k) : new NavLink(l.From, l.To, l.Span, l.PathCost, l.Kind, l.Ticks, l.Cross, l.Gap + 1)).ToArray();
            Assert.That(nav.WithLinks(changed).Digest, Is.Not.EqualTo(nav.Digest));
        }

        [Test]
        public void T3_14_BuiltTwiceIsTheSame()
        {
            foreach (var (name, make, _) in MainDigests)
            {
                LayeredNav a = Linked(make()), b = Linked(make());
                Assert.That(a.Digest, Is.EqualTo(b.Digest), name);
                Assert.That(Enumerable.Range(0, a.LinkCount).Select(a.Link), Is.EqualTo(Enumerable.Range(0, b.LinkCount).Select(b.Link)), name);
            }
        }

        [Test]
        public void WithLinksEqualsTheConstructor()
        {
            TerrainTables g = Line(5);
            NavLink[] links = { L(0, 2), L(2, 0), L(2, 4) };
            var built = new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][], links);
            var plain = new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][]);
            LayeredNav with = plain.WithLinks(links);
            Assert.That(with.Digest, Is.EqualTo(built.Digest));
            Assert.That(with.WithLinks(new NavLink[0]).Digest, Is.EqualTo(plain.WithLinks(new NavLink[0]).Digest), "replaces the table");
            Assert.That(plain.HasLinks, Is.False, "the source keeps no table");
            Assert.Throws<ArgumentNullException>(() => plain.WithLinks(null));
        }

        // ---- the queries ----

        [Test]
        public void TheIndexesAnswerFromAndTo()
        {
            TerrainTables g = Line(6);
            NavLink[] links = { L(0, 2), L(1, 3), L(2, 0), L(2, 4), L(4, 2), L(5, 3) };
            var nav = new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][], links);
            Assert.That(nav.LinkFromFirst(2), Is.EqualTo(2));
            Assert.That(nav.LinkFromCount(2), Is.EqualTo(2));
            Assert.That(nav.LinkFromCount(3), Is.Zero);
            Assert.That(nav.LinkToCount(2), Is.EqualTo(2));
            Assert.That(new[] { nav.LinkTo(2, 0), nav.LinkTo(2, 1) }, Is.EqualTo(new[] { 0, 4 }), "ascending start node");
            Assert.That(new[] { nav.LinkTo(3, 0), nav.LinkTo(3, 1) }, Is.EqualTo(new[] { 1, 5 }));
            for (int i = 0; i < links.Length; i++)
            {
                Assert.That(nav.TryLink(links[i].From, links[i].To, out int at), Is.True, links[i].ToString());
                Assert.That(at, Is.EqualTo(i));
                Assert.That(nav.Link(i), Is.EqualTo(links[i]));
            }
            Assert.That(nav.TryLink(0, 1, out int none), Is.False);
            Assert.That(none, Is.EqualTo(-1));
            Assert.That(nav.TryLink(3, 1, out _), Is.False, "links have a direction");
            Assert.That(nav.TryLink(-1, 1, out _), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => nav.Link(6));
            Assert.Throws<ArgumentOutOfRangeException>(() => nav.Link(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => nav.LinkTo(3, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => nav.LinkFromCount(6));
        }

        // ---- T3-18: the checks ----

        private static void Refused(TerrainTables g, string because, params NavLink[] links) =>
            Assert.Throws<ArgumentException>(() => new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][], links), because);

        [Test]
        public void T3_18_BadTablesAreRefused()
        {
            TerrainTables g = Line(6);
            Refused(g, "out of order", L(2, 4), L(0, 2));
            Refused(g, "out of order inside one start", L(2, 4), L(2, 0));
            Refused(g, "repeated", L(0, 2), L(0, 2, 2, 5));
            Refused(g, "not a node", L(0, 6));
            Refused(g, "negative node", L(-1, 1));
            Refused(g, "same node", new NavLink(2, 2, 1, 0, 0, 1, 0, 0));
            Refused(g, "neighbours on one layer", L(0, 1));
            Refused(g, "span 0", L(0, 2, 0));
            Refused(g, "span 3", L(0, 3, 3));
            Refused(g, "farther than the span", L(0, 2, 1));
            Refused(g, "negative path cost", L(0, 2, 2, -1));
            Refused(g, "path cost above the cap", L(0, 2, 2, NavLink.MaxPathCost + 1));
            Refused(g, "negative kind", L(0, 2, 2, 0, -1));
            Refused(g, "zero ticks", L(0, 2, 2, 0, 0, 0));
            Refused(Ml6Nav.Open(6, 1, (2, 0)), "an end not walkable", L(0, 2));
            TerrainTables square = Ml6Nav.Open(6, 6);
            Refused(square, "diagonal neighbours on one layer", L(0, 7));
            Refused(square, "two cells diagonally is farther than span 2", L(0, 14));
            Refused(square, "two east one north is farther than span 2", L(0, 8));
            Assert.DoesNotThrow(() => new LayeredNav(square, 0, new PlateSlab[0], new byte[0][], new ushort[0][], new[] { L(0, 2), L(0, 12) }), "two east, two north");
            Assert.DoesNotThrow(() => new LayeredNav(g, 0, new PlateSlab[0], new byte[0][], new ushort[0][], new[] { L(0, 2, 2, NavLink.MaxPathCost) }), "the cap itself");

            // A seam between the ends: a plate 0.3 m above the ground meets it at a seam.
            PlateSlab plate = Ml5Plates.Flat(1, 2, 0, 2, 1, 19660, 32768);
            LayeredNav seamed = Ml6Nav.Nav(Ml6Nav.Open(6, 1), plate);
            int up = Ml6Nav.Node(seamed, 1, 2, 0);
            Assert.That(seamed.SeamCount(1), Is.EqualTo(1), "ground (1, 0) meets the plate");
            Assert.Throws<ArgumentException>(() => seamed.WithLinks(new[] { L(1, up, 1) }), "seam");
            Assert.DoesNotThrow(() => seamed.WithLinks(new[] { L(0, up, 2) }), "no seam from (0, 0)");

            // More than 65535: a 300 x 300 ground, links two cells east.
            TerrainTables wide = Ml6Nav.Open(300, 300);
            var many = Enumerable.Range(0, 300 * 300).Where(n => n % 300 < 298).Take(LayeredNav.MaxLinks + 1).Select(n => L(n, n + 2)).ToArray();
            Assert.Throws<ArgumentException>(() => new LayeredNav(wide, 0, new PlateSlab[0], new byte[0][], new ushort[0][], many), "65536 links");
            Assert.DoesNotThrow(() => new LayeredNav(wide, 0, new PlateSlab[0], new byte[0][], new ushort[0][], many.Take(LayeredNav.MaxLinks).ToArray()), "65535 links");
        }

        // ---- T3-21: the shape filter ----

        [Test]
        public void T3_21_OnlyTheChosenShapes()
        {
            WcMap window = Bake(Window());
            // Edge only: the window's two vaults go, the corner box's four climbs and four drops stay.
            LayeredNav edgeOnly = Linked(window, null, TraversalShapes.Edge);
            Assert.That(edgeOnly.LinkCount, Is.EqualTo(8));
            Assert.That(Enumerable.Range(0, 8).Select(i => Rules[edgeOnly.Link(i).Kind].Id).Distinct(), Is.EquivalentTo(new[] { "climb", "drop" }));
            Assert.That(Enumerable.Range(0, 8).All(i => edgeOnly.ZOf(edgeOnly.Link(i).From) <= 2), Is.True, "all at the box in the corner");
            LayeredNav none = Linked(window, null, TraversalShapes.None);
            Assert.That(none.HasLinks, Is.True, "nothing chosen: an empty table, not none");
            Assert.That(none.LinkCount, Is.Zero);
            Assert.That(none.Digest, Is.EqualTo(WcMapLayeredNav.Build(window, Test).WithLinks(new NavLink[0]).Digest), "tag and count 0");
            Assert.That(none.Digest, Is.Not.EqualTo(MainDigests[1].Digest));

            LayeredNav boxWall = Linked(BoxWall(), null, TraversalShapes.Edge);
            string[] kinds = Enumerable.Range(0, boxWall.LinkCount).Select(i => Rules[boxWall.Link(i).Kind].Id).ToArray();
            Assert.That(kinds, Is.EquivalentTo(new[] { "climb", "climb", "climb", "drop", "drop", "drop" }), "up onto the box and down from it");
            Assert.That(Linked(BoxWall(), null, TraversalShapes.Over).LinkCount, Is.EqualTo(Linked(BoxWall()).LinkCount - 6));
        }
    }
}
