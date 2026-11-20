using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.E3Fixtures;
using static WarCore.Tests.Ed1Fixtures;

namespace WarCore.Tests
{
    // ED-1 acceptance 8 and 9: undo and redo, and the document facade.
    public sealed class Ed1SessionTests
    {
        private static MapDocument Doc() => MapDocument.Open(Bytes(Start()));

        private static Func<MapDocument, MapDocument> Height(int k) => d => d.WithHeight(k % (W + 1), k / (W + 1), 100 + k);

        [Test]
        public void TwentyStepsBackAndForward()
        {
            MapDocument start = Doc();
            var s = new MapEditSession(start);
            byte[] nineteen = null;
            for (int k = 0; k < 20; k++)
            {
                if (k == 19) nineteen = s.Current.Save();
                s.Do(Height(k));
            }
            byte[] twenty = s.Current.Save();
            Assert.That(s.UndoCount, Is.EqualTo(20));
            for (int k = 0; k < 20; k++) Assert.That(s.Undo(), Is.True);
            Assert.That(s.Current.Save(), Is.EqualTo(start.Save()));
            Assert.That((s.CanUndo, s.Undo()), Is.EqualTo((false, false)));
            for (int k = 0; k < 20; k++) Assert.That(s.Redo(), Is.True);
            Assert.That(s.Current.Save(), Is.EqualTo(twenty));
            Assert.That((s.CanRedo, s.Redo()), Is.EqualTo((false, false)));
            Assert.That(s.UndoCount, Is.EqualTo(20), "a redo can be undone");
            Assert.That(s.Undo(), Is.True);
            Assert.That(s.Current.Save(), Is.EqualTo(nineteen));
        }

        [Test]
        public void ADoAfterAnUndoClearsTheRedos()
        {
            var s = new MapEditSession(Doc());
            s.Do(Height(1));
            s.Do(Height(2));
            s.Undo();
            Assert.That(s.CanRedo, Is.True);
            s.Do(Height(3));
            Assert.That(s.CanRedo, Is.False);
            Assert.That(s.Redo(), Is.False);
            Assert.That(s.UndoCount, Is.EqualTo(2));
        }

        [Test]
        public void TheLimitDropsTheOldest()
        {
            var s = new MapEditSession(Doc(), 3);
            var versions = new byte[6][];
            versions[0] = s.Current.Save();
            for (int k = 1; k <= 5; k++) { s.Do(Height(k)); versions[k] = s.Current.Save(); }
            Assert.That(s.UndoCount, Is.EqualTo(3));
            for (int k = 0; k < 3; k++) Assert.That(s.Undo(), Is.True);
            Assert.That(s.Undo(), Is.False);
            Assert.That(s.Current.Save(), Is.EqualTo(versions[2]));
            for (int k = 0; k < 3; k++) s.Redo();
            Assert.That(s.Current.Save(), Is.EqualTo(versions[5]));
        }

        [Test]
        public void AFailedEditLeavesTheSessionAsItWas()
        {
            var s = new MapEditSession(Doc());
            s.Do(Height(1));
            s.Do(Height(2));
            s.Undo();
            MapDocument current = s.Current;
            var mine = new TimeoutException("edit");
            Exception got = Assert.Throws<TimeoutException>(() => s.Do(d => throw mine));
            Assert.That(got, Is.SameAs(mine));
            Assert.Throws<ArgumentException>(() => s.Do(d => d.WithSurface(0, 0, "nope")));
            Assert.Throws<InvalidOperationException>(() => s.Do(d => null));
            Assert.Throws<ArgumentNullException>(() => s.Do(null));
            Assert.That(s.Current, Is.SameAs(current));
            Assert.That((s.UndoCount, s.CanRedo), Is.EqualTo((1, true)));
        }

        [Test]
        public void AnEmptySessionHasNothingToUndoOrRedo()
        {
            MapDocument start = Doc();
            var s = new MapEditSession(start);
            Assert.That((s.Undo(), s.Redo(), s.CanUndo, s.CanRedo, s.UndoCount), Is.EqualTo((false, false, false, false, 0)));
            Assert.That(s.Current, Is.SameAs(start));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MapEditSession(start, 0));
            Assert.Throws<ArgumentNullException>(() => new MapEditSession(null));
            Assert.DoesNotThrow(() => new MapEditSession(start, 1));
        }

        // An edit that gives the same bytes is still a version (undo returns to the same bytes).
        [Test]
        public void AnEditWithTheSameBytesIsAVersion()
        {
            var s = new MapEditSession(Doc());
            byte[] before = s.Current.Save();
            s.Do(d => d.WithSurfaceName("mud"));
            Assert.That(s.UndoCount, Is.EqualTo(1));
            Assert.That(s.Current.Save(), Is.EqualTo(before));
        }

        [Test]
        public void WithEditsIsTheSameAsTheEditsOneByOne()
        {
            MapDocument doc = Doc();
            byte[] before = doc.Save();
            var list = new MapEditList().Height(1, 1, 100).Surface(0, 0, "mud").Height(2, 2, 300).Surface(0, 0, "road");
            Assert.That(list.Count, Is.EqualTo(4));
            MapDocument after = doc.WithEdits(list);
            var e = new MapEdit(Start());
            e.SetHeight(1, 1, 100);
            e.SetSurface(0, 0, "mud");
            e.SetHeight(2, 2, 300);
            e.SetSurface(0, 0, "road");
            Assert.That(after.Save(), Is.EqualTo(Bytes(e.Current)));
            Assert.That(doc.Save(), Is.EqualTo(before), "the old document is unchanged");
            Assert.That(doc.WithEdits(new MapEditList()).Save(), Is.EqualTo(before));

            Assert.Throws<ArgumentException>(() => doc.WithEdits(new MapEditList().Height(1, 1, 100).Surface(0, 0, "nope")));
            Assert.Throws<ArgumentNullException>(() => doc.WithEdits(null));
            Assert.That(doc.Save(), Is.EqualTo(before));
        }

        [Test]
        public void TheOtherWithMembersMatchMapEdit()
        {
            MapDocument doc = Doc();
            var e = new MapEdit(Start());
            e.AddSurfaceName("sand");
            Assert.That(doc.WithSurfaceName("sand").Save(), Is.EqualTo(Bytes(e.Current)));

            var spec = new MapNewSpec { Id = "flat", Pack = "rwr-terrain-demo", Width = 6, Depth = 4, CeilingRaw = 262144 };
            MapDocument flat = MapDocument.Open(Bytes(MapNew.Desc(spec)));
            var h = new MapEdit(MapNew.Desc(spec));
            h.EnableHeights(500);
            Assert.That(flat.WithHeightsEnabled(500).Save(), Is.EqualTo(Bytes(h.Current)));
            var g = new MapEdit(MapNew.Desc(spec));
            g.EnableSurfaces("grass");
            Assert.That(flat.WithSurfacesEnabled("grass").Save(), Is.EqualTo(Bytes(g.Current)));
            Assert.That(flat.Save(), Is.EqualTo(Bytes(MapNew.Desc(spec))));
        }
    }
}
