using NUnit.Framework;
using WarCore.Content;

namespace WarCore.Tests
{
    public sealed class Mt133ContentJsonTests
    {
        [Test]
        public void NewlineEscapesDecodeAsLineFeeds()
        {
            Assert.That(ContentJson.Parse("\"first\\nsecond\"", "json").Text, Is.EqualTo("first\nsecond"));
        }

        [Test]
        public void ArrayItemsRequireCommas()
        {
            Assert.Throws<ContentException>(() => ContentJson.Parse("[1 2]", "json"));
        }
    }
}
