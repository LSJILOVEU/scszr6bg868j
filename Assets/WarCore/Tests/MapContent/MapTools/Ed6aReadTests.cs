using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Ed6aFixtures;

namespace WarCore.Tests
{
    // Acceptance 7 (read, write back, EntriesIn) and 9 (the facade's public types).
    public sealed class Ed6aReadTests
    {
        // Every entry of the document written back unchanged through its Replace: the result read again equals the record
        // except for Owner, which becomes "hand".
        private static void RoundTripEachEntry(MapDocument doc)
        {
            for (int i = 0; i < doc.Walls().Count; i++)
            {
                MapWallInfo a = doc.Walls()[i], b = doc.WithWallReplaced(a).Walls()[i];
                Assert.That(Text(b), Is.EqualTo(Text(a)));
                Assert.That(b.Owner, Is.EqualTo("hand"));
            }
            for (int i = 0; i < doc.Points().Count; i++)
            {
                MapPointInfo a = doc.Points()[i], b = doc.WithPointReplaced(a).Points()[i];
                Assert.That(Text(b), Is.EqualTo(Text(a)));
                Assert.That(b.Owner, Is.EqualTo("hand"));
            }
            for (int i = 0; i < doc.Bases().Count; i++)
            {
                MapBaseInfo a = doc.Bases()[i], b = doc.WithBaseReplaced(a).Bases()[i];
                Assert.That(Text(b), Is.EqualTo(Text(a)));
                Assert.That(b.Owner, Is.EqualTo("hand"));
            }
            for (int i = 0; i < doc.Units().Count; i++)
            {
                MapUnitInfo a = doc.Units()[i], b = doc.WithUnitReplaced(a).Units()[i];
                Assert.That(Text(b), Is.EqualTo(Text(a)));
                Assert.That(b.Owner, Is.EqualTo("hand"));
            }
            for (int i = 0; i < doc.Plates().Count; i++)
            {
                MapPlateInfo a = doc.Plates()[i], b = doc.WithPlateReplaced(a).Plates()[i];
                Assert.That(Text(b), Is.EqualTo(Text(a)));
                Assert.That(b.Owner, Is.EqualTo("hand"));
            }
            for (int i = 0; i < doc.Cutouts().Count; i++)
            {
                MapCutoutInfo a = doc.Cutouts()[i], b = doc.WithCutoutReplaced(a).Cutouts()[i];
                Assert.That(Text(b), Is.EqualTo(Text(a)));
                Assert.That(b.Owner, Is.EqualTo("hand"));
            }
        }

        // Every field but Owner (RoundTripEachEntry checks it is "hand" after the write).
        private static string Text(MapWallInfo w) => string.Join("|", w.Id, w.Kind, w.Layer, w.CellX, w.CellZ, w.SizeX, w.SizeZ,
            w.HeightRaw, w.HasBase, w.BaseRaw, w.Stamp);
        private static string Text(MapPointInfo p) => string.Join("|", p.Id, p.XMm, p.ZMm, p.RadiusMm, p.InitialOwner, p.HasSpawn,
            p.SpawnXMm, p.SpawnZMm, p.HasSpawnPlate, p.SpawnPlate, p.HasPlate, p.Plate, p.Stamp);
        private static string Text(MapBaseInfo b) => string.Join("|", b.Id, b.Team, b.XMm, b.ZMm, b.HasPlate, b.Plate, b.Stamp);
        private static string Text(MapUnitInfo u) => string.Join("|", u.Key, u.Type, u.Team, u.Squad, u.XMm, u.ZMm, u.Reserve,
            u.HasPlate, u.Plate, u.Stamp);
        private static string Text(MapPlateInfo p) => string.Join("|", p.Id, p.CellX, p.CellZ, p.SizeX, p.SizeZ, p.Sloped, p.YRaw,
            p.SlopeAxis, p.FromRaw, p.ToRaw, p.ThickRaw, p.Surface, p.Stamp);
        private static string Text(MapCutoutInfo c) => string.Join("|", c.Id, c.Plate, c.CellX, c.CellZ, c.SizeX, c.SizeZ, c.Stamp);

        [TestCaseSource(nameof(Maps))]
        public void EveryEntryOfTheRepositoryMapsWritesBackWithOnlyItsOwnerChanged(string path)
        {
            MapDocument doc = Open(path);
            RoundTripEachEntry(doc);
        }

        private static IEnumerable<string> Maps() => AllMaps;

        // Unowned entries read Owner ""; stamp entries "stamp:<instance>" and their src instance as Stamp; a src-less entry Stamp "".
        [Test]
        public void OwnerAndStampMapping()
        {
            MapDocument m6 = Open(M6Path);
            Assert.That(m6.Walls()[0].Owner, Is.EqualTo(""));
            Assert.That(m6.Walls()[0].Stamp, Is.EqualTo(""));
            Assert.That(m6.Units()[0].Owner, Is.EqualTo(""));
            Assert.That(m6.Points()[0].Stamp, Is.EqualTo(""));
            MapDocument door = Open(DoorPath);
            foreach (MapWallInfo w in door.Walls())
            {
                Assert.That(w.Owner, Is.EqualTo("stamp:door"));
                Assert.That(w.Stamp, Is.EqualTo("door"));
            }
            Assert.That(door.Points()[0].Owner, Is.EqualTo(""));
            MapDocument synthetic = Open(Synthetic());
            Assert.That(synthetic.Walls()[0].Owner, Is.EqualTo(""));
            Assert.That(synthetic.Walls()[1].Owner, Is.EqualTo("stamp:s"));
            Assert.That(synthetic.Walls()[1].Stamp, Is.EqualTo("s"));
            Assert.That(synthetic.Points()[1].Owner, Is.EqualTo("hand"));
            Assert.That(synthetic.Points()[1].Stamp, Is.EqualTo(""));
        }

        // A hand entry written back unchanged gives the same description bytes.
        [TestCaseSource(nameof(Maps))]
        public void HandEntriesWriteBackToTheSameBytes(string path)
        {
            MapDocument doc = Open(path);
            if (doc.Walls().Count > 0)
            {
                MapDocument hand = doc.WithWallReplaced(doc.Walls()[0]);
                SameBytes(hand.WithWallReplaced(hand.Walls()[0]), hand);
            }
            if (doc.Points().Count > 0)
            {
                MapDocument hand = doc.WithPointReplaced(doc.Points()[0]);
                SameBytes(hand.WithPointReplaced(hand.Points()[0]), hand);
            }
            if (doc.Bases().Count > 0)
            {
                MapDocument hand = doc.WithBaseReplaced(doc.Bases()[0]);
                SameBytes(hand.WithBaseReplaced(hand.Bases()[0]), hand);
            }
            if (doc.Units().Count > 0)
            {
                MapDocument hand = doc.WithUnitReplaced(doc.Units()[0]);
                SameBytes(hand.WithUnitReplaced(hand.Units()[0]), hand);
            }
            if (doc.Plates().Count > 0)
            {
                MapDocument hand = doc.WithPlateReplaced(doc.Plates()[0]);
                SameBytes(hand.WithPlateReplaced(hand.Plates()[0]), hand);
            }
            if (doc.Cutouts().Count > 0)
            {
                MapDocument hand = doc.WithCutoutReplaced(doc.Cutouts()[0]);
                SameBytes(hand.WithCutoutReplaced(hand.Cutouts()[0]), hand);
            }
        }

        [Test]
        public void SyntheticEntriesReadEveryOptionalField()
        {
            MapDocument doc = Open(Synthetic());
            MapWallInfo w1 = doc.Walls()[0], w2 = doc.Walls()[1];
            Assert.That(w1.HasBase, Is.True);
            Assert.That(w1.BaseRaw, Is.EqualTo(M));
            Assert.That(w2.HasBase, Is.False);
            Assert.That(w2.BaseRaw, Is.EqualTo(0L));

            MapPlateInfo p1 = doc.Plates()[0], p2 = doc.Plates()[1];
            Assert.That(p1.Sloped, Is.False);
            Assert.That(p1.YRaw, Is.EqualTo(2 * M));
            Assert.That(p1.SlopeAxis, Is.EqualTo(""));
            Assert.That(p1.Surface, Is.EqualTo("grass"));
            Assert.That(p2.Sloped, Is.True);
            Assert.That(p2.YRaw, Is.EqualTo(0L));
            Assert.That(p2.SlopeAxis, Is.EqualTo("z"));
            Assert.That(p2.FromRaw, Is.EqualTo(M));
            Assert.That(p2.ToRaw, Is.EqualTo(2 * M));
            Assert.That(p2.Surface, Is.EqualTo(""));

            MapPointInfo q1 = doc.Points()[0], q2 = doc.Points()[1], q3 = doc.Points()[2];
            Assert.That(new object[] { q1.HasSpawn, q1.SpawnXMm, q1.SpawnZMm, q1.HasSpawnPlate, q1.SpawnPlate, q1.HasPlate, q1.Plate },
                Is.EqualTo(new object[] { true, -3500L, -1500L, true, 1UL, true, 1UL }));
            Assert.That(new object[] { q2.HasSpawn, q2.SpawnXMm, q2.SpawnZMm, q2.HasSpawnPlate, q2.HasPlate, q2.RadiusMm, q2.InitialOwner },
                Is.EqualTo(new object[] { true, 2500L, 1500L, false, false, 2000, 1 }));
            Assert.That(new object[] { q3.HasSpawn, q3.SpawnXMm, q3.HasSpawnPlate, q3.HasPlate, q3.InitialOwner },
                Is.EqualTo(new object[] { false, 0L, false, false, 2 }));

            Assert.That(doc.Bases()[0].HasPlate, Is.True);
            Assert.That(doc.Bases()[0].Plate, Is.EqualTo(1UL));
            Assert.That(doc.Bases()[1].HasPlate, Is.False);
            Assert.That(doc.Units()[0].HasPlate, Is.True);
            Assert.That(doc.Units()[0].Plate, Is.EqualTo(1UL));
            Assert.That(doc.Units()[1].HasPlate, Is.False);
            Assert.That(doc.Units()[1].XMm, Is.EqualTo(-1500L));
            Assert.That(doc.Units()[1].Reserve, Is.True);
            Assert.That(doc.Cutouts()[0].Plate, Is.EqualTo(1UL));
        }

        [Test]
        public void SyntheticEntriesWriteBackWithOnlyTheirOwnerChanged()
        {
            MapDocument doc = Open(Synthetic());
            RoundTripEachEntry(doc);
            MapDesc before = Desc(doc);
            MapDesc after = Desc(doc.WithPointReplaced(doc.Points()[0]).WithWallReplaced(doc.Walls()[1]).WithPlateReplaced(doc.Plates()[0]));
            Assert.That(after.Points[0].SpawnPlate, Is.EqualTo(1UL));
            Assert.That(after.Points[0].Plate, Is.EqualTo(1UL));
            Assert.That(after.Walls[1].Src.Instance, Is.EqualTo("s"));
            Assert.That(after.Walls[1].Owner, Is.EqualTo(0));
            Assert.That(after.Plates[0].Surface, Is.EqualTo("grass"));
            Assert.That(after.Walls[1].Id, Is.EqualTo(before.Walls[1].Id));

            MapPlateInfo p2 = doc.Plates()[1], p1 = doc.Plates()[0];
            MapDocument surfaces = doc.WithPlateReplaced(new MapPlateInfo(p2.Id, p2.CellX, p2.CellZ, p2.SizeX, p2.SizeZ, true, 0, "z", M, 2 * M,
                p2.ThickRaw, "mud", "", "")).WithPlateReplaced(new MapPlateInfo(p1.Id, p1.CellX, p1.CellZ, p1.SizeX, p1.SizeZ, false, p1.YRaw, "",
                0, 0, p1.ThickRaw, "", "", ""));
            Assert.That(surfaces.Plates()[1].Surface, Is.EqualTo("mud"));
            Assert.That(Desc(surfaces).Plates[1].Surface, Is.EqualTo("mud"));
            Assert.That(surfaces.Plates()[0].Surface, Is.EqualTo(""));
            Assert.That(Desc(surfaces).Plates[0].Surface, Is.Null);
        }

        [Test]
        public void AddIgnoresIdOwnerAndStampOfTheRecord()
        {
            MapDocument doc = Open(Synthetic());
            MapDocument added = doc.WithWallAdded(new MapWallInfo(77, "wall", "walls", 4, 6, 1, 1, 2 * M, false, 0, "stamp:s", "s"));
            MapWallInfo w = added.Walls()[2];
            Assert.That(w.Id, Is.EqualTo(3UL));
            Assert.That(w.Owner, Is.EqualTo("hand"));
            Assert.That(w.Stamp, Is.EqualTo(""));
        }

        [Test]
        public void PointAndBaseIdsAboveIntAreRefused()
        {
            MapDocument doc = Open(M6Path);
            MapPointInfo p = doc.Points()[0];
            var big = new MapPointInfo((ulong)int.MaxValue + 1, p.XMm, p.ZMm, p.RadiusMm, 0, false, 0, 0, false, 0, false, 0, "", "");
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.WithPointReplaced(big));
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.WithBaseReplaced(new MapBaseInfo((ulong)int.MaxValue + 1, 1, 1000, 1000, false, 0, "", "")));
            Assert.Throws<ArgumentException>(() => doc.WithBaseReplaced(new MapBaseInfo(int.MaxValue, 1, 1000, 1000, false, 0, "", "")));
        }

        // lanes-playtest, by hand: cells 80..105 x 59..70 hold wall 13 [82, 63], wall 16 [104, 59] and point 4 (100000, 70000);
        // cell 6 x 69..71 holds base 1 (6000, 70000) and units 41 (6000, 69000), 51 (6000, 71000), 141 (6000, 69000).
        [Test]
        public void EntriesInListsKindsInOrderThenDescriptionOrder()
        {
            MapDocument doc = Open(LanesPath);
            Assert.That(Strings(doc.EntriesIn(80, 59, 105, 70)), Is.EqualTo(new[] { "wall 13", "wall 16", "point 4" }));
            Assert.That(Strings(doc.EntriesIn(6, 69, 6, 71)), Is.EqualTo(new[] { "base 1", "unit 41", "unit 51", "unit 141" }));
            Assert.That(Strings(Open(PlatePath).EntriesIn(9, 9, 9, 9)), Is.EqualTo(new[] { "plate 1", "cutout 1" }));
        }

        // A box with a wall right on each edge and another just outside it.
        [Test]
        public void EntriesInIncludesBothEdges()
        {
            MapDocument doc = Empty("rwr-m6", 30, 20);
            foreach ((int x, int z) in new[] { (10, 12), (9, 12), (20, 12), (21, 12), (15, 5), (15, 4), (15, 15), (15, 16) })
                doc = doc.WithWallAdded(Wall(x, z, 1, 1));
            Assert.That(Strings(doc.EntriesIn(10, 5, 20, 15)), Is.EqualTo(new[] { "wall 1", "wall 3", "wall 5", "wall 7" }));
        }

        // Millimetre positions round toward negative infinity: in the synthetic map (origin -5, -3) unit 2 at (-1500, -500)
        // is in cell (3, 2) with cutout 1, point 1 at (-4500, -2500) in cell (0, 0).
        [Test]
        public void EntriesInFloorsNegativeMillimetres()
        {
            MapDocument doc = Open(Synthetic());
            Assert.That(Strings(doc.EntriesIn(3, 2, 3, 2)), Is.EqualTo(new[] { "unit 2", "cutout 1" }));
            Assert.That(Strings(doc.EntriesIn(4, 3, 4, 3)), Is.Empty);
            Assert.That(Strings(doc.EntriesIn(0, 0, 0, 0)), Is.EqualTo(new[] { "point 1", "plate 1" }));
            Assert.That(Strings(doc.EntriesIn(0, 0, 11, 7)).Count, Is.EqualTo(12));
        }

        [Test]
        public void EntriesInRefusesBadBoxes()
        {
            MapDocument doc = Open(M6Path);
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.EntriesIn(-1, 0, 5, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.EntriesIn(0, -1, 5, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.EntriesIn(0, 0, 80, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.EntriesIn(0, 0, 5, 40));
            Assert.Throws<ArgumentException>(() => doc.EntriesIn(6, 0, 5, 5));
            Assert.Throws<ArgumentException>(() => doc.EntriesIn(0, 6, 5, 5));
            Assert.That(doc.EntriesIn(79, 39, 79, 39), Is.Empty);
        }

        // Acceptance 9: the new records and lists pass the same public-type walk as MapDocument.
        [Test]
        public void NewFacadeTypesNameNoContentNullableOrArrayType()
        {
            Type[] types =
            {
                typeof(MapDocument), typeof(MapWallInfo), typeof(MapPointInfo), typeof(MapBaseInfo), typeof(MapUnitInfo), typeof(MapPlateInfo),
                typeof(MapCutoutInfo), typeof(MapEntryRef), typeof(MapEntryEditList),
            };
            foreach (Type t in types)
            {
                List<string> problems = E1bDocumentTests.SurfaceViolations(t, typeof(MapDocument).Assembly, out int members);
                Assert.That(problems, Is.Empty, t.Name);
                Assert.That(members, Is.GreaterThan(1), t.Name);
            }
            int facade = 0;
            foreach (System.Reflection.MethodInfo m in typeof(MapDocument).GetMethods())
                if (m.Name.StartsWith("With") || m.Name == "NextId" || m.Name == "EntriesIn") facade++;
            Assert.That(facade, Is.GreaterThanOrEqualTo(26));
        }
    }
}
