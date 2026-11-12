using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt148WcMapNavigationTests
    {
        [Test]
        public void Create_PreservesLastPassabilityByte()
        {
            NavGrid grid = WcMapNavigation.Create(M2MapFixture.Model(false));
            Assert.That(grid.IsPassable(7, 3), Is.True);
        }

        [Test]
        public void Create_PreservesOriginAxes()
        {
            WcMap source = M2MapFixture.Model(false);
            var map = new WcMap(source.MapId, source.ContentId, SimVec3.Metres(-8, 0, -4), SimVec3.Metres(0, 4, 0),
                source.ChunkSize, source.FlatHeight, source.Width, source.Depth,
                source.Layers.ToArray(), source.Walls.ToArray(), source.Passability.ToArray());
            NavGrid grid = WcMapNavigation.Create(map);
            Assert.That(grid.OriginX, Is.EqualTo(SimFix.FromInt(-8)));
            Assert.That(grid.OriginZ, Is.EqualTo(SimFix.FromInt(-4)));
        }
    }
}
