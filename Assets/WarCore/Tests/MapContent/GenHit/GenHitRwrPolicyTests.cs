using NUnit.Framework;
using WarCore.Content.Rwr;

namespace WarCore.Tests
{
    public sealed class GenHitRwrPolicyTests
    {
        // Recorded on origin/main (05976b4) before changing resolver admission.
        [TestCase("rwr", 0x8BE7D7DFA58533CDUL, 0x526DC7AB0741E51AUL)]
        [TestCase("rwr-m6", 0x934F8562D85447D2UL, 0x526DC7AB0741E51AUL)]
        [TestCase("rwr-melee", 0x74B9EA11CFF86DF2UL, 0x526DC7AB0741E51AUL)]
        [TestCase("rwr-terrain-demo", 0x8BE7D7DFA58533CDUL, 0x526DC7AB0741E51AUL)]
        public void GenHit_RwrPolicyDigestsMatchOriginMain(string id, ulong combat, ulong match)
        {
            var content = RwrPack.Load(id);
            Assert.That(content.BuildCombat().PolicyDigest, Is.EqualTo(combat));
            Assert.That(RwrMatch.CreateM6(content).PolicyDigest, Is.EqualTo(match));
        }
    }
}
