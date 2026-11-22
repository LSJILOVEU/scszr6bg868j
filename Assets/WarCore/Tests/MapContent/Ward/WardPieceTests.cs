using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // MAP-BL0 W4: the pieces of the map (counts, sizes, kinds, heights, shops) and rules 1 to 6, on the committed layout; then
    // the counter-examples D1 to D14: a copy of the layout with one change, built the same way, must be refused for the
    // card's reason (D14 is the positive one: a corner touch is not a one-cell gap).
    public sealed class WardPieceTests
    {
        private static List<string> Check(WardLayout layout) => WardCheck.Run(layout, WardBuild.Document(41, layout));

        [Test]
        public void TheBuiltLayoutPassesEveryCheck()
        {
            Assert.DoesNotThrow(() =>
            {
                List<string> found = WardCheck.Run(WardLayout.Default, Built());
                Assert.That(found, Is.Empty, string.Join("\n", found));
            });
        }

        // The build code's document (W1 holds the committed file to it), so a change to the build code shows here.
        private static MapDocument Built() => WardBuild.Document(41);

        [Test]
        public void EachKindHasTheCardsCountSizeAndHeight()
        {
            MapDocument doc = Built();
            WardLayout layout = WardLayout.Default;
            var walls = doc.Walls();
            Assert.That(layout.Shops.Count, Is.InRange(18, 24));
            Assert.That(layout.Houses.Count, Is.InRange(8, 24));
            Assert.That(layout.Stalls.Count, Is.InRange(30, 40));
            Assert.That(layout.Handcarts.Count + layout.Oxcarts.Count, Is.InRange(8, 12));
            Assert.That(layout.Oxcarts.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(layout.Wells.Count, Is.InRange(2, 3));
            Assert.That(layout.Trees.Count, Is.InRange(20, 30));
            var byKind = walls.GroupBy(w => w.Kind).ToDictionary(g => g.Key, g => g.ToList());
            Assert.That(byKind.Keys.OrderBy(k => k, StringComparer.Ordinal),
                Is.EqualTo(new[] { "handcart", "oxcart", "solid-house", "stall", "tree", "wall", "ward-wall", "well" }));
            Assert.That(byKind["wall"].Count, Is.EqualTo(10 * layout.Shops.Count));
            Assert.That(byKind["solid-house"].Count, Is.EqualTo(layout.Houses.Count));
            Assert.That(byKind["stall"].Count, Is.EqualTo(layout.Stalls.Count));
            Assert.That(byKind["handcart"].Count, Is.EqualTo(layout.Handcarts.Count));
            Assert.That(byKind["oxcart"].Count, Is.EqualTo(layout.Oxcarts.Count));
            Assert.That(byKind["well"].Count, Is.EqualTo(layout.Wells.Count));
            Assert.That(byKind["tree"].Count, Is.EqualTo(layout.Trees.Count));
            // Hand-added heights kind by kind (the prefab pieces are below).
            var heights = new Dictionary<string, long>
            {
                ["ward-wall"] = 196608, ["solid-house"] = 196608, ["stall"] = 65536, ["handcart"] = 65536, ["oxcart"] = 131072, ["well"] = 65536, ["tree"] = 196608,
            };
            foreach (var pair in heights)
                Assert.That(byKind[pair.Key].Select(w => (w.HeightRaw, w.HasBase)).Distinct(), Is.EqualTo(new[] { (pair.Value, false) }), pair.Key);
            Assert.That(byKind["stall"].All(w => w.SizeX * w.SizeZ == 2 && Math.Min(w.SizeX, w.SizeZ) == 1), Is.True, "stalls are 2 x 1");
            Assert.That(byKind["handcart"].All(w => w.SizeX * w.SizeZ == 2 && Math.Min(w.SizeX, w.SizeZ) == 1), Is.True, "hand carts are 2 x 1");
            Assert.That(byKind["oxcart"].All(w => w.SizeX * w.SizeZ == 6 && Math.Min(w.SizeX, w.SizeZ) == 2), Is.True, "ox carts are 3 x 2");
            Assert.That(byKind["well"].Concat(byKind["tree"]).All(w => w.SizeX == 1 && w.SizeZ == 1), Is.True, "wells and trunks are 1 x 1");
            Assert.That(byKind["solid-house"].All(w => Math.Min(w.SizeX, w.SizeZ) >= 6), Is.True, "solid houses are at least 6 a side");
            // Stalls in a street run along it.
            foreach (var w in byKind["stall"].Where(v => WardCheck.InBand(v.CellX)))
                Assert.That((w.SizeX, w.SizeZ), Is.EqualTo((1, 2)), "stall " + w.Id + " on the north-south street");
            foreach (var w in byKind["stall"].Where(v => WardCheck.InBand(v.CellZ) && !WardCheck.InStreet(v.CellX)))
                Assert.That((w.SizeX, w.SizeZ), Is.EqualTo((2, 1)), "stall " + w.Id + " on the east-west street");
        }

        // Each shop: one door part and one window part on the street face, the door's outside cell in a street band, the
        // prefab pieces at the card's heights (frames 3 m, sill 0.9 m, lintels 1 m on a 2 m base).
        [Test]
        public void EachShopHasOneDoorAndOneWindowOnItsStreet()
        {
            MapDocument doc = Built();
            var walls = doc.Walls();
            var doors = walls.Where(w => w.Stamp.StartsWith("door-", StringComparison.Ordinal)).ToList();
            var windows = walls.Where(w => w.Stamp.StartsWith("window-", StringComparison.Ordinal)).ToList();
            WardLayout layout = WardLayout.Default;
            Assert.That(doors.Select(w => w.Stamp).Distinct().Count(), Is.EqualTo(layout.Shops.Count));
            Assert.That(windows.Select(w => w.Stamp).Distinct().Count(), Is.EqualTo(layout.Shops.Count));
            Assert.That(doors.Count, Is.EqualTo(3 * layout.Shops.Count));
            Assert.That(windows.Count, Is.EqualTo(4 * layout.Shops.Count));
            foreach (var w in doors.Concat(windows).Where(v => v.HasBase))
                Assert.That((w.HeightRaw, w.BaseRaw, w.SizeX, w.SizeZ), Is.EqualTo((65536L, 131072L, 1, 1)), "lintel " + w.Id);
            foreach (var w in doors.Concat(windows).Where(v => !v.HasBase))
                Assert.That(w.HeightRaw, Is.EqualTo(w.Stamp.StartsWith("window-", StringComparison.Ordinal) && IsSill(w, layout) ? 58982L : 196608L), "piece " + w.Id);
            Assert.That(windows.Count(w => !w.HasBase && w.HeightRaw == 58982), Is.EqualTo(layout.Shops.Count), "one sill a shop");
            foreach (WardShop s in layout.Shops)
            {
                var lintels = doors.Where(w => w.HasBase && (w.CellX, w.CellZ) == s.Door).ToList();
                Assert.That(lintels.Count, Is.EqualTo(1), "door of " + s);
                Assert.That(windows.Count(w => w.HasBase && (w.CellX, w.CellZ) == s.Window), Is.EqualTo(1), "window of " + s);
                (int ox, int oz) = s.Outside(1);
                Assert.That(s.AlongZ ? WardCheck.InBand(ox) : WardCheck.InBand(oz), Is.True, "door of " + s + " opens on a street band");
                Assert.That(doors.Count(w => w.Stamp == lintels[0].Stamp), Is.EqualTo(3));
            }
            Assert.That(walls.Where(w => w.Kind == "wall" && w.Stamp == "").Select(w => (w.HeightRaw, w.HasBase)).Distinct(),
                Is.EqualTo(new[] { (196608L, false) }), "back and side walls");
        }

        private static bool IsSill(MapWallInfo w, WardLayout layout) => layout.Shops.Any(s => (w.CellX, w.CellZ) == s.Window);

        // ---- D1 to D14 ----

        private static void Refused(WardLayout layout, string item)
        {
            List<string> found = Check(layout);
            Assert.That(found.Any(f => f.StartsWith(item + ":", StringComparison.Ordinal)), Is.True, item + " expected; found:\n" + string.Join("\n", found));
        }

        [Test]
        public void D1_AStallOneCellFromAnotherPieceIsRefused()
        {
            WardLayout l = WardLayout.Default;
            l.Stalls[0] = new WardBox(75, 19, 1, 2); // a one-cell gap to the tree at (75, 22)
            Refused(l, "rule 2");
        }

        [Test]
        public void D2_TwoFloorWallsOnOneCellAreRefused()
        {
            WardLayout l = WardLayout.Default;
            l.Stalls[0] = new WardBox(75, 21, 1, 2); // over the tree at (75, 22)
            Refused(l, "rule 1");
        }

        [Test]
        public void D3_ATreeNearAPointIsRefused()
        {
            WardLayout l = WardLayout.Default;
            l.Trees.Add(new WardBox(74, 74, 1, 1));
            Refused(l, "rule 4");
        }

        [Test]
        public void D4_ACartOnTheSecondCellInFrontOfADoorIsRefused()
        {
            WardLayout l = WardLayout.Default;
            (int x, int z) = l.Shops[0].Outside(2);
            l.Handcarts.Add(new WardBox(x, z, 1, 2));
            Refused(l, "rule 3");
        }

        [Test]
        public void D5_AStallJustInsideAGateIsRefused()
        {
            WardLayout l = WardLayout.Default;
            l.Stalls.Add(new WardBox(11, 78, 1, 2));
            Refused(l, "rule 4");
        }

        [Test]
        public void D6_AWellOneAndAHalfMetresHighIsRefused()
        {
            WardLayout l = WardLayout.Default;
            l.WellRaw = 98304;
            Refused(l, "rule 5");
        }

        [Test]
        public void D7_FortyOneStallsAreRefused()
        {
            WardLayout l = WardLayout.Default;
            for (int k = 0; l.Stalls.Count < 41; k++) l.Stalls.Add(new WardBox(16 + 3 * k, 30, 1, 2));
            Refused(l, "W4");
        }

        [Test]
        public void D8_HousesThatShutABlockOffItsLaneAreRefused()
        {
            WardLayout l = WardLayout.Default;
            l.Houses.Add(new WardBox(14, 14, 6, 59));
            l.Houses.Add(new WardBox(20, 14, 53, 6));
            Refused(l, "rule 6");
        }

        [Test]
        public void D9_AShopInTheSquareIsRefused()
        {
            WardLayout l = WardLayout.Default;
            l.Shops.Add(new WardShop(WardFacing.East, 72, 120));
            Refused(l, "W3");
        }

        [Test]
        public void D10_AStallOnTheCarriagewayIsRefused()
        {
            WardLayout l = WardLayout.Default;
            l.Stalls.Add(new WardBox(79, 40, 1, 2));
            Refused(l, "W3");
        }

        [Test]
        public void D11_AStallOnTheStartingSquadIsRefused()
        {
            WardLayout l = WardLayout.Default;
            l.Stalls.Add(new WardBox(20, 80, 2, 1));
            Refused(l, "rule 4");
        }

        [Test]
        public void D12_ABlockInTheDoorwayUnderTheLintelIsRefused()
        {
            WardLayout l = WardLayout.Default;
            (int x, int z) = l.Shops[0].Door;
            l.Oxcarts.Add(new WardBox(x, z, 1, 1)); // 2 m high, touching the lintel's 2 m base
            Refused(l, "rule 3");
        }

        [Test]
        public void D13_AStallOnADeployPlaceOfABaseIsRefused()
        {
            WardLayout l = WardLayout.Default;
            l.Stalls.Add(new WardBox(1, 77, 2, 1)); // covers (1.75, 77.75)
            Refused(l, "rule 4");
        }

        [Test]
        public void D14_TwoStallsTouchingOnlyAtACornerAreNotAGap()
        {
            WardLayout l = WardLayout.Default;
            Assert.That(l.Stalls[5], Is.EqualTo(new WardBox(84, 16, 1, 2)));
            l.Stalls[5] = new WardBox(76, 18, 1, 2); // corner to corner with the stall at (75, 16)
            List<string> found = Check(l);
            Assert.That(found.Where(f => f.StartsWith("rule 2:", StringComparison.Ordinal)), Is.Empty, string.Join("\n", found));
            Assert.That(found, Is.Empty, string.Join("\n", found));
        }

        [Test]
        public void TheGapRuleCountsCornersAsTouching()
        {
            var a = new WardCheck.Piece("a", new WardBox(10, 10, 2, 2), true);
            Assert.That(WardCheck.Gap(a, new WardCheck.Piece("b", new WardBox(12, 12, 1, 1), true)), Is.EqualTo(0), "corner");
            Assert.That(WardCheck.Gap(a, new WardCheck.Piece("b", new WardBox(12, 10, 1, 1), true)), Is.EqualTo(0), "side");
            Assert.That(WardCheck.Gap(a, new WardCheck.Piece("b", new WardBox(13, 13, 1, 1), true)), Is.EqualTo(1), "one cell off on both axes");
            Assert.That(WardCheck.Gap(a, new WardCheck.Piece("b", new WardBox(7, 10, 2, 1), true)), Is.EqualTo(1), "one cell to the west");
            Assert.That(WardCheck.Gap(a, new WardCheck.Piece("b", new WardBox(10, 14, 1, 1), true)), Is.EqualTo(2), "two cells to the north");
        }
    }
}
