using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("StableIdentity")]
    public sealed class Mt223StableIdentityTests
    {
        [TestCase(7, 11UL, true)]
        [TestCase(8, 11UL, false)]
        [TestCase(7, 12UL, false)]
        public void BoxedEntityReferenceEqualityUsesSlotAndGeneration(int slot, ulong generation, bool expected)
        {
            var reference = new EntityRef(7, 11);
            Assert.That(reference.Equals((object)new EntityRef(slot, generation)), Is.EqualTo(expected));
        }

        [TestCase(0UL, 0UL, 0UL)]
        [TestCase(4UL, 0UL, 0UL)]
        [TestCase(1UL, 0UL, 1UL)]
        [TestCase(3UL, 0x8000000000000000UL, 0UL)]
        public void InvalidStableSourceFieldsAreRejected(ulong domain, ulong second, ulong third)
        {
            Assert.Throws<SimBoundaryException>(() => new StableEntityKey(domain, 7, second, third));
        }

        [TestCase(7UL, 11UL, true)]
        [TestCase(8UL, 11UL, false)]
        [TestCase(7UL, 12UL, false)]
        public void BoxedPlacedKeyEqualityUsesChunkAndPlacement(ulong chunk, ulong placement, bool expected)
        {
            var key = StableEntityKey.Placed(7, 11);
            Assert.That(key.Equals((object)StableEntityKey.Placed(chunk, placement)), Is.EqualTo(expected));
        }
    }
}
