using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.E3Plans;
using static WarCore.Tests.Ed1Fixtures;

namespace WarCore.Tests
{
    // ED-1 acceptance 7: new maps.
    public sealed class Ed1NewTests
    {
        private static MapNewSpec Spec(int? heightRaw, string surface, int width = 10, int depth = 8) => new MapNewSpec
        {
            Id = "new-map", Content = "rwr", Pack = "rwr-terrain-demo", Width = width, Depth = depth, FloorRaw = 0, CeilingRaw = 262144,
            Seed = 9, CrowdPass = "none", HeightRaw = heightRaw, Surface = surface,
        };

        [TestCase(null, null)]
        [TestCase(1000, null)]
        [TestCase(null, "grass")]
        [TestCase(1000, "grass")]
        public void AllFourKindsReadBackAndBake(int? heightRaw, string surface)
        {
            MapDesc d = MapNew.Desc(Spec(heightRaw, surface));
            byte[] bytes = Bytes(d);
            Assert.That(Bytes(MapDescJson.Read(bytes)), Is.EqualTo(bytes));
            StringAssert.Contains("\"version\": 1,", Encoding.UTF8.GetString(bytes));
            Assert.That(d.Owners, Is.EqualTo(new[] { "hand", "stamp:terrain" }));
            Assert.That(d.Heights == null, Is.EqualTo(heightRaw == null));
            Assert.That(d.Surfaces == null, Is.EqualTo(surface == null));
            Assert.That((d.Walls.Count, d.Points.Count, d.Bases.Count, d.Units.Count, d.Plates.Count), Is.EqualTo((0, 0, 0, 0, 0)));
            Assert.That((d.Next.Wall, d.Next.Point, d.Next.Base, d.Next.Unit), Is.EqualTo((1ul, 1, 1, 1ul)));
            Assert.That((d.Origin.X, d.Origin.Z, d.Run.Seed), Is.EqualTo((0L, 0L, 9ul)));
            Assert.DoesNotThrow(() => MapBake.Run(bytes));
        }

        [Test]
        public void TheSideLimitIsTheReaders()
        {
            int max = WarCore.Sim.HeightField.MaxSide;
            Assert.DoesNotThrow(() => MapNew.Desc(Spec(null, null, max, max)));
            Assert.Throws<InvalidDataException>(() => MapNew.Desc(Spec(null, null, max + 1, 8)));
            Assert.Throws<InvalidDataException>(() => MapNew.Desc(Spec(null, null, 10, max + 1)));
            Assert.Throws<InvalidDataException>(() => MapNew.Desc(Spec(null, null, 0, 8)));
        }

        [Test]
        public void AHeightOutsideFloorAndCeilingIsRefused()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => MapNew.Desc(Spec(-1, null)));
            Assert.Throws<ArgumentOutOfRangeException>(() => MapNew.Desc(Spec(262145, null)));
            Assert.Throws<ArgumentNullException>(() => MapNew.Desc(null));
        }

        [Test]
        public void TheTerrainStampIsOwnerOne()
        {
            var e = new MapEdit(MapNew.Desc(Spec(0, "grass")));
            e.AddSurfaceName("road");
            e.Stamp(new MapStampPlan(MapNew.TerrainInstance, Corners(0, 2, 0, 2, 500), Cells(0, 1, 0, 1, "road"),
                new MapStampEntry<MapWall>[0], new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0]));
            Assert.That(e.Current.Owners, Is.EqualTo(new[] { "hand", "stamp:terrain" }));
            Assert.That(e.HeightOwner(2, 2), Is.EqualTo("stamp:terrain"));
            Assert.That(e.Current.HeightOwner[2][2], Is.EqualTo('B'));
            Assert.That(e.SurfaceOwner(1, 1), Is.EqualTo("stamp:terrain"));
        }

        [Test]
        public void TheDocumentMakesTheSameMap()
        {
            MapDocument doc = MapDocument.New(new MapNewSettings
            {
                Id = "new-map", Content = "rwr", Pack = "rwr-terrain-demo", Width = 10, Depth = 8, FloorRaw = 0, CeilingRaw = 262144,
                Seed = 9, CrowdPass = "none", Heights = true, HeightRaw = 1000, Surface = "grass",
            });
            Assert.That(doc.Save(), Is.EqualTo(Bytes(MapNew.Desc(Spec(1000, "grass")))));
            MapDocument flat = MapDocument.New(new MapNewSettings
            {
                Id = "new-map", Content = "rwr", Pack = "rwr-terrain-demo", Width = 10, Depth = 8, CeilingRaw = 262144, Seed = 9, HeightRaw = 1000,
            });
            Assert.That(flat.Save(), Is.EqualTo(Bytes(MapNew.Desc(Spec(null, null)))), "Heights false ignores HeightRaw");
            Assert.Throws<ArgumentNullException>(() => MapDocument.New(null));
        }
    }
}
