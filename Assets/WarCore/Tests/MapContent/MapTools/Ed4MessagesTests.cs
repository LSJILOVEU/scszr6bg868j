using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // ED-4 acceptance 2: the message table, its reader, and the codes the sources can raise.
    public sealed class Ed4MessagesTests
    {
        private Ed0Dirs dirs;

        [SetUp] public void Make() => dirs = new Ed0Dirs();
        [TearDown] public void Clean() => dirs.Dispose();

        private static string Source(string folder)
        {
            string root = Path.Combine(ArchitectureTests.AssetRoot(), "WarCore", folder);
            var all = new StringBuilder();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
                all.Append(File.ReadAllText(file)).Append('\n');
            return all.ToString();
        }

        // Every code the Content and Presentation sources can raise: the four English prefixes, the writer codes handed to
        // Require(condition, "CODE") (the text may break across lines) and the two WCMAP_ literals.
        private static SortedSet<string> CodesInSource()
        {
            string text = Source("Content") + Source("Presentation");
            var codes = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match m in Regex.Matches(text, "(GATE|MAPDESC|MAPEDIT|MAPPREVIEW)_[A-Z_]+")) codes.Add(m.Value);
            foreach (Match m in Regex.Matches(text, "Require\\([^;]*?,\\s*\"([A-Z0-9_]+)\"\\)\\s*;", RegexOptions.Singleline)) codes.Add("WCMAP_" + m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, "\"(WCMAP_[A-Z0-9_]+)\"")) codes.Add(m.Groups[1].Value);
            return codes;
        }

        [Test]
        public void TheTableHoldsEveryCodeTheSourcesCanRaiseAndNoOther()
        {
            SortedSet<string> inSource = CodesInSource();
            Assert.That(inSource.Count(c => c.StartsWith("GATE_", StringComparison.Ordinal)), Is.EqualTo(12));
            Assert.That(inSource.Count(c => c.StartsWith("MAPDESC_", StringComparison.Ordinal)), Is.EqualTo(12));
            Assert.That(inSource.Count(c => c.StartsWith("MAPEDIT_", StringComparison.Ordinal)), Is.EqualTo(2));
            Assert.That(inSource, Does.Contain("MAPPREVIEW_GROUND_FALLBACK"));
            Assert.That(inSource, Does.Contain("WCMAP_HEIGHT_SLOPE").And.Contain("WCMAP_TRUNCATED").And.Contain("WCMAP_UTF8"));
            Assert.That(Ed4Fixtures.Messages().Codes, Is.EqualTo(inSource.ToArray()));
        }

        [Test]
        public void TheShippedTableIsUtf8WithoutByteOrderMarkAndOnlyLineFeeds()
        {
            byte[] bytes = File.ReadAllBytes(Ed4Fixtures.TablePath);
            Assert.That(bytes.Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(Array.IndexOf(bytes, (byte)'\r'), Is.EqualTo(-1));
            Assert.That(bytes.Last(), Is.EqualTo((byte)'\n'));
            string text = new UTF8Encoding(false, true).GetString(bytes);
            Assert.That(Regex.IsMatch(text, "[\\u3400-\\u9fff]"), Is.True);
        }

        [Test]
        public void EveryLineOfTheShippedTableIsRealTextForItsCode()
        {
            MapMessages table = Ed4Fixtures.Messages();
            foreach (string code in table.Codes)
            {
                string text = table.Text(code);
                Assert.That(text, Is.Not.Empty, code);
                Assert.That(text, Is.Not.EqualTo(code), code);
                Assert.That(text.Trim(), Is.EqualTo(text), code);
            }
        }

        [Test]
        public void TextOfAnUnknownCodeIsTheCodeItself()
        {
            MapMessages table = Ed4Fixtures.Messages();
            Assert.That(table.Text("GATE_NO_SUCH_CODE"), Is.EqualTo("GATE_NO_SUCH_CODE"));
            Assert.That(table.Text(""), Is.EqualTo(""));
        }

        [Test]
        public void FindReadsTheTableFromTheProjectFolder()
        {
            Assert.That(MapMessages.Find().Codes, Is.EqualTo(Ed4Fixtures.Messages().Codes));
        }

        [Test]
        public void CommentsAndBlankLinesAreIgnoredAndTheTextKeepsInnerTabs()
        {
            string path = Ed4Fixtures.TableFile(dirs, "# a comment\n\nA_ONE\tfirst\n   \nA_TWO\tsecond\tpart\n# trailing\n");
            MapMessages table = null;
            Assert.DoesNotThrow(() => table = MapMessages.Load(path));
            Assert.That(table.Codes, Is.EqualTo(new[] { "A_ONE", "A_TWO" }));
            Assert.That(table.Text("A_ONE"), Is.EqualTo("first"));
            Assert.That(table.Text("A_TWO"), Is.EqualTo("second\tpart"));
        }

        // The five mistakes of the format, each refused with the line it is on (comments and blank lines count as lines).
        [TestCase("A_ONE\tfine\nA_TWO no tab here\n", 2, "no tab", TestName = "NoTab")]
        [TestCase("# c\n\nA_ONE\tfine\n\tno code\n", 4, "empty code", TestName = "EmptyCode")]
        [TestCase("A_ONE\tfine\nA_TWO\t\n", 2, "empty text for A_TWO", TestName = "EmptyText")]
        [TestCase("A_ONE\tfine\nA_TWO\t   \n", 2, "empty text for A_TWO", TestName = "TextOfSpacesOnly")]
        [TestCase("A_ONE\tfine\nA_TWO\tfine\nA_ONE\tagain\n", 3, "code A_ONE is listed twice", TestName = "DuplicateNotNextToTheFirst")]
        [TestCase("A_ONE\tfine\nA_ONE\tagain\n", 2, "code A_ONE is listed twice", TestName = "DuplicateNextToTheFirst")]
        [TestCase("A_TWO\tfine\n\nA_ONE\tout of order\n", 3, "code A_ONE is out of order (after A_TWO)", TestName = "OutOfOrder")]
        public void AMistakeInTheFileIsRefusedWithItsLineNumberAndReason(string text, int line, string reason)
        {
            string path = Ed4Fixtures.TableFile(dirs, text);
            var e = Assert.Throws<InvalidDataException>(() => MapMessages.Load(path));
            // The message names the file (not its folder), the line and the reason.
            Assert.That(e.Message, Does.StartWith(Path.GetFileName(path) + " line " + line + ": " + reason));
            Assert.That(e.Message, Does.Not.Contain(dirs.Root));
        }

        [Test]
        public void ATableThatIsNotValidUtf8IsRefused()
        {
            string path = Path.Combine(dirs.Root, "bad-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllBytes(path, new byte[] { (byte)'A', (byte)'\t', 0xFF, 0xFE, (byte)'\n' });
            Assert.Throws<DecoderFallbackException>(() => MapMessages.Load(path));
        }

        [Test]
        public void ANullCodeHasNullText()
        {
            string text = "unset";
            Assert.DoesNotThrow(() => text = Ed4Fixtures.Messages().Text(null));
            Assert.That(text, Is.Null);
        }

        [Test]
        public void OrderIsOrdinalNotCultureOrCaseInsensitive()
        {
            Assert.DoesNotThrow(() => MapMessages.Load(Ed4Fixtures.TableFile(dirs, "B_UP\tx\na_low\ty\n")));
            Assert.Throws<InvalidDataException>(() => MapMessages.Load(Ed4Fixtures.TableFile(dirs, "a_low\tx\nB_UP\ty\n")));
        }

        [Test]
        public void ADescribedProblemIsItsCodeTextAndAWriterProblemAddsTheWriterCodeText()
        {
            MapMessages table = Ed4Fixtures.Messages();
            Assert.That(table.Describe(new MapProblem("GATE_REF", "unit 3")), Is.EqualTo(table.Text("GATE_REF")));
            Assert.That(table.Describe(new MapProblem("MAPDESC_WCMAP")), Is.EqualTo(table.Text("MAPDESC_WCMAP")));
            Assert.That(table.Describe(new MapProblem("MAPDESC_WCMAP", "WCMAP_NO_SUCH")), Does.EndWith("WCMAP_NO_SUCH"));
        }

        // A map whose heights jump 3 m between neighbours: the bake refuses it with a writer code, MapGate reports it as
        // MAPDESC_WCMAP with the writer code in Id.
        [Test]
        public void AWriterProblemFromTheRealGateCarriesBothTexts()
        {
            var map = new E2Map { Pack = "rwr-m6" };
            map.Heights = new int[map.Depth + 1][];
            for (int z = 0; z < map.Heights.Length; z++) map.Heights[z] = new int[map.Width + 1];
            map.Heights[4][6] = 3 * 65536;
            IReadOnlyList<MapProblem> problems = MapGate.Check(map.Bytes(), E2Fixtures.M6Pack());
            MapProblem problem = problems.Single();
            Assert.That(problem.Code, Is.EqualTo("MAPDESC_WCMAP"));
            Assert.That(problem.Id, Is.EqualTo("WCMAP_HEIGHT_SLOPE"));
            MapMessages table = Ed4Fixtures.Messages();
            string text = table.Describe(problem);
            Assert.That(text, Does.StartWith(table.Text("MAPDESC_WCMAP")));
            Assert.That(text, Does.EndWith(table.Text("WCMAP_HEIGHT_SLOPE")));
            Assert.That(table.Text("WCMAP_HEIGHT_SLOPE"), Is.Not.EqualTo("WCMAP_HEIGHT_SLOPE"));
            Assert.That(text.Length, Is.GreaterThan(table.Text("MAPDESC_WCMAP").Length + table.Text("WCMAP_HEIGHT_SLOPE").Length));
        }
    }
}
