using NUnit.Framework;
using WarCore.Content.Rwr;

namespace WarCore.Tests
{
    // Recorded on the C#-constant version (baseline 2e95e75) before any data loading existed.
    public sealed class Wp15ContentGoldenTests
    {
        [Test]
        public void RwrPolicyDigestsMatchTheConstantVersionBitForBit()
        {
            Assert.That(RwrPack.Create().PolicyDigest, Is.EqualTo(0x8BE7D7DFA58533CDUL));
            Assert.That(RwrPack.CreateM6().PolicyDigest, Is.EqualTo(0x934F8562D85447D2UL));
            Assert.That(RwrMatch.CreateM6().PolicyDigest, Is.EqualTo(0x526DC7AB0741E51AUL));
            Assert.That(RwrMatch.CreateM7().PolicyDigest, Is.EqualTo(0x582B535D9D24F6EBUL));
        }
    }
}
