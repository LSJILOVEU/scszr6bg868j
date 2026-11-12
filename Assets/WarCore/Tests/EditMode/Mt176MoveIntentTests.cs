using NUnit.Framework;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    public sealed class Mt176MoveIntentTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void WriteStateMatchesCanonicalFieldEncoding(bool active)
        {
            var intent = new MoveIntent(SimVec3.Metres(3, -2, 5), SimFix.FromRaw(98305), 7, active);
            var expected = new StateHash();
            expected.Add(3L * SimFix.Scale);
            expected.Add(-2L * SimFix.Scale);
            expected.Add(5L * SimFix.Scale);
            expected.Add(98305L);
            expected.Add(7);
            expected.Add(active);

            Assert.That(Fixtures.Hash(intent), Is.EqualTo(expected.Value));
        }
    }
}
