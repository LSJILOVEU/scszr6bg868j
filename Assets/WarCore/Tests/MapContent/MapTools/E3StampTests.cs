using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Fixtures;
using static WarCore.Tests.E3Plans;

namespace WarCore.Tests
{
    // Acceptance 1 (hand edits survive a stamp rerun), 2 (idempotence), 4 (overlapping stamps), 5 (ReleaseHand).
    public sealed class E3StampTests
    {
        private static byte[] Bytes(MapEdit e) => MapDescJson.Write(e.Current);

        // Stamp s1, then by hand: 2 height points, 3 surface cells, and the wall of src 2 moved from (2, 2) to (3, 3).
        internal static MapEdit HandEdited()
        {
            var e = new MapEdit(Read(Plain()));
            e.Stamp(S1());
            e.SetHeight(1, 1, 2000);
            e.SetHeight(2, 2, 2000);
            e.SetSurface(0, 0, "mud");
            e.SetSurface(1, 0, "mud");
            e.SetSurface(2, 0, "mud");
            MapWall moved = e.Current.Walls.Single(w => w.Src.Src == 2);
            Assert.That(moved.Cell, Is.EqualTo(new MapXZ(2, 2)));
            e.ReplaceWall(WallAt(3, 3, moved.Id));
            return e;
        }

        private static readonly string HandExtract = string.Join("\n",
            "h 1 1 2000", "h 2 2 2000", "s 0 0 mud", "s 1 0 mud", "s 2 0 mud",
            "  \"walls\": [", "    {", "      \"id\": 2,", "      \"kind\": \"wall\",", "      \"layer\": \"walls\",",
            "      \"cell\": [3, 3],", "      \"size\": [1, 1],", "      \"heightRaw\": 131072,", "      \"owner\": 0,",
            "      \"src\": {", "        \"instance\": \"s1\",", "        \"kind\": \"wall\",", "        \"src\": 2", "      }",
            "    }", "  ],", "  \"points\": [],", "  \"bases\": [],", "  \"units\": [],", "");

        [Test]
        public void HandEditsSurviveARerun()
        {
            MapEdit e = HandEdited();
            byte[] hand = Extract(e.Current, MapEdit.Hand);
            Assert.That(System.Text.Encoding.UTF8.GetString(hand), Is.EqualTo(HandExtract));

            MapStampResult r = e.Stamp(S1());

            Assert.That(Extract(e.Current, MapEdit.Hand), Is.EqualTo(hand));
            MapWall[] moved = e.Current.Walls.Where(w => w.Src != null && w.Src.Instance == "s1" && w.Src.Src == 2).ToArray();
            Assert.That(moved.Length, Is.EqualTo(1));
            Assert.That(moved[0].Cell, Is.EqualTo(new MapXZ(3, 3)));
            Assert.That(e.Current.Walls.Count, Is.EqualTo(2));
            Assert.That(new[] { e.Current.Points.Count, e.Current.Bases.Count, e.Current.Units.Count }, Is.EqualTo(new[] { 1, 1, 1 }));
            Assert.That(r.HeightsSkipped, Is.EqualTo(2));
            Assert.That(r.SurfacesSkipped, Is.EqualTo(3));
            Assert.That(r.EntriesSkipped, Is.EqualTo(1));
            Assert.That(r.HeightsWritten, Is.EqualTo(23));
            Assert.That(r.SurfacesWritten, Is.EqualTo(13));
            Assert.That(r.EntriesWritten, Is.EqualTo(4));
            Assert.That(MapDescJson.Write(MapDescJson.Read(Bytes(e))), Is.EqualTo(Bytes(e)));
        }

        [Test]
        public void SameStampTwiceIsIdempotent()
        {
            var e = new MapEdit(Read(Plain()));
            MapStampResult first = e.Stamp(S1());
            byte[] once = Bytes(e);
            ulong[] wallIds = e.Current.Walls.Select(w => w.Id).ToArray();
            ulong[] unitKeys = e.Current.Units.Select(u => u.Key).ToArray();
            MapNext next = e.Current.Next;

            MapStampResult second = e.Stamp(S1());

            Assert.That(Bytes(e), Is.EqualTo(once));
            Assert.That(wallIds, Is.EqualTo(new ulong[] { 1, 2 }));
            Assert.That(e.Current.Walls.Select(w => w.Id).ToArray(), Is.EqualTo(wallIds));
            Assert.That(e.Current.Units.Select(u => u.Key).ToArray(), Is.EqualTo(unitKeys));
            Assert.That(new[] { e.Current.Next.Wall, e.Current.Next.Unit }, Is.EqualTo(new[] { next.Wall, next.Unit }));
            Assert.That(new[] { e.Current.Next.Point, e.Current.Next.Base }, Is.EqualTo(new[] { next.Point, next.Base }));
            Assert.That(new[] { second.HeightsWritten, second.SurfacesWritten, second.EntriesWritten }, Is.EqualTo(new[] { 25, 16, 5 }));
            Assert.That(new[] { second.HeightsSkipped, second.SurfacesSkipped, second.EntriesSkipped }, Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(new[] { first.HeightsWritten, first.SurfacesWritten, first.EntriesWritten }, Is.EqualTo(new[] { 25, 16, 5 }));
            Assert.That(e.Current.Points.Select(p => p.Id).ToArray(), Is.EqualTo(new[] { 1 }));
            Assert.That(e.Current.Bases.Select(b => b.Id).ToArray(), Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void StampsRowWithoutItsWallIsRebuiltWithTheRowId()
        {
            var e = new MapEdit(Read(RowWithoutWall()));
            var plan = new MapStampPlan("s1", new MapHeightWrite[0], new MapSurfaceWrite[0],
                new[] { new MapStampEntry<MapWall>(5, WallAt(4, 4)) }, new MapStampEntry<MapPoint>[0],
                new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0]);

            MapStampResult r = e.Stamp(plan);

            Assert.That(e.Current.Walls.Count, Is.EqualTo(1));
            Assert.That(e.Current.Walls[0].Id, Is.EqualTo(3UL));
            Assert.That(e.Current.Walls[0].Src.Src, Is.EqualTo(5UL));
            Assert.That(e.Current.Walls[0].Cell, Is.EqualTo(new MapXZ(4, 4)));
            Assert.That(e.Current.Next.Wall, Is.EqualTo(4UL));
            Assert.That(e.Current.Stamps.Count, Is.EqualTo(1));
            Assert.That(r.EntriesWritten, Is.EqualTo(1));
        }

        [Test]
        public void HandEditedPointBaseAndUnitSurviveARerun()
        {
            var e = new MapEdit(Read(Plain()));
            e.Stamp(S1());
            e.ReplacePoint(new MapPoint(1, new MapMm(6500, 4500), 900, 2));
            e.ReplaceBase(new MapBase(1, 2, new MapMm(7500, 5500)));
            e.ReplaceUnit(new MapUnit(1, Soldier, 2, 3, new MapMm(4500, 4500), true));

            MapStampResult r = e.Stamp(S1());

            Assert.That(new[] { r.EntriesWritten, r.EntriesSkipped }, Is.EqualTo(new[] { 2, 3 }));
            MapPoint p = e.Current.Points.Single();
            Assert.That(new object[] { p.Id, p.At, p.RadiusMm, p.InitialOwner, p.Spawn, p.Owner, p.Src.Instance, p.Src.Src },
                Is.EqualTo(new object[] { 1, new MapMm(6500, 4500), 900, 2, null, 0, "s1", 1UL }));
            MapBase b = e.Current.Bases.Single();
            Assert.That(new object[] { b.Id, b.Team, b.At, b.Owner, b.Src.Src }, Is.EqualTo(new object[] { 1, 2, new MapMm(7500, 5500), 0, 1UL }));
            MapUnit u = e.Current.Units.Single();
            Assert.That(new object[] { u.Key, u.Type, u.Team, u.Squad, u.At, u.Reserve, u.Owner, u.Src.Src },
                Is.EqualTo(new object[] { 1UL, Soldier, 2, 3, new MapMm(4500, 4500), true, 0, 1UL }));
            Assert.That(MapDescJson.Write(MapDescJson.Read(Bytes(e))), Is.EqualTo(Bytes(e)));
        }

        // s1 then s2 on (Plain): s2 overlaps s1 on cells x in [2, 3] and corners i in [2, 4].
        internal static MapEdit Overlapped(out MapStampResult s2)
        {
            var e = new MapEdit(Read(Plain()));
            e.Stamp(S1());
            s2 = e.Stamp(S2());
            return e;
        }

        [Test]
        public void OverlappingStampKeepsTheFirstOwner()
        {
            MapEdit e = Overlapped(out MapStampResult r);
            Assert.That(r.SurfacesSkipped, Is.EqualTo(8));
            Assert.That(r.HeightsSkipped, Is.EqualTo(15));
            Assert.That(r.SurfacesWritten, Is.EqualTo(8));
            Assert.That(r.HeightsWritten, Is.EqualTo(10));
            Assert.That(CountSurfaces(e.Current, "stamp:s1"), Is.EqualTo(16));
            Assert.That(CountHeights(e.Current, "stamp:s1"), Is.EqualTo(25));
            Assert.That(CountSurfaces(e.Current, "stamp:s2"), Is.EqualTo(8));
            Assert.That(CountHeights(e.Current, "stamp:s2"), Is.EqualTo(10));
            Assert.That(SurfaceName(e.Current, 3, 0), Is.EqualTo("road"));
            Assert.That(SurfaceName(e.Current, 4, 0), Is.EqualTo("mud"));
            Assert.That(e.Current.Heights[0][4], Is.EqualTo(1000));
            Assert.That(e.Current.Heights[0][5], Is.EqualTo(2000));
        }

        [Test]
        public void ReleasedHandEditsGoBackToTheStamp()
        {
            MapEdit e = HandEdited();
            e.ReleaseHand(0, 0, 3, 3);
            Assert.That(CountHeights(e.Current, MapEdit.Hand) + CountSurfaces(e.Current, MapEdit.Hand), Is.EqualTo(0));
            Assert.That(e.EntryOwner("wall", 2), Is.Null);
            Assert.That(new[] { e.EntryOwner("wall", 1), e.EntryOwner("point", 1), e.EntryOwner("base", 1), e.EntryOwner("unit", 1) },
                Is.EqualTo(new[] { "stamp:s1", "stamp:s1", "stamp:s1", "stamp:s1" }));

            MapStampResult r = e.Stamp(S1());

            Assert.That(e.Current.Heights[1][1], Is.EqualTo(1000));
            Assert.That(e.Current.Heights[2][2], Is.EqualTo(1000));
            Assert.That(new[] { SurfaceName(e.Current, 0, 0), SurfaceName(e.Current, 1, 0), SurfaceName(e.Current, 2, 0) },
                Is.EqualTo(new[] { "road", "road", "road" }));
            MapWall back = e.Current.Walls.Single(w => w.Src.Src == 2);
            Assert.That(back.Cell, Is.EqualTo(new MapXZ(2, 2)));
            Assert.That(e.Current.Owners[back.Owner.Value], Is.EqualTo("stamp:s1"));
            Assert.That(new[] { r.HeightsSkipped, r.SurfacesSkipped, r.EntriesSkipped }, Is.EqualTo(new[] { 0, 0, 0 }));
        }

        [Test]
        public void ReleaseHandLeavesStampOwnersAlone()
        {
            MapEdit e = Overlapped(out _);
            Assert.That(OwnerOf(e.Current, SurfaceChar(e.Current, 5, 0)), Is.EqualTo("stamp:s2"));
            Assert.That(OwnerOf(e.Current, HeightChar(e.Current, 6, 0)), Is.EqualTo("stamp:s2"));
            e.SetSurface(5, 0, "grass");
            e.SetHeight(6, 0, 0);
            Assert.That(CountSurfaces(e.Current, "stamp:s2"), Is.EqualTo(7));
            Assert.That(CountHeights(e.Current, "stamp:s2"), Is.EqualTo(9));

            e.ReleaseHand(0, 0, 5, 3);

            Assert.That(CountSurfaces(e.Current, MapEdit.Hand), Is.EqualTo(0));
            Assert.That(CountHeights(e.Current, MapEdit.Hand), Is.EqualTo(0));
            Assert.That(SurfaceChar(e.Current, 5, 0), Is.EqualTo('.'));
            Assert.That(HeightChar(e.Current, 6, 0), Is.EqualTo('.'));
            Assert.That(CountSurfaces(e.Current, "stamp:s2"), Is.EqualTo(7));
            Assert.That(CountHeights(e.Current, "stamp:s2"), Is.EqualTo(9));
            Assert.That(CountSurfaces(e.Current, "stamp:s1"), Is.EqualTo(16));
            Assert.That(CountHeights(e.Current, "stamp:s1"), Is.EqualTo(25));
        }
    }
}
