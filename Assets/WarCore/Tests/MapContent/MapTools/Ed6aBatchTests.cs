using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Presentation;
using static WarCore.Tests.Ed6aFixtures;

namespace WarCore.Tests
{
    // One recorded facade write: its kind, the same write for a MapEntryEditList, and the direct call.
    internal sealed class Ed6aOp
    {
        internal readonly string Kind;
        internal readonly Action<MapEntryEditList> List;
        internal readonly Func<MapDocument, MapDocument> Direct;
        internal Ed6aOp(string kind, Action<MapEntryEditList> list, Func<MapDocument, MapDocument> direct) { Kind = kind; List = list; Direct = direct; }
    }

    // Acceptance 6: WithEntryEdits is one batch with the bytes of the same writes done one by one, and all or nothing.
    public sealed class Ed6aBatchTests
    {
        private const long Top = 3 * M;
        private const int W = 30, D = 20;

        internal static readonly string[] WriteKinds =
        {
            "addWall", "addPoint", "addBase", "addUnit", "addPlate", "addCutout", "replaceWall", "replacePoint", "replaceBase",
            "replaceUnit", "replacePlate", "replaceCutout", "moveCells", "moveMm", "copy", "remove", "door", "window", "removeStamp",
            "squad",
        };

        // A write drawn for the document cur; it may be refused (the caller then draws another).
        private static Ed6aOp Draw(Ed1Rng rng, MapDocument cur)
        {
            string kind = WriteKinds[rng.Next(WriteKinds.Length)];
            int x = rng.Next(W - 3), z = rng.Next(D - 3);
            long xm = 1000 + rng.Next((W - 2) * 1000), zm = 1000 + rng.Next((D - 2) * 1000);
            switch (kind)
            {
                case "addWall":
                {
                    MapWallInfo w = Wall(x, z, 1 + rng.Next(2), 1 + rng.Next(2), 2 * M * (1 + rng.Next(2)));
                    return new Ed6aOp(kind, l => l.AddWall(w), d => d.WithWallAdded(w));
                }
                case "addPoint":
                {
                    MapPointInfo p = Point(xm, zm);
                    return new Ed6aOp(kind, l => l.AddPoint(p), d => d.WithPointAdded(p));
                }
                case "addBase":
                {
                    MapBaseInfo b = Base(1 + rng.Next(2), xm, zm);
                    return new Ed6aOp(kind, l => l.AddBase(b), d => d.WithBaseAdded(b));
                }
                case "addUnit":
                {
                    MapUnitInfo u = Unit(xm, zm, 1 + rng.Next(2), 1 + rng.Next(3));
                    return new Ed6aOp(kind, l => l.AddUnit(u), d => d.WithUnitAdded(u));
                }
                case "addPlate":
                {
                    MapPlateInfo p = Flat(x, z, 2, 2, M * (1 + rng.Next(3)));
                    return new Ed6aOp(kind, l => l.AddPlate(p), d => d.WithPlateAdded(p));
                }
                case "addCutout":
                {
                    if (cur.Plates().Count == 0) return null;
                    MapPlateInfo on = cur.Plates()[rng.Next(cur.Plates().Count)];
                    MapCutoutInfo c = Cut(on.Id, on.CellX, on.CellZ, 1, 1);
                    return new Ed6aOp(kind, l => l.AddCutout(c), d => d.WithCutoutAdded(c));
                }
                case "replaceWall":
                {
                    if (cur.Walls().Count == 0) return null;
                    MapWallInfo o = cur.Walls()[rng.Next(cur.Walls().Count)];
                    var w = new MapWallInfo(o.Id, o.Kind, o.Layer, o.CellX, o.CellZ, o.SizeX, o.SizeZ, o.HeightRaw + 2, o.HasBase, o.BaseRaw, "", "");
                    return new Ed6aOp(kind, l => l.ReplaceWall(w), d => d.WithWallReplaced(w));
                }
                case "replacePoint":
                {
                    if (cur.Points().Count == 0) return null;
                    MapPointInfo o = cur.Points()[rng.Next(cur.Points().Count)];
                    var p = new MapPointInfo(o.Id, o.XMm, o.ZMm, o.RadiusMm + 100, 1, o.HasSpawn, o.SpawnXMm, o.SpawnZMm, o.HasSpawnPlate,
                        o.SpawnPlate, o.HasPlate, o.Plate, "", "");
                    return new Ed6aOp(kind, l => l.ReplacePoint(p), d => d.WithPointReplaced(p));
                }
                case "replaceBase":
                {
                    if (cur.Bases().Count == 0) return null;
                    MapBaseInfo o = cur.Bases()[rng.Next(cur.Bases().Count)];
                    var b = new MapBaseInfo(o.Id, 3 - o.Team, xm, zm, false, 0, "", "");
                    return new Ed6aOp(kind, l => l.ReplaceBase(b), d => d.WithBaseReplaced(b));
                }
                case "replaceUnit":
                {
                    if (cur.Units().Count == 0) return null;
                    MapUnitInfo o = cur.Units()[rng.Next(cur.Units().Count)];
                    var u = new MapUnitInfo(o.Key, "rwr.soldier-2", o.Team, o.Squad + 1, xm, zm, !o.Reserve, false, 0, "", "");
                    return new Ed6aOp(kind, l => l.ReplaceUnit(u), d => d.WithUnitReplaced(u));
                }
                case "replacePlate":
                {
                    if (cur.Plates().Count == 0) return null;
                    MapPlateInfo o = cur.Plates()[rng.Next(cur.Plates().Count)];
                    var p = new MapPlateInfo(o.Id, o.CellX, o.CellZ, o.SizeX, o.SizeZ, !o.Sloped, 2 * M, "z", M, 2 * M, o.ThickRaw, "", "", "");
                    return new Ed6aOp(kind, l => l.ReplacePlate(p), d => d.WithPlateReplaced(p));
                }
                case "replaceCutout":
                {
                    if (cur.Cutouts().Count == 0) return null;
                    MapCutoutInfo o = cur.Cutouts()[rng.Next(cur.Cutouts().Count)];
                    var c = new MapCutoutInfo(o.Id, o.Plate, o.CellX, o.CellZ, 1 + rng.Next(2), 1, "", "");
                    return new Ed6aOp(kind, l => l.ReplaceCutout(c), d => d.WithCutoutReplaced(c));
                }
                case "moveCells":
                {
                    (string k, ulong id) = Pick(rng, cur, "wall", "plate", "cutout");
                    if (k == null) return null;
                    int dx = rng.Next(3) - 1, dz = rng.Next(3) - 1;
                    return new Ed6aOp(kind, l => l.MoveCells(k, id, dx, dz), d => d.WithCellsMoved(k, id, dx, dz));
                }
                case "moveMm":
                {
                    (string k, ulong id) = Pick(rng, cur, "point", "base", "unit");
                    if (k == null) return null;
                    long dx = rng.Next(1001) - 500, dz = rng.Next(1001) - 500;
                    return new Ed6aOp(kind, l => l.MoveMm(k, id, dx, dz), d => d.WithMmMoved(k, id, dx, dz));
                }
                case "copy":
                {
                    (string k, ulong id) = Pick(rng, cur, Kinds);
                    if (k == null) return null;
                    bool cells = k == "wall" || k == "plate" || k == "cutout";
                    long dx = cells ? rng.Next(3) - 1 : rng.Next(1001) - 500, dz = cells ? rng.Next(3) - 1 : rng.Next(1001) - 500;
                    return new Ed6aOp(kind, l => l.Copy(k, id, dx, dz), d => d.WithCopy(k, id, dx, dz));
                }
                case "remove":
                {
                    (string k, ulong id) = Pick(rng, cur, Kinds);
                    if (k == null) return null;
                    return new Ed6aOp(kind, l => l.Remove(k, id), d => d.WithoutEntry(k, id));
                }
                case "door":
                {
                    int zz = z;
                    return new Ed6aOp(kind, l => l.DoorWall(x, zz, true, 6, 2, 2, Top), d => d.WithDoorWall(x, zz, true, 6, 2, 2, Top));
                }
                case "window":
                {
                    int xx = Math.Min(x, W - 2);
                    return new Ed6aOp(kind, l => l.Window(xx, z, false, 6, 2, 1, Top), d => d.WithWindow(xx, z, false, 6, 2, 1, Top));
                }
                case "removeStamp":
                {
                    var names = new List<string>();
                    foreach (MapWallInfo w in cur.Walls()) if (w.Stamp.Length > 0 && !names.Contains(w.Stamp)) names.Add(w.Stamp);
                    if (names.Count == 0) return null;
                    string name = names[rng.Next(names.Count)];
                    return new Ed6aOp(kind, l => l.RemoveStamp(name), d => d.WithoutStamp(name));
                }
                default:
                {
                    int count = 1 + rng.Next(12);
                    long cx = 3000 + rng.Next((W - 6) * 1000), cz = 3000 + rng.Next((D - 6) * 1000);
                    int team = 1 + rng.Next(2), squad = 1 + rng.Next(9);
                    bool reserve = rng.Next(2) == 0;
                    return new Ed6aOp(kind, l => l.Squad("rwr.soldier-1", team, squad, cx, cz, count, reserve),
                        d => d.WithSquad("rwr.soldier-1", team, squad, cx, cz, count, reserve));
                }
            }
        }

        private static (string, ulong) Pick(Ed1Rng rng, MapDocument cur, params string[] kinds)
        {
            var refs = new List<(string, ulong)>();
            foreach (string k in kinds)
            {
                if (k == "wall") foreach (MapWallInfo e in cur.Walls()) refs.Add((k, e.Id));
                if (k == "point") foreach (MapPointInfo e in cur.Points()) refs.Add((k, e.Id));
                if (k == "base") foreach (MapBaseInfo e in cur.Bases()) refs.Add((k, e.Id));
                if (k == "unit") foreach (MapUnitInfo e in cur.Units()) refs.Add((k, e.Key));
                if (k == "plate") foreach (MapPlateInfo e in cur.Plates()) refs.Add((k, e.Id));
                if (k == "cutout") foreach (MapCutoutInfo e in cur.Cutouts()) refs.Add((k, e.Id));
            }
            return refs.Count == 0 ? (null, 0) : refs[rng.Next(refs.Count)];
        }

        // Writes that every state refuses, each with its exception type.
        private static readonly Ed6aOp[] Refused =
        {
            new Ed6aOp("missing", l => l.MoveCells("wall", 9999, 1, 0), d => d.WithCellsMoved("wall", 9999, 1, 0)),
            new Ed6aOp("off", l => l.AddWall(Wall(W, 0, 1, 1)), d => d.WithWallAdded(Wall(W, 0, 1, 1))),
            new Ed6aOp("count", l => l.Squad("rwr.soldier-1", 1, 1, 5000, 5000, 0, false), d => d.WithSquad("rwr.soldier-1", 1, 1, 5000, 5000, 0, false)),
            new Ed6aOp("stamp", l => l.RemoveStamp("nothing"), d => d.WithoutStamp("nothing")),
            new Ed6aOp("kind", l => l.Copy("door", 1, 0, 0), d => d.WithCopy("door", 1, 0, 0)),
        };

        private static MapDocument Start() => Empty(Ed6aBodyCartridge.Layer, W, D).WithWallAdded(Wall(1, 1, 2, 1)).WithPlateAdded(Flat(10, 10, 3, 3, 2 * M));

        [Test]
        public void HundredSeededSequencesMatchTheWritesOneByOne()
        {
            using (new Ed6aBodyCartridge())
            {
                var drawn = new HashSet<string>();
                int refused = 0;
                for (ulong seed = 1; seed <= 100; seed++)
                {
                    var rng = new Ed1Rng(seed);
                    MapDocument start = Start(), cur = start;
                    var ops = new List<Ed6aOp>();
                    while (ops.Count < 8)
                    {
                        Ed6aOp op = Draw(rng, cur);
                        if (op == null) continue;
                        MapDocument next;
                        try { next = op.Direct(cur); }
                        catch (ArgumentException) { continue; }
                        ops.Add(op);
                        drawn.Add(op.Kind);
                        cur = next;
                    }
                    var list = new MapEntryEditList();
                    foreach (Ed6aOp op in ops) op.List(list);
                    Assert.That(list.Count, Is.EqualTo(ops.Count));
                    Assert.That(start.WithEntryEdits(list).Save(), Is.EqualTo(cur.Save()), "seed " + seed);

                    // The same sequence with a refused write at a drawn place: the batch throws the direct call's exception type.
                    Ed6aOp bad = Refused[rng.Next(Refused.Length)];
                    int at = rng.Next(ops.Count + 1);
                    var badList = new MapEntryEditList();
                    MapDocument direct = start;
                    Type expected = null;
                    for (int i = 0; i <= ops.Count; i++)
                    {
                        Ed6aOp op = i == at ? bad : ops[i < at ? i : i - 1];
                        op.List(badList);
                        if (expected != null) continue;
                        try { direct = op.Direct(direct); }
                        catch (Exception e) { expected = e.GetType(); }
                    }
                    Assert.That(expected, Is.Not.Null);
                    byte[] before = start.Save();
                    Exception thrown = Assert.Catch(() => start.WithEntryEdits(badList));
                    Assert.That(thrown.GetType(), Is.EqualTo(expected), "seed " + seed);
                    Assert.That(start.Save(), Is.EqualTo(before));
                    refused++;
                }
                Assert.That(drawn, Is.EquivalentTo(WriteKinds));
                Assert.That(refused, Is.EqualTo(100));
            }
        }

        // (a) Instance names count the doors and windows made earlier in the same batch.
        [Test]
        public void DoorsAndWindowsInOneBatchTakeSuccessiveNames()
        {
            using (new Ed6aBodyCartridge())
            {
                MapDocument start = Empty(Ed6aBodyCartridge.Layer, W, D);
                var list = new MapEntryEditList().DoorWall(2, 2, true, 6, 2, 2, Top).DoorWall(2, 6, true, 6, 2, 2, Top)
                    .Window(2, 10, true, 6, 2, 2, Top).Window(2, 14, true, 6, 2, 2, Top);
                MapDocument done = start.WithEntryEdits(list);
                Assert.That(done.Walls().Count, Is.EqualTo(14));
                Assert.That(done.Walls()[0].Stamp, Is.EqualTo("door-1"));
                Assert.That(done.Walls()[3].Stamp, Is.EqualTo("door-2"));
                Assert.That(done.Walls()[6].Stamp, Is.EqualTo("window-1"));
                Assert.That(done.Walls()[10].Stamp, Is.EqualTo("window-2"));
                MapDocument oneByOne = start.WithDoorWall(2, 2, true, 6, 2, 2, Top).WithDoorWall(2, 6, true, 6, 2, 2, Top)
                    .WithWindow(2, 10, true, 6, 2, 2, Top).WithWindow(2, 14, true, 6, 2, 2, Top);
                SameBytes(done, oneByOne);
            }
        }

        // (b) A cutout may name a plate added earlier in the same batch (by the plate's next id read before the batch).
        [Test]
        public void CutoutCanCutAPlateAddedInTheSameBatch()
        {
            MapDocument start = Empty("rwr-m6", W, D);
            ulong plate = start.NextId("plate");
            MapDocument done = start.WithEntryEdits(new MapEntryEditList().AddPlate(Flat(4, 4, 3, 3, 2 * M)).AddCutout(Cut(plate, 5, 5, 1, 1)));
            Assert.That(done.Plates()[0].Id, Is.EqualTo(plate));
            Assert.That(done.Cutouts()[0].Plate, Is.EqualTo(plate));
            Assert.That(done.Cutouts()[0].Id, Is.EqualTo(1UL));
        }

        // (c) A copy may name a unit a squad added earlier in the same batch.
        [Test]
        public void CopyCanNameAUnitAddedInTheSameBatch()
        {
            MapDocument start = Empty("rwr-m6", W, D).WithUnitAdded(Unit(1000, 1000));
            ulong first = start.NextId("unit");
            MapDocument done = start.WithEntryEdits(new MapEntryEditList().Squad("rwr.soldier-2", 2, 3, 10000, 10000, 4, false)
                .Copy("unit", first + 1, 500, 0));
            Assert.That(done.Units().Count, Is.EqualTo(6));
            MapUnitInfo copy = done.Units()[5], source = done.Units()[2];
            Assert.That(source.Key, Is.EqualTo(first + 1));
            Assert.That(copy.Key, Is.EqualTo(first + 4));
            Assert.That(copy.XMm, Is.EqualTo(source.XMm + 500));
            Assert.That(copy.ZMm, Is.EqualTo(source.ZMm));
            Assert.That(copy.Squad, Is.EqualTo(3));
            Assert.That(copy.Team, Is.EqualTo(2));
        }

        [Test]
        public void EmptyListChangesNothingAndNullIsRefused()
        {
            MapDocument start = Open(M6Path);
            SameBytes(start.WithEntryEdits(new MapEntryEditList()), start);
            Assert.Throws<ArgumentNullException>(() => start.WithEntryEdits(null));
        }
    }
}
