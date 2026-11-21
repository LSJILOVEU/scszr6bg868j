using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    // TRV-7a: the table as the world builds it (combat's sight, the body pack's 1.5 m eye, built after the walls are in the
    // spatial index): walls and plates hide candidates, units do not, the point's side, walkable watch cells on the point's
    // island, the same table after a restore, no effect on the simulation, no allocation when read (T7-11, T7-12, T7-14, T7-17,
    // T7-18, T7-19, acceptance 3 and 7).
    public sealed class Trv7aWiringTests
    {
        private const long S = SimFix.Scale;

        // 9 x 12: a 3 m wall on row z = 4 with a door at x = 4; the point's cell (4, 10) in the north room. The door's inner side
        // is (4, 5) (cost 5000), its outer side (4, 3) (cost 7000). Row 8 is replaced when given.
        private static string[] Door(string row8 = ".........") => new[]
        {
            ".........", ".........", ".........", ".........", "####.####",
            ".........", ".........", ".........", row8, ".........", "....P....", ".........",
        };

        private static (int, int)[] Watches(LayeredNav nav, PointEntryTable t, int point, int k) =>
            Enumerable.Range(0, t.WatchCount(point, k)).Select(j => (nav.XOf(t.Watch(point, k, j).Node), nav.ZOf(t.Watch(point, k, j).Node))).ToArray();

        // Every candidate, not only the first three, with the world's own sight.
        private static PointEntryTable All(M6World w, int x, int z)
        {
            LayeredNav nav = Trv7aFixture.Nav(w);
            var reach = new LayeredReach(nav, Ml6Nav.Node(nav, 0, x, z));
            return PointEntryTable.Build(nav, EntryTable.Build(nav), new[] { reach }, 15000, int.MaxValue, w.Combat.Sees);
        }

        // The door map as built: one entry, inner (4, 5), outer (4, 3); watch cells (4, 10) (exactly 5 m), then (3, 10) and (5, 10)
        // (sqrt 26 m, 0.099 m off; (3, 10) the smaller node), all three seeing the outer side through the door. (7, 9) is also 5 m
        // off but the wall beside the door hides (4, 3) from it.
        [Test]
        public void TheDoorMapByHand()
        {
            M6World w = Trv7aFixture.Drawn(Door(), 0, 0);
            LayeredNav nav = Trv7aFixture.Nav(w);
            PointEntryTable t = w.Session.Entries;
            Assert.That(t.PointCount, Is.EqualTo(1));
            Assert.That(t.EntryCount(0), Is.EqualTo(1));
            PointEntry e = t.Entry(0, 0);
            Assert.That((Trv7aFixture.Cell(nav, e.InnerNode), Trv7aFixture.Cell(nav, e.OuterNode), e.InnerCost, e.OuterCost),
                Is.EqualTo(("L0(4,5)", "L0(4,3)", 5000L, 7000L)));
            Assert.That(Watches(nav, t, 0, 0), Is.EqualTo(new[] { (4, 10), (3, 10), (5, 10) }));
            Assert.That(Enumerable.Range(0, 3).All(j => t.Watch(0, 0, j).SeesOuter), Is.True);
        }

        // T7-17: no candidate is outside the door. The outer side (4, 3) itself is 2 m from the inner side and sees it through the
        // door, but its cost equals the outer cost (7000): not on the point's side. (4, 1), 4 m out, sees the inner side too and
        // costs 9000. Inside, (4, 7) costs 3000 and is a candidate. (Costs are sums of 1000 and 1414 a cell, so a cell costing
        // the outer cost less 1 does not exist.)
        [Test]
        public void T7_17_WatchCellsAreOnThePointsSide()
        {
            M6World w = Trv7aFixture.Drawn(Door(), 0, 0);
            LayeredNav nav = Trv7aFixture.Nav(w);
            PointEntryTable all = All(w, 4, 10);
            (int, int)[] cells = Watches(nav, all, 0, 0);
            Assert.That(cells.Where(c => c.Item2 < 5), Is.Empty);
            var reach = new LayeredReach(nav, Ml6Nav.Node(nav, 0, 4, 10));
            Assert.That(reach.CostFrom(Ml6Nav.Node(nav, 0, 4, 3)), Is.EqualTo(all.Entry(0, 0).OuterCost));
            Assert.That(w.Combat.Sees(EntryTable.Centre(nav, Ml6Nav.Node(nav, 0, 4, 3)), all.Entry(0, 0).InnerPoint), Is.True);
            Assert.That(w.Combat.Sees(EntryTable.Centre(nav, Ml6Nav.Node(nav, 0, 4, 1)), all.Entry(0, 0).InnerPoint), Is.True);
            Assert.That(reach.CostFrom(Ml6Nav.Node(nav, 0, 4, 1)), Is.EqualTo(9000L));
            Assert.That(cells, Has.Member((4, 7)));
        }

        // T7-18: through the real wiring a wall hides a candidate. A 3 m pillar at (5, 8) stands on the line from (5, 10) to the
        // inner side (and from (6, 10)): the table picks (4, 10), (3, 10), (2, 10) ((2, 10) is sqrt 29 m, 0.385 m off, and sees the
        // outer side), and neither hidden cell is a candidate at all. With a sight that ignores walls (what a table built before
        // the walls reach the spatial index would see) both are.
        [Test]
        public void T7_18_AWallHidesACandidateInTheRealWiring()
        {
            M6World w = Trv7aFixture.Drawn(Door(".....#..."), 0, 0);
            LayeredNav nav = Trv7aFixture.Nav(w);
            Assert.That(Watches(nav, w.Session.Entries, 0, 0), Is.EqualTo(new[] { (4, 10), (3, 10), (2, 10) }));
            var reach = new LayeredReach(nav, Ml6Nav.Node(nav, 0, 4, 10));
            PointEntryTable blind = PointEntryTable.Build(nav, EntryTable.Build(nav), new[] { reach }, 15000, int.MaxValue, (a, b) => true);
            Assert.That(Watches(nav, blind, 0, 0), Has.Member((5, 10)).And.Member((6, 10)));
            (int, int)[] seen = Watches(nav, All(w, 4, 10), 0, 0);
            Assert.That(seen, Has.No.Member((5, 10)).And.No.Member((6, 10)));
            Assert.That(seen, Has.Member((2, 10)).And.Member((4, 10)));
        }

        // T7-18: the table built with the world equals the one built again at tick 0, after 60 ticks, and in a fresh world after
        // restoring the snapshot taken at tick 60 (the assault map, its battle running).
        [Test]
        public void T7_18_TheSameTableAfterARestore()
        {
            M6World a = Trv7aFixture.Assault();
            PointEntryTable start = a.Session.Entries;
            Same(start, a.Session.RebuildEntriesForCheck());
            a.Step(60);
            Same(start, a.Session.RebuildEntriesForCheck());
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            M6World b = Trv7aFixture.Assault();
            b.Sim.RestoreRetainedSnapshot(snapshot);
            Same(start, b.Session.RebuildEntriesForCheck());
            Same(start, b.Session.Entries);
        }

        internal static void Same(PointEntryTable x, PointEntryTable y)
        {
            Assert.That(y.Entries.Count, Is.EqualTo(x.Entries.Count));
            for (int e = 0; e < x.Entries.Count; e++) Assert.That(y.Entries[e], Is.EqualTo(x.Entries[e]), "entry " + e);
            Assert.That((y.PointCount, y.SightChecks), Is.EqualTo((x.PointCount, x.SightChecks)));
            for (int p = 0; p < x.PointCount; p++)
            {
                Assert.That(y.EntryCount(p), Is.EqualTo(x.EntryCount(p)));
                for (int k = 0; k < x.EntryCount(p); k++)
                {
                    Assert.That(y.Entry(p, k), Is.EqualTo(x.Entry(p, k)));
                    Assert.That(y.WatchCount(p, k), Is.EqualTo(x.WatchCount(p, k)));
                    for (int j = 0; j < x.WatchCount(p, k); j++) Assert.That(y.Watch(p, k, j), Is.EqualTo(x.Watch(p, k, j)));
                }
            }
        }

        // T7-11: the sight counts walls and plates, never units. A soldier standing at (4, 8), between (4, 10) and the inner side,
        // changes nothing. A plate whose slab spans 1.0-1.6 m over (5, 8) hides (5, 10) as the pillar does (the 1.5 m eye line
        // passes through it; the ground under it is too low to walk, as under the pillar); the same plate at 3 m (slab 2.5-3 m,
        // the ground under it walkable) hides nothing.
        [Test]
        public void T7_11_OnlyWallsAndPlatesHide()
        {
            M6World plain = Trv7aFixture.Drawn(Door(), 0, 0), manned = Trv7aFixture.Drawn(Door(), 0, 0, units: (4, 8));
            Assert.That(manned.Session.MatchPack.Points.Length, Is.EqualTo(1));
            Same(plain.Session.Entries, manned.Session.Entries);
            LayeredNav nav = Trv7aFixture.Nav(manned);
            Assert.That(manned.Combat.Sees(EntryTable.Centre(nav, Ml6Nav.Node(nav, 0, 4, 10)), manned.Session.Entries.Entry(0, 0).InnerPoint), Is.True);
            M6World table = Trv7aFixture.Drawn(Door(), 5, 8, plateTop: 104858, plateThick: 39322);
            LayeredNav low = Trv7aFixture.Nav(table);
            Assert.That(low.IsPassable(Ml6Nav.Node(low, 0, 5, 8)), Is.False, "too low to walk under");
            Assert.That(Watches(low, table.Session.Entries, 0, 0), Is.EqualTo(new[] { (4, 10), (3, 10), (2, 10) }));
            M6World high = Trv7aFixture.Drawn(Door(), 5, 8);
            Assert.That(Watches(Trv7aFixture.Nav(high), high.Session.Entries, 0, 0), Is.EqualTo(new[] { (4, 10), (3, 10), (5, 10) }));
        }

        // T7-12: every watch cell of the shipped demos is walkable, carries its node's layer and its cell middle, and reaches the
        // point (as its entry's inner side does): with two-way edges only, both are on the point's island.
        [Test]
        public void T7_12_WatchCellsAreWalkableOnThePointsIsland()
        {
            int cells = 0;
            foreach (M6World w in new[] { Trv7aFixture.Demo(), Trv7aFixture.Assault() })
            {
                LayeredNav nav = Trv7aFixture.Nav(w);
                PointEntryTable t = w.Session.Entries;
                Assert.That(t.PointCount, Is.EqualTo(w.Session.MatchPack.Points.Length));
                for (int p = 0; p < t.PointCount; p++)
                {
                    var reach = new LayeredReach(nav, Target(w, nav, p));
                    for (int k = 0; k < t.EntryCount(p); k++)
                    {
                        PointEntry e = t.Entry(p, k);
                        Assert.That((reach.CostFrom(e.InnerNode), reach.CostFrom(e.OuterNode)), Is.EqualTo((e.InnerCost, e.OuterCost)));
                        for (int j = 0; j < t.WatchCount(p, k); j++)
                        {
                            WatchCell c = t.Watch(p, k, j);
                            Assert.That(nav.IsPassable(c.Node), Is.True);
                            Assert.That(c.Layer, Is.EqualTo(nav.LayerOf(c.Node)));
                            Assert.That(c.Point, Is.EqualTo(EntryTable.Centre(nav, c.Node)));
                            Assert.That(reach.CostFrom(c.Node), Is.GreaterThanOrEqualTo(0L).And.LessThan(e.OuterCost));
                            Assert.That(e.InnerCost, Is.GreaterThanOrEqualTo(0L));
                            cells++;
                        }
                    }
                }
            }
            Assert.That(cells, Is.GreaterThanOrEqualTo(40));
        }

        // The shipped demos' counts on the body pack (the report's numbers): the demo house 3 entries (point 0 has all three,
        // point 1 none: its nearest inner side is farther than 15 m); the assault map 9 (points 0, 1, 2: 6, 8, 1).
        [Test]
        public void TheDemosCounts()
        {
            PointEntryTable demo = Trv7aFixture.Demo().Session.Entries, assault = Trv7aFixture.Assault().Session.Entries;
            Assert.That((demo.Entries.Count, demo.EntryCount(0), demo.EntryCount(1)), Is.EqualTo((3, 3, 0)));
            Assert.That((assault.Entries.Count, assault.EntryCount(0), assault.EntryCount(1), assault.EntryCount(2)), Is.EqualTo((9, 6, 8, 1)));
            Assert.That(Enumerable.Range(0, assault.EntryCount(0)).Max(k => assault.Entry(0, k).InnerCost), Is.EqualTo(14828L));
        }

        // A point's walking target as PointReach picks it on these maps: its centre's cell.
        private static int Target(M6World w, LayeredNav nav, int point)
        {
            var def = w.Session.MatchPack.Points[point];
            return Ml6Nav.Node(nav, def.Layer, (int)(def.Center.X.Raw / S), (int)(def.Center.Z.Raw / S));
        }

        // T7-19 on the stair: the assault map's point 1 (upstairs), entry 1 (the west door) has the stair cell (18, 12) as a watch
        // cell: its middle is at (18.5, 1.125, 12.5) (corners 0.75 and 1.5 m).
        [Test]
        public void T7_19_AWatchCellOnTheStairStandsOnItsMiddle()
        {
            M6World w = Trv7aFixture.Assault();
            LayeredNav nav = Trv7aFixture.Nav(w);
            PointEntryTable t = w.Session.Entries;
            int k = Enumerable.Range(0, t.EntryCount(1)).Single(i => t.Entry(1, i).Entry == 1);
            WatchCell stair = Enumerable.Range(0, t.WatchCount(1, k)).Select(j => t.Watch(1, k, j)).First(c => c.Layer == 1);
            Assert.That(Trv7aFixture.Cell(nav, stair.Node), Is.EqualTo("L1(18,12)"));
            Assert.That((stair.Point.X.Raw, stair.Point.Y.Raw, stair.Point.Z.Raw), Is.EqualTo((18 * S + S / 2, 73728L, 12 * S + S / 2)));
        }

        // T7-14: a world without plates has no table; one with plates has it whatever its options (Traversal does not exist yet;
        // the table reads no switch).
        [Test]
        public void T7_14_OnlyAMapWithPlatesHasATable()
        {
            var flat = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1), M6World.U(2, 2, 60) }));
            Assert.That(flat.Session.Entries, Is.Null);
            Assert.That(flat.Session.RebuildEntriesForCheck(), Is.Null);
            Assert.That(Trv7aFixture.Demo("rwr-m6").Session.Entries, Is.Not.Null);
        }

        // Acceptance 3: building the table again and again while the battle runs changes no hash and no snapshot byte (the
        // assault map to the end of its match, and the demo house (one team only, so on a match that never ends) for 600 ticks,
        // both on the body pack, rebuilt every 25).
        [Test]
        public void BuildingTheTableChangesNothingInTheSimulation()
        {
            foreach (Func<M6World> make in new Func<M6World>[] { () => Trv7aFixture.Assault(), () => Trv7aFixture.Demo(endless: true) })
            {
                M6World a = make(), b = make();
                int tick = 0;
                while (!a.Sim.IsFinished && tick < 600)
                {
                    if (tick % 25 == 0) Assert.That(b.Session.RebuildEntriesForCheck(), Is.Not.Null);
                    a.Step(); b.Step(); tick++;
                    Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + tick);
                }
                Assert.That(b.Sim.IsFinished, Is.EqualTo(a.Sim.IsFinished));
                Assert.That(b.Sim.SaveRetainedSnapshot(), Is.EqualTo(a.Sim.SaveRetainedSnapshot()));
                TestContext.Out.WriteLine("TRV-7a rebuilt every 25 ticks: " + tick + " ticks, finished " + a.Sim.IsFinished + ", hash " + a.Hash.ToString("X16"));
            }
        }

        // Acceptance 7: reading the table allocates nothing.
        [Test]
        public void ReadingAllocatesNothing()
        {
            PointEntryTable t = Trv7aFixture.Assault().Session.Entries;
            long sum = Read(t) + Read(t);
            AllocationMeter.Stabilize();
            long total = 0;
            for (int i = 0; i < 100; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                sum += Read(t);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            long start = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[64];
            long positive = GC.GetAllocatedBytesForCurrentThread() - start;
            GC.KeepAlive(control);
            Assert.That(total, Is.EqualTo(0L));
            Assert.That(positive, Is.GreaterThanOrEqualTo(64), "direct counter positive control");
            Assert.That(sum, Is.Not.EqualTo(0L));
        }

        private static long Read(PointEntryTable t)
        {
            long sum = 0;
            for (int e = 0; e < t.Entries.Count; e++)
            {
                EntryInfo info = t.Entries[e];
                sum += info.NodeA + info.Width + info.PointB.Y.Raw;
                for (int k = 0; k < t.Entries.OwnCount(e); k++) sum += t.Entries.Own(e, k);
                if (t.Entries.Owns(e, info.NodeA)) sum++;
            }
            for (int p = 0; p < t.PointCount; p++)
                for (int k = 0; k < t.EntryCount(p); k++)
                {
                    PointEntry pe = t.Entry(p, k);
                    sum += pe.InnerCost + pe.OuterNode;
                    for (int j = 0; j < t.WatchCount(p, k); j++) sum += t.Watch(p, k, j).Node + (t.Watch(p, k, j).SeesOuter ? 1 : 0);
                }
            return sum;
        }

        // The headless report (acceptance 5, E): the demos' tables as text and top views. Writes into the folder named by
        // TRV7A_OUT; by hand only.
        [Test, Explicit, Category("Manual")]
        public void WriteTheReports()
        {
            string dir = Environment.GetEnvironmentVariable("TRV7A_OUT");
            Assert.That(dir, Is.Not.Null.And.Not.Empty, "set TRV7A_OUT");
            Directory.CreateDirectory(dir);
            foreach ((string name, M6World w) in new[] { ("demo", Trv7aFixture.Demo()), ("assault", Trv7aFixture.Assault()) })
            {
                LayeredNav nav = Trv7aFixture.Nav(w);
                File.WriteAllText(Path.Combine(dir, name + "-entries.txt"), Trv7aFixture.Report(name, nav, w.Session.Entries));
                File.WriteAllText(Path.Combine(dir, name + "-entries.svg"), Trv7aFixture.Svg(nav, w.Session.Entries, w.Session.MatchPack.Points.ToArray()));
            }
        }
    }
}
