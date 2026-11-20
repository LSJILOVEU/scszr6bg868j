using System;
using System.IO;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Fixtures;
using static WarCore.Tests.E3Plans;
using static WarCore.Tests.Ed1Fixtures;

namespace WarCore.Tests
{
    // ED-1 acceptance 5 and 6: the surface name table, and turning on heights or surfaces.
    public sealed class Ed1SurfaceTests
    {
        // Plain E3 map: row 0 holds grass, mud, road by hand, cells x 2..5, z 0..3 are stamp s2's "mud".
        private static MapEdit Mixed()
        {
            var e = new MapEdit(Read(Plain()));
            e.SetSurface(0, 0, "grass");
            e.SetSurface(1, 0, "mud");
            e.Stamp(S2());
            e.SetSurface(6, 5, "road");
            return e;
        }

        private static string NameAt(MapDesc d, int x, int z) => d.Surfaces.Names[MapEditDraft.OwnerIndex(d.Surfaces.Rows[z][x])];

        [TestCase("aaa", new[] { "aaa", "grass", "mud", "road" })]
        [TestCase("hhh", new[] { "grass", "hhh", "mud", "road" })]
        [TestCase("zzz", new[] { "grass", "mud", "road", "zzz" })]
        public void ANewNameKeepsEveryCellsName(string name, string[] table)
        {
            MapEdit e = Mixed();
            MapDesc before = e.Current;
            e.AddSurfaceName(name);
            MapDesc after = e.Current;
            Assert.That(after.Surfaces.Names, Is.EqualTo(table));
            for (int z = 0; z < D; z++)
                for (int x = 0; x < W; x++)
                    Assert.That(NameAt(after, x, z), Is.EqualTo(NameAt(before, x, z)), x + "," + z);
            Assert.That(after.SurfaceOwner, Is.EqualTo(before.SurfaceOwner));
            Assert.That(after.HeightOwner, Is.EqualTo(before.HeightOwner));
            Assert.That(Bytes(MapDescJson.Read(Bytes(after))), Is.EqualTo(Bytes(after)), "written and read back");
            e.SetSurface(7, 5, name);
            Assert.That(NameAt(e.Current, 7, 5), Is.EqualTo(name));
        }

        [Test]
        public void ANameAlreadyThereChangesNothing()
        {
            MapEdit e = Mixed();
            byte[] before = Bytes(e.Current);
            e.AddSurfaceName("mud");
            Assert.That(Bytes(e.Current), Is.EqualTo(before));
        }

        [Test]
        public void TheSixtyFifthNameIsRefused()
        {
            MapEdit e = Mixed();
            for (int n = 0; n < 61; n++) e.AddSurfaceName("n" + n.ToString("D2"));
            Assert.That(e.Current.Surfaces.Names.Count, Is.EqualTo(64));
            e.AddSurfaceName("grass");
            byte[] full = Bytes(e.Current);
            var ex = Assert.Throws<InvalidOperationException>(() => e.AddSurfaceName("zzzz"));
            StringAssert.StartsWith("MAPDESC_GRID", ex.Message);
            Assert.That(Bytes(e.Current), Is.EqualTo(full));
            Assert.That(Bytes(MapDescJson.Read(full)), Is.EqualTo(full), "64 names are written and read back");
        }

        [Test]
        public void WithoutASurfaceTableANameIsRefused()
        {
            var e = new MapEdit(MapNew.Desc(Spec()));
            Assert.Throws<ArgumentException>(() => e.AddSurfaceName("grass"));
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("a\0b")]
        public void ANameTheBakeRefusesIsRefused(string name)
        {
            MapEdit e = Mixed();
            byte[] before = Bytes(e.Current);
            Assert.Throws<ArgumentException>(() => e.AddSurfaceName(name));
            Assert.That(Bytes(e.Current), Is.EqualTo(before));
            var flat = new MapEdit(MapNew.Desc(Spec()));
            Assert.Throws<ArgumentException>(() => flat.EnableSurfaces(name));
        }

        // Built in code: an attribute cannot carry a lone surrogate.
        [Test]
        public void ANameWithALoneSurrogateIsRefused() => ANameTheBakeRefusesIsRefused(new string(new[] { (char)0xD800, 'x' }));

        [Test]
        public void NamesUpTo255BytesAreTaken()
        {
            MapEdit e = Mixed();
            e.AddSurfaceName(new string('x', 255));
            Assert.Throws<ArgumentException>(() => e.AddSurfaceName(new string('y', 256)));
            Assert.Throws<ArgumentException>(() => e.AddSurfaceName(new string('é', 128)), "256 UTF-8 bytes");
        }

        private static MapNewSpec Spec(long floor = 0, long ceiling = 262144) =>
            new MapNewSpec { Id = "ed1", Content = "rwr", Pack = "rwr-terrain-demo", Width = 6, Depth = 4, FloorRaw = floor, CeilingRaw = ceiling, Seed = 3 };

        [Test]
        public void HeightsTurnOnInsideFloorAndCeiling()
        {
            Assert.DoesNotThrow(() => new MapEdit(MapNew.Desc(Spec())).EnableHeights(0));
            Assert.DoesNotThrow(() => new MapEdit(MapNew.Desc(Spec())).EnableHeights(262144));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MapEdit(MapNew.Desc(Spec())).EnableHeights(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MapEdit(MapNew.Desc(Spec())).EnableHeights(262145));
            var e = new MapEdit(MapNew.Desc(Spec()));
            e.EnableHeights(65536);
            Assert.That(e.Current.Heights.Count, Is.EqualTo(5));
            Assert.That(e.Current.Heights[4].Count, Is.EqualTo(7));
            Assert.That(e.Current.Heights[4][6], Is.EqualTo(65536));
            Assert.That(e.HeightOwner(6, 4), Is.Null);
            byte[] on = Bytes(e.Current);
            Assert.Throws<ArgumentException>(() => e.EnableHeights(0), "already on");
            Assert.That(Bytes(e.Current), Is.EqualTo(on));
            Assert.That(Bytes(MapDescJson.Read(on)), Is.EqualTo(on));
            Assert.DoesNotThrow(() => MapBake.Run(on));
        }

        [Test]
        public void HeightsStayInTheReadersRange()
        {
            const int limit = MapDescReader.HeightLimit;
            MapNewSpec wide = Spec(-(1L << 30), 1L << 30);
            Assert.DoesNotThrow(() => new MapEdit(MapNew.Desc(wide)).EnableHeights(-limit));
            Assert.DoesNotThrow(() => new MapEdit(MapNew.Desc(wide)).EnableHeights(limit));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MapEdit(MapNew.Desc(wide)).EnableHeights(-limit - 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MapEdit(MapNew.Desc(wide)).EnableHeights(limit + 1));
            var e = new MapEdit(MapNew.Desc(wide));
            e.EnableHeights(limit);
            Assert.DoesNotThrow(() => MapDescJson.Read(Bytes(e.Current)));
        }

        [Test]
        public void SurfacesTurnOnOnce()
        {
            var e = new MapEdit(MapNew.Desc(Spec()));
            e.EnableSurfaces("grass");
            Assert.That(e.Current.Surfaces.Names, Is.EqualTo(new[] { "grass" }));
            Assert.That(NameAt(e.Current, 5, 3), Is.EqualTo("grass"));
            Assert.That(e.SurfaceOwner(5, 3), Is.Null);
            byte[] on = Bytes(e.Current);
            Assert.Throws<ArgumentException>(() => e.EnableSurfaces("mud"), "already on");
            Assert.That(Bytes(e.Current), Is.EqualTo(on));
            Assert.That(Bytes(MapDescJson.Read(on)), Is.EqualTo(on));
            Assert.DoesNotThrow(() => MapBake.Run(on));
            Assert.Throws<ArgumentException>(() => new MapEdit(Read(Plain())).EnableSurfaces("grass"), "a map that has them");
            Assert.Throws<ArgumentException>(() => new MapEdit(Read(Plain())).EnableHeights(0), "a map that has them");
        }
    }
}
