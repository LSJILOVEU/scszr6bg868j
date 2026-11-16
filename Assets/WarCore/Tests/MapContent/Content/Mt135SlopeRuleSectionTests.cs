using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;

namespace WarCore.Tests
{
    public sealed class Mt135SlopeRuleSectionTests
    {
        [Test]
        public void SlopeRule_Build_MinimumDenominatorIsAccepted()
        {
            var source = new MemoryContentSource(new Dictionary<string, string>
            {
                ["slope"] = "{ \"id\": \"slope\", \"extends\": \"rwr\", \"slopeRule\": { \"maxWalkableNum\": 1, \"maxWalkableDen\": 1 } }"
            }, FileContentSource.Rwr());
            ContentPack pack = null;
            Assert.DoesNotThrow(() => pack = ContentLoader.Load(source, "slope"));
            Assert.That((pack.Terrain.MaxWalkableNum, pack.Terrain.MaxWalkableDen), Is.EqualTo((1, 1)));
        }

        [Test]
        public void SlopeRule_Build_UnknownFieldIsRejected()
        {
            var source = new MemoryContentSource(new Dictionary<string, string>
            {
                ["slope"] = "{ \"id\": \"slope\", \"extends\": \"rwr\", \"slopeRule\": { \"maxWalkableNum\": 1, \"maxWalkableDen\": 2, \"unknown\": 0 } }"
            }, FileContentSource.Rwr());
            string message = Assert.Throws<ContentException>(() => ContentLoader.Load(source, "slope")).Message;
            Assert.That(message, Is.EqualTo("content pack 'slope' layer 'slope' slopeRule field 'unknown': unknown field name (misspelled?)"));
        }
    }
}
