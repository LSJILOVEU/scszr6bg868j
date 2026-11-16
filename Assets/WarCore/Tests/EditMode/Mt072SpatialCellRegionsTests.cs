using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bB")]
    public sealed class Mt072SpatialCellRegionsTests
    {
        [Test]
        public void TinyGridZSplitPrunesAnExcludedDescendant()
        {
            var scene = new QueryModeFixture(cellRaw: 1, capacity: 16, memberships: 128);
            int[] z = { 0, 64, 32, 96 };
            for (int i = 0; i < 16; i++)
                scene.Add(SimVec3.Metres(16 * i, 0, i < z.Length ? z[i] : 0));
            var output = new EntityId[16];

            Assert.That(scene.Grid.Sphere(SimVec3.Metres(16, 0, 64), SimFix.FromInt(4), output), Is.EqualTo(1));
            Assert.That(output[0], Is.EqualTo(scene.Bodies[1].Entity));
            // The Z split skips a descendant: three overlapping bounds and four rejected roots.
            Assert.That(scene.Counters.RegionNodes, Is.EqualTo(7));
        }

        [TestCase(-16, 0, 0)]
        [TestCase(0, -16, 0)]
        [TestCase(0, 0, -16)]
        [TestCase(16, 0, 0)]
        [TestCase(0, 16, 0)]
        [TestCase(0, 0, 16)]
        public void TinyGridRegionBoundsRetainEachAxisExtreme(int x, int y, int z)
        {
            var scene = new QueryModeFixture(cellRaw: 1, capacity: 3, memberships: 32);
            scene.Add(default);
            scene.Add(SimVec3.Metres(-x, -y, -z));
            SimVec3 position = SimVec3.Metres(x, y, z);
            EntityId expected = scene.Add(position);
            var output = new EntityId[3];

            // A four-metre radius spans 33 top cells per axis and selects region traversal.
            Assert.That(scene.Grid.Sphere(position, SimFix.FromInt(4), output), Is.EqualTo(1));
            Assert.That(output[0], Is.EqualTo(expected));
            Assert.That(scene.Counters.RegionNodes, Is.EqualTo(3));
        }
    }
}
