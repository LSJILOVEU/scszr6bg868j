using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt061SimConfigTests
    {
        [Test]
        public void MaximumRequestCapacityIsAccepted()
        {
            SimConfig config = null;
            Assert.DoesNotThrow(() => config = new SimConfig(spawnRequestCapacity: 134217728));
            Assert.That(config.SpawnRequestCapacity, Is.EqualTo(134217728));
        }

        [Test]
        public void SignedAdditionCanReachMaxValue()
        {
            long result = 0;
            Assert.DoesNotThrow(() => result = SimChecked.Add(long.MaxValue - 1, 1));
            Assert.That(result, Is.EqualTo(long.MaxValue));
        }
    }
}
