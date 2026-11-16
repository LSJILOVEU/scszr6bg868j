using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;

namespace WarCore.Tests
{
    public sealed class Mt134MatchSectionTests
    {
        private static ContentPack Pack(string fields)
        {
            var source = new MemoryContentSource(new Dictionary<string, string>
            {
                ["edge"] = "{ \"id\": \"edge\", \"extends\": \"rwr\", \"match\": { " + fields + " } }"
            }, FileContentSource.Rwr());
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = ContentLoader.Load(source, "edge"));
            return pack;
        }

        [TestCase(1)]
        [TestCase(2)]
        public void EqualCaptureAdvantageIsAccepted(int advantage)
        {
            var match = Pack("\"captureAdvantageNum\": " + advantage + ", \"captureAdvantageDen\": " + advantage).Match;
            Assert.That((match.CaptureAdvantageNum, match.CaptureAdvantageDen), Is.EqualTo((advantage, advantage)));
        }

        [Test]
        public void OneTickCaptureIsAccepted()
        {
            var match = Pack("\"captureTicks\": 1").Match;
            Assert.That(match.CaptureTicks, Is.EqualTo(1));
        }

        [Test]
        public void OneTickThinkIntervalIsAccepted()
        {
            var match = Pack("\"thinkInterval\": 1").Match;
            Assert.That(match.ThinkInterval, Is.EqualTo(1));
        }

        [Test]
        public void MaximumGridColumnsIsAccepted()
        {
            var match = Pack("\"gridColumns\": 4096").Match;
            Assert.That(match.GridColumns, Is.EqualTo(4096));
        }
    }
}
