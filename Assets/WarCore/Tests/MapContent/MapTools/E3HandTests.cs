using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Fixtures;
using static WarCore.Tests.E3Plans;

namespace WarCore.Tests
{
    // Acceptance 3 (tombstones, monotonic ids), 6 (immutability and atomic steps), 8 (edges).
    public sealed class E3HandTests
    {
        private static byte[] Bytes(MapEdit e) => MapDescJson.Write(e.Current);

        [Test]
        public void DeletedStampOutputStaysDeleted()
        {
            var e = new MapEdit(Read(Plain()));
            e.Stamp(S1());
            Assert.That(e.Current.Walls.Select(w => w.Id).ToArray(), Is.EqualTo(new ulong[] { 1, 2 }));
            Assert.That(e.Current.Next.Wall, Is.EqualTo(3UL));
            MapStamp[] rows = e.Current.Stamps.ToArray();

            e.Remove("wall", 2);
            MapStampResult r = e.Stamp(S1());

            Assert.That(e.Current.Walls.Any(w => w.Src.Src == 2), Is.False);
            Assert.That(e.Current.Walls.Count, Is.EqualTo(1));
            Assert.That(r.EntriesSkipped, Is.EqualTo(1));
            Assert.That(e.Current.Removed.Count, Is.EqualTo(1));
            Assert.That(new[] { e.Current.Removed[0].Instance, e.Current.Removed[0].Kind }, Is.EqualTo(new[] { "s1", "wall" }));
            Assert.That(e.Current.Removed[0].Src, Is.EqualTo(2UL));
            Assert.That(e.Current.Stamps, Is.EqualTo(rows));

            MapDesc back = MapDescJson.Read(Bytes(e));
            Assert.That(back.Removed.Select(s => s.Instance + "/" + s.Kind + "/" + s.Src),
                Is.EqualTo(e.Current.Removed.Select(s => s.Instance + "/" + s.Kind + "/" + s.Src)));
            Assert.That(back.Stamps.Select(s => s.Instance + "/" + s.Kind + "/" + s.Src + "/" + s.Id),
                Is.EqualTo(e.Current.Stamps.Select(s => s.Instance + "/" + s.Kind + "/" + s.Src + "/" + s.Id)));
            Assert.That(back.Stamps.Count, Is.EqualTo(5));

            ulong added = e.AddWall(WallAt(6, 5));
            Assert.That(added, Is.EqualTo(3UL));
            Assert.That(e.Current.Next.Wall, Is.EqualTo(4UL));
            Assert.That(e.Current.Walls.Last().Owner, Is.EqualTo(0));
            Assert.That(e.Current.Walls.Last().Src, Is.Null);
        }

        [Test]
        public void ReplaceKeepsArrayPositionAndSrc()
        {
            var e = new MapEdit(Read(ThreeWalls()));
            e.ReplaceWall(new MapWall(2, "wall", "walls", new MapXZ(6, 5), new MapXZ(1, 1), 65536, null,
                new MapSrc("s7", "wall", 9)));
            Assert.That(e.Current.Walls.Select(w => w.Id).ToArray(), Is.EqualTo(new ulong[] { 1, 2, 3 }));
            Assert.That(e.Current.Walls[1].Cell, Is.EqualTo(new MapXZ(6, 5)));
            Assert.That(e.Current.Walls[1].HeightRaw, Is.EqualTo(65536L));
            Assert.That(e.Current.Walls[1].Src, Is.Null);
            Assert.That(e.Current.Walls[1].Owner, Is.EqualTo(0));
        }

        [Test]
        public void FullWallPoolRefusesAHandWall()
        {
            var e = new MapEdit(Read(WallPoolFull()));
            byte[] before = Bytes(e);
            var x = Assert.Throws<InvalidOperationException>(() => e.AddWall(WallAt(0, 0)));
            Assert.That(x.Message, Is.EqualTo("MAPDESC_ID wall"));
            Assert.That(Bytes(e), Is.EqualTo(before));
        }

        [Test]
        public void PlanCopiesItsInputs()
        {
            MapHeightWrite[] heights = Corners(0, 4, 0, 4, 1000);
            MapSurfaceWrite[] surfaces = Cells(0, 3, 0, 3, "road");
            var walls = new[] { new MapStampEntry<MapWall>(1, WallAt(0, 0)), new MapStampEntry<MapWall>(2, WallAt(2, 2)) };
            var units = new[] { new MapStampEntry<MapUnit>(1, UnitAt(1500, 1500)) };
            var points = new[] { new MapStampEntry<MapPoint>(1, PointAt(2500, 500)) };
            var bases = new[] { new MapStampEntry<MapBase>(1, BaseAt(500, 2500)) };
            var plan = new MapStampPlan("s1", heights, surfaces, walls, points, bases, units);
            heights[0] = new MapHeightWrite(7, 6, 9000);
            surfaces[0] = new MapSurfaceWrite(7, 5, "mud");
            walls[0] = new MapStampEntry<MapWall>(9, WallAt(7, 5));
            units[0] = new MapStampEntry<MapUnit>(9, UnitAt(7500, 5500));
            points[0] = new MapStampEntry<MapPoint>(9, PointAt(7500, 5500));
            bases[0] = new MapStampEntry<MapBase>(9, BaseAt(7500, 5500));

            Assert.That(plan.Heights[0].Raw, Is.EqualTo(1000));
            Assert.That(plan.Walls[0].Src, Is.EqualTo(1UL));
            var e = new MapEdit(Read(Plain()));
            e.Stamp(plan);
            var fresh = new MapEdit(Read(Plain()));
            fresh.Stamp(S1());
            Assert.That(Bytes(e), Is.EqualTo(Bytes(fresh)));
            Assert.That(new object[] { e.Current.Points[0].Src.Src, e.Current.Points[0].At, e.Current.Points[0].RadiusMm },
                Is.EqualTo(new object[] { 1UL, new MapMm(2500, 500), 400 }));
            Assert.That(new object[] { e.Current.Bases[0].Src.Src, e.Current.Bases[0].At, e.Current.Bases[0].Team },
                Is.EqualTo(new object[] { 1UL, new MapMm(500, 2500), 1 }));
            Assert.That(new object[] { e.Current.Units[0].Src.Src, e.Current.Units[0].At }, Is.EqualTo(new object[] { 1UL, new MapMm(1500, 1500) }));
            Assert.That(new object[] { e.Current.Walls[0].Src.Src, e.Current.Walls[0].Cell }, Is.EqualTo(new object[] { 1UL, new MapXZ(0, 0) }));
            Assert.That(e.Current.Heights[0][0], Is.EqualTo(1000));
            Assert.That(SurfaceName(e.Current, 0, 0), Is.EqualTo("road"));
        }

        [Test]
        public void EntriesOffTheMapAreRefused()
        {
            var e = new MapEdit(Read(Plain()));
            e.Stamp(S1());
            byte[] before = Bytes(e);
            Assert.Throws<ArgumentOutOfRangeException>(() => e.AddWall(WallAt(W, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => e.AddWall(new MapWall(0, "wall", "walls", new MapXZ(7, 4), new MapXZ(1, 3), WallHeight)));
            Assert.Throws<ArgumentOutOfRangeException>(() => e.AddUnit(UnitAt(W * 1000, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => e.AddPoint(new MapPoint(0, new MapMm(500, 500), 400, 0, new MapMm(-1, 500))));
            Assert.Throws<ArgumentOutOfRangeException>(() => e.AddBase(BaseAt(500, D * 1000)));
            Assert.Throws<ArgumentOutOfRangeException>(() => e.ReplaceWall(WallAt(-1, 0, 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => e.ReplaceUnit(new MapUnit(1, Soldier, 1, 0, new MapMm(0, -1), false)));
            var off = new MapStampPlan("s2", Corners(5, 8, 5, 6, 3000), Cells(4, 7, 4, 5, "mud"),
                new[] { new MapStampEntry<MapWall>(1, WallAt(7, 5)) }, new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0],
                new[] { new MapStampEntry<MapUnit>(1, UnitAt(7500, 5500)), new MapStampEntry<MapUnit>(2, UnitAt(8000, 5500)) });
            Assert.Throws<ArgumentOutOfRangeException>(() => e.Stamp(off));
            Assert.That(Bytes(e), Is.EqualTo(before));
            Assert.That(e.AddWall(WallAt(W - 1, D - 1)), Is.EqualTo(3UL));
            Assert.That(e.AddUnit(UnitAt(W * 1000 - 1, D * 1000 - 1)), Is.EqualTo(2UL));
        }

        [Test]
        public void CollectionsAreReadOnly()
        {
            var e = new MapEdit(Read(Plain()));
            e.Stamp(S1());
            Assert.Throws<NotSupportedException>(() => ((IList<MapWall>)e.Current.Walls)[0] = WallAt(5, 5));
            Assert.Throws<NotSupportedException>(() => ((IList<MapStamp>)e.Current.Stamps).Add(new MapStamp("s9", "wall", 1, 9)));
            Assert.Throws<NotSupportedException>(() => ((IList<int>)e.Current.Heights[0])[0] = 5);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)e.Current.Owners).Add("stamp:s9"));
            MapStampPlan plan = S1();
            Assert.Throws<NotSupportedException>(() => ((IList<MapHeightWrite>)plan.Heights).Add(new MapHeightWrite(0, 0, 1)));
            Assert.Throws<NotSupportedException>(() => ((IList<MapStampEntry<MapWall>>)plan.Walls).Clear());
        }

        [Test]
        public void FailedStepsLeaveCurrentUnchanged()
        {
            var e = new MapEdit(Read(Plain()));
            e.Stamp(S1());
            byte[] before = Bytes(e);
            Assert.Throws<ArgumentOutOfRangeException>(() => e.SetHeight(W + 1, 0, 5));
            Assert.That(Bytes(e), Is.EqualTo(before));

            MapSurfaceWrite[] surfaces = Cells(4, 7, 4, 5, "mud");
            surfaces[surfaces.Length - 1] = new MapSurfaceWrite(7, 5, "lava");
            var bad = new MapStampPlan("s2", Corners(5, 8, 5, 6, 3000), surfaces,
                new[] { new MapStampEntry<MapWall>(1, WallAt(7, 5)) }, new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0],
                new MapStampEntry<MapUnit>[0]);
            Assert.Throws<ArgumentException>(() => e.Stamp(bad));
            Assert.That(Bytes(e), Is.EqualTo(before));
        }

        [Test]
        public void ReleaseHandFloorsNegativeMillimetres()
        {
            var e = new MapEdit(Read(NegativeOrigin()));
            Assert.That(e.EntryOwner("unit", 1), Is.EqualTo(MapEdit.Hand));
            e.ReleaseHand(3, 3, 3, 3);
            Assert.That(e.Current.Units[0].Owner, Is.Null);
            Assert.That(e.EntryOwner("unit", 1), Is.Null);
        }

        [Test]
        public void EmptyBoxIsRefused()
        {
            var e = new MapEdit(Read(Plain()));
            e.SetSurface(1, 0, "mud");
            byte[] before = Bytes(e);
            Assert.Throws<ArgumentException>(() => e.ReleaseHand(2, 0, 1, 0));
            Assert.That(Bytes(e), Is.EqualTo(before));
        }

        [Test]
        public void LastCellAndCornerAreInRange()
        {
            var e = new MapEdit(Read(Plain()));
            e.SetSurface(W - 1, D - 1, "mud");
            e.SetHeight(W, D, 500);
            Assert.That(e.SurfaceOwner(W - 1, D - 1), Is.EqualTo(MapEdit.Hand));
            Assert.That(e.HeightOwner(W, D), Is.EqualTo(MapEdit.Hand));
            e.ReleaseHand(W - 1, D - 1, W - 1, D - 1);
            Assert.That(e.SurfaceOwner(W - 1, D - 1), Is.Null);
            Assert.That(e.HeightOwner(W, D), Is.Null);
            Assert.That(e.Current.Heights[D][W], Is.EqualTo(500));
            byte[] before = Bytes(e);
            Assert.Throws<ArgumentOutOfRangeException>(() => e.SetSurface(W, 0, "mud"));
            Assert.Throws<ArgumentOutOfRangeException>(() => e.SetHeight(W + 1, 0, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => e.ReleaseHand(0, 0, W, 0));
            Assert.That(Bytes(e), Is.EqualTo(before));
        }

        [Test]
        public void FullOwnerTable()
        {
            var e = new MapEdit(Read(Owners64()));
            string[] owners = e.Current.Owners.ToArray();
            Assert.That(owners.Length, Is.EqualTo(64));
            MapStampResult r = e.Stamp(S1());
            Assert.That(r.SurfacesWritten, Is.EqualTo(16));
            Assert.That(e.Current.Owners, Is.EqualTo(owners));
            Assert.That(e.SurfaceOwner(0, 0), Is.EqualTo("stamp:s1"));

            byte[] before = Bytes(e);
            var s9 = new MapStampPlan("s9", Corners(6, 6, 6, 6, 10), new MapSurfaceWrite[0], new MapStampEntry<MapWall>[0],
                new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0]);
            var x = Assert.Throws<InvalidOperationException>(() => e.Stamp(s9));
            Assert.That(x.Message, Is.EqualTo("MAPDESC_OWNER"));
            Assert.That(Bytes(e), Is.EqualTo(before));
        }

        [Test]
        public void EmptyInstanceNameIsRefused()
        {
            Assert.Throws<ArgumentException>(() => new MapStampPlan("", new MapHeightWrite[0], new MapSurfaceWrite[0],
                new MapStampEntry<MapWall>[0], new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0]));
        }

        [Test]
        public void FullWallPoolRefusesAStampWall()
        {
            var e = new MapEdit(Read(WallPoolFull()));
            byte[] before = Bytes(e);
            var x = Assert.Throws<InvalidOperationException>(() => e.Stamp(S1()));
            Assert.That(x.Message, Is.EqualTo("MAPDESC_ID wall"));
            Assert.That(Bytes(e), Is.EqualTo(before));
        }
    }
}
