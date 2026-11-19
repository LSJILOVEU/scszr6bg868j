using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ML-3 acceptance 10 (the window prefab: its gate on each side of every bound, and a shot from behind a window on a
    // 3 m floor at the street) and the wall-height report.
    public sealed class Ml3WindowTests
    {
        private const long M = Ml3World.M;
        private static readonly MapNext Next = new MapNext(1, 1, 1, 1);

        private static MapDesc Desc(string id, int width, int depth, int[][] heights) =>
            new MapDesc(id, "none", "ml3-body", new MapXZ(width, depth), new MapXZ(0, 0), 0, 10 * M, new[] { new MapLayer("walls", 1) },
                heights, null, new MapWall[0], new MapPoint[0], new MapBase[0], new MapUnit[0], new MapRun(41, "teamIdle"),
                new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0], Next);

        private static MapDesc Flat() => Desc("ml3-flat", 12, 4, null);

        private static MapStampPlan Window(MapDesc d, CombatBody body, long sill = MapPrefabs.DefaultSillRaw, long opening = MapPrefabs.DefaultOpeningRaw, long top = 4 * M) =>
            MapPrefabs.Window(d, "win", new MapXZ(0, 1), true, 10, 4, 2, top, "walls", "wall", body, sill, opening);

        // An accepted window: a refusal fails the assertion here instead of erroring the test.
        private static MapStampPlan Accepted(CombatBody body, long sill = MapPrefabs.DefaultSillRaw, long opening = MapPrefabs.DefaultOpeningRaw)
        {
            MapStampPlan plan = null;
            Assert.DoesNotThrow(() => plan = Window(Flat(), body, sill, opening));
            return plan;
        }

        // Flat map: four walls, the sill a floor wall 0.9 m high, the lintel from 2.0 m to the top.
        [Test]
        public void FourWallsWithTheSillAndLintelAtThePlaceholders()
        {
            MapStampPlan plan = Window(Flat(), Ml3World.Rwr);
            Assert.That(plan.Walls.Select(e => e.Src), Is.EqualTo(new ulong[] { 1, 2, 3, 4 }));
            MapWall[] walls = plan.Walls.Select(e => e.Template).ToArray();
            Assert.That(walls.Select(w => (w.Cell.X, w.Cell.Z, w.Size.X, w.Size.Z)), Is.EqualTo(new[] { (0, 1, 4, 1), (4, 1, 2, 1), (4, 1, 2, 1), (6, 1, 4, 1) }));
            Assert.That(walls.Select(w => w.BaseRaw), Is.EqualTo(new long?[] { null, null, 2 * M, null }));
            Assert.That(walls.Select(w => w.HeightRaw), Is.EqualTo(new[] { 4 * M, 58982L, 2 * M, 4 * M }));
        }

        // Gate: sill >= 2r = 32768 raw (an odd 32767 grows 1 raw to 32768 and passes; 32766 is refused).
        [Test]
        public void SillAtLeastTwiceTheRadius()
        {
            Assert.That(Accepted(Ml3World.Rwr, sill: 32768).Walls[1].Template.HeightRaw, Is.EqualTo(32768));
            Assert.That(Accepted(Ml3World.Rwr, sill: 32767).Walls[1].Template.HeightRaw, Is.EqualTo(32768));
            Assert.That(Assert.Throws<ArgumentException>(() => Window(Flat(), Ml3World.Rwr, sill: 32766)).Message, Does.StartWith("the sill is lower than 2 x the unit radius"));
        }

        // Gate: sill <= min(eye, muzzle) - r and opening top >= max(eye, muzzle) + r. With eye 1.5 and muzzle 1.25 m (and the
        // other way round): sill limit 1.0 m (65536 raw), opening limit 1.75 m (114688 raw); an odd value grows 1 raw first.
        [TestCase(98304L, 81920L)]
        [TestCase(81920L, 98304L)]
        public void SillAndOpeningAgainstEyeAndMuzzle(long eye, long muzzle)
        {
            CombatBody body = Ml3World.Body(114688, eye, muzzle);
            Assert.That(Accepted(body, sill: 65536).Walls[1].Template.HeightRaw, Is.EqualTo(65536));
            Assert.That(Assert.Throws<ArgumentException>(() => Window(Flat(), body, sill: 65537)).Message, Does.StartWith("the sill is higher than min(eye, muzzle)"));
            Assert.That(Accepted(body, opening: 114688).Walls[2].Template.BaseRaw, Is.EqualTo(114688));
            Assert.That(Accepted(body, opening: 114687).Walls[2].Template.BaseRaw, Is.EqualTo(114688));
            Assert.That(Assert.Throws<ArgumentException>(() => Window(Flat(), body, opening: 114686)).Message, Does.StartWith("the opening top is lower than max(eye, muzzle)"));
        }

        // The RWR body's limits: sill up to 1.25 m, opening from 1.75 m; the placeholders 0.9 and 2.0 sit inside.
        [Test]
        public void RwrLimits()
        {
            Assert.DoesNotThrow(() => Window(Flat(), Ml3World.Rwr, sill: 81920, opening: 114688));
            Assert.Throws<ArgumentException>(() => Window(Flat(), Ml3World.Rwr, sill: 81922));
        }

        [Test]
        public void ArgumentsLikeTheDoor()
        {
            Assert.That(Assert.Throws<ArgumentException>(() => MapPrefabs.Window(Flat(), "w", new MapXZ(0, 1), true, 10, 0, 2, 4 * M, "walls", "wall", Ml3World.Rwr)).ParamName, Is.EqualTo("windowStart"));
            Assert.That(Assert.Throws<ArgumentException>(() => MapPrefabs.Window(Flat(), "w", new MapXZ(0, 1), true, 10, 4, 0, 4 * M, "walls", "wall", Ml3World.Rwr)).ParamName, Is.EqualTo("windowWidth"));
            Assert.That(Assert.Throws<ArgumentException>(() => MapPrefabs.Window(Flat(), "w", new MapXZ(0, 1), true, 10, 8, 2, 4 * M, "walls", "wall", Ml3World.Rwr)).ParamName, Is.EqualTo("length"));
            Assert.That(Assert.Throws<ArgumentException>(() => MapPrefabs.Window(Flat(), "w", new MapXZ(3, 1), true, 10, 4, 2, 4 * M, "walls", "wall", Ml3World.Rwr)).ParamName, Is.EqualTo("start"));
            Assert.That(Assert.Throws<ArgumentException>(() => Window(Flat(), Ml3World.Rwr, top: 2 * M)).ParamName, Is.EqualTo("topRaw"));
            Assert.That(Assert.Throws<ArgumentException>(() => Window(Flat(), Ml3World.Rwr, top: 11 * M)).ParamName, Is.EqualTo("topRaw"));
        }

        // The "second floor": a 20 x 8 m map whose west part is a 3 m plateau (height points x <= 10 at 3 m, x = 11 at 1.5 m,
        // x >= 12 at 0, the street). A wall along z at x = 9 (1 m thick, cells x 9..10 on the plateau) with a 2-cell window at
        // z 3..4: sill top 3.9 m, opening top 5.0 m, wall top 7 m.
        internal static MapDesc SecondFloor()
        {
            var heights = new int[9][];
            for (int j = 0; j <= 8; j++)
                heights[j] = Enumerable.Range(0, 21).Select(i => i <= 10 ? (int)(3 * M) : i == 11 ? (int)(3 * M / 2) : 0).ToArray();
            MapDesc start = Desc("ml3-window", 20, 8, heights);
            var edit = new MapEdit(start);
            edit.Stamp(MapPrefabs.Window(start, "win", new MapXZ(9, 0), false, 8, 3, 2, 7 * M, "walls", "wall", Ml3World.Rwr));
            return edit.Current;
        }

        private static WcMap Baked() => WcMapReader.Read(MapBake.Run(MapDescJson.Write(SecondFloor())).WcMapBytes);

        [Test]
        public void TerrainWindowStandsOnTheFloor()
        {
            MapWall[] walls = SecondFloor().Walls.ToArray();
            Assert.That(walls.Select(w => (w.BaseRaw, w.HeightRaw)), Is.EqualTo(new (long?, long)[] { (null, 7 * M), (3 * M, 58982L), (5 * M, 2 * M), (null, 7 * M) }));
            WcMap map = Baked();
            Assert.That(new[] { map.IsPassable(9, 2), map.IsPassable(9, 3), map.IsPassable(9, 4), map.IsPassable(8, 3) }, Is.EqualTo(new[] { false, false, false, true }));
        }

        // Acceptance 10: the shooter stands 0.35 m inside the wall's inner face (x = 8.65 m, feet at 3 m, muzzle 4.5 m), a
        // street target at z = 4 m. 7 m away (x = 15.65) the line falls 3 m over 7 m (0.43 < 0.48): over the sill's far edge
        // at 3.92 m, a hit. 5 m away (x = 13.65) it falls 0.6 per metre: 3.69 m at the far edge, the sill stops it. A shooter
        // 2 m back (x = 7) is stopped 7 m away too (3.21 m at the far edge).
        [TestCase(8.65, 15.65, FireOutcome.Hit)]
        [TestCase(8.65, 13.65, FireOutcome.WallBlocked)]
        [TestCase(7.0, 14.0, FireOutcome.WallBlocked)]
        public void SecondFloorWindowShotAtTheStreet(double shooterX, double targetX, FireOutcome outcome)
        {
            long sx = (long)Math.Round(shooterX * 100) * M / 100, tx = (long)Math.Round(targetX * 100) * M / 100;
            var w = new Ml3World(Ml3World.Pack(Ml3World.Rwr), new[] { new Ml3World.Unit(sx, 3 * M, 4 * M, 1), new Ml3World.Unit(tx, 0, 4 * M, 2, -1) }, map: Baked());
            w.Produce = c => w.Combat.Submit(c, FireIntent.Aimed(w.Id(0), w.Point(1)));
            FireRecord f = w.StepFires().Single();
            Assert.That((f.Outcome, f.Origin.Y.Raw), Is.EqualTo((outcome, 3 * M + 98304)));
            if (outcome == FireOutcome.WallBlocked) Assert.That(f.End.X.Raw, Is.InRange(9 * M - 1, 10 * M), "stopped at the sill (the near face may round 1 raw short)");
            Assert.That(w.Combat.Visible(w.U(0), w.U(1)), Is.EqualTo(outcome == FireOutcome.Hit));
        }

        // ---- wall-height report ----

        // Flat map, RWR body (eye 1.5, height 1.75 m): walls 1.0 and 1.5 m let sight over; 1.6 m hides the eye but shows the
        // body; 1.75 and 3 m are not listed.
        [Test]
        public void ReportOnAFlatMap()
        {
            var edit = new MapEdit(Desc("ml3-report", 12, 12, null));
            long[] heights = { M, 98304, 104858, 114688, 3 * M };
            for (int i = 0; i < heights.Length; i++)
                edit.Stamp(new MapStampPlan("w" + i, Array.Empty<MapHeightWrite>(), Array.Empty<MapSurfaceWrite>(),
                    new[] { new MapStampEntry<MapWall>(1, new MapWall(0, "wall", "walls", new MapXZ(1, 1 + 2 * i), new MapXZ(3, 1), heights[i])) },
                    Array.Empty<MapStampEntry<MapPoint>>(), Array.Empty<MapStampEntry<MapBase>>(), Array.Empty<MapStampEntry<MapUnit>>()));
            var report = MapGate.WallHeights(MapDescJson.Write(edit.Current), Ml3World.Rwr);
            Assert.That(report.Select(l => (l.CellZ, l.TopAboveGroundRaw, l.Band)), Is.EqualTo(new[]
            {
                (1, M, WallSightBand.SightPasses), (3, 98304L, WallSightBand.SightPasses), (5, 104858L, WallSightBand.BodyShows),
            }));
        }

        // Slope: a 2 m wall over ground whose corners under it rise 0 .. 0.6 m reads 2.0 - 0.6 = 1.4 m (sight passes); from
        // the lowest corner it would read 2.0 m and not be listed.
        [Test]
        public void ReportReadsTheHighestGroundUnderTheWall()
        {
            var heights = new int[5][];
            for (int j = 0; j <= 4; j++) heights[j] = Enumerable.Range(0, 7).Select(i => (int)(i * M / 10)).ToArray();
            var edit = new MapEdit(Desc("ml3-report-slope", 6, 4, heights));
            edit.Stamp(new MapStampPlan("w", Array.Empty<MapHeightWrite>(), Array.Empty<MapSurfaceWrite>(),
                new[] { new MapStampEntry<MapWall>(1, new MapWall(0, "wall", "walls", new MapXZ(0, 1), new MapXZ(6, 1), 2 * M)) },
                Array.Empty<MapStampEntry<MapPoint>>(), Array.Empty<MapStampEntry<MapBase>>(), Array.Empty<MapStampEntry<MapUnit>>()));
            var report = MapGate.WallHeights(MapDescJson.Write(edit.Current), Ml3World.Rwr);
            Assert.That(report.Select(l => (l.TopAboveGroundRaw, l.Band)), Is.EqualTo(new[] { (2 * M - 6 * M / 10, WallSightBand.SightPasses) }));
        }

        // The second-floor window: the sill (0.9 m above its floor) lets sight over; frames and lintel are not listed.
        [Test]
        public void ReportListsTheWindowSill()
        {
            var report = MapGate.WallHeights(MapDescJson.Write(SecondFloor()), Ml3World.Rwr);
            Assert.That(report.Select(l => (l.CellX, l.CellZ, l.TopAboveGroundRaw, l.Band)), Is.EqualTo(new[] { (9, 3, 58982L, WallSightBand.SightPasses) }));
        }
    }
}
