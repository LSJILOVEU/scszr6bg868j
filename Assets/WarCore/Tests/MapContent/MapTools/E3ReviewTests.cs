using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Fixtures;
using static WarCore.Tests.E3Plans;

namespace WarCore.Tests
{
    // Review additions: stamp instances and kinds are kept apart, the edit-link check catches a hand entry on a stamps id,
    // ReleaseHand stays inside its box and wall size counts toward the map edge.
    public sealed class E3ReviewTests
    {
        private static byte[] Bytes(MapEdit e) => MapDescJson.Write(e.Current);

        private static MapStampPlan OneWall(string instance, ulong src, int x, int z) => new MapStampPlan(instance,
            new MapHeightWrite[0], new MapSurfaceWrite[0], new[] { new MapStampEntry<MapWall>(src, WallAt(x, z)) },
            new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0]);

        [Test]
        public void SameSrcInTwoInstancesMakesTwoWalls()
        {
            var e = new MapEdit(Read(Plain()));
            e.Stamp(OneWall("s1", 1, 0, 0));
            MapStampResult r = e.Stamp(OneWall("s2", 1, 5, 5));

            Assert.That(new[] { r.EntriesWritten, r.EntriesSkipped }, Is.EqualTo(new[] { 1, 0 }));
            Assert.That(e.Current.Walls.Select(w => w.Id + " " + w.Src.Instance + " " + w.Cell.X + "," + w.Cell.Z).ToArray(),
                Is.EqualTo(new[] { "1 s1 0,0", "2 s2 5,5" }));
            Assert.That(e.Current.Stamps.Select(s => s.Instance + "/" + s.Kind + "/" + s.Src + "/" + s.Id).ToArray(),
                Is.EqualTo(new[] { "s1/wall/1/1", "s2/wall/1/2" }));
            byte[] once = Bytes(e);

            MapStampResult again = e.Stamp(OneWall("s2", 1, 5, 5));

            Assert.That(new[] { again.EntriesWritten, again.EntriesSkipped }, Is.EqualTo(new[] { 1, 0 }));
            Assert.That(Bytes(e), Is.EqualTo(once));
            Assert.That(MapDescJson.Write(MapDescJson.Read(once)), Is.EqualTo(once));
        }

        [Test]
        public void TombstoneOfAnotherKindDoesNotBlock()
        {
            var e = new MapEdit(Read(Map(removed: Src("s1", "wall", 1))));
            MapStampResult r = e.Stamp(S1());

            Assert.That(e.Current.Walls.Select(w => w.Src.Src).ToArray(), Is.EqualTo(new ulong[] { 2 }));
            Assert.That(e.Current.Units.Count, Is.EqualTo(1));
            Assert.That(new object[] { e.Current.Units[0].Src.Kind, e.Current.Units[0].Src.Src, e.Current.Units[0].At },
                Is.EqualTo(new object[] { "unit", 1UL, new MapMm(1500, 1500) }));
            Assert.That(new[] { r.EntriesWritten, r.EntriesSkipped }, Is.EqualTo(new[] { 4, 1 }));
        }

        [Test]
        public void HandWallOnAStampsIdIsRefused()
        {
            MapDesc desc = Read(Map(walls: WallJson(3, 0, 0, 0), stamps: Row("s1", "wall", 5, 3), next: "4, 1, 1, 1"));
            Assert.That(desc.Walls[0].Src, Is.Null);
            var x = Assert.Throws<ArgumentException>(() => new MapEdit(desc));
            Assert.That(x.Message, Does.StartWith("MAPEDIT_LINK wall"));
        }

        [Test]
        public void ReleaseHandKeepsHandEntriesOutsideTheBox()
        {
            var e = new MapEdit(Read(Plain()));
            ulong inside = e.AddWall(WallAt(1, 1));
            ulong outside = e.AddWall(WallAt(6, 4));
            ulong unit = e.AddUnit(UnitAt(6500, 4500));
            e.ReleaseHand(0, 0, 3, 3);
            Assert.That(e.EntryOwner("wall", inside), Is.Null);
            Assert.That(e.EntryOwner("wall", outside), Is.EqualTo(MapEdit.Hand));
            Assert.That(e.EntryOwner("unit", unit), Is.EqualTo(MapEdit.Hand));
        }

        [Test]
        public void WallSizeCountsTowardTheEdge()
        {
            var e = new MapEdit(Read(Plain()));
            byte[] before = Bytes(e);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                e.AddWall(new MapWall(0, "wall", "walls", new MapXZ(7, 0), new MapXZ(2, 1), WallHeight)));
            Assert.That(Bytes(e), Is.EqualTo(before));
        }
    }
}
