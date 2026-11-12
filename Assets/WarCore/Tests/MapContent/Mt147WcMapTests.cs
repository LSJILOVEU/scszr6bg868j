using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt147WcMapTests
    {
        [Test]
        public void TryCell_UsesEachAxisOrigin()
        {
            WcMap source = M2MapFixture.Model(false);
            var map = new WcMap("shift", "c", SimVec3.Metres(-8, 0, -4), SimVec3.Metres(0, 4, 0),
                source.ChunkSize, default, source.Width, source.Depth,
                source.Layers.ToArray(), source.Walls.ToArray(), source.Passability.ToArray());

            Assert.That(map.TryCell(SimFix.FromInt(-6), SimFix.FromInt(-3), out int x, out int z), Is.True);
            Assert.That(x, Is.EqualTo(2));
            Assert.That(z, Is.EqualTo(1));
        }
    }
}
