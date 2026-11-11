using NUnit.Framework;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt103StateHashTests
    {
        [Test]
        public void FirstNonAsciiCharacterMatchesUtf8Vector()
        {
            var hash = new StateHash();
            hash.Add("\u0080");
            // Eight-byte length 2, followed by C2 80.
            Assert.That(hash.Value, Is.EqualTo(0x9DA27DD329BAF30DUL));
        }

        [Test]
        public void SurrogatePairAcrossChunkBoundaryMatchesUtf8Vector()
        {
            var hash = new StateHash();
            hash.Add(new string('a', 63) + "\ud83d\ude00");
            // Eight-byte length 67, followed by 63 bytes of 61 and F0 9F 98 80.
            Assert.That(hash.Value, Is.EqualTo(0xF9B4E2629C9CBCCAUL));
        }
    }
}
