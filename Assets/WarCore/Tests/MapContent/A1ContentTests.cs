using NUnit.Framework;
using WarCore.Content;

namespace WarCore.Tests
{
    public sealed class A1ContentTests
    {
        [TestCase(null, 0)]
        [TestCase("{ }", 0)]
        [TestCase("{ \"meleeChaseRange\": 0 }", 0)]
        [TestCase("{ \"meleeChaseRange\": 80 }", 80)]
        [TestCase("{ \"meleeChaseRange\": 2147483647 }", 2147483647)]
        public void A1_ContentRangeAndDefaults(string ai, int expected)
        {
            Assert.That(A1.Pack(ai).Section<AiRules>().MeleeChaseRange, Is.EqualTo(expected));
        }

        [TestCase("-1", "out of range [0, 2147483647]")]
        [TestCase("1.5", "must be an integer")]
        [TestCase("\"5\"", "must be an integer")]
        public void A1_ContentRejectsInvalidRange(string value, string reason)
        {
            var e = Assert.Throws<ContentException>(() => A1.Pack("{ \"meleeChaseRange\": " + value + " }"));
            Assert.That(e.Message, Is.EqualTo("content pack 'l4-test' layer 'l4-test' ai field 'meleeChaseRange': " + reason));
        }
    }
}
