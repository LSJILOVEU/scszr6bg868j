using NUnit.Framework;
using WarCore.Content;

namespace WarCore.Tests
{
    // The ai content section: the value, its default, and the exact errors.
    public sealed class L4SectionTests
    {
        [Test]
        public void ValueAndDefaults()
        {
            Assert.That(L4.Pack(L4.Ticks(300)).Section<AiRules>().FailedSlotTicks, Is.EqualTo(300));
            Assert.That(L4.Pack(L4.Ticks(1000000)).Section<AiRules>().FailedSlotTicks, Is.EqualTo(1000000));
            Assert.That(L4.Pack("{ }").Section<AiRules>().FailedSlotTicks, Is.EqualTo(0), "field left out");
            Assert.That(L4.Pack(null).Section<AiRules>().FailedSlotTicks, Is.EqualTo(0), "section left out");
        }

        [TestCase("{ \"failedSlotTicks\": -1 }", "content pack 'l4-test' layer 'l4-test' ai field 'failedSlotTicks': out of range [0, 1000000]")]
        [TestCase("{ \"failedSlotTicks\": 1000001 }", "content pack 'l4-test' layer 'l4-test' ai field 'failedSlotTicks': out of range [0, 1000000]")]
        [TestCase("{ \"failedSlotTicks\": 1.5 }", "content pack 'l4-test' layer 'l4-test' ai field 'failedSlotTicks': must be an integer")]
        [TestCase("{ \"failedSlotTicks\": \"5\" }", "content pack 'l4-test' layer 'l4-test' ai field 'failedSlotTicks': must be an integer")]
        [TestCase("{ \"failedSlotTicks\": 5, \"other\": 1 }", "content pack 'l4-test' layer 'l4-test' ai field 'other': unknown field name (misspelled?)")]
        public void Errors(string ai, string message)
        {
            var e = Assert.Throws<ContentException>(() => L4.Pack(ai));
            Assert.That(e.Message, Is.EqualTo(message));
        }
    }
}
