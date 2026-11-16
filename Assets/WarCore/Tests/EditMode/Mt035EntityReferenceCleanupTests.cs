using System.Reflection;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("MF5ReferenceCleanup")]
    public sealed class Mt035EntityReferenceCleanupTests
    {
        [Test]
        public void ReferenceSlotOrderingIncludesMostSignificantByte()
        {
            var store = new EntityStore(2,IdentityMode.StableWorld);
            var storage = store.Register<Seat>("seat",new SeatSerializer()).Storage;
            storage.MarkReferenceValue(0);
            storage.MarkReferenceValue(1);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var type = storage.GetType();
            var slots = (int[])type.GetField("referenceSlots",flags).GetValue(storage);
            // Test sorting independently of component allocation at these sparse slots.
            slots[0] = 0x01000000;
            slots[1] = 0x00FFFFFF;

            type.GetMethod("OrderReferenceSlots",flags).Invoke(storage,null);

            var ordered = (int[])type.GetField("orderedReferences",flags).GetValue(storage);
            CollectionAssert.AreEqual(new[] {0x00FFFFFF,0x01000000},ordered);
        }
    }
}
