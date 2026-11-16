using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [Category("Step2bB")]
    public sealed class Mt210SpatialCellRegionsTests
    {
        [Test]
        public void SingleTopCellRemainsQueryable()
        {
            var fixture = new QueryModeFixture(cellRaw: 1, capacity: 1, memberships: 8);
            EntityId expected = fixture.Add(default);
            var output = new EntityId[1];

            Assert.That(fixture.Grid.Sphere(default, SimFix.FromInt(2), output), Is.EqualTo(1));
            Assert.That(output[0], Is.EqualTo(expected));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void EachTopCellRemainsQueryableAfterPruning(int target)
        {
            var fixture = new QueryModeFixture(cellRaw: 1, capacity: 3, memberships: 32);
            var points = new[] { SimVec3.Metres(-16, -16, -16), default, SimVec3.Metres(16, 16, 16) };
            var entities = new EntityId[3];
            for (int i = 0; i < points.Length; i++)
                entities[i] = fixture.Add(points[i]);
            var output = new EntityId[3];

            // A quarter-metre top cell makes this query span 17 cubed cells and use region pruning.
            Assert.That(fixture.Grid.Sphere(points[target], SimFix.FromInt(2), output), Is.EqualTo(1));
            Assert.That(output[0], Is.EqualTo(entities[target]));
        }
    }
}
