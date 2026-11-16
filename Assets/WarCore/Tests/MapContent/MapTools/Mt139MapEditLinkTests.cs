using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Fixtures;

namespace WarCore.Tests
{
    public sealed class Mt139MapEditLinkTests
    {
        [Test]
        public void StampIdEqualToNextIsRefused()
        {
            MapDesc desc = Read(Map(stamps: Row("s1", "wall", 7, 1)));
            var x = Assert.Throws<ArgumentException>(() => new MapEdit(desc));
            Assert.That(x.Message, Is.EqualTo("MAPEDIT_LINK wall: stamps id not below next"));
        }
    }
}
