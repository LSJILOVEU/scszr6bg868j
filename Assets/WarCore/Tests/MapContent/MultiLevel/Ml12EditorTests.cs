using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Ml12Fixtures;

namespace WarCore.Tests
{
    // ML-12a: upper-floor doors and windows, the .build.json text, and the floor view.
    public sealed class Ml12EditorTests
    {
        private const string B1 = "building-1";

        private static byte[] Direct(MapDesc start, Func<MapDesc, MapStampPlan> plan)
        {
            var e = new MapEdit(start);
            e.Batch(x => x.Stamp(plan(x.Current)));
            return MapDescJson.Write(e.Current);
        }

        [Test]
        public void ADoorOnAFloorIsTheDirectStamp()
        {
            MapDocument doc = Doc(Blank(24, 16)).WithBuilding(null, Tower(2)).Document;
            MapDocument door = doc.WithDoorOn(4, 5, true, 6, 2, 2, 3 * M, 6 * M);
            Assert.That(door.Save(), Is.EqualTo(Direct(Desc(doc), d => MapPrefabs.DoorOn(d, "door-1", new MapXZ(4, 5), true, 6, 2, 2, 3 * M, 6 * M, "walls", "wall"))));
            MapDesc d2 = Desc(door);
            List<MapWall> parts = d2.Walls.Where(w => w.Src?.Instance == "door-1").OrderBy(w => w.Src.Src).ToList();
            Assert.That(parts.Select(w => w.Cell.X + "x" + w.Size.X + " b" + w.BaseRaw + " h" + w.HeightRaw).ToArray(), Is.EqualTo(new[]
            {
                "4x2 b" + 3 * M + " h" + 3 * M,
                "6x2 b" + (3 * M + WallSpan.ClearanceRaw) + " h" + (3 * M - WallSpan.ClearanceRaw),
                "8x2 b" + 3 * M + " h" + 3 * M,
            }));
            // A second door takes the next name.
            Assert.That(Desc(door.WithDoorOn(4, 7, true, 6, 2, 2, 3 * M, 6 * M)).Walls.Count(w => w.Src?.Instance == "door-2"), Is.EqualTo(3));
        }

        [Test]
        public void AWindowOnAFloorNeedsABody()
        {
            using (var cart = new Ed6aBodyCartridge())
            {
                MapDocument doc = Doc(Blank(24, 16, pack: Ed6aBodyCartridge.Layer)).WithBuilding(null, Tower(2)).Document;
                MapDocument win = doc.WithWindowOn(2, 3, false, 8, 2, 3, 3 * M, 6 * M);
                Assert.That(win.Save(), Is.EqualTo(Direct(Desc(doc), d => MapPrefabs.WindowOn(d, "window-1", new MapXZ(2, 3), false, 8, 2, 3, 3 * M, 6 * M,
                    "walls", "wall", cart.Body))));
                List<MapWall> parts = Desc(win).Walls.Where(w => w.Src?.Instance == "window-1").ToList();
                Assert.That(parts.Count, Is.EqualTo(4));
                Assert.That(parts.All(w => w.BaseRaw >= 3 * M), Is.True);
            }
            MapDocument plain = Doc(Blank(24, 16)).WithBuilding(null, Tower(2)).Document;
            byte[] before = plain.Save();
            Assert.Throws<InvalidOperationException>(() => plain.WithWindowOn(2, 3, false, 8, 2, 3, 3 * M, 6 * M));
            Assert.That(plain.Save(), Is.EqualTo(before));
        }

        [Test]
        public void TheBuildFileIsCanonical()
        {
            MapBuildResult r = Doc(Blank(40, 16)).WithBuilding(null, Tower(2));
            MapBuildResult two = r.Document.WithBuilding(r.BuildJson, new MapBuildingInfo(20, 2, 12, 10, 2, 3 * M, M / 2, "e", 2, 3, false, 25, 4, 4));
            string json = two.BuildJson;
            Assert.That(MapBuildSpec.Read(json).Write(), Is.EqualTo(json));
            // Other spacing and key order read back to the same text.
            string loose = json.Replace("\n", "\n\n  ").Replace("{", "{ ");
            Assert.That(MapBuildSpec.Read(loose).Write(), Is.EqualTo(json));
            IReadOnlyList<MapBuildingInfo> list = two.Document.Buildings(json);
            Assert.That(list.Select(b => b.Instance + " " + b.CellX + "," + b.CellZ + " " + b.SizeX + "x" + b.SizeZ + " f" + b.Floors + " " + b.StoreyRaw + " "
                + b.ThickRaw + " " + b.DoorSide + b.DoorAt + "+" + b.DoorWidth + " " + b.StairAlongX + " " + b.StairX + "," + b.StairZ + " " + b.StairLength).ToArray(),
                Is.EqualTo(new[]
                {
                    "building-1 2,2 12x10 f2 " + 3 * M + " " + M / 2 + " s3+2 True 4,8 6",
                    "building-2 20,2 12x10 f2 " + 3 * M + " " + M / 2 + " e2+3 False 25,4 4",
                }));
            Assert.That(r.Document.Buildings(null), Is.Empty);

            string unknown = json.Replace("\"floors\"", "\"roof\": 1, \"floors\"");
            Assert.That(Assert.Throws<ArgumentException>(() => MapBuildSpec.Read(unknown)).Message, Does.Contain("unknown key"));
            Assert.Throws<ArgumentException>(() => two.Document.Buildings(unknown));
            Assert.Throws<ArgumentException>(() => MapBuildSpec.Read(json.Replace("\"version\": 1", "\"version\": 2")));
        }

        // Without the .build.json floors cannot change; a new building starts a new file.
        [Test]
        public void WithoutTheBuildFile()
        {
            MapBuildResult r = Doc(Blank(24, 16)).WithBuilding(null, Tower(2));
            Assert.Throws<InvalidOperationException>(() => r.Document.WithFloors(null, B1, 3));
            MapBuildResult again = r.Document.WithBuilding(null, new MapBuildingInfo(14, 2, 10, 8, 1, 3 * M, M / 2, "w", 2, 2, true, 16, 7, 4));
            Assert.That(again.Instance, Is.EqualTo("building-2"));
            Assert.That(again.Document.Buildings(again.BuildJson).Select(b => b.Instance).ToArray(), Is.EqualTo(new[] { "building-2" }));
        }

        // Tower(3) with hand-made entries, viewed at floors 0, 1 and 2.
        [Test]
        public void TheFloorViewFollowsTheTable()
        {
            MapDocument doc = Doc(Blank(24, 16)).WithBuilding(null, Tower(3)).Document;
            MapDesc d = Desc(doc);
            ulong P(ulong src) => BySrc(d.Plates, p => p.Src, B1, src).Id;
            ulong first = P(1001), second = P(2001);
            doc = doc.WithWallAdded(new MapWallInfo("wall", "walls", 6, 5, 1, 1, 2 * M, false, 0))   // inside, ground
                .WithWallAdded(new MapWallInfo("wall", "walls", 6, 6, 1, 1, 2 * M, true, 3 * M))       // inside, on floor 1
                .WithWallAdded(new MapWallInfo("wall", "walls", 6, 7, 1, 1, 2 * M, true, 6 * M))       // inside, on floor 2
                .WithWallAdded(new MapWallInfo("wall", "walls", 20, 10, 1, 1, 2 * M, true, 3 * M))     // outside, raised
                .WithPlateAdded(new MapPlateInfo(16, 2, 3, 3, false, M, "", 0, 0, M / 2, ""))           // outside, hand-made
                .WithUnitAdded(new MapUnitInfo("rwr.soldier-1", 1, 1, 18500, 12500, false, false, 0))  // outside
                .WithUnitAdded(new MapUnitInfo("rwr.soldier-1", 1, 1, 9500, 4500, false, false, 0))    // inside, ground
                .WithUnitAdded(new MapUnitInfo("rwr.soldier-1", 1, 1, 9500, 5500, false, true, first)) // floor 1
                .WithUnitAdded(new MapUnitInfo("rwr.soldier-1", 1, 1, 9500, 6500, false, true, second));// floor 2
            MapDesc h = Desc(doc);
            ulong handPlate = h.Plates.Single(p => p.Src == null).Id;
            doc = doc.WithUnitAdded(new MapUnitInfo("rwr.soldier-1", 1, 1, 17500, 3500, false, true, handPlate)); // on the hand plate
            h = Desc(doc);

            var want = new Dictionary<string, int>();
            foreach (MapWall w in h.Walls)
            {
                if (w.Src != null) { want["wall " + w.Id] = w.Src.Src >= 2000 ? 2 : w.Src.Src >= 1000 ? 1 : 0; continue; }
                want["wall " + w.Id] = w.Cell.X == 6 ? w.Cell.Z - 5 : 0;
            }
            foreach (MapPlate p in h.Plates)
                want["plate " + p.Id] = p.Src == null ? 0 : new Dictionary<ulong, int> { { 1001, 1 }, { 2001, 2 }, { 30, 0 }, { 1030, 1 } }[p.Src.Src];
            foreach (MapCutout c in h.Cutouts) want["cutout " + c.Id] = c.Src.Src == 1040 ? 1 : 2;
            int[] units = { 0, 0, 1, 2, 0 };
            for (int k = 0; k < h.Units.Count; k++) want["unit " + h.Units[k].Key] = units[k];
            Assert.That(want.Values.Count(v => v == 0), Is.EqualTo(6 + 2 + 2 + 3), "walls 11-13 and 20-22, the inside ground and outside walls, "
                + "stair 0 and the hand plate, three units");

            for (int floor = 0; floor <= 2; floor++)
            {
                IReadOnlyList<MapFloorItem> items = MapFloorView.Of(doc, floor);
                Assert.That(items.Count, Is.EqualTo(want.Count));
                foreach (MapFloorItem item in items)
                {
                    string key = item.Kind + " " + item.Id;
                    Assert.That(item.Floor, Is.EqualTo(want[key]), key);
                    Assert.That(item.Ghost, Is.EqualTo(want[key] > floor), key + " at floor " + floor);
                }
            }
            Assert.That(MapFloorView.Of(doc, 5).Any(i => i.Ghost), Is.False);
            Assert.Throws<ArgumentException>(() => MapFloorView.Of(doc, -1));
        }
    }
}
