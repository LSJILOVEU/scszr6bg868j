using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.Ed2Fixtures;

namespace WarCore.Tests
{
    // ED-2 acceptance 8: each With* brush is the same batch of hand edits as the brush's writes applied through MapEdit.
    public sealed class Ed2DocumentTests
    {
        private static MapDesc Ground() => Map(20, 16, (i, j) => (int)(S / 8 * ((i * 5 + j * 3) % 7)), 0, 8 * S);

        private static byte[] Through(MapDesc d, Action<MapEdit> body)
        {
            var e = new MapEdit(d);
            e.Batch(body);
            return MapDescJson.Write(e.Current);
        }

        private static byte[] Heights(MapDesc d, MapHeightWrite[] writes) =>
            Through(d, b => { foreach (MapHeightWrite w in writes) b.SetHeight(w.I, w.J, w.Raw); });

        [Test]
        public void EachBrushMatchesMapEdit()
        {
            MapDesc d = Ground();
            byte[] before = MapDescJson.Write(d);
            MapDocument doc = MapDocument.Open(before);
            Assert.That(doc.WithRaise(8, 8, 5, 40000).Save(), Is.EqualTo(Heights(d, MapBrush.Raise(d, 8, 8, 5, 40000))));
            Assert.That(doc.WithFlatten(8, 8, 5, 30000).Save(), Is.EqualTo(Heights(d, MapBrush.Flatten(d, 8, 8, 5, 30000))));
            Assert.That(doc.WithSmooth(8, 8, 5).Save(), Is.EqualTo(Heights(d, MapBrush.Smooth(d, 8, 8, 5))));
            Assert.That(doc.WithRamp(2, 8, 0, 14, 8, (int)S, 1).Save(),
                Is.EqualTo(Heights(d, MapBrush.Ramp(d, 2, 8, 0, 14, 8, (int)S, 1, 3, 4))));
            Assert.That(doc.WithBank(2, 4, 14, 4, 3, 50000).Save(), Is.EqualTo(Heights(d, MapBrush.Bank(d, 2, 4, 14, 4, 3, 50000))));
            Assert.That(doc.WithSurfaceCircle(5, 5, 3, "mud").Save(), Is.EqualTo(Through(d, b =>
            {
                b.AddSurfaceName("mud");
                foreach (MapSurfaceWrite w in MapBrush.SurfaceCircle(d, 5, 5, 3, "mud")) b.SetSurface(w.X, w.Z, w.Name);
            })));
            Assert.That(doc.WithSurfaceSquare(5, 5, 2, "road").Save(), Is.EqualTo(Through(d, b =>
            {
                b.AddSurfaceName("road");
                foreach (MapSurfaceWrite w in MapBrush.SurfaceSquare(d, 5, 5, 2, "road")) b.SetSurface(w.X, w.Z, w.Name);
            })));
            Assert.That(doc.Save(), Is.EqualTo(before), "the old document is unchanged");
        }

        [Test]
        public void BrushedPointsBecomeHandOwnedAndOthersKeepTheirOwner()
        {
            var e = new MapEdit(Ground());
            e.Stamp(new MapStampPlan(MapNew.TerrainInstance, E3Plans.Corners(0, 20, 0, 16, (int)S), new MapSurfaceWrite[0],
                new MapStampEntry<MapWall>[0], new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0]));
            MapDocument doc = MapDocument.Open(MapDescJson.Write(e.Current)).WithRaise(10, 8, 2, 20000);
            Assert.That(doc.HeightOwnerAt(10, 8), Is.EqualTo("hand"));
            Assert.That(doc.HeightOwnerAt(12, 8), Is.EqualTo("stamp:terrain"), "weight 0 at the edge");
            Assert.That(doc.HeightOwnerAt(0, 0), Is.EqualTo("stamp:terrain"));
        }
    }
}
