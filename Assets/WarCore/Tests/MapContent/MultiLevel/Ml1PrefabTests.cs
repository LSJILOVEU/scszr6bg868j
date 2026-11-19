using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;

namespace WarCore.Tests
{
    // ML-1 acceptance 9: the door prefab's arguments, idempotence and hand edits.
    public sealed class Ml1PrefabTests
    {
        private const long M = Ml1Desc.M;

        private static MapDesc Flat() => Ml1Desc.Make(10, 3, null);

        private static MapStampPlan Door(MapDesc d, int length = 10, int doorStart = 4, int doorWidth = 2, long top = 3 * M) =>
            MapPrefabs.Door(d, "door", new MapXZ(0, 1), true, length, doorStart, doorWidth, top, "walls", "wall");

        [Test]
        public void ThreeWallsWithTheLintelAtTheHeadroom()
        {
            MapWall[] walls = Door(Flat()).Walls.Select(e => e.Template).ToArray();
            Assert.That(Door(Flat()).Walls.Select(e => e.Src), Is.EqualTo(new ulong[] { 1, 2, 3 }));
            Assert.That(walls.Select(w => (w.Cell.X, w.Cell.Z, w.Size.X, w.Size.Z)),
                Is.EqualTo(new[] { (0, 1, 4, 1), (4, 1, 2, 1), (6, 1, 4, 1) }));
            Assert.That(walls.Select(w => w.BaseRaw), Is.EqualTo(new long?[] { null, 2 * M, null }));
            Assert.That(walls.Select(w => w.HeightRaw), Is.EqualTo(new[] { 3 * M, M, 3 * M }));
        }

        [TestCase(10, 4, 0, "doorWidth")]
        [TestCase(10, 0, 2, "doorStart")]
        [TestCase(10, 8, 2, "length")]
        [TestCase(10, 1, 9, "length")]
        public void DoorAndFramesAreAtLeastOneCell(int length, int doorStart, int doorWidth, string parameter)
        {
            ArgumentException e = Assert.Throws<ArgumentException>(() => Door(Flat(), length, doorStart, doorWidth));
            Assert.That(e.ParamName, Is.EqualTo(parameter));
            Assert.DoesNotThrow(() => Door(Flat(), 10, 1, 8));
        }

        // The lintel needs at least 2 raw: top = underside + 2 raw works, + 1 raw cannot (odd, the underside moves up 1 raw).
        [Test]
        public void TopTooLowForALintel()
        {
            MapWall lintel = Door(Flat(), top: 2 * M + 2).Walls[1].Template;
            Assert.That((lintel.BaseRaw, lintel.HeightRaw), Is.EqualTo(((long?)(2 * M), 2L)));
            Assert.That(Assert.Throws<ArgumentException>(() => Door(Flat(), top: 2 * M + 1)).ParamName, Is.EqualTo("topRaw"));
            Assert.That(Assert.Throws<ArgumentException>(() => Door(Flat(), top: 2 * M)).ParamName, Is.EqualTo("topRaw"));
        }

        // Odd top - underside: the underside goes 1 raw up, never down (down would block the doorway); then the frame
        // height top - floor is odd and that is refused too.
        [Test]
        public void OddLintelMovesUpAndOddFrameIsRefused()
        {
            MapDesc raised = Ml1Desc.Make(10, 3, new int[4][].Select(_ => Enumerable.Repeat(1, 11).ToArray()).ToArray());
            MapWall lintel = Door(raised, top: 4 * M).Walls[1].Template;
            Assert.That(lintel.BaseRaw, Is.EqualTo(2 * M + 2), "ground 1 raw, underside 2 m + 1 raw, odd height: up 1 more");
            Assert.That(lintel.BaseRaw + lintel.HeightRaw, Is.EqualTo(4 * M));
            Assert.That(Assert.Throws<ArgumentException>(() => Door(Flat(), top: 3 * M + 1)).ParamName, Is.EqualTo("topRaw"));
        }

        // On the terrain demo the right frame stands on the hill (highest point 5.9 m): a 6 m top fails the 1 m rule.
        [Test]
        public void FrameMustPassTheOneMetreRule()
        {
            MapDesc t = Ml1DoorMaps.DoorTerrain();
            ArgumentException e = Assert.Throws<ArgumentException>(() =>
                MapPrefabs.Door(t, "door2", new MapXZ(0, 2), true, t.Size.X, 13, 2, 6 * M, "walls", "wall"));
            Assert.That(e.Message, Does.StartWith("a frame does not stand 1 m above the ground"));
            Assert.DoesNotThrow(() => MapPrefabs.Door(t, "door2", new MapXZ(0, 2), true, t.Size.X, 13, 2, 7 * M, "walls", "wall"));
        }

        [Test]
        public void WallMustStayOnTheMapAndUnderTheCeiling()
        {
            Assert.That(Assert.Throws<ArgumentException>(() => MapPrefabs.Door(Flat(), "d", new MapXZ(1, 1), true, 10, 4, 2, 3 * M, "walls", "wall")).ParamName,
                Is.EqualTo("start"));
            Assert.That(Assert.Throws<ArgumentException>(() => MapPrefabs.Door(Flat(), "d", new MapXZ(0, 0), false, 4, 1, 2, 3 * M, "walls", "wall")).ParamName,
                Is.EqualTo("start"));
            Assert.That(Assert.Throws<ArgumentException>(() => Door(Flat(), top: 10 * M + 2)).ParamName, Is.EqualTo("topRaw"));
        }

        [Test]
        public void StampingTwiceChangesNothing()
        {
            var e = new MapEdit(Flat());
            e.Stamp(Door(Flat()));
            byte[] once = MapDescJson.Write(e.Current);
            e.Stamp(Door(Flat()));
            Assert.That(MapDescJson.Write(e.Current), Is.EqualTo(once));
            Assert.That(e.Current.Walls[1].BaseRaw, Is.EqualTo(2 * M), "the replaced lintel keeps its bottom");
            Assert.That(e.Current.Version, Is.EqualTo(2));
        }

        // A lintel lowered by hand to block the doorway survives a rerun of the stamp; the frames are restamped.
        [Test]
        public void HandEditedLintelSurvivesARerun()
        {
            var e = new MapEdit(Flat());
            e.Stamp(Door(Flat()));
            MapWall lintel = e.Current.Walls.Single(w => w.Src.Src == 2);
            e.ReplaceWall(new MapWall(lintel.Id, lintel.Kind, lintel.Layer, lintel.Cell, lintel.Size, 2 * M, baseRaw: M));
            MapStampResult r = e.Stamp(Door(Flat()));
            MapWall kept = e.Current.Walls.Single(w => w.Id == lintel.Id);
            Assert.That((kept.BaseRaw, kept.HeightRaw, kept.Owner), Is.EqualTo(((long?)M, 2 * M, (int?)0)));
            Assert.That((r.EntriesWritten, r.EntriesSkipped), Is.EqualTo((2, 1)));
            Assert.That(Ml1Desc.Bake(e.Current).IsPassable(4, 1), Is.False);
        }
    }
}
