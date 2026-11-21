using NUnit.Framework;

namespace WarCore.Tests
{
    // BASE-0 V19, support matrix row 1: an existing world (no action switch) keeps every locked tick hash of the F5 baseline.
    public sealed class Base0LockedWorldTests
    {
        [Test]
        public void V19_Row1_AnExistingWorldKeepsItsLockedHashes()
        {
            ulong[] hashes = F5BaselineLockTests.Run(out _);
            Assert.That(hashes, Is.EqualTo(F5BaselineData.Hashes));
        }
    }
}
