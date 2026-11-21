using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Ed3Fixtures;

namespace WarCore.Tests
{
    // ED-3a acceptance 7 and 10: hand edits under the terrain stamp (E0 section 8 points 1 to 3) and the facade.
    public sealed class Ed3DocumentTests
    {
        private static string Flat(ulong seed = 1) => Gen(30, 20, "", seed);

        private static string Rough(ulong seed) => Gen(30, 20, Join(Hills(5, 0, 0, 30, 20, 2, 6, 500, 3000), Patch(15, 10, 5, "mud", 400)), seed);

        // A map with a hand edit at height point P = (10, 8) and surface cell C = (4, 4).
        private static MapDesc Edited(int raise)
        {
            var e = new MapEdit(Map(30, 20));
            e.SetHeight(10, 8, raise);
            e.AddSurfaceName("road");
            e.SetSurface(4, 4, "road");
            return e.Current;
        }

        [Test]
        public void HandPointsAndCellsAreSkippedAndCounted()
        {
            MapDesc d = Edited(30000);
            MapDocument doc = MapDocument.Open(MapDescJson.Write(d));
            MapTerrainResult r = Ok(() => doc.WithTerrain(Rough(3)));
            Assert.That(r.HeightsSkipped, Is.EqualTo(1));
            Assert.That(r.SurfacesSkipped, Is.EqualTo(1));
            MapDesc after = MapDescJson.Read(r.Document.Save());
            MapStampPlan plan = Compose(d, Rough(3));
            Assert.That(H(after, 10, 8), Is.EqualTo(30000));
            Assert.That(Surface(after, 4, 4), Is.EqualTo("road"));
            foreach (MapHeightWrite w in plan.Heights)
                if (w.I != 10 || w.J != 8) Assert.That(H(after, w.I, w.J), Is.EqualTo(w.Raw), w.I + "," + w.J);
            foreach (MapSurfaceWrite w in plan.Surfaces)
                if (w.X != 4 || w.Z != 4) Assert.That(Surface(after, w.X, w.Z), Is.EqualTo(w.Name), w.X + "," + w.Z);
            Assert.That(r.Document.HeightOwnerAt(10, 8), Is.EqualTo("hand"));
            Assert.That(r.Document.HeightOwnerAt(11, 8), Is.EqualTo("stamp:terrain"));
            Assert.That(r.Document.SurfaceOwnerAt(4, 4), Is.EqualTo("hand"));
            Assert.That(r.Document.SurfaceOwnerAt(5, 4), Is.EqualTo("stamp:terrain"));
        }

        [Test]
        public void TooSteepHandPointIsReportedNotRepaired()
        {
            MapDocument doc = MapDocument.Open(MapDescJson.Write(Edited((int)(10 * S))));
            MapTerrainResult r = Ok(() => doc.WithTerrain(Flat()));
            MapDesc after = MapDescJson.Read(r.Document.Save());
            Assert.That(H(after, 10, 8), Is.EqualTo(10 * S));
            Assert.That(H(after, 11, 8), Is.EqualTo(0));
            List<MapProblem> problems = Problems(after);
            Assert.That(problems.Exists(p => p.Code == "MAPDESC_WCMAP" && p.Id == "WCMAP_HEIGHT_SLOPE"), Is.True, string.Join("; ", problems));
        }

        [Test]
        public void ReleasedHandPointTakesThePlanAndANewSeedKeepsHandEdits()
        {
            MapDocument doc = Ok(() => MapDocument.Open(MapDescJson.Write(Edited((int)(10 * S)))).WithTerrain(Rough(3))).Document;
            MapDocument released = Ok(() => doc.WithHandReleased(9, 7, 10, 8).WithTerrain(Rough(3))).Document;
            MapDesc plan = Run(Map(30, 20), Rough(3));
            MapDesc rel = MapDescJson.Read(released.Save());
            Assert.That(H(rel, 10, 8), Is.EqualTo(H(plan, 10, 8)));
            Assert.That(Surface(rel, 4, 4), Is.EqualTo("road"), "the cell outside the box stays hand");
            MapTerrainResult other = Ok(() => doc.WithTerrain(Rough(4)));
            MapDesc o = MapDescJson.Read(other.Document.Save());
            Assert.That(H(o, 10, 8), Is.EqualTo(10 * S));
            Assert.That(Surface(o, 4, 4), Is.EqualTo("road"));
            Assert.That(other.HeightsSkipped, Is.EqualTo(1));
            Assert.That(MapDescJson.Write(o), Is.Not.EqualTo(doc.Save()));
        }

        [Test]
        public void WithTerrainIsTheSameBatchAsComposeAndStamp()
        {
            MapDesc d = Map(30, 20);
            byte[] before = MapDescJson.Write(d);
            string gen = Gen(30, 20, Join(Patch(10, 10, 4, "mud", 0), Road("[[0, 5], [30, 5]]", 1)), ground: "grass");
            MapDocument doc = MapDocument.Open(before);
            MapTerrainResult r = Ok(() => doc.WithTerrain(gen));
            var e = new MapEdit(d);
            e.Batch(b =>
            {
                b.AddSurfaceName("mud");
                b.AddSurfaceName("road");
                b.Stamp(MapTerrain.Compose(d, MapGenSpec.Read(gen), Rules()));
            });
            Assert.That(r.Document.Save(), Is.EqualTo(MapDescJson.Write(e.Current)));
            Assert.That(doc.Save(), Is.EqualTo(before), "the old document is unchanged");
            Assert.That(MapDescJson.Read(r.Document.Save()).Surfaces.Names, Is.EqualTo(new[] { "grass", "mud", "road" }));
        }

        [Test]
        public void StartingSpecRunsAsAFlatMap()
        {
            MapDocument doc = MapDocument.Open(MapDescJson.Write(Map(12, 9)));
            string start = MapGenSpecs.ForDocument(doc, 77);
            Assert.That(start, Is.EqualTo("{\n  \"version\": 1,\n  \"width\": 12,\n  \"depth\": 9,\n  \"pack\": \"rwr-terrain-demo\",\n  \"seed\": 77,\n"
                + "  \"baseMm\": 0,\n  \"ground\": \"grass\",\n  \"stamps\": []\n}\n"));
            MapTerrainResult r = Ok(() => doc.WithTerrain(start));
            MapDesc after = MapDescJson.Read(r.Document.Save());
            Assert.That(H(after, 5, 5), Is.EqualTo(0));
            Assert.That(r.HeightsSkipped + r.SurfacesSkipped, Is.EqualTo(0));
            string bare = MapGenSpecs.ForDocument(MapDocument.Open(MapDescJson.Write(Map(12, 9, false, false))), 0);
            Assert.That(bare, Does.Not.Contain("baseMm").And.Not.Contain("ground"));
            Assert.That(MapGenSpecs.Normalize("{\"version\":1,\"width\":12,\"depth\":9,\"pack\":\"rwr-terrain-demo\",\"seed\":77,\"stamps\":[],"
                + "\"ground\":\"grass\",\"baseMm\":0}"), Is.EqualTo(start));
            Assert.Throws<ArgumentException>(() => MapGenSpecs.Normalize("{}"));
            Assert.Throws<ArgumentNullException>(() => MapGenSpecs.ForDocument(null, 1));
        }

        private static readonly string[] TerrainMaps = { "Assets/WarCore/Maps/terrain-demo.map.json", "Assets/WarCore/Maps/door-terrain-demo.map.json" };

        [Test]
        public void ExistingTerrainMapsGainTheTerrainOwnerOnTheFirstRun()
        {
            foreach (string path in TerrainMaps)
            {
                MapDocument doc = MapDocument.Open(File.ReadAllBytes(path));
                MapDesc d = MapDescJson.Read(doc.Save());
                Assert.That(d.Owners, Does.Not.Contain("stamp:terrain"), path);
                MapTerrainResult r = Ok(() => doc.WithTerrain(MapGenSpecs.ForDocument(doc, 5)));
                MapDesc after = MapDescJson.Read(r.Document.Save());
                int k = new List<string>(after.Owners).IndexOf("stamp:terrain");
                Assert.That(k, Is.InRange(1, 63), path);
            }
        }

        [Test]
        public void LayerWithoutTerrainRulesAndAFullNameTable()
        {
            MapDocument m6 = MapDocument.Open(File.ReadAllBytes("Assets/WarCore/Maps/m6-demo.map.json"));
            Assert.Throws<InvalidOperationException>(() => m6.WithTerrain(MapGenSpecs.ForDocument(m6, 1)));
            var e = new MapEdit(Map(10, 10));
            e.Batch(b => { for (int n = 0; n < 63; n++) b.AddSurfaceName("s" + n.ToString("D2")); });
            Assert.That(e.Current.Surfaces.Names.Count, Is.EqualTo(64));
            byte[] full = MapDescJson.Write(e.Current);
            MapDocument doc = MapDocument.Open(full);
            InvalidOperationException x = Assert.Throws<InvalidOperationException>(() => doc.WithTerrain(Gen(10, 10, Patch(5, 5, 2, "mud", 0))));
            Assert.That(x.Message, Does.Contain("MAPDESC_GRID"));
            Assert.That(doc.Save(), Is.EqualTo(full));
            ArgumentException bad = Assert.Throws<ArgumentException>(() => doc.WithTerrain(Gen(11, 10, "")));
            Assert.That(bad.Message, Does.Contain("width"));
        }

        [Test]
        public void PublicSurfaceStaysPlain()
        {
            List<string> problems = E1bDocumentTests.SurfaceViolations(typeof(MapTerrainResult), typeof(MapDocument).Assembly, out int members);
            Assert.That(problems, Is.Empty);
            Assert.That(members, Is.GreaterThanOrEqualTo(3));
            problems = E1bDocumentTests.SurfaceViolations(typeof(MapGenSpecs), typeof(MapDocument).Assembly, out members);
            Assert.That(problems, Is.Empty);
            Assert.That(members, Is.GreaterThanOrEqualTo(2));
        }
    }
}
