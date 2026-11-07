using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.E3Fixtures;

namespace WarCore.Tests
{
    // Acceptance 7: the MapDocument edit facade returns new documents equal to the same MapEdit step.
    public sealed class E3DocumentTests
    {
        private static void Same(MapDesc start, Func<MapDocument, MapDocument> facade, Action<MapEdit> step)
        {
            byte[] startBytes = MapDescJson.Write(start);
            MapDocument doc = MapDocument.Open(startBytes);
            MapDocument edited = facade(doc);
            var e = new MapEdit(start);
            step(e);
            Assert.That(edited, Is.Not.SameAs(doc));
            Assert.That(doc.Save(), Is.EqualTo(startBytes));
            Assert.That(edited.Save(), Is.Not.EqualTo(startBytes));
            Assert.That(edited.Save(), Is.EqualTo(MapDescJson.Write(e.Current)));
        }

        [Test]
        public void WithHeight() => Same(E3StampTests.HandEdited().Current, d => d.WithHeight(6, 5, 700), e => e.SetHeight(6, 5, 700));

        [Test]
        public void WithSurface() => Same(E3StampTests.HandEdited().Current, d => d.WithSurface(6, 5, "road"), e => e.SetSurface(6, 5, "road"));

        [Test]
        public void WithoutEntry()
        {
            MapDesc start = E3StampTests.HandEdited().Current;
            MapWall stamped = start.Walls.Single(w => w.Src.Src == 1);
            Same(start, d => d.WithoutEntry("wall", stamped.Id), e => e.Remove("wall", stamped.Id));
            MapDocument after = MapDocument.Open(MapDescJson.Write(start)).WithoutEntry("wall", stamped.Id);
            MapDesc read = MapDescJson.Read(after.Save());
            Assert.That(read.Removed.Count, Is.EqualTo(start.Removed.Count + 1));
            Assert.That(read.Walls.Any(w => w.Id == stamped.Id), Is.False);
        }

        [Test]
        public void WithHandReleased() =>
            Same(E3StampTests.HandEdited().Current, d => d.WithHandReleased(0, 0, 3, 3), e => e.ReleaseHand(0, 0, 3, 3));

        [Test]
        public void OwnerQueries()
        {
            MapDesc desc = E3StampTests.HandEdited().Current;
            MapDocument doc = MapDocument.Open(MapDescJson.Write(desc));
            ulong moved = desc.Walls.Single(w => w.Src.Src == 2).Id;
            ulong other = desc.Walls.Single(w => w.Src.Src == 1).Id;
            Assert.That(doc.HeightOwnerAt(1, 1), Is.EqualTo("hand"));
            Assert.That(doc.HeightOwnerAt(0, 0), Is.EqualTo("stamp:s1"));
            Assert.That(doc.HeightOwnerAt(W, D), Is.Null);
            Assert.That(doc.SurfaceOwnerAt(0, 0), Is.EqualTo("hand"));
            Assert.That(doc.SurfaceOwnerAt(3, 3), Is.EqualTo("stamp:s1"));
            Assert.That(doc.SurfaceOwnerAt(W - 1, D - 1), Is.Null);
            Assert.That(doc.EntryOwner("wall", moved), Is.EqualTo("hand"));
            Assert.That(doc.EntryOwner("wall", other), Is.EqualTo("stamp:s1"));
            Assert.That(doc.EntryOwner("unit", 1), Is.EqualTo("stamp:s1"));
            MapDocument released = doc.WithHandReleased(0, 0, 3, 3);
            Assert.That(released.EntryOwner("wall", moved), Is.Null);
        }
    }
}
