using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;
using static WarCore.Tests.E1aSamples;

namespace WarCore.Tests
{
    // Acceptance 4 (frozen codes, full messages, priorities, id bounds) and 5 (ownership fields). Each case changes the
    // contract sample in one place; line numbers refer to the sample's layout.
    public sealed class E1aErrorTests
    {
        [Test]
        public void SampleBakes()
        {
            MapBakeResult result = Bake(Sample);
            Assert.That(result.PackId, Is.EqualTo("rwr-m6"));
            Assert.That(result.Setup.ControlPoints[0].Id, Is.EqualTo(1));
            Assert.That(result.Setup.Bases[0].Team, Is.EqualTo(2));
        }

        [TestCase("\"version\": 1,", "\"version\": 2,", "MAPDESC_FORMAT line 3")]
        [TestCase("\"format\": \"warcore.map\",", "\"format\": \"warcore.maps\",", "MAPDESC_FORMAT line 2")]
        [TestCase("\"id\": \"sample\",", "\"id\": \"sample\"", "MAPDESC_FORMAT line 5")]
        [TestCase("  \"content\": \"rwr\",\n", "  \"content\": \"rwr\",\n  \"id\": \"again\",\n", "MAPDESC_KEY line 6")]
        [TestCase("  \"pack\": \"rwr-m6\",\n", "", "MAPDESC_KEY line 1")]
        [TestCase("      \"radiusMm\": 400,\n", "", "MAPDESC_KEY point 1 line 39")]
        [TestCase("\"squad\": 7,", "\"squad\": \"7\",", "MAPDESC_KEY unit 1 line 59")]
        [TestCase("\"squad\": 7,", "\"squad\": -1,", "MAPDESC_KEY unit 1 line 59")]
        [TestCase("      \"bit\": 1\n", "      \"bit\": 1,\n      \"color\": 2\n", "MAPDESC_KEY layer line 15")]
        [TestCase("\"crowdPass\": \"none\"", "\"crowdPass\": \"fast\"", "MAPDESC_KEY line 72")]
        [TestCase("\"version\": 1,", "\"version\": 1.0,", "MAPDESC_KEY line 3")]
        [TestCase("\"size\": [2, 1],", "\"size\": [0, 1],", "MAPDESC_SIZE line 7")]
        [TestCase("\"origin\": [-1, 0],", "\"origin\": [16383, 0],", "MAPDESC_SIZE line 8")]
        [TestCase("[-65536, 0, 0]", "[-65536, 0, 3000000000]", "MAPDESC_GRID line 19")]
        [TestCase("\"AB\"", "\"AC\"", "MAPDESC_GRID line 24")]
        [TestCase("[\"grass\", \"mud\"]", "[\"mud\", \"grass\"]", "MAPDESC_GRID line 22")]
        [TestCase("      \"id\": 1,\n      \"kind\": \"wall\",", "      \"id\": 2,\n      \"kind\": \"wall\",", "MAPDESC_ID wall 2 line 29")]
        [TestCase("      \"id\": 1,\n      \"at\"", "      \"id\": 2147483648,\n      \"at\"", "MAPDESC_ID point line 40")]
        [TestCase("\"key\": 1,", "\"key\": 18446744073709551616,", "MAPDESC_ID unit line 56")]
        [TestCase("\"team\": 2,", "\"team\": 3,", "MAPDESC_TEAM base 1 line 50")]
        [TestCase("\"initialOwner\": 0,", "\"initialOwner\": 3,", "MAPDESC_TEAM point 1 line 43")]
        [TestCase("\"layer\": \"walls\",", "\"layer\": \"roof\",", "MAPDESC_WALL wall 1 line 31")]
        [TestCase("\"heightRaw\": 131072,", "\"heightRaw\": 131071,", "MAPDESC_WALL wall 1 line 34")]
        [TestCase("\"cell\": [1, 0],", "\"cell\": [2, 0],", "MAPDESC_WALL wall 1 line 33")]
        [TestCase("\"at\": [-500, 500],", "\"at\": [5000, 500],", "MAPDESC_POINT point 1 line 41")]
        [TestCase("\"radiusMm\": 400,", "\"radiusMm\": 0,", "MAPDESC_POINT point 1 line 42")]
        [TestCase("\"owner\": 0", "\"owner\": 2", "MAPDESC_OWNER wall 1 line 35")]
        public void OneChangeGivesTheFrozenMessage(string oldText, string newText, string message)
        {
            Assert.That(Error(Edit(Sample, oldText, newText)), Is.EqualTo(message));
        }

        [Test]
        public void HeightAboveInt32IsAGridErrorNotAKeyError()
        {
            Assert.That(Error(Edit(Sample, "[-65536, 0, 0]", "[-65536, 0, 3000000000]")), Is.EqualTo("MAPDESC_GRID line 19"));
        }

        [Test]
        public void TeamThreeIsReportedBeforeAnyBattleTypeIsBuilt()
        {
            InvalidDataException e = Fails(Utf8(Edit(Sample, "\"team\": 2,", "\"team\": 3,")));
            Assert.That(e.Message, Is.EqualTo("MAPDESC_TEAM base 1 line 50"));
        }

        [Test]
        public void WriterErrorsKeepTheirCodeAndInnerException()
        {
            InvalidDataException e = Fails(Utf8(Edit(Sample, "\"bit\": 1", "\"bit\": 32")));
            Assert.That(e.Message, Is.EqualTo("MAPDESC_WCMAP WCMAP_LAYERS"));
            Assert.That(e.InnerException, Is.TypeOf<InvalidDataException>());
            Assert.That(e.InnerException.Message, Is.EqualTo("WCMAP_LAYERS"));
        }

        [Test]
        public void ByteErrorsAreFormatErrorsWithoutLine()
        {
            byte[] bytes = Encoding.UTF8.GetBytes("\uFEFF" + Sample);
            Assert.That(bytes[0], Is.EqualTo(0xEF));
            Assert.That(Fails(bytes).Message, Is.EqualTo("MAPDESC_FORMAT"));
            byte[] invalid = Utf8(Sample);
            invalid[invalid.Length - 3] = 0xC3;
            Assert.That(Fails(invalid).Message, Is.EqualTo("MAPDESC_FORMAT"));
        }

        // Priorities: number lexis before the version value; grid shape on the array or row node; values on their own line.
        [TestCase("\"version\": 1,", "\"version\": 1.0,", "MAPDESC_KEY line 3")]
        [TestCase("\"version\": 1,", "\"version\": 2,", "MAPDESC_FORMAT line 3")]
        [TestCase("  \"heights\": [\n    [0, 0, 0],\n    [-65536, 0, 0]\n  ],\n", "  \"heights\": [],\n", "MAPDESC_GRID line 17")]
        [TestCase("[-65536, 0, 0]", "[-65536, 0]", "MAPDESC_GRID line 19")]
        [TestCase("[-65536, 0, 0]", "[-65536, 0,\n3000000000]", "MAPDESC_GRID line 20")]
        public void ErrorPriorityCounterExamples(string oldText, string newText, string message)
        {
            Assert.That(Error(Edit(Sample, oldText, newText)), Is.EqualTo(message));
        }

        // Order within the frozen sequence: the version value before a lone surrogate (pass 1 string check).
        [Test]
        public void VersionValueComesBeforeALoneSurrogate()
        {
            Assert.That(Error(Edit(Edit(Sample, "\"version\": 1,", "\"version\": 2,"), "\"id\": \"sample\"", "\"id\": \"s\\uD800\"")),
                Is.EqualTo("MAPDESC_FORMAT line 3"));
        }

        // A pair's first element is type- and range-checked before its second element.
        [Test]
        public void PairElementsAreCheckedOneAfterTheOther()
        {
            Assert.That(Error(Edit(Sample, "\"size\": [2, 1],", "\"size\": [0, \"x\"],")), Is.EqualTo("MAPDESC_SIZE line 7"));
            Assert.That(Error(Edit(Sample, "\"size\": [2, 1],", "\"size\": [2, \"x\"],")), Is.EqualTo("MAPDESC_KEY line 7"));
        }

        // Canonical key order, depth first: heightOwner's elements before surfaceOwner's type.
        [Test]
        public void HeightOwnerIsCheckedBeforeSurfaceOwner()
        {
            Assert.That(Error(Edit(Edit(Sample, "  \"heightOwner\": [\n    \"...\",\n    \".A.\"\n  ],\n", "  \"heightOwner\": [0],\n"),
                "  \"surfaceOwner\": [\n    \".B\"\n  ],\n", "  \"surfaceOwner\": 0,\n")), Is.EqualTo("MAPDESC_KEY line 75"));
        }

        // Size bounds come from the map side limit (HeightField.MaxSide, the same value as the writer's limit), not a literal.
        private static string Flat(int width, int depth) => MapDescJson.WriteText(new MapDesc("flat", "rwr", "rwr",
            new MapXZ(width, depth), new MapXZ(0, 0), 0, 262144, new[] { new MapLayer("walls", 1) }, null, null, new MapWall[0],
            new MapPoint[0], new MapBase[0], new MapUnit[0], new MapRun(1, "none"), new[] { "hand" }, null, null,
            new MapStamp[0], new MapSrc[0], new MapNext(1, 1, 1, 1)));

        [Test]
        public void SizeUpToTheMapSideLimitBakes()
        {
            int side = HeightField.MaxSide;
            WcMap wide = WcMapReader.Read(Bake(Flat(side, 1)).WcMapBytes);
            Assert.That(wide.Width, Is.EqualTo((uint)side));
            WcMap deep = WcMapReader.Read(Bake(Flat(1, side)).WcMapBytes);
            Assert.That(deep.Depth, Is.EqualTo((uint)side));
        }

        [Test]
        public void SizeAboveTheMapSideLimitIsASizeError()
        {
            int over = HeightField.MaxSide + 1;
            Assert.That(Error(Edit(Sample, "\"size\": [2, 1],", "\"size\": [" + over + ", 1],")), Is.EqualTo("MAPDESC_SIZE line 7"));
            Assert.That(Error(Edit(Sample, "\"size\": [2, 1],", "\"size\": [2, " + over + "],")), Is.EqualTo("MAPDESC_SIZE line 7"));
        }

        [Test]
        public void IdsUpToOneBelowTheNextCounterBake()
        {
            string points = Edit(Edit(Sample, "      \"id\": 1,\n      \"at\"", "      \"id\": 2147483646,\n      \"at\""),
                "\"point\": 2,", "\"point\": 2147483647,");
            Assert.That(Bake(points).Setup.ControlPoints[0].Id, Is.EqualTo(2147483646));
            string units = Edit(Edit(Sample, "\"key\": 1,", "\"key\": 18446744073709551614,"), "\"unit\": 2", "\"unit\": 18446744073709551615");
            Assert.That(Bake(units).Setup.Soldiers[0].Key, Is.EqualTo(18446744073709551614UL));
        }

        // The sample has a single point (id 1, next.point 2): an id that narrows to 1 must still be refused.
        [Test]
        public void PointIdIsRangeCheckedBeforeNarrowing()
        {
            StringAssert.Contains("\"point\": 2,", Sample);
            Assert.That(Error(Edit(Sample, "      \"id\": 1,\n      \"at\"", "      \"id\": 4294967297,\n      \"at\"")),
                Is.EqualTo("MAPDESC_ID point line 40"));
        }

        // ---- acceptance 5: ownership fields ----

        [Test]
        public void OwnershipSampleRoundTripsAndKeepsTeamAndSquad()
        {
            Assert.That(MapDescJson.Write(MapDescJson.Read(Utf8(Sample))), Is.EqualTo(Utf8(Sample)));
            MapSoldier soldier = Bake(Sample).Setup.Soldiers[0];
            Assert.That(soldier.Team, Is.EqualTo(1));
            Assert.That(soldier.Squad, Is.EqualTo(7));
            Assert.That(soldier.Type, Is.EqualTo("rwr.soldier-1"));
            Assert.That(soldier.XMm, Is.EqualTo(-200L));
            Assert.That(soldier.ZMm, Is.EqualTo(800L));
            Assert.That(soldier.Reserve, Is.False);
        }

        private const string StampEnd = "      \"id\": 1\n    }\n  ],\n  \"removed\"";

        private static string SecondStamp(string kind, int id) => "      \"id\": 1\n    },\n    {\n      \"instance\": \"s1\",\n      \"kind\": \""
            + kind + "\",\n      \"src\": 3,\n      \"id\": " + id + "\n    }\n  ],\n  \"removed\"";

        [Test]
        public void StampWithTheSameSourceButAnotherKindIsAccepted()
        {
            string text = Edit(Sample, StampEnd, SecondStamp("wall", 1));
            MapDesc desc = MapDescJson.Read(Utf8(text));
            Assert.That(desc.Stamps.Count, Is.EqualTo(2));
            Assert.That(desc.Stamps[1].Kind, Is.EqualTo("wall"));
            Assert.That(MapDescJson.Write(desc), Is.EqualTo(Utf8(text)));
            Bake(text);
        }

        [Test]
        public void OwnershipViolationsAreOwnerErrors()
        {
            Assert.That(Error(Edit(Sample, "      \"kind\": \"unit\",\n      \"src\": 3,\n      \"id\": 1",
                "      \"kind\": \"tree\",\n      \"src\": 3,\n      \"id\": 1")), Is.EqualTo("MAPDESC_OWNER stamp line 85"));
            Assert.That(Error(Edit(Sample, StampEnd, SecondStamp("unit", 2))), Is.EqualTo("MAPDESC_OWNER stamp 2 line 89"));
            var owners = new StringBuilder("\"owners\": [\"hand\"");
            for (int i = 1; i < 65; i++) owners.Append(", \"o").Append(i).Append('"');
            Assert.That(Error(Edit(Sample, "\"owners\": [\"hand\", \"stamp:s1\"]", owners.Append(']').ToString())),
                Is.EqualTo("MAPDESC_OWNER line 74"));
            Assert.That(Error(Edit(Sample, "  \"heights\": [\n    [0, 0, 0],\n    [-65536, 0, 0]\n  ],\n", "")),
                Is.EqualTo("MAPDESC_OWNER line 71"));
            Assert.That(Error(Edit(Sample, "\"owners\": [\"hand\", \"stamp:s1\"]", "\"owners\": [\"stamp:s1\", \"hand\"]")),
                Is.EqualTo("MAPDESC_OWNER line 74"));
            Assert.That(Error(Edit(Sample, "\".A.\"", "\".C.\"")), Is.EqualTo("MAPDESC_OWNER line 77"));
            Assert.That(Error(Edit(Sample, "\"kind\": \"unit\",\n        \"src\": 3", "\"kind\": \"wall\",\n        \"src\": 3")),
                Is.EqualTo("MAPDESC_OWNER unit 1 line 65"));
            Assert.That(Error(Edit(Sample, "      \"kind\": \"wall\",\n      \"src\": 2\n    }\n  ],",
                "      \"kind\": \"wall\",\n      \"src\": 2\n    },\n    {\n      \"instance\": \"s1\",\n      \"kind\": \"wall\",\n      \"src\": 2\n    }\n  ],")),
                Is.EqualTo("MAPDESC_OWNER removed line 96"));
        }

        [Test]
        public void AllDotOwnerGridIsTheSameAsNoGrid()
        {
            string text = Edit(Sample, "  \"surfaceOwner\": [\n    \".B\"\n  ],\n", "  \"surfaceOwner\": [\n    \"..\"\n  ],\n");
            MapDesc desc = MapDescJson.Read(Utf8(text));
            Assert.That(desc.SurfaceOwner, Is.Null);
            Assert.That(MapDescJson.WriteText(desc), Is.EqualTo(Edit(Sample, "  \"surfaceOwner\": [\n    \".B\"\n  ],\n", "")));
        }
    }
}
