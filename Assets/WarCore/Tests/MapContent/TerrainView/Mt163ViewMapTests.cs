using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests.TerrainView
{
    public sealed class Mt163ViewMapTests
    {
        [Test]
        public void Origin_UsesSeparateAxesInMetres()
        {
            ViewMap view = ViewMap.Parse(L8aFixtures.Bytes(L8aFixtures.F(minX: -2)));
            Assert.That(view.MinX, Is.EqualTo(-2f));
            Assert.That(view.MinZ, Is.EqualTo(0f));
        }

        [Test]
        public void Walls_UseZOriginAndFullWidth()
        {
            ViewMap view = ViewMap.Parse(L8aFixtures.Bytes(L8aFixtures.F(minX: -2)));
            Assert.That(view.Walls[0].CenterZ, Is.EqualTo(2f));
            Assert.That(view.Walls[0].SizeX, Is.EqualTo(1f));
        }

        [Test]
        public void Passability_SetBitIsOpenAndClearBitIsBlocked()
        {
            ViewMap view = ViewMap.Parse(L8aFixtures.Bytes(L8aFixtures.F()));
            Assert.That(view.IsPassable(0, 0), Is.True);
            Assert.That(view.IsPassable(1, 1), Is.False);
        }

        [Test]
        public void SurfaceIndex_UsesRowMajorCells()
        {
            ViewMap view = ViewMap.Parse(L8aFixtures.Bytes(L8aFixtures.F()));
            Assert.That(view.SurfaceIndex(6, 5), Is.EqualTo(1));
            Assert.That(view.SurfaceIndex(7, 4), Is.EqualTo(0));
        }
    }
}
