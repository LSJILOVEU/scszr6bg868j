using System;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Fixtures;

namespace WarCore.Tests
{
    public sealed class Mt140MapEditTests
    {
        [Test]
        public void UnknownEntryKindIsRejected()
        {
            var e = new MapEdit(Read(Plain()));
            Assert.Throws<ArgumentException>(() => e.EntryOwner("unknown", 1));
        }

        [Test]
        public void RemovingPointKeepsBaseWithSameId()
        {
            var e = new MapEdit(Read(Plain()));
            Assert.That(e.AddPoint(PointAt(500, 500)), Is.EqualTo(1));
            Assert.That(e.AddBase(BaseAt(1500, 500)), Is.EqualTo(1));

            e.Remove("point", 1);

            Assert.That(e.Current.Points.Count, Is.EqualTo(0));
            Assert.That(e.Current.Bases.Count, Is.EqualTo(1));
        }

        [Test]
        public void RemovingMissingIdIsRejected()
        {
            var e = new MapEdit(Read(ThreeWalls()));
            byte[] before = MapDescJson.Write(e.Current);

            Assert.Throws<ArgumentException>(() => e.Remove("wall", 4));

            Assert.That(MapDescJson.Write(e.Current), Is.EqualTo(before));
        }
    }
}
