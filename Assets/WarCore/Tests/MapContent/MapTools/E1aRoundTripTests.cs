using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.MapTools;
using static WarCore.Tests.E1aSamples;

namespace WarCore.Tests
{
    // Acceptance 2 (canonical round trip) and 6 (deep immutability).
    public sealed class E1aRoundTripTests
    {
        private static readonly string[] Files = { TerrainPath, M6Path };

        [Test]
        public void RepositoryDescriptionsAreCanonical()
        {
            foreach (string path in Files)
            {
                byte[] bytes = File.ReadAllBytes(path);
                Assert.That(MapDescJson.Write(MapDescJson.Read(bytes)), Is.EqualTo(bytes), path);
            }
            Assert.That(MapDescJson.Write(MapDescJson.Read(Utf8(Sample))), Is.EqualTo(Utf8(Sample)));
        }

        [Test]
        public void ShuffledKeysWhitespaceAndCrLfWriteBackCanonical()
        {
            foreach (string text in new[] { Encoding.UTF8.GetString(File.ReadAllBytes(TerrainPath)), Encoding.UTF8.GetString(File.ReadAllBytes(M6Path)), Sample })
            {
                var sb = new StringBuilder();
                Scramble(ContentJson.Parse(text, "map"), sb);
                string scrambled = sb.ToString();
                Assert.That(scrambled, Is.Not.EqualTo(text));
                Assert.That(scrambled, Does.Contain("\r\n"));
                Assert.That(scrambled.IndexOf("\"next\"", StringComparison.Ordinal), Is.LessThan(scrambled.IndexOf("\"format\"", StringComparison.Ordinal)));
                Assert.That(MapDescJson.Write(MapDescJson.Read(Utf8(scrambled))), Is.EqualTo(Utf8(text)));
            }
        }

        // Members in reverse order at every level, extra blanks and CR LF line ends.
        private static void Scramble(JsonNode node, StringBuilder sb)
        {
            switch (node.Kind)
            {
                case JsonKind.Object:
                    sb.Append("{ \r\n");
                    for (int i = node.Members.Count - 1; i >= 0; i--)
                    {
                        sb.Append("\t ").Append(Quote(node.Members[i].Name)).Append(" :\r\n  ");
                        Scramble(node.Members[i].Value, sb);
                        sb.Append(i > 0 ? " ,\r\n" : "\r\n");
                    }
                    sb.Append(" }");
                    break;
                case JsonKind.Array:
                    sb.Append("[ ");
                    for (int i = 0; i < node.Items.Count; i++)
                    {
                        if (i > 0) sb.Append(" ,\r\n ");
                        Scramble(node.Items[i], sb);
                    }
                    sb.Append("\r\n]");
                    break;
                case JsonKind.String: sb.Append(Quote(node.Text)); break;
                case JsonKind.Number: sb.Append(node.Text); break;
                default: sb.Append(node.Flag ? "true" : "false"); break;
            }
        }

        private static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ' || c > '~') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }

        [Test]
        public void BakingTwiceGivesTheSameBytesAndTheCanonicalHash()
        {
            foreach (string path in Files)
            {
                byte[] bytes = File.ReadAllBytes(path);
                MapBakeResult first = Bake(bytes), second = Bake(bytes);
                Assert.That(second.WcMapBytes, Is.EqualTo(first.WcMapBytes), path);
                Assert.That(second.DescHash, Is.EqualTo(first.DescHash), path);
                Assert.That(first.DescHash.Length, Is.EqualTo(32));
                Assert.That(BitConverter.ToString(first.DescHash).Replace("-", ""), Is.EqualTo(E1aRecordTests.Sha(bytes)), path);
            }
        }

        internal static MapDesc Negative() => new MapDesc("neg", "rwr", "rwr", new MapXZ(2, 1), new MapXZ(-3, -2), -65536, 131072,
            new[] { new MapLayer("walls", 1) }, new[] { new[] { -32768, 0, -1 }, new[] { 0, -65536, 0 } }, null, new MapWall[0],
            new[] { new MapPoint(1, new MapMm(-2500, -1500), 500, 0, new MapMm(-1999, -1001)) },
            new[] { new MapBase(1, 1, new MapMm(-3000, -2000)) }, new MapUnit[0], new MapRun(7, "none"), new[] { "hand" }, null, null,
            new MapStamp[0], new MapSrc[0], new MapNext(1, 2, 2, 1));

        private const string NegativeText = ""
            + "{\n"
            + "  \"format\": \"warcore.map\",\n"
            + "  \"version\": 1,\n"
            + "  \"id\": \"neg\",\n"
            + "  \"content\": \"rwr\",\n"
            + "  \"pack\": \"rwr\",\n"
            + "  \"size\": [2, 1],\n"
            + "  \"origin\": [-3, -2],\n"
            + "  \"floorRaw\": -65536,\n"
            + "  \"ceilingRaw\": 131072,\n"
            + "  \"layers\": [\n"
            + "    {\n"
            + "      \"name\": \"walls\",\n"
            + "      \"bit\": 1\n"
            + "    }\n"
            + "  ],\n"
            + "  \"heights\": [\n"
            + "    [-32768, 0, -1],\n"
            + "    [0, -65536, 0]\n"
            + "  ],\n"
            + "  \"walls\": [],\n"
            + "  \"points\": [\n"
            + "    {\n"
            + "      \"id\": 1,\n"
            + "      \"at\": [-2500, -1500],\n"
            + "      \"radiusMm\": 500,\n"
            + "      \"initialOwner\": 0,\n"
            + "      \"spawn\": [-1999, -1001]\n"
            + "    }\n"
            + "  ],\n"
            + "  \"bases\": [\n"
            + "    {\n"
            + "      \"id\": 1,\n"
            + "      \"team\": 1,\n"
            + "      \"at\": [-3000, -2000]\n"
            + "    }\n"
            + "  ],\n"
            + "  \"units\": [],\n"
            + "  \"run\": {\n"
            + "    \"seed\": 7,\n"
            + "    \"crowdPass\": \"none\"\n"
            + "  },\n"
            + "  \"owners\": [\"hand\"],\n"
            + "  \"stamps\": [],\n"
            + "  \"removed\": [],\n"
            + "  \"next\": {\n"
            + "    \"wall\": 1,\n"
            + "    \"point\": 2,\n"
            + "    \"base\": 2,\n"
            + "    \"unit\": 1\n"
            + "  }\n"
            + "}\n";

        [Test]
        public void NegativeNumbersIgnoreTheCurrentCulture()
        {
            CultureInfo tilde = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            tilde.NumberFormat.NegativeSign = "~";
            CultureInfo saved = Thread.CurrentThread.CurrentCulture;
            try
            {
                foreach (CultureInfo culture in new[] { new CultureInfo("fr-FR"), new CultureInfo("tr-TR"), tilde })
                {
                    Thread.CurrentThread.CurrentCulture = culture;
                    Assert.That(MapDescJson.Write(Negative()), Is.EqualTo(Utf8(NegativeText)), culture.Name);
                }
                Thread.CurrentThread.CurrentCulture = tilde;
                Assert.That((-1L).ToString(), Is.EqualTo("~1"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = saved;
            }
            Assert.That(NegativeText, Does.Contain("-65536"));
            Assert.That(MapDescJson.Write(MapDescJson.Read(Utf8(NegativeText))), Is.EqualTo(Utf8(NegativeText)));
            Bake(NegativeText);
        }

        // owners[1] holds U+0001 U+0008 U+0009 U+000A U+000C U+000D " \ / U+007F U+4E2D.
        private const string Awkward = "\u0001\u0008\u0009\u000A\u000C\u000D\"\\/\u007F\u4E2D";

        [Test]
        public void ControlCharactersAreWrittenAsUpperCaseUnicodeEscapes()
        {
            MapDesc read = MapDescJson.Read(Utf8(Sample));
            string[] owners = { "hand", Awkward };
            var desc = new MapDesc(read.Id, read.Content, read.Pack, read.Size, read.Origin, read.FloorRaw, read.CeilingRaw,
                Copy(read.Layers), Rows(read.Heights), read.Surfaces, Copy(read.Walls), Copy(read.Points), Copy(read.Bases),
                Copy(read.Units), read.Run, owners, Copy(read.HeightOwner), Copy(read.SurfaceOwner), Copy(read.Stamps),
                Copy(read.Removed), read.Next);
            string written = MapDescJson.WriteText(desc);
            string token = "\"\\u0001\\u0008\\u0009\\u000A\\u000C\\u000D\\\"\\\\/\u007F\u4E2D\"";
            string line = "  \"owners\": [\"hand\", " + token + "],";
            Assert.That(written.Split('\n'), Has.Member(line));
            byte[] bytes = MapDescJson.Write(desc);
            Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo(written));
            Assert.That(MapDescJson.Read(bytes).Owners[1], Is.EqualTo(Awkward));
        }

        [TestCase("\"origin\": [-1, 0]", "\"origin\": [-1, -0]", "MAPDESC_KEY line 8")]
        [TestCase("\"version\": 1,", "\"version\": 1.0,", "MAPDESC_KEY line 3")]
        [TestCase("\"id\": \"sample\"", "\"id\": \"s\\uD800\"", "MAPDESC_KEY line 4")]
        public void LexicalErrorsAreKeyErrors(string oldText, string newText, string message)
        {
            var e = Assert.Throws<InvalidDataException>(() => MapDescJson.Read(Utf8(Edit(Sample, oldText, newText))));
            Assert.That(e.Message, Is.EqualTo(message));
        }

        [Test]
        public void ByteOrderMarkAndInvalidUtf8AreFormatErrors()
        {
            byte[] sample = Utf8(Sample);
            var bom = new byte[sample.Length + 3];
            bom[0] = 0xEF; bom[1] = 0xBB; bom[2] = 0xBF;
            Array.Copy(sample, 0, bom, 3, sample.Length);
            byte[] invalid = (byte[])sample.Clone();
            invalid[Array.IndexOf(invalid, (byte)'s')] = 0xFF;
            foreach (byte[] bytes in new[] { bom, invalid })
            {
                Assert.That(Assert.Throws<InvalidDataException>(() => MapDescJson.Read(bytes)).Message, Is.EqualTo("MAPDESC_FORMAT"));
                Assert.That(Fails(bytes).Message, Is.EqualTo("MAPDESC_FORMAT"));
            }
        }

        private static T[] Copy<T>(IReadOnlyList<T> items)
        {
            if (items == null) return null;
            var copy = new T[items.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = items[i];
            return copy;
        }

        private static int[][] Rows(IReadOnlyList<IReadOnlyList<int>> rows)
        {
            if (rows == null) return null;
            var copy = new int[rows.Count][];
            for (int j = 0; j < copy.Length; j++) copy[j] = Copy(rows[j]);
            return copy;
        }

        [Test]
        public void ModelCopiesItsInputsAndHandsOutReadOnlyCollections()
        {
            int[][] heights = { new[] { 0, 0, 0 }, new[] { -65536, 0, 0 } };
            string[] owners = { "hand", "stamp:s1" };
            MapStamp[] stamps = { new MapStamp("s1", "unit", 3, 1) };
            string[] heightOwner = { "...", ".A." };
            MapDesc read = MapDescJson.Read(Utf8(Sample));
            MapUnit[] units = Copy(read.Units);
            var desc = new MapDesc("sample", "rwr", "rwr-m6", new MapXZ(2, 1), new MapXZ(-1, 0), -65536, 262144, Copy(read.Layers),
                heights, read.Surfaces, Copy(read.Walls), Copy(read.Points), Copy(read.Bases), units, read.Run, owners,
                heightOwner, Copy(read.SurfaceOwner), stamps, Copy(read.Removed), read.Next);
            byte[] before = MapDescJson.Write(desc);
            Assert.That(before, Is.EqualTo(Utf8(Sample)));

            heights[1][0] = 7; heights[0] = new[] { 9, 9, 9 };
            owners[1] = "other";
            stamps[0] = new MapStamp("s9", "wall", 1, 1);
            units[0] = null;
            heightOwner[1] = "...";
            Assert.That(desc.Heights[1][0], Is.EqualTo(-65536));
            Assert.That(desc.Heights[0][0], Is.EqualTo(0));
            Assert.That(desc.Owners[1], Is.EqualTo("stamp:s1"));
            Assert.That(desc.Stamps[0].Instance, Is.EqualTo("s1"));
            Assert.That(desc.Units[0].Key, Is.EqualTo(1UL));
            Assert.That(desc.HeightOwner[1], Is.EqualTo(".A."));
            Assert.That(MapDescJson.Write(desc), Is.EqualTo(before));

            Assert.Throws<NotSupportedException>(() => ((IList<int>)desc.Heights[0])[0] = 5);
            Assert.Throws<NotSupportedException>(() => ((IList<IReadOnlyList<int>>)desc.Heights)[0] = new int[3]);
            Assert.Throws<NotSupportedException>(() => ((IList<MapStamp>)desc.Stamps)[0] = null);
            Assert.Throws<NotSupportedException>(() => ((IList<MapUnit>)desc.Units).Add(null));
            Assert.Throws<NotSupportedException>(() => ((IList<string>)desc.Owners)[0] = "x");
            Assert.Throws<NotSupportedException>(() => ((IList<string>)desc.Surfaces.Rows)[0] = "x");
            Assert.That(MapDescJson.Write(desc), Is.EqualTo(before));
        }
    }
}
