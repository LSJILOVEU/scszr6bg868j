using System;
using System.IO;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Ed6aFixtures;

namespace WarCore.Tests
{
    // Acceptance 3 (ids, moves, copies, E0 E6 2) and 8 (plates and cutouts).
    public sealed class Ed6aIdTests
    {
        [Test]
        public void ReplaceMoveAndResizeKeepTheIdAndMakeTheEntryHandOwned()
        {
            MapDocument doc = Open(M6Path);
            MapWallInfo w = doc.Walls()[1];
            Assert.That(w.Id, Is.EqualTo(2UL));
            Assert.That(w.Owner, Is.EqualTo(""));

            MapDocument resized = doc.WithWallReplaced(new MapWallInfo(w.Id, w.Kind, w.Layer, w.CellX, w.CellZ, 2, 10, 65536, false, 0, "", ""));
            MapWallInfo r = resized.Walls()[1];
            Assert.That(r.Id, Is.EqualTo(2UL));
            Assert.That(r.SizeX, Is.EqualTo(2));
            Assert.That(r.SizeZ, Is.EqualTo(10));
            Assert.That(r.HeightRaw, Is.EqualTo(65536L));
            Assert.That(r.Owner, Is.EqualTo("hand"));

            MapDocument moved = doc.WithCellsMoved("wall", 2, 3, -2);
            MapWallInfo m = moved.Walls()[1];
            Assert.That(m.Id, Is.EqualTo(2UL));
            Assert.That(m.CellX, Is.EqualTo(31));
            Assert.That(m.CellZ, Is.EqualTo(22));
            Assert.That(m.SizeX, Is.EqualTo(1));
            Assert.That(m.SizeZ, Is.EqualTo(16));
            Assert.That(m.Owner, Is.EqualTo("hand"));
            Assert.That(moved.Walls().Count, Is.EqualTo(4));

            MapDocument point = doc.WithMmMoved("point", 1, 1500, -700);
            MapPointInfo p = point.Points()[0];
            Assert.That(p.Id, Is.EqualTo(1UL));
            Assert.That(p.XMm, Is.EqualTo(17500L));
            Assert.That(p.ZMm, Is.EqualTo(19300L));
            Assert.That(p.HasSpawn, Is.True);
            Assert.That(p.SpawnXMm, Is.EqualTo(17500L));
            Assert.That(p.SpawnZMm, Is.EqualTo(19300L));
            Assert.That(p.Owner, Is.EqualTo("hand"));
        }

        [Test]
        public void IdsAreNeverReused()
        {
            MapDocument doc = Open(M6Path);
            ulong next = doc.NextId("wall");
            Assert.That(next, Is.EqualTo(5UL));
            MapDocument removed = doc.WithoutEntry("wall", 4);
            Assert.That(removed.NextId("wall"), Is.EqualTo(5UL));
            MapDocument added = removed.WithWallAdded(Wall(40, 2, 1, 3));
            Assert.That(added.Walls()[3].Id, Is.EqualTo(5UL));
            Assert.That(added.Walls()[3].Owner, Is.EqualTo("hand"));
            Assert.That(added.NextId("wall"), Is.EqualTo(6UL));

            MapDocument unit = doc.WithoutEntry("unit", 80).WithUnitAdded(Unit(30000, 30000));
            Assert.That(unit.Units()[79].Key, Is.EqualTo(81UL));
        }

        [Test]
        public void CopiesTakeTheNextIdAndNoSource()
        {
            MapDocument doc = Open(DoorPath);
            Assert.That(doc.Walls()[2].Stamp, Is.EqualTo("door"));
            ulong next = doc.NextId("wall");
            MapDocument copied = doc.WithCopy("wall", 3, -3, 0);
            MapWallInfo c = copied.Walls()[3];
            Assert.That(c.Id, Is.EqualTo(next));
            Assert.That(c.Stamp, Is.EqualTo(""));
            Assert.That(c.Owner, Is.EqualTo("hand"));
            Assert.That(c.CellX, Is.EqualTo(27));
            Assert.That(c.CellZ, Is.EqualTo(21));
            MapWallInfo original = copied.Walls()[2];
            Assert.That(original.Stamp, Is.EqualTo("door"));
            Assert.That(original.Owner, Is.EqualTo("stamp:door"));
            MapDesc desc = Desc(copied);
            Assert.That(desc.Walls[3].Src, Is.Null);
            Assert.That(desc.Stamps.Count, Is.EqualTo(3));
        }

        [Test]
        public void CopyMovesWallsByCellsAndPointsByMillimetres()
        {
            MapDocument doc = Open(M6Path);
            MapWallInfo w = doc.WithCopy("wall", 2, 3, -2).Walls()[4];
            Assert.That(w.Id, Is.EqualTo(5UL));
            Assert.That(w.CellX, Is.EqualTo(31));
            Assert.That(w.CellZ, Is.EqualTo(22));
            Assert.That(w.SizeZ, Is.EqualTo(16));
            Assert.That(w.HeightRaw, Is.EqualTo(131072L));

            MapPointInfo p = doc.WithCopy("point", 1, 3000, -2000).Points()[3];
            Assert.That(p.Id, Is.EqualTo(4UL));
            Assert.That(p.XMm, Is.EqualTo(19000L));
            Assert.That(p.ZMm, Is.EqualTo(18000L));
            Assert.That(p.SpawnXMm, Is.EqualTo(19000L));
            Assert.That(p.SpawnZMm, Is.EqualTo(18000L));
            Assert.That(p.RadiusMm, Is.EqualTo(6000));
            Assert.That(p.Owner, Is.EqualTo("hand"));

            MapBaseInfo b = doc.WithCopy("base", 2, -1000, 500).Bases()[2];
            Assert.That(b.Id, Is.EqualTo(3UL));
            Assert.That(b.Team, Is.EqualTo(2));
            Assert.That(b.XMm, Is.EqualTo(73000L));
            Assert.That(b.ZMm, Is.EqualTo(20500L));

            MapUnitInfo u = doc.WithCopy("unit", 41, 250, -750).Units()[80];
            Assert.That(u.Key, Is.EqualTo(81UL));
            Assert.That(u.Team, Is.EqualTo(2));
            Assert.That(u.Squad, Is.EqualTo(2));
            Assert.That(u.Reserve, Is.False);
            Assert.That(u.XMm, Is.EqualTo(doc.Units()[40].XMm + 250));
            Assert.That(u.ZMm, Is.EqualTo(doc.Units()[40].ZMm - 750));
        }

        [TestCase("wall", 9UL)]
        [TestCase("point", 3UL)]
        [TestCase("base", 3UL)]
        [TestCase("unit", 9UL)]
        [TestCase("plate", 4UL)]
        [TestCase("cutout", 2UL)]
        public void NextIdReadsEachKind(string kind, ulong expected)
        {
            Assert.That(Open(PlatePath).NextId(kind), Is.EqualTo(expected));
        }

        [TestCase("door")]
        [TestCase("walls")]
        [TestCase("")]
        public void NextIdRefusesOtherKinds(string kind)
        {
            Assert.Throws<ArgumentException>(() => Open(PlatePath).NextId(kind));
        }

        [TestCase("wall", 1UL, true)]
        [TestCase("point", 1UL, false)]
        [TestCase("base", 1UL, false)]
        [TestCase("unit", 1UL, false)]
        [TestCase("plate", 2UL, true)]
        [TestCase("cutout", 1UL, true)]
        public void CellMovesTakeWallsPlatesAndCutouts(string kind, ulong id, bool accepted)
        {
            MapDocument doc = Open(PlatePath);
            if (!accepted)
            {
                Assert.Throws<ArgumentException>(() => doc.WithCellsMoved(kind, id, 1, 2));
                return;
            }
            MapDesc before = Desc(doc), after = Desc(doc.WithCellsMoved(kind, id, 1, 2));
            (int X, int Z) cell0 = CellOf(before, kind, id), cell1 = CellOf(after, kind, id);
            Assert.That(cell1.X, Is.EqualTo(cell0.X + 1));
            Assert.That(cell1.Z, Is.EqualTo(cell0.Z + 2));
        }

        [TestCase("wall", 1UL, false)]
        [TestCase("point", 1UL, true)]
        [TestCase("base", 1UL, true)]
        [TestCase("unit", 1UL, true)]
        [TestCase("plate", 2UL, false)]
        [TestCase("cutout", 1UL, false)]
        public void MillimetreMovesTakePointsBasesAndUnits(string kind, ulong id, bool accepted)
        {
            MapDocument doc = Open(PlatePath);
            if (!accepted)
            {
                Assert.Throws<ArgumentException>(() => doc.WithMmMoved(kind, id, 100, 200));
                return;
            }
            MapDesc before = Desc(doc), after = Desc(doc.WithMmMoved(kind, id, 100, 200));
            MapMm at0 = AtOf(before, kind, id), at1 = AtOf(after, kind, id);
            Assert.That(at1.X, Is.EqualTo(at0.X + 100));
            Assert.That(at1.Z, Is.EqualTo(at0.Z + 200));
        }

        [Test]
        public void MovesAndCopiesRefuseOtherKindsAndMissingIds()
        {
            MapDocument doc = Open(PlatePath);
            Assert.Throws<ArgumentException>(() => doc.WithCellsMoved("door", 1, 1, 1));
            Assert.Throws<ArgumentException>(() => doc.WithMmMoved("door", 1, 1, 1));
            Assert.Throws<ArgumentException>(() => doc.WithCopy("door", 1, 1, 1));
            Assert.Throws<ArgumentException>(() => doc.WithCellsMoved("wall", 99, 1, 1));
            Assert.Throws<ArgumentException>(() => doc.WithMmMoved("unit", 99, 1, 1));
            Assert.Throws<ArgumentException>(() => doc.WithCopy("base", 99, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.WithCellsMoved("wall", 1, 100, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.WithMmMoved("unit", 1, 0, -100000));
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.WithCopy("wall", 1, int.MaxValue + 1L, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.WithMmMoved("unit", 1, long.MaxValue, 0));
        }

        // E0 E6 2: after a wall moves, the baked passability is the new footprint, cell by cell.
        [Test]
        public void MovedWallMovesThePassabilityFootprint()
        {
            MapDocument doc = Open(M6Path).WithCellsMoved("wall", 1, 5, 3);
            WcMap map = Baked(doc);
            var blocked = new bool[80, 40];
            foreach (MapWallInfo w in doc.Walls())
                for (int x = w.CellX; x < w.CellX + w.SizeX; x++)
                    for (int z = w.CellZ; z < w.CellZ + w.SizeZ; z++) blocked[x, z] = true;
            Assert.That(blocked[33, 3], Is.True);
            Assert.That(blocked[33, 18], Is.True);
            Assert.That(blocked[28, 0], Is.False);
            int count = 0;
            for (int z = 0; z < 40; z++)
                for (int x = 0; x < 80; x++)
                {
                    Assert.That(Passable(map, x, z), Is.EqualTo(!blocked[x, z]), "cell " + x + "," + z);
                    if (blocked[x, z]) count++;
                }
            Assert.That(count, Is.EqualTo(64));
        }

        // A stamped entry removed through the facade leaves a tombstone, so stamping the same instance again (directly through
        // MapEdit: the facade always picks a new name) does not bring it back.
        [Test]
        public void RemovedStampEntryStaysRemovedWhenTheSameStampRunsAgain()
        {
            MapDocument doc = Open(DoorPath).WithoutEntry("wall", 1);
            MapDesc desc = Desc(doc);
            Assert.That(desc.Removed.Count, Is.EqualTo(1));
            var edit = new MapEdit(desc);
            MapStampResult result = edit.Stamp(MapPrefabs.Door(desc, "door", new MapXZ(30, 0), false, 40, 19, 2, 196608, "walls", "wall"));
            Assert.That(result.EntriesSkipped, Is.EqualTo(1));
            Assert.That(edit.Current.Walls.Count, Is.EqualTo(2));
            foreach (MapWall w in edit.Current.Walls) Assert.That(w.Src.Src, Is.Not.EqualTo(1UL));
        }

        // Acceptance 8.
        [Test]
        public void PlatesAndCutoutsAddMoveReshapeAndRemove()
        {
            MapDocument doc = Open(M6Path);
            Assert.That(Desc(doc).Version, Is.EqualTo(1));
            doc = doc.WithPlateAdded(Flat(2, 2, 4, 3, 2 * M));
            Assert.That(Desc(doc).Version, Is.EqualTo(2));
            MapPlateInfo p = doc.Plates()[0];
            Assert.That(p.Id, Is.EqualTo(1UL));
            Assert.That(p.Sloped, Is.False);
            Assert.That(p.YRaw, Is.EqualTo(2 * M));

            doc = doc.WithCellsMoved("plate", 1, 2, 1);
            p = doc.Plates()[0];
            Assert.That(p.CellX, Is.EqualTo(4));
            Assert.That(p.CellZ, Is.EqualTo(3));
            Assert.That(p.Owner, Is.EqualTo("hand"));

            doc = doc.WithPlateReplaced(new MapPlateInfo(1, 4, 3, 4, 3, true, 0, "x", M, 2 * M, M, "", "", ""));
            p = doc.Plates()[0];
            Assert.That(p.Sloped, Is.True);
            Assert.That(p.SlopeAxis, Is.EqualTo("x"));
            Assert.That(p.FromRaw, Is.EqualTo(M));
            Assert.That(p.ToRaw, Is.EqualTo(2 * M));
            Assert.That(p.ThickRaw, Is.EqualTo(M));
            Assert.That(Desc(doc).Plates[0].YRaw.HasValue, Is.False);

            doc = doc.WithPlateReplaced(new MapPlateInfo(1, 4, 3, 4, 3, false, 3 * M, "", 0, 0, M / 2, "", "", ""));
            p = doc.Plates()[0];
            Assert.That(p.Sloped, Is.False);
            Assert.That(p.YRaw, Is.EqualTo(3 * M));
            Assert.That(p.ThickRaw, Is.EqualTo(M / 2));
            Assert.That(Desc(doc).Plates[0].Slope, Is.Null);

            doc = doc.WithCutoutAdded(Cut(1, 5, 4, 1, 1));
            MapDocument withCutout = doc;
            Assert.Throws<ArgumentException>(() => doc.WithoutEntry("plate", 1));
            SameBytes(doc, withCutout);

            doc = doc.WithCopy("cutout", 1, 1, 1);
            MapCutoutInfo c = doc.Cutouts()[1];
            Assert.That(c.Id, Is.EqualTo(2UL));
            Assert.That(c.Plate, Is.EqualTo(1UL));
            Assert.That(c.CellX, Is.EqualTo(6));
            Assert.That(c.CellZ, Is.EqualTo(5));

            doc = doc.WithCellsMoved("cutout", 2, -1, 0);
            Assert.That(doc.Cutouts()[1].CellX, Is.EqualTo(5));

            doc = doc.WithoutEntry("cutout", 1).WithoutEntry("cutout", 2).WithoutEntry("plate", 1);
            Assert.That(doc.Plates().Count, Is.EqualTo(0));
            Assert.That(doc.Cutouts().Count, Is.EqualTo(0));
            Assert.That(doc.NextId("plate"), Is.EqualTo(2UL));
            Assert.That(Desc(doc).Version, Is.EqualTo(2));
            Assert.That(MapDocument.Open(doc.Save()).Save(), Is.EqualTo(doc.Save()));
        }

        [Test]
        public void CutoutOnAMissingPlateIsRefused()
        {
            MapDocument doc = Open(M6Path);
            Assert.Throws<ArgumentException>(() => doc.WithCutoutAdded(Cut(1, 2, 2, 1, 1)));
        }

        private static (int X, int Z) CellOf(MapDesc d, string kind, ulong id)
        {
            switch (kind)
            {
                case "wall": foreach (MapWall w in d.Walls) if (w.Id == id) return (w.Cell.X, w.Cell.Z); break;
                case "plate": foreach (MapPlate p in d.Plates) if (p.Id == id) return (p.Cell.X, p.Cell.Z); break;
                case "cutout": foreach (MapCutout c in d.Cutouts) if (c.Id == id) return (c.Cell.X, c.Cell.Z); break;
            }
            throw new InvalidDataException(kind + " " + id);
        }

        private static MapMm AtOf(MapDesc d, string kind, ulong id)
        {
            switch (kind)
            {
                case "point": foreach (MapPoint p in d.Points) if ((ulong)p.Id == id) return p.At; break;
                case "base": foreach (MapBase b in d.Bases) if ((ulong)b.Id == id) return b.At; break;
                case "unit": foreach (MapUnit u in d.Units) if (u.Key == id) return u.At; break;
            }
            throw new InvalidDataException(kind + " " + id);
        }
    }
}
