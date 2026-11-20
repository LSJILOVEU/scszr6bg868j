using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Ed6aFixtures;

namespace WarCore.Tests
{
    // Acceptance 5: door walls, windows, their instance names and removing a whole stamp.
    public sealed class Ed6aStampTests
    {
        private const long Top = 3 * M;

        private static MapStampPlan Door(MapDesc d, string instance) =>
            MapPrefabs.Door(d, instance, new MapXZ(5, 2), true, 8, 3, 2, Top, "walls", "wall");

        // The empty map with its owner, stamps, removed and wall tables replaced.
        private static MapDocument Tables(string[] owners, MapStamp[] stamps, MapSrc[] removed, MapWall[] walls, ulong nextWall)
        {
            MapDesc d = Desc(Empty("rwr-m6"));
            var layers = new List<MapLayer>(d.Layers).ToArray();
            return Open(new MapDesc(d.Id, d.Content, d.Pack, d.Size, d.Origin, d.FloorRaw, d.CeilingRaw, layers, null, null, walls,
                new MapPoint[0], new MapBase[0], new MapUnit[0], d.Run, owners, null, null, stamps, removed, new MapNext(nextWall, 1, 1, 1)));
        }

        private static string FirstStamp(MapDocument doc) => doc.WithDoorWall(5, 2, true, 8, 3, 2, Top).Walls()[0].Stamp;

        [Test]
        public void DoorWallIsTheDoorPrefabStampedAsDoor1()
        {
            MapDocument doc = Empty("rwr-m6");
            MapDocument door = doc.WithDoorWall(5, 2, true, 8, 3, 2, Top);
            Assert.That(door.Save(), Is.EqualTo(StampedBytes(doc, d => Door(d, "door-1"))));
            Assert.That(door.Walls().Count, Is.EqualTo(3));
            foreach (MapWallInfo w in door.Walls())
            {
                Assert.That(w.Stamp, Is.EqualTo("door-1"));
                Assert.That(w.Owner, Is.EqualTo("stamp:door-1"));
                Assert.That(w.Layer, Is.EqualTo("walls"));
                Assert.That(w.Kind, Is.EqualTo("wall"));
            }
            MapDocument second = door.WithDoorWall(5, 10, false, 6, 2, 2, Top);
            Assert.That(second.Walls()[3].Stamp, Is.EqualTo("door-2"));
            Assert.That(second.Save(), Is.EqualTo(StampedBytes(door, d =>
                MapPrefabs.Door(d, "door-2", new MapXZ(5, 10), false, 6, 2, 2, Top, "walls", "wall"))));
        }

        [Test]
        public void DoorNameSkipsOwnerRows()
        {
            MapDocument one = Tables(new[] { "hand", "stamp:terrain", "stamp:door-1" }, new MapStamp[0], new MapSrc[0], new MapWall[0], 1);
            Assert.That(FirstStamp(one), Is.EqualTo("door-2"));
            MapDocument oneThree = Tables(new[] { "hand", "stamp:door-1", "stamp:door-3" }, new MapStamp[0], new MapSrc[0], new MapWall[0], 1);
            Assert.That(FirstStamp(oneThree), Is.EqualTo("door-2"));
            MapDocument other = Tables(new[] { "hand", "stamp:window-1", "stamp:door" }, new MapStamp[0], new MapSrc[0], new MapWall[0], 1);
            Assert.That(FirstStamp(other), Is.EqualTo("door-1"));
        }

        // A hand-written description may hold a tombstone, a stamps row or a src without an owner row for that name.
        [Test]
        public void DoorNameSkipsTombstonesStampsRowsAndSources()
        {
            MapDocument tomb = Tables(new[] { "hand" }, new MapStamp[0], new[] { new MapSrc("door-1", "wall", 1) }, new MapWall[0], 1);
            Assert.That(FirstStamp(tomb), Is.EqualTo("door-2"));
            Assert.That(tomb.WithDoorWall(5, 2, true, 8, 3, 2, Top).Walls().Count, Is.EqualTo(3));
            MapDocument row = Tables(new[] { "hand" }, new[] { new MapStamp("door-1", "wall", 2, 1) }, new MapSrc[0], new MapWall[0], 2);
            Assert.That(FirstStamp(row), Is.EqualTo("door-2"));
            var src = new MapWall(1, "wall", "walls", new MapXZ(20, 15), new MapXZ(1, 1), 2 * M, 0, new MapSrc("door-1", "wall", 5));
            MapDocument entry = Tables(new[] { "hand" }, new MapStamp[0], new MapSrc[0], new[] { src }, 2);
            Assert.That(entry.WithDoorWall(5, 2, true, 8, 3, 2, Top).Walls()[1].Stamp, Is.EqualTo("door-2"));
        }

        [Test]
        public void WithoutStampRemovesEveryEntryOfTheInstanceAndLeavesTombstones()
        {
            MapDocument door = Empty("rwr-m6").WithDoorWall(5, 2, true, 8, 3, 2, Top).WithWallAdded(Wall(20, 15, 1, 1));
            MapDocument gone = door.WithoutStamp("door-1");
            Assert.That(gone.Walls().Count, Is.EqualTo(1));
            Assert.That(gone.Walls()[0].Id, Is.EqualTo(4UL));
            MapDesc before = Desc(door), after = Desc(gone);
            Assert.That(after.Removed.Count, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(after.Removed[i].Instance, Is.EqualTo("door-1"));
                Assert.That(after.Removed[i].Src, Is.EqualTo((ulong)i + 1));
            }
            Assert.That(after.Owners, Is.EqualTo(before.Owners));
            Assert.That(after.Stamps.Count, Is.EqualTo(3));
            Assert.That(gone.WithDoorWall(5, 2, true, 8, 3, 2, Top).Walls()[1].Stamp, Is.EqualTo("door-2"));
        }

        [Test]
        public void WithoutStampAlsoRemovesHandEditedEntriesOfTheInstance()
        {
            MapDocument door = Empty("rwr-m6").WithDoorWall(5, 2, true, 8, 3, 2, Top);
            MapWallInfo w = door.Walls()[0];
            door = door.WithWallReplaced(new MapWallInfo(w.Id, w.Kind, w.Layer, w.CellX, w.CellZ, w.SizeX, w.SizeZ, 2 * M, false, 0, "", ""));
            Assert.That(door.Walls()[0].Owner, Is.EqualTo("hand"));
            Assert.That(door.Walls()[0].Stamp, Is.EqualTo("door-1"));
            MapDocument gone = door.WithoutStamp("door-1");
            Assert.That(gone.Walls().Count, Is.EqualTo(0));
            Assert.That(Desc(gone).Removed.Count, Is.EqualTo(3));
        }

        [TestCase(DoorPath)]
        [TestCase(DoorTerrainPath)]
        public void WithoutStampOnTheDoorDemosKeepsTheTerrainAndTables(string path)
        {
            MapDocument doc = Open(path);
            MapDocument gone = doc.WithoutStamp("door");
            MapDesc before = Desc(doc), after = Desc(gone);
            Assert.That(before.Walls.Count - after.Walls.Count, Is.EqualTo(3));
            foreach (MapWallInfo w in gone.Walls()) Assert.That(w.Stamp, Is.EqualTo(""));
            Assert.That(after.Removed.Count, Is.EqualTo(3));
            Assert.That(after.Owners, Is.EqualTo(before.Owners));
            Assert.That(after.Stamps.Count, Is.EqualTo(before.Stamps.Count));
            Assert.That(after.Heights, Is.EqualTo(before.Heights));
            Assert.That(after.HeightOwner, Is.EqualTo(before.HeightOwner));
            Assert.That(after.Surfaces == null, Is.EqualTo(before.Surfaces == null));
            Assert.That(after.Points.Count, Is.EqualTo(before.Points.Count));
            Assert.That(after.Units.Count, Is.EqualTo(before.Units.Count));
        }

        [Test]
        public void WithoutStampOfAnUnknownInstanceIsRefused()
        {
            MapDocument doc = Open(DoorPath);
            Assert.Throws<ArgumentException>(() => doc.WithoutStamp("door-1"));
            Assert.Throws<ArgumentException>(() => doc.WithoutStamp("terrain"));
            Assert.Throws<ArgumentNullException>(() => doc.WithoutStamp(null));
        }

        [Test]
        public void WindowUsesTheLayerBody()
        {
            using (var cartridge = new Ed6aBodyCartridge())
            {
                MapDocument doc = Empty(Ed6aBodyCartridge.Layer);
                MapDocument window = null;
                Assert.DoesNotThrow(() => window = doc.WithWindow(5, 2, true, 8, 3, 2, Top));
                Assert.That(window.Save(), Is.EqualTo(StampedBytes(doc, d =>
                    MapPrefabs.Window(d, "window-1", new MapXZ(5, 2), true, 8, 3, 2, Top, "walls", "wall", cartridge.Body))));
                Assert.That(window.Walls().Count, Is.EqualTo(4));
                MapDocument second = window.WithWindow(5, 10, false, 6, 2, 2, Top).WithDoorWall(20, 2, true, 6, 2, 2, Top);
                Assert.That(second.Walls()[4].Stamp, Is.EqualTo("window-2"));
                Assert.That(second.Walls()[8].Stamp, Is.EqualTo("door-1"));
            }
        }

        // A body changes the window's gate: a sill above min(eye, muzzle) - radius is refused, so the body must be read.
        [Test]
        public void WindowGateReadsTheLayerBody()
        {
            using (new Ed6aBodyCartridge())
            {
                MapDocument doc = Empty(Ed6aBodyCartridge.Layer);
                byte[] text = System.Text.Encoding.UTF8.GetBytes("{ \"id\": \"ed6a-low\", \"extends\": \"rwr\", "
                    + "\"combat\": { \"body\": { \"height\": 1.75, \"eye\": 1.0, \"muzzle\": 1.0 } } }");
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(
                    Environment.GetEnvironmentVariable(WarCore.Content.ContentCatalog.DirectoriesVariable), "ed6a-low.json"), text);
                MapDocument low = Empty("ed6a-low");
                Assert.Throws<ArgumentException>(() => low.WithWindow(5, 2, true, 8, 3, 2, Top));
                Assert.DoesNotThrow(() => doc.WithWindow(5, 2, true, 8, 3, 2, Top));
            }
        }

        [Test]
        public void WindowWithoutABodyIsRefusedClearly()
        {
            MapDocument doc = Empty("rwr-m6");
            byte[] before = doc.Save();
            InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => doc.WithWindow(5, 2, true, 8, 3, 2, Top));
            Assert.That(e.Message, Does.Contain("no combat body"));
            Assert.That(e.Message, Does.Contain("rwr-m6"));
            Assert.That(doc.Save(), Is.EqualTo(before));
        }
    }
}
