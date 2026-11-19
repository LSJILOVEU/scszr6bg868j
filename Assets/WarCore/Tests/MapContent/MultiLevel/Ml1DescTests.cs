using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Content.MapTools;

namespace WarCore.Tests
{
    // ML-1 acceptance 5: "baseRaw" in the description, its range and the version it needs.
    public sealed class Ml1DescTests
    {
        private const long M = Ml1Desc.M;

        private static MapDesc Read(string text)
        {
            MapDesc d = null;
            Assert.DoesNotThrow(() => d = MapDescJson.Read(Encoding.UTF8.GetBytes(text)));
            return d;
        }

        private static string Error(string text) => Assert.Throws<InvalidDataException>(() => MapDescJson.Read(Encoding.UTF8.GetBytes(text))).Message;

        private static string Raised(long baseRaw, long heightRaw = 2) =>
            Ml1Desc.Text(Ml1Desc.Make(3, 1, null, Ml1Desc.Wall(1, 1, 0, 1, 1, heightRaw, baseRaw: baseRaw)));

        private static int Line(string text, string fragment)
        {
            int at = text.IndexOf(fragment, System.StringComparison.Ordinal);
            Assert.That(at < 0, Is.False, fragment);
            return text.Substring(0, at).Split('\n').Length;
        }

        [Test]
        public void BaseMustBeAboveTheFloor()
        {
            string equal = Raised(0);
            Assert.That(Error(equal), Is.EqualTo("MAPDESC_WALL wall 1 line " + Line(equal, "\"baseRaw\"")));
            Assert.That(Read(Raised(1)).Walls[0].BaseRaw, Is.EqualTo(1));
        }

        // baseRaw - floorRaw + heightRaw <= ceilingRaw - floorRaw (ceiling 10 m here).
        [Test]
        public void TopMustStayUnderTheCeiling()
        {
            Assert.That(Read(Raised(8 * M, 2 * M)).Walls[0].BaseRaw, Is.EqualTo(8 * M));
            string over = Raised(8 * M + 1, 2 * M);
            Assert.That(Error(over), Is.EqualTo("MAPDESC_WALL wall 1 line " + Line(over, "\"baseRaw\"")));
        }

        // The same rules with a negative floor and the widest room: compared without overflow.
        [Test]
        public void RangeRulesHoldAtTheLongLimits()
        {
            string text = Raised(5).Replace("\"floorRaw\": 0,", "\"floorRaw\": -9223372036854775808,")
                .Replace("\"ceilingRaw\": 655360,", "\"ceilingRaw\": 9223372036854775807,");
            Assert.That(Read(text).Walls[0].BaseRaw, Is.EqualTo(5));
            string low = text.Replace("\"baseRaw\": 5\n", "\"baseRaw\": -9223372036854775803\n");
            Assert.That(Read(low).Walls[0].BaseRaw, Is.EqualTo(long.MinValue + 5), "5 raw above the lowest floor, room 2^64 - 1");
            string top = text.Replace("\"baseRaw\": 5\n", "\"baseRaw\": 9223372036854775805\n");
            Assert.That(Read(top).Walls[0].BaseRaw, Is.EqualTo(long.MaxValue - 2));
            string over = text.Replace("\"baseRaw\": 5\n", "\"baseRaw\": 9223372036854775806\n");
            Assert.That(Error(over), Is.EqualTo("MAPDESC_WALL wall 1 line " + Line(over, "\"baseRaw\"")));
            string atFloor = text.Replace("\"baseRaw\": 5\n", "\"baseRaw\": -9223372036854775808\n");
            Assert.That(Error(atFloor), Is.EqualTo("MAPDESC_WALL wall 1 line " + Line(atFloor, "\"baseRaw\"")));
        }

        [Test]
        public void VersionOneDoesNotKnowBaseRaw()
        {
            string text = Raised(M).Replace("\"version\": 2,", "\"version\": 1,");
            Assert.That(Error(text), Is.EqualTo("MAPDESC_KEY wall 1 line " + Line(text, "\"baseRaw\"")));
        }

        // Version 2 is only for what version 1 cannot hold; it is judged on the version line, before pass 1.
        [Test]
        public void VersionTwoWithoutBaseRawIsAVersionError()
        {
            string text = Ml1Desc.Text(Ml1Desc.Make(3, 1, null, Ml1Desc.Wall(1, 1, 0, 1, 1, M))).Replace("\"version\": 1,", "\"version\": 2,");
            Assert.That(Error(text), Is.EqualTo("MAPDESC_FORMAT line 3"));
            Assert.That(Error(text.Replace("\"walls\": [", "\"walls\": 7, \"x\": [")), Is.EqualTo("MAPDESC_FORMAT line 3"));
            Assert.That(Error(text.Replace("\"heightRaw\": 65536", "\"heightRaw\": 65536, \"other\": { \"baseRaw\": 1 }")),
                Is.EqualTo("MAPDESC_FORMAT line 3"), "baseRaw must be a key of a wall object");
            Assert.That(Error(Raised(M).Replace("\"version\": 2,", "\"version\": 3,")), Is.EqualTo("MAPDESC_FORMAT line 3"));
        }

        [Test]
        public void RaisedWallWritesVersionTwoAndRoundTrips()
        {
            MapDesc d = Ml1Desc.Make(3, 1, null, Ml1Desc.Wall(1, 0, 0, 1, 1, M), Ml1Desc.Wall(2, 1, 0, 1, 1, M, baseRaw: 2 * M));
            Assert.That(d.Version, Is.EqualTo(2));
            byte[] once = MapDescJson.Write(d);
            string text = Encoding.UTF8.GetString(once);
            Assert.That(text, Does.Contain("\n  \"version\": 2,\n"));
            Assert.That(text, Does.Contain("\"heightRaw\": 65536,\n      \"baseRaw\": 131072\n"));
            Assert.That(text.Split("baseRaw").Length, Is.EqualTo(2), "only the raised wall writes baseRaw");
            MapDesc read = Read(text);
            Assert.That(read.Version, Is.EqualTo(2));
            Assert.That(new[] { read.Walls[0].BaseRaw, read.Walls[1].BaseRaw }, Is.EqualTo(new long?[] { null, 2 * M }));
            Assert.That(MapDescJson.Write(read), Is.EqualTo(once));
            Assert.That(Ml1Desc.Make(3, 1, null, Ml1Desc.Wall(1, 0, 0, 1, 1, M)).Version, Is.EqualTo(1));
        }
    }
}
