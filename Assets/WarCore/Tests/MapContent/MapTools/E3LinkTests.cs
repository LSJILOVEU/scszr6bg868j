using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.E3Fixtures;

namespace WarCore.Tests
{
    // Acceptance 9: descriptions Read accepts but whose edit links are inconsistent are refused by MapEdit.
    public sealed class E3LinkTests
    {
        private static readonly string[] Broken = { LinkSrcRepeated(), LinkRowNotBelowNext(), LinkIdTaken(), LinkRowIdRepeated() };

        [Test]
        public void InconsistentLinksAreRefused([Range(0, 3)] int n)
        {
            MapDesc desc = Read(Broken[n]);
            Assert.That(desc.Format, Is.EqualTo("warcore.map"));
            var x = Assert.Throws<ArgumentException>(() => new MapEdit(desc));
            Assert.That(x.Message, Does.StartWith("MAPEDIT_LINK wall"));
        }

        [Test]
        public void TheFacadeRefusesThemToo([Range(0, 3)] int n)
        {
            MapDocument doc = MapDocument.Open(E1aSamples.Utf8(Broken[n]));
            var x = Assert.Throws<ArgumentException>(() => doc.WithHeight(0, 0, 1));
            Assert.That(x.Message, Does.StartWith("MAPEDIT_LINK wall"));
        }

        [Test]
        public void SameIdAcrossKindsIsAccepted()
        {
            MapDesc desc = Read(LinkAcrossKinds());
            Assert.That(desc.Walls[0].Id, Is.EqualTo(1UL));
            Assert.That(desc.Units[0].Key, Is.EqualTo(1UL));
            var e = new MapEdit(desc);
            Assert.That(e.Current, Is.SameAs(desc));
        }
    }
}
