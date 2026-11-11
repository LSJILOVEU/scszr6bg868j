using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt049NavGridTests
    {
        [Test]
        public void IsPassable_XAtWidth_IsBlocked()
        {
            NavGrid grid = M3GridPathfinderTests.Rows("..", "..");
            Assert.That(grid.IsPassable(grid.Width, 0), Is.False);
        }

        [TestCase(2, 1)]
        [TestCase(1, 2)]
        public void TryCell_UpperEdge_IsRejected(int x, int z)
        {
            NavGrid grid = M3GridPathfinderTests.Rows("..", "..");
            Assert.That(grid.TryCell(SimVec3.Metres(x, 0, z), out int key), Is.False);
            Assert.That(key, Is.EqualTo(-1));
        }

        [Test]
        public void Center_KeyAtCount_Throws()
        {
            NavGrid grid = M3GridPathfinderTests.Rows("..", "..");
            Assert.Throws<ArgumentOutOfRangeException>(() => grid.Center(grid.Count, default));
        }
    }
}
