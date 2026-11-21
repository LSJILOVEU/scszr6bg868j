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
    // ML-12a: the source-number table, floors added and removed, and the cutouts' plates.
    public sealed class Ml12FloorsTests
    {
        private const string B1 = "building-1";

        private static ulong[] U(params ulong[] v) => v;

        // Floor f at generation g: the four walls (the door replaces its side's wall on the ground), the plate, the cutout.
        private static (string, ulong[])[] Floor(int f, int g, char door = 's')
        {
            ulong S(ulong part) => MapBuildings.Src(f, g, part);
            var walls = new List<ulong>();
            foreach ((char side, ulong part) in new[] { ('s', 10UL), ('n', 11UL), ('w', 12UL), ('e', 13UL) })
                if (f == 0 && side == door) walls.AddRange(U(S(MapBuildings.PartDoorFirst), S(MapBuildings.PartDoorLintel), S(MapBuildings.PartDoorLast)));
                else walls.Add(S(part));
            if (f == 0) return new[] { ("wall", walls.ToArray()) };
            return new[] { ("wall", walls.ToArray()), ("plate", U(S(MapBuildings.PartPlate))), ("cutout", U(S(MapBuildings.PartCutout))) };
        }

        private static (string, ulong[]) Stair(int f, int g) => ("plate", U(MapBuildings.Src(f, g, MapBuildings.PartStair)));

        private static List<string> Table(params (string, ulong[])[][] groups) => Expected(groups.SelectMany(x => x).ToArray());

        // The table, entry by entry, for Tower(3).
        [Test]
        public void SourceNumbersFollowTheTable()
        {
            MapDesc d = Desc(Doc(Blank(24, 16)).WithBuilding(null, Tower(3)).Document);
            Assert.That(Srcs(d, B1), Is.EqualTo(Table(Floor(0, 0), Floor(1, 0), Floor(2, 0), new[] { Stair(0, 0), Stair(1, 0) })));
            Assert.That(Srcs(d, B1), Is.EqualTo(Expected(
                ("wall", U(11, 12, 13, 20, 21, 22, 1010, 1011, 1012, 1013, 2010, 2011, 2012, 2013)),
                ("plate", U(1001, 2001, 30, 1030)), ("cutout", U(1040, 2040)))));
            // Geometry of the parts: plates inside the ring, stair slopes, raised walls.
            MapPlate p1 = BySrc(d.Plates, p => p.Src, B1, 1001), p2 = BySrc(d.Plates, p => p.Src, B1, 2001);
            MapPlate s0 = BySrc(d.Plates, p => p.Src, B1, 30), s1 = BySrc(d.Plates, p => p.Src, B1, 1030);
            Assert.That(Text(p1), Does.StartWith(p1.Id + " 3,3 10x8 y" + 3 * M + " s- t" + M / 2));
            Assert.That(Text(p2), Does.StartWith(p2.Id + " 3,3 10x8 y" + 6 * M + " s- t" + M / 2));
            Assert.That(Text(s0), Does.StartWith(s0.Id + " 4,8 6x1 y sx0>" + 3 * M + " t" + M / 2));
            Assert.That(s0.Slope.Axis + s1.Slope.Axis + s1.Slope.FromRaw + ">" + s1.Slope.ToRaw, Is.EqualTo("xx" + 3 * M + ">" + 6 * M));
            MapWall w2 = BySrc(d.Walls, w => w.Src, B1, 2012);
            Assert.That((w2.Cell.X, w2.Cell.Z, w2.Size.X, w2.Size.Z, w2.HeightRaw, w2.BaseRaw), Is.EqualTo((2, 3, 1, 8, 3 * M, (long?)(6 * M))));
            MapWall n0 = BySrc(d.Walls, w => w.Src, B1, 11);
            Assert.That((n0.Cell.X, n0.Cell.Z, n0.Size.X, n0.Size.Z, n0.HeightRaw, n0.BaseRaw), Is.EqualTo((2, 11, 12, 1, 3 * M, (long?)null)));
            // The door: x 2..4, the lintel over x 5..6, then x 7..13.
            MapWall a = BySrc(d.Walls, w => w.Src, B1, 20), l = BySrc(d.Walls, w => w.Src, B1, 21), z = BySrc(d.Walls, w => w.Src, B1, 22);
            Assert.That((a.Cell.X, a.Size.X, l.Cell.X, l.Size.X, z.Cell.X, z.Size.X), Is.EqualTo((2, 3, 5, 2, 7, 7)));
            Assert.That(l.BaseRaw, Is.Not.Null);
        }

        // Each cutout names its floor's plate: on the first run, a re-run of the same floors (via up and down), and after
        // adding a floor.
        [Test]
        public void EachCutoutNamesItsFloorsPlate()
        {
            void Check(MapDesc d, string instance)
            {
                foreach (MapCutout c in d.Cutouts.Where(c => c.Src?.Instance == instance))
                {
                    MapBuildings.Decode(c.Src.Src, out int f, out _);
                    MapPlate plate = d.Plates.Single(p => p.Id == c.Plate);
                    Assert.That(plate.Src.Instance, Is.EqualTo(instance));
                    MapBuildings.Decode(plate.Src.Src, out int pf, out ulong part);
                    Assert.That((pf, part, plate.Src.Src / MapBuildings.GenerationStep), Is.EqualTo((f, MapBuildings.PartPlate, c.Src.Src / MapBuildings.GenerationStep)));
                }
            }
            MapBuildResult r = Doc(Blank(24, 16)).WithBuilding(null, Tower(2));
            Check(Desc(r.Document), B1);
            MapBuildResult up = r.Document.WithFloors(r.BuildJson, B1, 3);
            Check(Desc(up.Document), B1);
            Assert.That(Desc(up.Document).Cutouts.Count, Is.EqualTo(2));
            MapBuildResult down = up.Document.WithFloors(up.BuildJson, B1, 2);
            Check(Desc(down.Document), B1);
            MapBuildResult again = down.Document.WithFloors(down.BuildJson, B1, 3);
            Check(Desc(again.Document), B1);
            Assert.That(Desc(again.Document).Cutouts.Count, Is.EqualTo(2));
        }

        // 2 -> 3: old entries keep id and bytes, the new ones follow the table; a hand-edited wall on floor 1 stays as it is.
        [Test]
        public void AddingAFloorKeepsWhatWasThere()
        {
            MapBuildResult r = Doc(Blank(24, 16)).WithBuilding(null, Tower(2));
            MapDesc before = Desc(r.Document);
            MapBuildResult up = r.Document.WithFloors(r.BuildJson, B1, 3);
            MapDesc after = Desc(up.Document);
            Assert.That(Entries(before).Except(Entries(after)).ToList(), Is.Empty, "changed or gone");
            Assert.That(Srcs(after, B1).Except(Srcs(before, B1)).ToList(), Is.EqualTo(Table(Floor(2, 0), new[] { Stair(1, 0) })));
            Assert.That(after.Removed, Is.Empty);
            Assert.That(MapBuildSpec.Read(up.BuildJson).Find(B1).Floors, Is.EqualTo(3));

            // A wall of floor 1 lowered by hand: adding a floor leaves it alone.
            MapWall w = BySrc(before.Walls, x => x.Src, B1, 1011);
            MapDocument hand = r.Document.WithWallReplaced(new MapWallInfo(w.Id, w.Kind, w.Layer, w.Cell.X, w.Cell.Z, w.Size.X, w.Size.Z,
                w.HeightRaw - M / 2, true, w.BaseRaw.Value, "", ""));
            string edited = Text(Desc(hand).Walls.Single(x => x.Id == w.Id));
            MapDesc handUp = Desc(hand.WithFloors(r.BuildJson, B1, 3).Document);
            Assert.That(Text(handUp.Walls.Single(x => x.Id == w.Id)), Is.EqualTo(edited));
            Assert.That(edited, Does.Contain(" h" + (w.HeightRaw - M / 2) + " "));
        }

        // 3 -> 2: floor 2's plate, walls and cutout and floor 1's stair go, each leaving a tombstone; the rest stays. 2 -> 3
        // again: they come back under the next generation (src + 100000), the old tombstones stay, floors 0 and 1 unchanged.
        [Test]
        public void RemovingAndReAddingAFloor()
        {
            MapBuildResult three = Doc(Blank(24, 16)).WithBuilding(null, Tower(3));
            MapDesc d3 = Desc(three.Document);
            MapBuildResult two = three.Document.WithFloors(three.BuildJson, B1, 2);
            MapDesc d2 = Desc(two.Document);
            List<string> gone = Table(Floor(2, 0), new[] { Stair(1, 0) });
            Assert.That(Srcs(d3, B1).Except(Srcs(d2, B1)).ToList(), Is.EqualTo(gone));
            Assert.That(Srcs(d2, B1).Except(Srcs(d3, B1)).ToList(), Is.Empty);
            Assert.That(d2.Removed.Select(s => s.Kind + " " + s.Src).OrderBy(s => s, StringComparer.Ordinal).ToList(), Is.EqualTo(gone));
            Assert.That(d2.Removed.All(s => s.Instance == B1), Is.True);
            Assert.That(Entries(d2).Except(Entries(d3)).ToList(), Is.Empty, "a kept entry changed");
            MapBuilding b2 = MapBuildSpec.Read(two.BuildJson).Find(B1);
            Assert.That((b2.Floors, string.Join(",", b2.Generations)), Is.EqualTo((2, "0,0,1")));

            MapBuildResult back = two.Document.WithFloors(two.BuildJson, B1, 3);
            MapDesc d = Desc(back.Document);
            Assert.That(Srcs(d, B1).Except(Srcs(d2, B1)).ToList(), Is.EqualTo(Table(Floor(2, 1), new[] { Stair(1, 1) })));
            Assert.That(Srcs(d, B1).Except(Srcs(d2, B1)).ToList(), Is.EqualTo(Expected(
                ("wall", U(102010, 102011, 102012, 102013)), ("plate", U(102001, 101030)), ("cutout", U(102040)))));
            Assert.That(d.Removed.Select(s => s.Kind + " " + s.Src).OrderBy(s => s, StringComparer.Ordinal).ToList(), Is.EqualTo(gone));
            Assert.That(Entries(d2).Except(Entries(d)).ToList(), Is.Empty, "floors 0 and 1 changed");
            // The floor view reads the floor through the generation.
            Assert.That(MapFloorView.Of(back.Document, 1).Where(i => i.Ghost).Select(i => i.Floor).Distinct().ToArray(), Is.EqualTo(new[] { 2 }));
            Assert.That(MapFloorView.Of(back.Document, 1).Count(i => i.Ghost), Is.EqualTo(6));
            // The same geometry as the first time round.
            Assert.That(Text(BySrc(d.Plates, p => p.Src, B1, 102001)).Split(' ').Skip(1).Take(5),
                Is.EqualTo(Text(BySrc(d3.Plates, p => p.Src, B1, 2001)).Split(' ').Skip(1).Take(5)));
        }

        // Two buildings: building-1 and building-2, each with its own entries; changing one leaves the other.
        [Test]
        public void TwoBuildingsKeepApart()
        {
            MapBuildResult a = Doc(Blank(40, 16)).WithBuilding(null, Tower(2));
            var east = new MapBuildingInfo(20, 2, 12, 10, 2, 3 * M, M / 2, "n", 3, 2, false, 25, 4, 6);
            MapBuildResult b = a.Document.WithBuilding(a.BuildJson, east);
            Assert.That((a.Instance, b.Instance), Is.EqualTo(("building-1", "building-2")));
            MapDesc d = Desc(b.Document);
            List<string> first = Srcs(Desc(a.Document), B1);
            Assert.That(Srcs(d, B1), Is.EqualTo(first));
            Assert.That(Srcs(d, "building-2"), Is.EqualTo(Table(Floor(0, 0, 'n'), Floor(1, 0, 'n'), new[] { Stair(0, 0) })));
            Assert.That(Gate(b.Document), Is.Empty);
            IReadOnlyList<MapBuildingInfo> list = b.Document.Buildings(b.BuildJson);
            Assert.That(list.Select(x => x.Instance + " " + x.CellX + " " + x.DoorSide + " " + x.StairAlongX).ToArray(),
                Is.EqualTo(new[] { "building-1 2 s True", "building-2 20 n False" }));

            MapBuildResult up = b.Document.WithFloors(b.BuildJson, "building-2", 3);
            MapDesc du = Desc(up.Document);
            List<string> b1 = Entries(d).Where(s => s.Contains(" " + B1 + "/")).ToList();
            Assert.That(Entries(du).Where(s => s.Contains(" " + B1 + "/")).ToList(), Is.EqualTo(b1));
            MapBuildResult gone = up.Document.WithoutBuilding(up.BuildJson, B1);
            Assert.That(Srcs(Desc(gone.Document), B1), Is.Empty);
            Assert.That(Srcs(Desc(gone.Document), "building-2"), Is.EqualTo(Srcs(du, "building-2")));
            Assert.That(gone.Document.Buildings(gone.BuildJson).Select(x => x.Instance).ToArray(), Is.EqualTo(new[] { "building-2" }));
        }

        // A .build.json for another width, depth or layer stops with nothing changed.
        [Test]
        public void ABuildFileForAnotherMapStops()
        {
            MapBuildResult r = Doc(Blank(24, 16)).WithBuilding(null, Tower(2));
            foreach (MapDocument other in new[] { Doc(Blank(25, 16)), Doc(Blank(24, 17)), Doc(Blank(24, 16, pack: "rwr")) })
            {
                Assert.Throws<ArgumentException>(() => other.WithBuilding(r.BuildJson, Tower(2)));
                Assert.Throws<ArgumentException>(() => other.WithFloors(r.BuildJson, B1, 3));
                Assert.Throws<ArgumentException>(() => other.Buildings(r.BuildJson));
            }
            Assert.Throws<ArgumentException>(() => r.Document.WithFloors(r.BuildJson, "building-9", 3));
            Assert.Throws<ArgumentException>(() => r.Document.WithFloors(r.BuildJson, B1, 2));
            Assert.Throws<ArgumentException>(() => r.Document.WithFloors(r.BuildJson, B1, 0));
            Assert.Throws<ArgumentException>(() => r.Document.WithFloors(r.BuildJson, B1, 33));
        }
    }
}
