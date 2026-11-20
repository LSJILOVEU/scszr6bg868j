using System;
using System.IO;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Ed2Fixtures;

namespace WarCore.Tests
{
    // ED-2 acceptance 7: surface brushes.
    public sealed class Ed2SurfaceTests
    {
        private static string NameAt(MapDesc d, int x, int z) => d.Surfaces.Names[MapEditDraft.OwnerIndex(d.Surfaces.Rows[z][x])];

        [TestCase(0, 1, 1)]
        [TestCase(1, 5, 9)]
        [TestCase(3, 29, 49)]
        public void CellCountsByHand(int radius, int circle, int square)
        {
            MapDesc d = Flat(10, 10);
            MapSurfaceWrite[] c = MapBrush.SurfaceCircle(d, 5, 5, radius, "mud");
            MapSurfaceWrite[] s = MapBrush.SurfaceSquare(d, 5, 5, radius, "mud");
            Assert.That((c.Length, s.Length), Is.EqualTo((circle, square)));
            foreach (MapSurfaceWrite w in c)
                Assert.That((w.X - 5) * (w.X - 5) + (w.Z - 5) * (w.Z - 5), Is.LessThanOrEqualTo(radius * radius));
            for (int n = 1; n < s.Length; n++)
                Assert.That(s[n].Z > s[n - 1].Z || (s[n].Z == s[n - 1].Z && s[n].X > s[n - 1].X), Is.True, "order");
        }

        [Test]
        public void CellsAlreadyNamedSoAreNotWritten()
        {
            MapDesc d = Flat(10, 10);
            Assert.That(MapBrush.SurfaceCircle(d, 5, 5, 3, "grass"), Is.Empty);
            var e = new MapEdit(d);
            e.AddSurfaceName("mud");
            e.SetSurface(5, 5, "mud");
            Assert.That(MapBrush.SurfaceSquare(e.Current, 5, 5, 1, "mud").Length, Is.EqualTo(8));
            // Clipped at the corner: r = 1 at (0, 0) covers (0, 0), (1, 0), (0, 1).
            Assert.That(MapBrush.SurfaceCircle(d, 0, 0, 1, "mud").Length, Is.EqualTo(3));
        }

        [Test]
        public void ANewNameIsAddedAndOldCellsKeepTheirNames()
        {
            MapDocument doc = MapDocument.Open(MapDescJson.Write(Flat(10, 10))).WithSurfaceCircle(2, 2, 1, "mud");
            MapDocument after = doc.WithSurfaceSquare(6, 6, 1, "sand");
            MapDesc d = MapDescJson.Read(after.Save());
            Assert.That(d.Surfaces.Names, Is.EqualTo(new[] { "grass", "mud", "sand" }));
            Assert.That(NameAt(d, 2, 2), Is.EqualTo("mud"));
            Assert.That(NameAt(d, 0, 0), Is.EqualTo("grass"));
            Assert.That(NameAt(d, 7, 7), Is.EqualTo("sand"));
            Assert.That(after.SurfaceOwnerAt(7, 7), Is.EqualTo("hand"));
            // A brush that changes no cell adds no name.
            Assert.That(after.WithSurfaceCircle(40, 40, 2, "zzz").Save(), Is.EqualTo(after.Save()));
        }

        [Test]
        public void TheSixtyFifthNameIsRefusedAndBadNamesToo()
        {
            var e = new MapEdit(Flat(10, 10));
            for (int n = 0; n < 63; n++) e.AddSurfaceName("n" + n.ToString("D2"));
            MapDocument full = MapDocument.Open(MapDescJson.Write(e.Current));
            var ex = Assert.Throws<InvalidOperationException>(() => full.WithSurfaceCircle(1, 1, 0, "extra"));
            StringAssert.StartsWith("MAPDESC_GRID", ex.Message);
            Assert.DoesNotThrow(() => full.WithSurfaceCircle(1, 1, 0, "n05"));
            Assert.Throws<ArgumentException>(() => MapBrush.SurfaceCircle(e.Current, 1, 1, 0, ""));
            Assert.Throws<ArgumentException>(() => MapBrush.SurfaceSquare(e.Current, 1, 1, 0, "a\0b"));
            Assert.Throws<ArgumentException>(() => MapBrush.SurfaceSquare(e.Current, 1, 1, 0, new string('x', 256)));
            Assert.Throws<ArgumentException>(() => MapBrush.SurfaceSquare(e.Current, 1, 1, 0, null));
        }

        [Test]
        public void NoSurfaceTableIsRefused()
        {
            MapDesc none = MapNew.Desc(new MapNewSpec { Id = "x", Pack = Pack, Width = 4, Depth = 4, CeilingRaw = 1000, HeightRaw = 0 });
            Assert.Throws<ArgumentException>(() => MapBrush.SurfaceCircle(none, 1, 1, 1, "grass"));
            Assert.Throws<ArgumentException>(() => MapBrush.SurfaceSquare(none, 1, 1, 1, "grass"));
        }
    }
}
