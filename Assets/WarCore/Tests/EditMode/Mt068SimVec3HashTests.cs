using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt068SimVec3HashTests
    {
        [Test]
        public void NonzeroVectorHashMatchesKnownValue()
        {
            Assert.That(SimVec3.Metres(1, 2, 3).GetHashCode(), Is.EqualTo(28311552));
        }
    }
}
