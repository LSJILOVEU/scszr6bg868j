using System;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt146WcMapTests
    {
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void HasTerrain_WithOneComponent_IsTrue(bool heights, bool surfaces)
        {
            WcMap map = TerrainView.L8aFixtures.F(heights: heights, surfaces: surfaces);
            Assert.That(map.HasTerrain, Is.True);
        }

        [Test]
        public void Constructor_CopiesTerrainArrays()
        {
            int[] points = { 0, 1, 2, 3 };
            string[] names = { "grass", "mud" };
            byte[] cells = { 0 };
            var map = new WcMap("cell", "c", default, SimVec3.Metres(1, 4, 1), SimFix.FromInt(1), default, 1, 1,
                new[] { new WcMapLayer("walls", 1) }, Array.Empty<WcMapWall>(), new byte[] { 1 }, points, names, cells);

            points[0] = 9;
            names[0] = "dirt";
            cells[0] = 1;

            Assert.That(map.HeightPoints, Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(map.SurfaceNames, Is.EqualTo(new[] { "grass", "mud" }));
            Assert.That(map.SurfaceCells, Is.EqualTo(new byte[] { 0 }));
        }

        [Test]
        public void LayerMask_RejectsDifferentCase()
        {
            WcMap map = M2MapFixture.Model();
            Assert.That(map.LayerMask("walls"), Is.EqualTo(2u));
            Assert.Throws<ArgumentException>(() => map.LayerMask("Walls"));
        }

        [Test]
        public void IsPassable_RejectsXAtWidth()
        {
            WcMap map = M2MapFixture.Model();
            Assert.Throws<ArgumentOutOfRangeException>(() => map.IsPassable((int)map.Width, 0));
        }

        [TestCase(0, 0, true)]
        [TestCase(4, 1, false)]
        public void IsPassable_MatchesStoredBit(int x, int z, bool expected)
        {
            WcMap map = M2MapFixture.Model();
            Assert.That(map.IsPassable(x, z), Is.EqualTo(expected));
        }

        [Test]
        public void TryCell_ExcludesMaximumX()
        {
            WcMap map = M2MapFixture.Model();
            Assert.That(map.TryCell(SimFix.FromRaw(map.Max.X.Raw - 1), map.Min.Z, out int x, out int z), Is.True);
            Assert.That(x, Is.EqualTo(7));
            Assert.That(z, Is.EqualTo(0));
            Assert.That(map.TryCell(map.Max.X, map.Min.Z, out x, out z), Is.False);
            Assert.That(x, Is.EqualTo(-1));
            Assert.That(z, Is.EqualTo(-1));
        }
    }
}
