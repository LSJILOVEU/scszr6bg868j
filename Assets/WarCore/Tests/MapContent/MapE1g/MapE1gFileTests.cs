using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // MAP-E1g E1 (the file is what the build code makes), E2 (the gate), E4 (points, bases, soldiers) and E5 (the ways are open).
    public sealed class MapE1gFileTests
    {
        [Test]
        public void TheFileIsWhatTheBuildCodeMakes()
        {
            byte[] built = MapE1gBuild.Build().Save();
            Assert.That(MapE1gWorld.File, Is.EqualTo(built));
            Assert.That(MapDocument.Open(MapE1gWorld.File).Save(), Is.EqualTo(MapE1gWorld.File), "open and save changes nothing");
        }

        [TestCase("emmental-hamlet-160-ground.gen.json")]
        [TestCase("emmental-hamlet-160-ground.map.json")]
        public void TheFilesCarryAnImportSettingFile(string name)
        {
            string meta = File.ReadAllText(MapE1gBuild.MapsDir + name + ".meta");
            Assert.That(meta, Does.StartWith("fileFormatVersion: 2\nguid: "));
            Assert.That(meta, Does.Contain("TextScriptImporter:"));
        }

        [Test]
        public void TheDescriptionIsTheOneTheCardNames()
        {
            MapDesc d = MapDescJson.Read(MapE1gWorld.File);
            Assert.That((d.Id, d.Content, d.Pack, d.FloorRaw, d.CeilingRaw, d.Run.Seed, d.Run.CrowdPass),
                Is.EqualTo(("emmental-hamlet-160-ground", "rwr", "rwr-field", 0L, 2097152L, 41UL, "teamIdle")));
            Assert.That((d.Size.X, d.Size.Z, d.Origin.X, d.Origin.Z), Is.EqualTo((160, 160, 0, 0)));
            Assert.That(d.Layers.Select(l => (l.Name, l.Bit)), Is.EqualTo(new[] { ("walls", (byte)1) }));
            Assert.That(d.Heights, Is.Not.Null);
            Assert.That(d.Surfaces.Names, Is.EqualTo(new[] { "grass" }));
            Assert.That(d.Plates.Count, Is.EqualTo(0), "no floor plates: the squad rules need that");
            Assert.That(d.Cutouts.Count, Is.EqualTo(0));
        }

        [Test]
        public void TheGateFindsNothing()
        {
            Assert.That(MapGate.Check(MapE1gWorld.File, MapE1gWorld.Pack), Is.Empty);
        }

        [Test]
        public void PointsAndBasesAreTheCardsTable()
        {
            MapDocument doc = MapDocument.Open(MapE1gWorld.File);
            var points = doc.Points();
            Assert.That(points.Count, Is.EqualTo(5));
            long[][] spawn =
            {
                new long[] { 109500, 38500 }, new long[] { 40500, 30500 }, new long[] { 10500, 89500 },
                new long[] { 61500, 150500 }, new long[] { 119500, 123500 },
            };
            long[][] at =
            {
                new long[] { 101500, 52500 }, new long[] { 40500, 48500 }, new long[] { 20500, 98500 },
                new long[] { 57500, 133500 }, new long[] { 114500, 113500 },
            };
            int[] owner = { 1, 0, 0, 0, 2 };
            for (int k = 0; k < 5; k++)
            {
                MapPointInfo p = points[k];
                Assert.That((p.Id, p.XMm, p.ZMm, p.RadiusMm, p.InitialOwner, p.HasSpawn, p.SpawnXMm, p.SpawnZMm, p.HasSpawnPlate, p.HasPlate),
                    Is.EqualTo(((ulong)(k + 1), at[k][0], at[k][1], 6000, owner[k], true, spawn[k][0], spawn[k][1], false, false)), "point " + "ABCDE"[k]);
            }
            var bases = doc.Bases();
            Assert.That(bases.Select(b => (b.Id, b.Team, b.XMm, b.ZMm, b.HasPlate)),
                Is.EqualTo(new[] { (1UL, 1, 98500L, 8500L, false), (2UL, 2, 118500L, 150500L, false) }));
        }

        [Test]
        public void FortyOnTheFieldAndTwentyInReservePerTeam()
        {
            var units = MapDocument.Open(MapE1gWorld.File).Units();
            Assert.That(units.Count, Is.EqualTo(120));
            foreach (int team in new[] { 1, 2 })
            {
                var mine = units.Where(u => u.Team == team).ToList();
                Assert.That(mine.Count(u => !u.Reserve), Is.EqualTo(40), "team " + team + " on the field");
                Assert.That(mine.Count(u => u.Reserve), Is.EqualTo(20), "team " + team + " in reserve");
                Assert.That(mine.All(u => u.Squad == team && u.Type == (team == 1 ? "rwr.soldier-1" : "rwr.soldier-2") && !u.HasPlate), Is.True);
                Assert.That(mine.Select(u => u.Key).Distinct().Count(), Is.EqualTo(60));
            }
            Assert.That(units.Select(u => u.Key), Is.EqualTo(Enumerable.Range(1, 120).Select(k => (ulong)k)));
        }

        [Test]
        public void NobodyOnTheFieldStandsOnAWallOrUnwalkableGround()
        {
            var units = MapDocument.Open(MapE1gWorld.File).Units();
            foreach (MapUnitInfo u in units.Where(u => !u.Reserve))
            {
                Assert.That(MapE1gWorld.Walkable(u.XMm, u.ZMm), Is.True, "unit " + u.Key + " cell");
                SimVec3 v = Wp23.V(u.XMm, 0, u.ZMm);
                Assert.That(MapE1gWorld.Tables.Nav.SegmentClear(v, v, InterimLocomotion.UnitFootprint), Is.True, "unit " + u.Key + " footprint");
            }
        }

        [Test]
        public void EveryWayFromABaseAndFromASpawnIsOpen()
        {
            var finder = new GridPathfinder(MapE1gWorld.Tables);
            MapDocument doc = MapDocument.Open(MapE1gWorld.File);
            foreach (MapBaseInfo b in doc.Bases())
                foreach (MapPointInfo p in doc.Points())
                    Assert.That(finder.Find(Wp23.V(b.XMm, 0, b.ZMm), Wp23.V(p.XMm, 0, p.ZMm)), Is.EqualTo(GridPathStatus.Found), "base " + b.Id + " to point " + p.Id);
            foreach (MapPointInfo p in doc.Points())
            {
                Assert.That(MapE1gWorld.Walkable(p.SpawnXMm, p.SpawnZMm), Is.True, "spawn of point " + p.Id);
                Assert.That(finder.Find(Wp23.V(p.SpawnXMm, 0, p.SpawnZMm), Wp23.V(p.XMm, 0, p.ZMm)), Is.EqualTo(GridPathStatus.Found), "spawn to point " + p.Id);
            }
        }

        [Test]
        public void TheCentresAndBasesStandOnWalkableCells()
        {
            MapDocument doc = MapDocument.Open(MapE1gWorld.File);
            foreach (MapPointInfo p in doc.Points()) Assert.That(MapE1gWorld.Walkable(p.XMm, p.ZMm), Is.True, "point " + p.Id);
            foreach (MapBaseInfo b in doc.Bases()) Assert.That(MapE1gWorld.Walkable(b.XMm, b.ZMm), Is.True, "base " + b.Id);
        }
    }
}
