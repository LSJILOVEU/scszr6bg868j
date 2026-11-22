using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // MAP-BL0 W1 (the file is what the build code makes), W2 (the gate) and W3 (the skeleton).
    public sealed class WardFileTests
    {
        [Test]
        public void TheFileIsWhatTheBuildCodeMakes()
        {
            Assert.DoesNotThrow(() =>
            {
                byte[] built = WardBuild.Document(41).Save();
                Assert.That(WardWorld.File, Is.EqualTo(built));
                Assert.That(MapDocument.Open(WardWorld.File).Save(), Is.EqualTo(WardWorld.File), "open and save changes nothing");
            });
        }

        [Test]
        public void AnotherSeedChangesTheSeedAndNothingElse()
        {
            MapDocument a = WardBuild.Document(41), b = MapDocument.Open(WardBuild.Document(42).Save());
            Assert.That(MapDescJson.Read(b.Save()).Run.Seed, Is.EqualTo(42UL));
            Assert.That(MapDescJson.Read(a.Save()).Run.Seed, Is.EqualTo(41UL));
            Assert.That(b.Walls().Select(Line), Is.EqualTo(a.Walls().Select(Line)));
            Assert.That(b.Units().Select(u => (u.Key, u.Type, u.Team, u.Squad, u.XMm, u.ZMm, u.Reserve)),
                Is.EqualTo(a.Units().Select(u => (u.Key, u.Type, u.Team, u.Squad, u.XMm, u.ZMm, u.Reserve))));
            Assert.That(b.Points().Select(p => (p.XMm, p.ZMm, p.RadiusMm)), Is.EqualTo(a.Points().Select(p => (p.XMm, p.ZMm, p.RadiusMm))));
        }

        private static string Line(MapWallInfo w) =>
            w.Id + " " + w.Kind + " " + w.Layer + " " + w.CellX + " " + w.CellZ + " " + w.SizeX + " " + w.SizeZ + " " + w.HeightRaw + " " + w.HasBase + " " + w.BaseRaw + " " + w.Stamp;

        [Test]
        public void TheFileCarriesAnImportSettingFile()
        {
            string meta = File.ReadAllText(WardWorld.MapPath + ".meta");
            Assert.That(meta, Does.StartWith("fileFormatVersion: 2\nguid: "));
            Assert.That(meta, Does.Contain("TextScriptImporter:"));
        }

        [Test]
        public void TheGateFindsNothing()
        {
            Assert.That(MapGate.Check(WardWorld.File, WardWorld.Pack), Is.Empty);
        }

        // W3, on the build code's document (W1 holds the file to it).
        [Test]
        public void TheDescriptionIsTheOneTheCardNames()
        {
            MapDesc d = MapDescJson.Read(WardBuild.Document(41).Save());
            Assert.That((d.Id, d.Content, d.Pack, d.FloorRaw, d.CeilingRaw, d.Run.Seed, d.Run.CrowdPass),
                Is.EqualTo(("walled-ward-160", "rwr", "rwr-showcase", 0L, 1048576L, 41UL, "teamIdle")));
            Assert.That((d.Size.X, d.Size.Z, d.Origin.X, d.Origin.Z), Is.EqualTo((160, 160, 0, 0)));
            Assert.That(d.Layers.Select(l => (l.Name, l.Bit)), Is.EqualTo(new[] { ("walls", (byte)1) }));
            Assert.That(d.Heights, Is.Null, "flat");
            Assert.That(d.Surfaces, Is.Null, "no surface table");
            Assert.That(d.Plates.Count, Is.EqualTo(0), "no floor plates: the squad rules need that");
            Assert.That(d.Cutouts.Count, Is.EqualTo(0));
        }

        // W3: the ward wall pieces 1 to 8 and the gates, and the empty places; WardCheck's skeleton lines on the file.
        [Test]
        public void TheSkeletonIsTheCards()
        {
            MapDocument doc = WardBuild.Document(41);
            var walls = doc.Walls();
            (int, int, int, int)[] table =
            {
                (8, 8, 2, 70), (8, 82, 2, 70), (150, 8, 2, 70), (150, 82, 2, 70), (10, 8, 68, 2), (82, 8, 68, 2), (10, 150, 68, 2), (82, 150, 68, 2),
            };
            for (int i = 0; i < 8; i++)
            {
                MapWallInfo w = walls[i];
                Assert.That((w.Id, w.Kind, w.Layer, w.HeightRaw, w.HasBase), Is.EqualTo(((ulong)(i + 1), "ward-wall", "walls", 196608L, false)), "ward wall " + (i + 1));
                Assert.That((w.CellX, w.CellZ, w.SizeX, w.SizeZ), Is.EqualTo(table[i]), "ward wall " + (i + 1));
            }
            var nav = WardCheck.Nav(doc);
            for (int a = 78; a <= 81; a++)
                for (int d = 8; d <= 9; d++)
                {
                    Assert.That(nav.IsPassable(d, a) && nav.IsPassable(159 - d, a) && nav.IsPassable(a, d) && nav.IsPassable(a, 159 - d), Is.True, "gate cell " + a + " " + d);
                    Assert.That(nav.IsPassable(d, 77) || nav.IsPassable(d, 82), Is.False, "a gate is 4 cells wide");
                }
            Assert.That(WardCheck.Run(WardLayout.Default, doc).Where(f => f.StartsWith("W3")), Is.Empty);
            foreach (MapWallInfo w in walls.Where(v => v.Kind != "ward-wall"))
                foreach ((int x, int z) in WardCheck.Cells(w))
                {
                    Assert.That(WardCheck.InRing(x) || WardCheck.InRing(z) || WardCheck.InLane(x) || WardCheck.InLane(z), Is.False, "wall " + w.Id);
                    Assert.That(WardCheck.InCarriage(x) || WardCheck.InCarriage(z), Is.False, "wall " + w.Id);
                    if (WardCheck.InSquare(x, z))
                        Assert.That(new[] { "stall", "handcart", "oxcart", "well", "tree" }, Does.Contain(w.Kind), "wall " + w.Id + " in the square");
                }
        }
    }
}
