using System;
using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    [TestFixture, Category("Round4Capacity")]
    public sealed class Mt033BoundedLookupTests
    {
        [TestCase(1,2)]
        [TestCase(3,8)]
        [TestCase(4,8)]
        public void ConstructorUsesMinimumPowerOfTwoStorage(int capacity, int length)
        {
            var lookup = new BoundedLookup<int>(capacity);
            foreach (string name in new[] { "keys", "values", "used" })
            {
                var storage = (Array)GridIndexRegressionAudit.Field(lookup,name);
                Assert.That(storage.Length,Is.EqualTo(length),name);
            }
        }

        [Test]
        public void RemovingMissingKeyPreservesEntriesAndCapacity()
        {
            var lookup = new BoundedLookup<int>(2);
            lookup.Add(1,10);
            lookup.Add(5,20);
            lookup.Remove(9);
            Assert.That(lookup.Find(1),Is.EqualTo(10));
            Assert.That(lookup.Find(5),Is.EqualTo(20));
            Assert.Throws<SimBoundaryException>(() => lookup.Add(9,30));
        }
    }
}
