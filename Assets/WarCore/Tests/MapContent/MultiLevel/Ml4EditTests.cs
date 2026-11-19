using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Fixtures;
using static WarCore.Tests.E3Plans;

namespace WarCore.Tests
{
    // ML-4 acceptance 11 (editor): plates and cutouts as editor entries, stamps, owners past 64, capacity.
    public sealed class Ml4EditTests
    {
        private const long M = Ml4Maps.M;

        private static byte[] Bytes(MapEdit e) => MapDescJson.Write(e.Current);
        private static MapDesc RoundTrip(MapEdit e) => MapDescJson.Read(Bytes(e));

        private static MapEdit Empty() => new MapEdit(Ml4Maps.Make(0, 8, 6, null, new MapPlate[0]));

        private static MapStampEntry<MapPlate> PlateEntry(ulong src, int x, long y) => new MapStampEntry<MapPlate>(src, Ml4Maps.Flat(99, x, 0, 2, 2, y));
        private static MapStampEntry<MapCutout> CutEntry(ulong src, ulong plate, int x) => new MapStampEntry<MapCutout>(src, Ml4Maps.Cut(99, plate, x, 0, 1, 1));

        private static MapStampPlan Plan(string instance, MapStampEntry<MapPlate>[] plates, MapStampEntry<MapCutout>[] cutouts,
            MapHeightWrite[] heights = null, MapStampEntry<MapWall>[] walls = null) =>
            new MapStampPlan(instance, heights ?? new MapHeightWrite[0], new MapSurfaceWrite[0], walls ?? new MapStampEntry<MapWall>[0],
                new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0], plates, cutouts);

        [Test]
        public void HandAddReplaceRemove()
        {
            MapEdit e = Empty();
            ulong plate = e.AddPlate(Ml4Maps.Flat(7, 1, 1, 3, 2, 3 * M));
            ulong cut = e.AddCutout(Ml4Maps.Cut(9, plate, 1, 1, 1, 1));
            Assert.That((plate, cut), Is.EqualTo((1ul, 1ul)), "ids from next, the template's ignored");
            Assert.That(e.EntryOwner("plate", plate), Is.EqualTo("hand"));
            Assert.That(e.EntryOwner("cutout", cut), Is.EqualTo("hand"));
            Assert.That(RoundTrip(e).Version, Is.EqualTo(2));
            e.ReplacePlate(Ml4Maps.Flat(plate, 1, 1, 3, 2, 4 * M));
            Assert.That(e.Current.Plates[0].YRaw, Is.EqualTo(4 * M));
            Assert.That(e.Current.Plates[0].Owner, Is.EqualTo(0));
            e.ReplaceCutout(Ml4Maps.Cut(cut, plate, 2, 1, 1, 1));
            Assert.That(e.Current.Cutouts[0].Cell.X, Is.EqualTo(2));

            byte[] before = Bytes(e);
            Assert.Throws<ArgumentException>(() => e.Remove("plate", plate), "a cutout still names it");
            Assert.Throws<ArgumentException>(() => e.AddCutout(Ml4Maps.Cut(1, 5, 1, 1, 1, 1)), "no plate 5");
            Assert.Throws<ArgumentException>(() => e.ReplaceCutout(Ml4Maps.Cut(cut, 5, 1, 1, 1, 1)));
            Assert.That(Bytes(e), Is.EqualTo(before));

            e.Remove("cutout", cut);
            e.Remove("plate", plate);
            Assert.That(e.Current.Plates.Count + e.Current.Cutouts.Count, Is.EqualTo(0));
            Assert.That(e.Current.Removed, Is.Empty, "hand entries leave no tombstone");
            Assert.That(RoundTrip(e).Version, Is.EqualTo(2), "next.plate and next.cutout stay above 1");
            Assert.That(e.AddPlate(Ml4Maps.Flat(1, 0, 0, 1, 1, M)), Is.EqualTo(2ul), "ids are not reused");
        }

        // Stamped plates and cutouts: stamped again they stay the same; a hand change is not overwritten; a removed one
        // leaves a tombstone and stays removed; ReleaseHand frees a plate anchored in its box.
        [Test]
        public void StampedPlatesFollowTheWallRules()
        {
            MapEdit e = Empty();
            ulong hand = e.AddPlate(Ml4Maps.Flat(1, 6, 4, 2, 2, 3 * M));
            MapStampPlan plan = Plan("s1", new[] { PlateEntry(1, 0, 3 * M), PlateEntry(2, 3, 3 * M) }, new[] { CutEntry(1, hand, 6) });
            MapStampResult first = e.Stamp(plan);
            Assert.That((first.EntriesWritten, first.EntriesSkipped), Is.EqualTo((3, 0)));
            Assert.That(e.Current.Plates.Select(p => p.Id), Is.EqualTo(new ulong[] { 1, 2, 3 }));
            Assert.That(e.EntryOwner("plate", 2), Is.EqualTo("stamp:s1"));
            Assert.That(e.EntryOwner("cutout", 1), Is.EqualTo("stamp:s1"));
            byte[] once = Bytes(e);
            e.Stamp(plan);
            Assert.That(Bytes(e), Is.EqualTo(once), "stamping again changes nothing");

            e.ReplacePlate(Ml4Maps.Flat(2, 0, 0, 2, 2, 4 * M));
            Assert.That(e.EntryOwner("plate", 2), Is.EqualTo("hand"));
            MapStampResult again = e.Stamp(plan);
            Assert.That(again.EntriesSkipped, Is.EqualTo(1));
            Assert.That(e.Current.Plates.First(p => p.Id == 2).YRaw, Is.EqualTo(4 * M), "the hand change stays");

            e.Remove("plate", 3);
            Assert.That(e.Current.Removed.Select(r => r.Instance + "/" + r.Kind + "/" + r.Src), Is.EqualTo(new[] { "s1/plate/2" }));
            MapStampResult third = e.Stamp(plan);
            Assert.That(e.Current.Plates.Any(p => p.Id == 3), Is.False, "a tombstone keeps it removed");
            Assert.That(third.EntriesSkipped, Is.EqualTo(2));
            Assert.That(RoundTrip(e).Removed.Count, Is.EqualTo(1));

            e.ReleaseHand(0, 0, 1, 1);
            Assert.That(e.EntryOwner("plate", 2), Is.Null);
            e.Stamp(plan);
            Assert.That(e.Current.Plates.First(p => p.Id == 2).YRaw, Is.EqualTo(3 * M), "a released plate is the stamp's again");
        }

        // A cutout naming a plate not in the description (removed by hand, or never there) is skipped and counted.
        [Test]
        public void StampCutoutOfAMissingPlateIsSkipped()
        {
            MapEdit e = Empty();
            MapStampResult r = e.Stamp(Plan("s1", new[] { PlateEntry(1, 0, 3 * M) }, new[] { CutEntry(1, 1, 0), CutEntry(2, 7, 0) }));
            Assert.That((r.EntriesWritten, r.EntriesSkipped), Is.EqualTo((2, 1)));
            Assert.That(e.Current.Cutouts.Select(c => c.Plate), Is.EqualTo(new ulong[] { 1 }));
            Assert.DoesNotThrow(() => RoundTrip(e));
        }

        // The old plan constructor is the new one with no plates or cutouts, write for write.
        [Test]
        public void OldPlanConstructorIsUnchanged()
        {
            var a = new MapEdit(Read(Plain()));
            var b = new MapEdit(Read(Plain()));
            MapStampPlan old = S1();
            var full = new MapStampPlan(old.Instance, old.Heights.ToArray(), old.Surfaces.ToArray(), old.Walls.ToArray(), old.Points.ToArray(),
                old.Bases.ToArray(), old.Units.ToArray(), new MapStampEntry<MapPlate>[0], new MapStampEntry<MapCutout>[0]);
            MapStampResult ra = a.Stamp(old), rb = b.Stamp(full);
            Assert.That(Bytes(a), Is.EqualTo(Bytes(b)));
            Assert.That(new[] { ra.HeightsWritten, ra.SurfacesWritten, ra.EntriesWritten }, Is.EqualTo(new[] { rb.HeightsWritten, rb.SurfacesWritten, rb.EntriesWritten }));
            Assert.That(RoundTrip(a).Version, Is.EqualTo(1));
        }

        private static string OwnersText(int count) => "\"hand\"" + string.Concat(Enumerable.Range(1, count - 1).Select(k => ", \"stamp:o" + k + "\""));

        // 64 owners and an entries-only stamp: the 65th owner is added and the description becomes version 2.
        [Test]
        public void SixtyFifthOwnerForAnEntriesOnlyStamp()
        {
            var e = new MapEdit(Read(Map(owners: OwnersText(64))));
            MapStampResult r = e.Stamp(Plan("s65", new MapStampEntry<MapPlate>[0], new MapStampEntry<MapCutout>[0], walls: new[] { new MapStampEntry<MapWall>(1, WallAt(3, 3)) }));
            Assert.That(r.EntriesWritten, Is.EqualTo(1));
            Assert.That(e.Current.Owners.Count, Is.EqualTo(65));
            Assert.That(e.EntryOwner("wall", 1), Is.EqualTo("stamp:s65"));
            Assert.That(RoundTrip(e).Version, Is.EqualTo(2));
            Assert.That(MapDescJson.Write(RoundTrip(e)), Is.EqualTo(Bytes(e)));
        }

        // An owner number of 64 or more cannot sit in the owner grids: a plan with any height or surface write is refused
        // whole, even when every such write would be skipped.
        [Test]
        public void OwnerPastSixtyFourCannotWriteGrids()
        {
            var e = new MapEdit(Read(Map(owners: OwnersText(65)).Replace("\"version\": 1,", "\"version\": 2,")));
            e.SetHeight(0, 0, 500);
            Assert.That(e.HeightOwner(0, 0), Is.EqualTo("hand"));
            byte[] before = Bytes(e);
            var x = Assert.Throws<InvalidOperationException>(() => e.Stamp(Plan("s66", new MapStampEntry<MapPlate>[0], new MapStampEntry<MapCutout>[0],
                heights: new[] { new MapHeightWrite(0, 0, 900) }, walls: new[] { new MapStampEntry<MapWall>(1, WallAt(3, 3)) })));
            Assert.That(x.Message, Is.EqualTo("MAPDESC_OWNER"));
            Assert.That(Bytes(e), Is.EqualTo(before));
            var s = new MapStampPlan("s67", new MapHeightWrite[0], new[] { new MapSurfaceWrite(0, 0, "road") }, new MapStampEntry<MapWall>[0],
                new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0]);
            Assert.That(Assert.Throws<InvalidOperationException>(() => e.Stamp(s)).Message, Is.EqualTo("MAPDESC_OWNER"));
            Assert.That(Bytes(e), Is.EqualTo(before));
            e.Stamp(Plan("s1", new[] { PlateEntry(1, 0, 3 * M) }, new MapStampEntry<MapCutout>[0]));
            Assert.That(e.EntryOwner("plate", 1), Is.EqualTo("stamp:s1"), "owner number 65, entries only");
        }

        private static MapEdit Full(bool cutouts)
        {
            MapPlate[] plates = cutouts ? new[] { Ml4Maps.Flat(1, 0, 0, 64, 64, M) }
                : Enumerable.Range(0, 4096).Select(k => Ml4Maps.Flat((ulong)k + 1, k % 64, k / 64, 1, 1, M)).ToArray();
            MapCutout[] cuts = cutouts ? Enumerable.Range(0, 4096).Select(k => Ml4Maps.Cut((ulong)k + 1, 1, k % 64, k / 64, 1, 1)).ToArray() : new MapCutout[0];
            return new MapEdit(Ml4Maps.Make(0, 64, 64, null, plates, cuts));
        }

        [Test]
        public void The4097thPlateIsRefused()
        {
            MapEdit e = Full(false);
            byte[] before = Bytes(e);
            Assert.That(Assert.Throws<InvalidOperationException>(() => e.AddPlate(Ml4Maps.Flat(1, 0, 0, 1, 1, 4 * M))).Message, Is.EqualTo("MAPDESC_PLATE"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => e.Stamp(Plan("s1", new[] { PlateEntry(1, 0, 4 * M) }, new MapStampEntry<MapCutout>[0]))).Message,
                Is.EqualTo("MAPDESC_PLATE"));
            Assert.That(Bytes(e), Is.EqualTo(before));
            e.Remove("plate", 7);
            Assert.That(e.AddPlate(Ml4Maps.Flat(1, 0, 0, 1, 1, 4 * M)), Is.EqualTo(4097ul));
            Assert.DoesNotThrow(() => RoundTrip(e));
        }

        [Test]
        public void The4097thCutoutIsRefused()
        {
            MapEdit e = Full(true);
            byte[] before = Bytes(e);
            Assert.That(Assert.Throws<InvalidOperationException>(() => e.AddCutout(Ml4Maps.Cut(1, 1, 0, 0, 1, 1))).Message, Is.EqualTo("MAPDESC_PLATE"));
            Assert.That(Assert.Throws<InvalidOperationException>(() => e.Stamp(Plan("s1", new MapStampEntry<MapPlate>[0], new[] { CutEntry(1, 1, 0) }))).Message,
                Is.EqualTo("MAPDESC_PLATE"));
            Assert.That(Bytes(e), Is.EqualTo(before));
        }

        [Test]
        public void The4097thOwnerIsRefused()
        {
            var e = new MapEdit(Read(Map(owners: OwnersText(4096)).Replace("\"version\": 1,", "\"version\": 2,")));
            byte[] before = Bytes(e);
            var x = Assert.Throws<InvalidOperationException>(() => e.Stamp(Plan("new", new MapStampEntry<MapPlate>[0], new MapStampEntry<MapCutout>[0],
                walls: new[] { new MapStampEntry<MapWall>(1, WallAt(3, 3)) })));
            Assert.That(x.Message, Is.EqualTo("MAPDESC_OWNER"));
            Assert.That(Bytes(e), Is.EqualTo(before));
            e.Stamp(Plan("o4095", new MapStampEntry<MapPlate>[0], new MapStampEntry<MapCutout>[0], walls: new[] { new MapStampEntry<MapWall>(1, WallAt(3, 3)) }));
            Assert.That(e.EntryOwner("wall", 1), Is.EqualTo("stamp:o4095"), "a listed owner is reused");
        }
    }
}
